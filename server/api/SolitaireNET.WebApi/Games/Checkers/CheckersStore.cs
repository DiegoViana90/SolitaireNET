using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Chess;
sealed class CheckersStore
{
    static readonly TimeSpan InactiveRoomLifetime = TimeSpan.FromMinutes(30);
    static readonly TimeSpan CanceledRoomLifetime = TimeSpan.FromMinutes(2);
    readonly object gate = new();
    readonly ConcurrentDictionary<string, CheckersSession> rooms = new();
    string? waitingRandomCode;

    public CheckersJoinResult CreatePrivateRoom()
    {
        CheckersSession room = CreateRoom();
        return room.AddPlayer();
    }

    public CheckersJoinResult JoinRoom(string code)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out CheckersSession? room))
            return CheckersJoinResult.Fail(code, "Sala nao encontrada.");

        return room.AddPlayer();
    }

    public CheckersJoinResult JoinRandomRoom()
    {
        lock (gate)
        {
            if (waitingRandomCode != null &&
                rooms.TryGetValue(waitingRandomCode, out CheckersSession? waitingRoom) &&
                waitingRoom.IsWaiting)
            {
                waitingRandomCode = null;
                return waitingRoom.AddPlayer();
            }

            CheckersSession room = CreateRoom();
            waitingRandomCode = room.Code;
            return room.AddPlayer();
        }
    }

    public CheckersJoinResult GetRoom(string code, string playerId)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out CheckersSession? room))
            return CheckersJoinResult.Fail(code, "Sala nao encontrada.");

        return room.ToJoinResult(playerId);
    }

    public CheckersJoinResult ApplyMove(string code, CheckersMoveAction action)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out CheckersSession? room))
            return CheckersJoinResult.Fail(code, "Sala nao encontrada.");

        return room.ApplyMove(action);
    }

    public CheckersJoinResult LeaveRoom(string code, string playerId)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out CheckersSession? room))
            return CheckersJoinResult.Fail(code, "Sala nao encontrada.");

        CheckersJoinResult result = room.MarkPlayerDisconnected(playerId);
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
        foreach ((string code, CheckersSession room) in rooms)
        {
            room.ExpireDisconnects(now);
            TimeSpan lifetime = room.IsCanceled ? CanceledRoomLifetime : InactiveRoomLifetime;
            if (now - room.LastActivityAt > lifetime)
            {
                rooms.TryRemove(new KeyValuePair<string, CheckersSession>(code, room));
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

    CheckersSession CreateRoom()
    {
        string code;
        do
        {
            code = CreateCode();
        } while (rooms.ContainsKey(code));

        var room = CheckersSession.New(code);
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

sealed class CheckersSession
{
    static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromMinutes(1);
    readonly object gate = new();
    readonly CheckersPiece?[,] board = new CheckersPiece?[8, 8];
    DateTimeOffset? lightDisconnectedAt;
    DateTimeOffset? darkDisconnectedAt;
    TimeSpan lightDisconnectRemaining = DisconnectGracePeriod;
    TimeSpan darkDisconnectRemaining = DisconnectGracePeriod;

    CheckersSession(string code)
    {
        Code = code;
        Deal();
    }

    public string Code { get; }
    public string? LightPlayerId { get; private set; }
    public string? DarkPlayerId { get; private set; }
    public string Turn { get; private set; } = "light";
    public string? ForcedPieceId { get; private set; }
    public string? Winner { get; private set; }
    public string? CanceledBy { get; private set; }
    public CheckersMoveEvent? LastMove { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsCanceled => CanceledBy != null;
    public bool IsWaiting => !IsCanceled && LightPlayerId != null && DarkPlayerId == null && lightDisconnectedAt == null;

    public static CheckersSession New(string code) => new(code);

    public CheckersJoinResult AddPlayer()
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            UpdateDisconnectState(now, reconnectingSide: null);

            if (IsCanceled)
                return CheckersJoinResult.Fail(Code, "Partida encerrada.");

            string playerId = Guid.NewGuid().ToString("N");
            string side;

            if (LightPlayerId == null)
            {
                LightPlayerId = playerId;
                side = "light";
            }
            else if (DarkPlayerId == null)
            {
                DarkPlayerId = playerId;
                side = "dark";
            }
            else
            {
                return CheckersJoinResult.Fail(Code, "Sala cheia.");
            }

            Touch(now);
            return BuildJoinResult(playerId, side, now);
        }
    }

    public CheckersJoinResult ToJoinResult(string playerId)
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string? side = SideFor(playerId);
            if (side == null)
                return CheckersJoinResult.Fail(Code, "Jogador nao pertence a esta sala.");

            UpdateDisconnectState(now, side);
            Touch(now);
            return BuildJoinResult(playerId, side, now);
        }
    }

    public CheckersJoinResult MarkPlayerDisconnected(string playerId)
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string? side = SideFor(playerId);
            if (side == null)
                return CheckersJoinResult.Fail(Code, "Jogador nao pertence a esta sala.");

            UpdateDisconnectState(now, reconnectingSide: null);
            if (!IsCanceled)
                StartDisconnect(side, now);

            Touch(now);
            return BuildJoinResult(playerId, side, now);
        }
    }

    public CheckersJoinResult ApplyMove(CheckersMoveAction action)
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string? side = SideFor(action.PlayerId);
            if (side == null)
                return CheckersJoinResult.Fail(Code, "Jogador nao pertence a esta sala.");

            UpdateDisconnectState(now, side);

            if (IsCanceled)
                return CheckersJoinResult.Fail(Code, "Partida encerrada.");

            if (!IsReady)
                return CheckersJoinResult.Fail(Code, "Aguardando segundo jogador.");

            if (Winner != null)
                return CheckersJoinResult.Fail(Code, "Partida finalizada.");

            if (DisconnectedSide() != null)
                return CheckersJoinResult.Fail(Code, "Aguardando jogador reconectar.");

            if (side != Turn)
                return CheckersJoinResult.Fail(Code, "Aguarde sua vez.");

            CheckersPiece? piece = PieceAt(action.From.Row, action.From.Col);
            if (piece == null || piece.Owner != side)
                return CheckersJoinResult.Fail(Code, "Peca de origem invalida.");

            if (ForcedPieceId != null && piece.Id != ForcedPieceId)
                return CheckersJoinResult.Fail(Code, "Continue a captura com a mesma peca.");

            List<CheckersMove> legalMoves = GetMovesByPiece(side)
                .SelectMany(pair => pair.Value)
                .ToList();

            CheckersMove? move = legalMoves.FirstOrDefault(candidate =>
                candidate.From.Row == action.From.Row &&
                candidate.From.Col == action.From.Col &&
                candidate.To.Row == action.To.Row &&
                candidate.To.Col == action.To.Col);

            if (move == null)
                return CheckersJoinResult.Fail(Code, "Jogada invalida.");

            ApplyLegalMove(piece, move, side);
            Touch(now);
            return BuildJoinResult(action.PlayerId, side, now);
        }
    }

    public void ExpireDisconnects(DateTimeOffset now)
    {
        lock (gate)
            UpdateDisconnectState(now, reconnectingSide: null);
    }

    bool IsReady => LightPlayerId != null && DarkPlayerId != null;

    void Deal()
    {
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (IsDarkSquare(row, col))
                    board[row, col] = new CheckersPiece($"dark-{row}-{col}", "dark", false);
            }
        }

        for (int row = 5; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (IsDarkSquare(row, col))
                    board[row, col] = new CheckersPiece($"light-{row}-{col}", "light", false);
            }
        }
    }

    void ApplyLegalMove(CheckersPiece piece, CheckersMove move, string side)
    {
        board[move.From.Row, move.From.Col] = null;
        board[move.To.Row, move.To.Col] = piece;

        if (move.Captured != null)
            board[move.Captured.Row, move.Captured.Col] = null;

        PromoteIfNeeded(piece, move.To.Row);
        LastMove = new CheckersMoveEvent(
            Guid.NewGuid().ToString("N"),
            side,
            move.From,
            move.To,
            move.Captured,
            new CheckersPublicPiece(piece.Id, piece.Owner, piece.King));

        bool canContinueCapture =
            move.Captured != null &&
            GetMovesForPiece(move.To.Row, move.To.Col, onlyCaptures: true).Count > 0;

        if (canContinueCapture)
        {
            ForcedPieceId = piece.Id;
            return;
        }

        ForcedPieceId = null;
        Turn = Opponent(Turn);
        Winner = ResolveWinner();
    }

    void PromoteIfNeeded(CheckersPiece piece, int row)
    {
        if (piece.King)
            return;

        if ((piece.Owner == "light" && row == 0) ||
            (piece.Owner == "dark" && row == 7))
        {
            piece.King = true;
        }
    }

    Dictionary<string, List<CheckersMove>> GetMovesByPiece(string owner)
    {
        Dictionary<string, List<CheckersMove>> captures = new();
        Dictionary<string, List<CheckersMove>> regular = new();

        ForEachPiece((piece, row, col) =>
        {
            if (piece.Owner != owner)
                return;

            if (ForcedPieceId != null && piece.Id != ForcedPieceId)
                return;

            List<CheckersMove> captureMoves = GetMovesForPiece(row, col, onlyCaptures: true);
            if (captureMoves.Count > 0)
            {
                captures[piece.Id] = captureMoves;
                return;
            }

            List<CheckersMove> moves = GetMovesForPiece(row, col, onlyCaptures: false);
            if (moves.Count > 0)
                regular[piece.Id] = moves;
        });

        return captures.Count > 0 ? captures : regular;
    }

    List<CheckersMove> GetMovesForPiece(int row, int col, bool onlyCaptures)
    {
        CheckersPiece? piece = PieceAt(row, col);
        if (piece == null)
            return new List<CheckersMove>();

        List<CheckersMove> moves = new();
        IEnumerable<(int Row, int Col)> directions = onlyCaptures
            ? CaptureDirections(piece)
            : MoveDirections(piece);

        foreach ((int dr, int dc) in directions)
        {
            int stepRow = row + dr;
            int stepCol = col + dc;
            int jumpRow = row + dr * 2;
            int jumpCol = col + dc * 2;
            CheckersPiece? stepPiece = PieceAt(stepRow, stepCol);

            if (stepPiece != null &&
                stepPiece.Owner != piece.Owner &&
                IsInside(jumpRow, jumpCol) &&
                PieceAt(jumpRow, jumpCol) == null)
            {
                moves.Add(new CheckersMove(
                    new CheckersPosition(row, col),
                    new CheckersPosition(jumpRow, jumpCol),
                    new CheckersPosition(stepRow, stepCol)));
                continue;
            }

            if (!onlyCaptures && IsInside(stepRow, stepCol) && stepPiece == null)
            {
                moves.Add(new CheckersMove(
                    new CheckersPosition(row, col),
                    new CheckersPosition(stepRow, stepCol),
                    null));
            }
        }

        return moves;
    }

    string? ResolveWinner()
    {
        int light = 0;
        int dark = 0;
        ForEachPiece((piece, _, _) =>
        {
            if (piece.Owner == "light")
                light++;
            else
                dark++;
        });

        if (light == 0)
            return "dark";
        if (dark == 0)
            return "light";
        if (GetMovesByPiece(Turn).Count == 0)
            return Opponent(Turn);

        return null;
    }

    CheckersJoinResult BuildJoinResult(string playerId, string side, DateTimeOffset now) =>
        new(
            Code,
            playerId,
            side,
            !IsReady,
            null,
            ToPublicState(side, now));

    CheckersPublicState ToPublicState(string playerSide, DateTimeOffset now)
    {
        List<List<CheckersPublicPiece?>> rows = new();
        for (int row = 0; row < 8; row++)
        {
            List<CheckersPublicPiece?> current = new();
            for (int col = 0; col < 8; col++)
            {
                CheckersPiece? piece = board[row, col];
                current.Add(piece == null
                    ? null
                    : new CheckersPublicPiece(piece.Id, piece.Owner, piece.King));
            }
            rows.Add(current);
        }

        (string? disconnectedSide, int? disconnectSecondsRemaining) = DisconnectSnapshot(playerSide, now);

        return new CheckersPublicState(
            rows,
            Turn,
            playerSide,
            IsReady,
            ForcedPieceId,
            Winner,
            IsCanceled,
            CanceledBy,
            disconnectedSide,
            disconnectSecondsRemaining,
            LastMove);
    }

    void UpdateDisconnectState(DateTimeOffset now, string? reconnectingSide)
    {
        if (IsCanceled)
            return;

        if (reconnectingSide == "light")
            ReconnectSide("light", now);
        else if (reconnectingSide == "dark")
            ReconnectSide("dark", now);

        ExpireSideIfNeeded("light", now);
        ExpireSideIfNeeded("dark", now);
    }

    void StartDisconnect(string side, DateTimeOffset now)
    {
        if (DisconnectRemaining(side, now) <= TimeSpan.Zero)
        {
            CancelForDisconnect(side, now);
            return;
        }

        if (side == "light" && lightDisconnectedAt == null)
            lightDisconnectedAt = now;

        if (side == "dark" && darkDisconnectedAt == null)
            darkDisconnectedAt = now;
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
        ForcedPieceId = null;
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
        if (lightDisconnectedAt != null)
            return "light";
        if (darkDisconnectedAt != null)
            return "dark";
        return null;
    }

    int SecondsRemaining(string side, DateTimeOffset now) =>
        Math.Max(0, (int)Math.Ceiling(DisconnectRemaining(side, now).TotalSeconds));

    TimeSpan DisconnectRemaining(string side, DateTimeOffset now)
    {
        TimeSpan remaining = side == "light" ? lightDisconnectRemaining : darkDisconnectRemaining;
        DateTimeOffset? disconnectedAt = DisconnectedAt(side);
        if (disconnectedAt != null)
            remaining -= now - disconnectedAt.Value;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    DateTimeOffset? DisconnectedAt(string side) =>
        side == "light" ? lightDisconnectedAt : darkDisconnectedAt;

    void SetDisconnectedAt(string side, DateTimeOffset? value)
    {
        if (side == "light")
            lightDisconnectedAt = value;
        else
            darkDisconnectedAt = value;
    }

    void SetDisconnectRemaining(string side, TimeSpan value)
    {
        if (side == "light")
            lightDisconnectRemaining = value;
        else
            darkDisconnectRemaining = value;
    }

    string? SideFor(string playerId)
    {
        if (playerId == LightPlayerId)
            return "light";
        if (playerId == DarkPlayerId)
            return "dark";
        return null;
    }

    CheckersPiece? PieceAt(int row, int col) =>
        IsInside(row, col) ? board[row, col] : null;

    void ForEachPiece(Action<CheckersPiece, int, int> callback)
    {
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
                if (board[row, col] is { } piece)
                    callback(piece, row, col);
    }

    static IEnumerable<(int Row, int Col)> MoveDirections(CheckersPiece piece)
    {
        if (piece.King)
            return AllDirections;

        int dr = piece.Owner == "light" ? -1 : 1;
        return new[] { (dr, 1), (dr, -1) };
    }

    static IEnumerable<(int Row, int Col)> CaptureDirections(CheckersPiece piece) =>
        piece.King ? AllDirections : AllDirections;

    static readonly (int Row, int Col)[] AllDirections =
    {
        (1, 1),
        (1, -1),
        (-1, 1),
        (-1, -1)
    };

    static string Opponent(string owner) => owner == "light" ? "dark" : "light";
    static bool IsInside(int row, int col) => row >= 0 && row < 8 && col >= 0 && col < 8;
    static bool IsDarkSquare(int row, int col) => (row + col) % 2 == 1;

    void Touch(DateTimeOffset now) => LastActivityAt = now;
}

