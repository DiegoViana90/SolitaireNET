using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Chess;
sealed class ChessStore
{
    static readonly TimeSpan InactiveRoomLifetime = TimeSpan.FromMinutes(30);
    static readonly TimeSpan CanceledRoomLifetime = TimeSpan.FromMinutes(2);
    readonly object gate = new();
    readonly ConcurrentDictionary<string, ChessSession> rooms = new();
    string? waitingRandomCode;

    public ChessJoinResult CreatePrivateRoom()
    {
        ChessSession room = CreateRoom();
        return room.AddPlayer();
    }
    public ChessJoinResult CreateBotRoom(string difficulty)
    {
        var room = CreateRoom(difficulty); var player = room.AddPlayer(); room.AddPlayer();
        return player;
    }
    public ChessJoinResult ApplyBotMove(string code) => rooms.TryGetValue(NormalizeCode(code), out var room) ? room.ApplyBotMove() : ChessJoinResult.Fail(code, "Sala nao encontrada.");

    public ChessJoinResult JoinRoom(string code)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out ChessSession? room))
            return ChessJoinResult.Fail(code, "Sala nao encontrada.");

        return room.AddPlayer();
    }

    public ChessJoinResult JoinRandomRoom()
    {
        lock (gate)
        {
            if (waitingRandomCode != null &&
                rooms.TryGetValue(waitingRandomCode, out ChessSession? waitingRoom) &&
                waitingRoom.IsWaiting)
            {
                waitingRandomCode = null;
                return waitingRoom.AddPlayer();
            }

            ChessSession room = CreateRoom();
            waitingRandomCode = room.Code;
            return room.AddPlayer();
        }
    }

    public ChessJoinResult GetRoom(string code, string playerId)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out ChessSession? room))
            return ChessJoinResult.Fail(code, "Sala nao encontrada.");

        return room.ToJoinResult(playerId);
    }

    public ChessJoinResult ApplyMove(string code, ChessMoveAction action)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out ChessSession? room))
            return ChessJoinResult.Fail(code, "Sala nao encontrada.");

        return room.ApplyMove(action);
    }

    public ChessJoinResult LeaveRoom(string code, string playerId)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out ChessSession? room))
            return ChessJoinResult.Fail(code, "Sala nao encontrada.");

        ChessJoinResult result = room.MarkPlayerDisconnected(playerId);
        if (result.Error == null && waitingRandomCode == code)
        {
            lock (gate)
            {
                if (waitingRandomCode == code)
                    waitingRandomCode = null;
            }
        }

        return result;
    }

    public void RemoveExpired(DateTimeOffset now)
    {
        foreach ((string code, ChessSession room) in rooms)
        {
            room.ExpireDisconnects(now);
            TimeSpan lifetime = room.IsCanceled ? CanceledRoomLifetime : InactiveRoomLifetime;
            if (now - room.LastActivityAt > lifetime)
            {
                rooms.TryRemove(new KeyValuePair<string, ChessSession>(code, room));
                if (waitingRandomCode == code)
                {
                    lock (gate)
                    {
                        if (waitingRandomCode == code)
                            waitingRandomCode = null;
                    }
                }
            }
        }
    }

    ChessSession CreateRoom(string difficulty = "medium")
    {
        string code;
        do
        {
            code = CreateCode();
        } while (rooms.ContainsKey(code));

        var room = ChessSession.New(code, difficulty);
        rooms[code] = room;
        return room;
    }

    static string CreateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> value = stackalloc char[4];
        for (int i = 0; i < value.Length; i++)
            value[i] = chars[Random.Shared.Next(chars.Length)];
        return new string(value);
    }

    static string NormalizeCode(string code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();
}

sealed class ChessSession
{
    static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromMinutes(1);
    readonly object gate = new();
    readonly ChessBoard board = new();
    readonly string difficulty;
    DateTimeOffset? whiteDisconnectedAt;
    DateTimeOffset? blackDisconnectedAt;
    TimeSpan whiteDisconnectRemaining = DisconnectGracePeriod;
    TimeSpan blackDisconnectRemaining = DisconnectGracePeriod;

    ChessSession(string code, string difficulty = "medium")
    {
        Code = code;
        this.difficulty = difficulty;
    }