sealed record CheckersJoinResult(
    string RoomCode,
    string? PlayerId,
    string? PlayerSide,
    bool Waiting,
    string? Error,
    CheckersPublicState? State)
{
    public static CheckersJoinResult Fail(string roomCode, string error) =>
        new(roomCode, null, null, false, error, null);
}

sealed record CheckersPublicState(
    List<List<CheckersPublicPiece?>> Board,
    string Turn,
    string PlayerSide,
    bool Ready,
    string? ForcedPieceId,
    string? Winner,
    bool Canceled,
    string? CanceledBy,
    string? DisconnectedSide,
    int? DisconnectSecondsRemaining,
    CheckersMoveEvent? LastMove);

sealed record CheckersPublicPiece(string Id, string Owner, bool King);
sealed record CheckersMoveAction(string PlayerId, CheckersPosition From, CheckersPosition To);
sealed record CheckersLeaveAction(string PlayerId);
sealed record CheckersMoveEvent(
    string Id,
    string PlayerSide,
    CheckersPosition From,
    CheckersPosition To,
    CheckersPosition? Captured,
    CheckersPublicPiece Piece);
sealed record CheckersMove(CheckersPosition From, CheckersPosition To, CheckersPosition? Captured);
sealed record CheckersPosition(int Row, int Col);

sealed class CheckersPiece(string id, string owner, bool king)
{
    public string Id { get; } = id;
    public string Owner { get; } = owner;
    public bool King { get; set; } = king;
}