    public string Code { get; }
    public string? WhitePlayerId { get; private set; }
    public string? BlackPlayerId { get; private set; }
    public string? CanceledBy { get; private set; }
    public ChessMoveEvent? LastMove { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsCanceled => CanceledBy != null;
    public bool IsWaiting => !IsCanceled && WhitePlayerId != null && BlackPlayerId == null && whiteDisconnectedAt == null;

    public static ChessSession New(string code, string difficulty = "medium") => new(code, difficulty);

    public ChessJoinResult AddPlayer()
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            UpdateDisconnectState(now, reconnectingSide: null);

            if (IsCanceled)
                return ChessJoinResult.Fail(Code, "Partida encerrada.");

            string playerId = Guid.NewGuid().ToString("N");
            string side;

            if (WhitePlayerId == null)
            {
                WhitePlayerId = playerId;
                side = "white";
            }
            else if (BlackPlayerId == null)
            {
                BlackPlayerId = playerId;
                side = "black";
            }
            else
            {
                return ChessJoinResult.Fail(Code, "Sala cheia.");
            }

            Touch(now);
            return BuildJoinResult(playerId, side, now);
        }
    }

    public ChessJoinResult ToJoinResult(string playerId)
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string? side = SideFor(playerId);
            if (side == null)
                return ChessJoinResult.Fail(Code, "Jogador nao pertence a esta sala.");

            UpdateDisconnectState(now, side);
            Touch(now);
            return BuildJoinResult(playerId, side, now);
        }
    }

    public ChessJoinResult MarkPlayerDisconnected(string playerId)
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string? side = SideFor(playerId);
            if (side == null)
                return ChessJoinResult.Fail(Code, "Jogador nao pertence a esta sala.");

            UpdateDisconnectState(now, reconnectingSide: null);
            if (!IsCanceled)
                StartDisconnect(side, now);

            Touch(now);
            return BuildJoinResult(playerId, side, now);
        }
    }

    public ChessJoinResult ApplyMove(ChessMoveAction action)
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string? side = SideFor(action.PlayerId);
            if (side == null)
                return ChessJoinResult.Fail(Code, "Jogador nao pertence a esta sala.");

            UpdateDisconnectState(now, side);

            if (IsCanceled)
                return ChessJoinResult.Fail(Code, "Partida encerrada.");

            if (!IsReady)
                return ChessJoinResult.Fail(Code, "Aguardando segundo jogador.");

            if (board.IsEndGame)
                return ChessJoinResult.Fail(Code, "Partida finalizada.");

            if (DisconnectedSide() != null)
                return ChessJoinResult.Fail(Code, "Aguardando jogador reconectar.");

            if (side != SideFromColor(board.Turn))
                return ChessJoinResult.Fail(Code, "Aguarde sua vez.");

            Chess.Move? legalMove = FindLegalMove(action);
            if (legalMove == null)
                return ChessJoinResult.Fail(Code, "Jogada invalida.");

            try
            {
                board.Move(legalMove);
            }
            catch (ChessException error)
            {
                return ChessJoinResult.Fail(Code, error.Message);
            }

            LastMove = new ChessMoveEvent(
                Guid.NewGuid().ToString("N"),
                side,
                legalMove.OriginalPosition.ToString(),
                legalMove.NewPosition.ToString(),
                legalMove.San ?? legalMove.ToString(),
                PieceText(legalMove.Piece!),
                legalMove.CapturedPiece != null,
                legalMove.IsCastling,
                legalMove.IsPromotion);

            Touch(now);
            return BuildJoinResult(action.PlayerId, side, now);
        }
    }
    public ChessJoinResult ApplyBotMove()
    {
        var moves = board.Moves(allowAmbiguousCastle: false, generateSan: true).ToList();
        var move = difficulty == "easy" ? moves[Random.Shared.Next(moves.Count)] : moves.OrderByDescending(m => m.CapturedPiece != null ? 10 : 0).ThenByDescending(m => m.Piece?.Type?.Value ?? 0).FirstOrDefault();
        return move == null ? ToJoinResult(WhitePlayerId!) : ApplyMove(new ChessMoveAction(BotId, move.OriginalPosition.ToString(), move.NewPosition.ToString(), PromotionText(move)));
    }
    string BotId => BlackPlayerId!;

    public void ExpireDisconnects(DateTimeOffset now)
    {
        lock (gate)
            UpdateDisconnectState(now, reconnectingSide: null);
    }

    bool IsReady => WhitePlayerId != null && BlackPlayerId != null;

    Chess.Move? FindLegalMove(ChessMoveAction action)
    {
        if (string.IsNullOrWhiteSpace(action.From) || string.IsNullOrWhiteSpace(action.To))
            return null;

        Chess.Position from;
        Chess.Position to;
        try
        {
            from = new Chess.Position(action.From.Trim().ToLowerInvariant());
            to = new Chess.Position(action.To.Trim().ToLowerInvariant());
        }
        catch
        {
            return null;
        }

        string? promotion = NormalizePromotion(action.Promotion);
        return board.Moves(from, allowAmbiguousCastle: false, generateSan: true)
            .FirstOrDefault(move =>
                move.NewPosition.Equals(to) &&
                PromotionText(move) == promotion);
    }

    ChessJoinResult BuildJoinResult(string playerId, string side, DateTimeOffset now) =>
        new(
            Code,
            playerId,
            side,
            !IsReady,
            null,
            ToPublicState(side, now));

    ChessPublicState ToPublicState(string playerSide, DateTimeOffset now)
    {
        (string? disconnectedSide, int? disconnectSecondsRemaining) = DisconnectSnapshot(playerSide, now);
        EndGameInfo? endGame = board.IsEndGame ? board.EndGame : null;
        string? inCheckSide = board.WhiteKingChecked
            ? "white"
            : board.BlackKingChecked ? "black" : null;

        return new ChessPublicState(
            board.ToFen(),
            SideFromColor(board.Turn),
            playerSide,
            IsReady,
            board.IsEndGame,
            endGame == null ? null : EndGameTypeText(endGame.EndgameType),
            WinnerSide(endGame),
            inCheckSide,
            IsCanceled,
            CanceledBy,
            disconnectedSide,
            disconnectSecondsRemaining,
            BuildLegalMoves(),
            LastMove);
    }

    List<ChessPublicMove> BuildLegalMoves()
    {
        if (!IsReady || IsCanceled || board.IsEndGame || DisconnectedSide() != null)
            return new List<ChessPublicMove>();

        return board.Moves(allowAmbiguousCastle: false, generateSan: true)
            .Select(move => new ChessPublicMove(
                move.OriginalPosition.ToString(),
                move.NewPosition.ToString(),
                move.San ?? move.ToString(),
                PieceText(move.Piece!),
                move.CapturedPiece != null,
                move.IsCastling,
                move.IsEnPassant,
                move.IsPromotion,
                PromotionText(move)))
            .ToList();
    }

    void UpdateDisconnectState(DateTimeOffset now, string? reconnectingSide)
    {
        if (IsCanceled)
            return;

        if (reconnectingSide == "white")
            ReconnectSide("white", now);
        else if (reconnectingSide == "black")
            ReconnectSide("black", now);

        ExpireSideIfNeeded("white", now);
        ExpireSideIfNeeded("black", now);
    }

    void StartDisconnect(string side, DateTimeOffset now)
    {
        if (DisconnectRemaining(side, now) <= TimeSpan.Zero)
        {
            CancelForDisconnect(side, now);
            return;
        }

        if (side == "white" && whiteDisconnectedAt == null)
            whiteDisconnectedAt = now;

        if (side == "black" && blackDisconnectedAt == null)
            blackDisconnectedAt = now;
    }

    void ReconnectSide(string side, DateTimeOffset now)
    {
        DateTimeOffset? disconnectedAt = DisconnectedAt(side);
        if (disconnectedAt == null)
            return;

        TimeSpan remaining = DisconnectRemaining(side, now);
        SetDisconnectRemaining(side, remaining);
        SetDisconnectedAt(side, null);

        if (remaining <= TimeSpan.Zero)
            CancelForDisconnect(side, now);
    }

    void ExpireSideIfNeeded(string side, DateTimeOffset now)
    {
        if (DisconnectedAt(side) != null && DisconnectRemaining(side, now) <= TimeSpan.Zero)
            CancelForDisconnect(side, now);
    }

    void CancelForDisconnect(string side, DateTimeOffset now)
    {
        if (CanceledBy != null)
            return;

        CanceledBy = side;
        SetDisconnectedAt(side, null);
        SetDisconnectRemaining(side, TimeSpan.Zero);
        LastActivityAt = now;
    }

    (string? Side, int? SecondsRemaining) DisconnectSnapshot(string playerSide, DateTimeOffset now)
    {
        string opponent = Opponent(playerSide);
        if (DisconnectedAt(opponent) != null)
            return (opponent, SecondsRemaining(opponent, now));

        if (DisconnectedAt(playerSide) != null)
            return (playerSide, SecondsRemaining(playerSide, now));

        return (null, null);
    }

    string? DisconnectedSide()
    {
        if (whiteDisconnectedAt != null)
            return "white";
        if (blackDisconnectedAt != null)
            return "black";
        return null;
    }

    int SecondsRemaining(string side, DateTimeOffset now) =>
        Math.Max(0, (int)Math.Ceiling(DisconnectRemaining(side, now).TotalSeconds));

    TimeSpan DisconnectRemaining(string side, DateTimeOffset now)
    {
        TimeSpan remaining = side == "white" ? whiteDisconnectRemaining : blackDisconnectRemaining;
        DateTimeOffset? disconnectedAt = DisconnectedAt(side);
        if (disconnectedAt != null)
            remaining -= now - disconnectedAt.Value;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    DateTimeOffset? DisconnectedAt(string side) =>
        side == "white" ? whiteDisconnectedAt : blackDisconnectedAt;

    void SetDisconnectedAt(string side, DateTimeOffset? value)
    {
        if (side == "white")
            whiteDisconnectedAt = value;
        else
            blackDisconnectedAt = value;
    }

    void SetDisconnectRemaining(string side, TimeSpan value)
    {
        if (side == "white")
            whiteDisconnectRemaining = value;
        else
            blackDisconnectRemaining = value;
    }

    string? SideFor(string playerId)
    {
        if (playerId == WhitePlayerId)
            return "white";
        if (playerId == BlackPlayerId)
            return "black";
        return null;
    }

    static string Opponent(string side) => side == "white" ? "black" : "white";
    static string SideFromColor(PieceColor color) => color == PieceColor.White ? "white" : "black";
    static string EndGameTypeText(EndgameType type) => type.ToString();
    static string? WinnerSide(EndGameInfo? endGame) =>
        endGame?.EndgameType == EndgameType.Checkmate ||
        endGame?.EndgameType == EndgameType.Resigned ||
        endGame?.EndgameType == EndgameType.Timeout
            ? SideFromColor(endGame.WonSide!)
            : null;
    static string PieceText(Piece piece) => $"{SideFromColor(piece.Color)}-{piece.Type.Name.ToLowerInvariant()}";
    static string? PromotionText(Chess.Move move) => move.Promotion == null ? null : move.Promotion.Type.Name.ToLowerInvariant();

    static string? NormalizePromotion(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        return value switch
        {
            null => null,
            "q" or "queen" => "queen",
            "r" or "rook" => "rook",
            "b" or "bishop" => "bishop",
            "n" or "knight" => "knight",
            _ => value
        };
    }

    void Touch(DateTimeOffset now) => LastActivityAt = now;
}

sealed record ChessJoinResult(
    string RoomCode,
    string? PlayerId,
    string? PlayerSide,
    bool Waiting,
    string? Error,
    ChessPublicState? State)
{
    public static ChessJoinResult Fail(string roomCode, string error) =>
        new(roomCode, null, null, false, error, null);
}

sealed record ChessPublicState(
    string Fen,
    string Turn,
    string PlayerSide,
    bool Ready,
    bool Ended,
    string? EndedBy,
    string? Winner,
    string? InCheckSide,
    bool Canceled,
    string? CanceledBy,
    string? DisconnectedSide,
    int? DisconnectSecondsRemaining,
    List<ChessPublicMove> LegalMoves,
    ChessMoveEvent? LastMove);

sealed record ChessPublicMove(
    string From,
    string To,
    string San,
    string Piece,
    bool Captured,
    bool Castling,
    bool EnPassant,
    bool Promotion,
    string? PromotionTo);

sealed record ChessMoveAction(string PlayerId, string From, string To, string? Promotion);
sealed record ChessLeaveAction(string PlayerId);
sealed record ChessMoveEvent(
    string Id,
    string PlayerSide,
    string From,
    string To,
    string San,
    string Piece,
    bool Captured,
    bool Castling,
    bool Promotion);
