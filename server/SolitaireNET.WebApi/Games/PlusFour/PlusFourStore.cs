using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Chess;
sealed class PlusFourStore
{
    static readonly TimeSpan InactiveRoomLifetime = TimeSpan.FromMinutes(45);
    static readonly TimeSpan CanceledRoomLifetime = TimeSpan.FromMinutes(2);
    readonly object gate = new();
    readonly ConcurrentDictionary<string, PlusFourSession> rooms = new();
    string? waitingRandomCode;

    public PlusFourJoinResult CreatePrivateRoom()
    {
        PlusFourSession room = CreateRoom();
        return room.AddPlayer();
    }

    public PlusFourJoinResult JoinRoom(string code)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out PlusFourSession? room))
            return PlusFourJoinResult.Fail(code, "Sala nao encontrada.");

        return room.AddPlayer();
    }

    public PlusFourJoinResult JoinRandomRoom()
    {
        lock (gate)
        {
            if (waitingRandomCode != null &&
                rooms.TryGetValue(waitingRandomCode, out PlusFourSession? waitingRoom) &&
                waitingRoom.IsWaiting)
            {
                waitingRandomCode = null;
                return waitingRoom.AddPlayer();
            }

            PlusFourSession room = CreateRoom();
            waitingRandomCode = room.Code;
            return room.AddPlayer();
        }
    }

    public PlusFourJoinResult GetRoom(string code, string playerId)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out PlusFourSession? room))
            return PlusFourJoinResult.Fail(code, "Sala nao encontrada.");

        return room.ToJoinResult(playerId);
    }

    public PlusFourJoinResult ApplyAction(string code, PlusFourAction action)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out PlusFourSession? room))
            return PlusFourJoinResult.Fail(code, "Sala nao encontrada.");

        return room.ApplyAction(action);
    }

    public PlusFourJoinResult LeaveRoom(string code, string playerId)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out PlusFourSession? room))
            return PlusFourJoinResult.Fail(code, "Sala nao encontrada.");

        PlusFourJoinResult result = room.Leave(playerId);
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
        foreach ((string code, PlusFourSession room) in rooms)
        {
            TimeSpan lifetime = room.IsCanceled ? CanceledRoomLifetime : InactiveRoomLifetime;
            if (now - room.LastActivityAt > lifetime)
            {
                rooms.TryRemove(new KeyValuePair<string, PlusFourSession>(code, room));
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

    PlusFourSession CreateRoom()
    {
        string code;
        do
        {
            code = CreateCode();
        } while (rooms.ContainsKey(code));

        var room = PlusFourSession.New(code);
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

sealed class PlusFourSession
{
    const int InitialHandSize = 7;
    const int MatchTarget = 100;
    static readonly string[] Colors = ["red", "blue", "green", "yellow"];
    readonly object gate = new();
    readonly List<PlusFourCard> drawPile = new();
    readonly List<PlusFourCard> discardPile = new();
    readonly Dictionary<string, List<PlusFourCard>> hands = new()
    {
        ["one"] = new List<PlusFourCard>(),
        ["two"] = new List<PlusFourCard>()
    };

    PlusFourSession(string code)
    {
        Code = code;
        DealRound(startingSide: "one");
    }

    public string Code { get; }
    public string? OnePlayerId { get; private set; }
    public string? TwoPlayerId { get; private set; }
    public string Turn { get; private set; } = "one";
    public string CurrentColor { get; private set; } = "red";
    public int Round { get; private set; } = 1;
    public int OneScore { get; private set; }
    public int TwoScore { get; private set; }
    public string? RoundWinner { get; private set; }
    public string? MatchWinner { get; private set; }
    public string? CanceledBy { get; private set; }
    public PlusFourEvent? LastEvent { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsCanceled => CanceledBy != null;
    public bool IsWaiting => !IsCanceled && OnePlayerId != null && TwoPlayerId == null;
    bool IsReady => OnePlayerId != null && TwoPlayerId != null;
    bool RoundOver => RoundWinner != null;

    public static PlusFourSession New(string code) => new(code);

    public PlusFourJoinResult AddPlayer()
    {
        lock (gate)
        {
            Touch();
            if (IsCanceled)
                return PlusFourJoinResult.Fail(Code, "Sala encerrada.");

            if (OnePlayerId == null)
            {
                OnePlayerId = Guid.NewGuid().ToString("N");
                return ToJoinResult(OnePlayerId);
            }

            if (TwoPlayerId == null)
            {
                TwoPlayerId = Guid.NewGuid().ToString("N");
                return ToJoinResult(TwoPlayerId);
            }

            return PlusFourJoinResult.Fail(Code, "Sala cheia.");
        }
    }

    public PlusFourJoinResult ToJoinResult(string playerId)
    {
        lock (gate)
        {
            string? side = SideForPlayer(playerId);
            if (side == null)
                return PlusFourJoinResult.Fail(Code, "Jogador nao encontrado nesta sala.");

            return new PlusFourJoinResult(Code, playerId, side, !IsReady, null, ToPublicState(side));
        }
    }

    public PlusFourJoinResult ApplyAction(PlusFourAction action)
    {
        lock (gate)
        {
            Touch();
            string? side = SideForPlayer(action.PlayerId);
            if (side == null)
                return PlusFourJoinResult.Fail(Code, "Jogador nao encontrado nesta sala.");

            if (IsCanceled)
                return PlusFourJoinResult.Fail(Code, "Sala encerrada.");

            if (!IsReady)
                return PlusFourJoinResult.Fail(Code, "Aguardando outro jogador.");

            string type = (action.Type ?? string.Empty).Trim().ToLowerInvariant();
            if (type == "next-round")
                return StartNextRound(side);

            if (RoundOver)
                return PlusFourJoinResult.Fail(Code, "A rodada ja terminou.");

            if (side != Turn)
                return PlusFourJoinResult.Fail(Code, "Nao e sua vez.");

            return type switch
            {
                "draw" => Draw(side),
                "play" => Play(side, action.CardId, action.Color),
                _ => PlusFourJoinResult.Fail(Code, "Acao invalida.")
            };
        }
    }

    public PlusFourJoinResult Leave(string playerId)
    {
        lock (gate)
        {
            string? side = SideForPlayer(playerId);
            if (side == null)
                return PlusFourJoinResult.Fail(Code, "Jogador nao encontrado nesta sala.");

            CanceledBy = side;
            LastEvent = new PlusFourEvent(Guid.NewGuid().ToString("N"), side, "leave", null, null, null);
            Touch();
            return ToJoinResult(playerId);
        }
    }

    PlusFourJoinResult Draw(string side)
    {
        EnsureDrawPile();
        if (drawPile.Count == 0)
            return PlusFourJoinResult.Fail(Code, "Nao ha cartas para comprar.");

        PlusFourCard card = drawPile[^1];
        drawPile.RemoveAt(drawPile.Count - 1);
        hands[side].Add(card);
        Turn = Other(side);
        LastEvent = new PlusFourEvent(Guid.NewGuid().ToString("N"), side, "draw", null, null, Other(side));
        return ToJoinResult(PlayerIdForSide(side)!);
    }

    PlusFourJoinResult Play(string side, string? cardId, string? chosenColor)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return PlusFourJoinResult.Fail(Code, "Carta invalida.");

        List<PlusFourCard> hand = hands[side];
        PlusFourCard? card = hand.FirstOrDefault(item => item.Id == cardId);
        if (card == null)
            return PlusFourJoinResult.Fail(Code, "Carta nao esta na sua mao.");

        if (!CanPlay(card))
            return PlusFourJoinResult.Fail(Code, "Essa carta nao pode ser jogada agora.");

        string color = card.Color == "wild" || card.Value == "Inverte"
            ? NormalizeColor(chosenColor) ?? ""
            : card.Color;
        if (string.IsNullOrEmpty(color))
            return PlusFourJoinResult.Fail(Code, "Escolha uma cor.");

        hand.Remove(card);
        discardPile.Add(card with { PlayedColor = color });
        CurrentColor = color;

        if (hand.Count == 0)
        {
            FinishRound(side, card);
            return ToJoinResult(PlayerIdForSide(side)!);
        }

        string next = NextTurnAfter(card, side);
        Turn = next;
        LastEvent = new PlusFourEvent(Guid.NewGuid().ToString("N"), side, "play", card.ToPublic(), color, next);
        return ToJoinResult(PlayerIdForSide(side)!);
    }

    PlusFourJoinResult StartNextRound(string side)
    {
        if (!RoundOver)
            return PlusFourJoinResult.Fail(Code, "A rodada ainda nao terminou.");

        if (MatchWinner != null)
            return PlusFourJoinResult.Fail(Code, "A partida ja terminou.");

        Round++;
        DealRound(startingSide: Other(RoundWinner!));
        LastEvent = new PlusFourEvent(Guid.NewGuid().ToString("N"), side, "next-round", null, null, Turn);
        return ToJoinResult(PlayerIdForSide(side)!);
    }

    void FinishRound(string winner, PlusFourCard card)
    {
        int points = hands[Other(winner)].Sum(CardPoints);
        if (winner == "one")
            OneScore += points;
        else
            TwoScore += points;

        RoundWinner = winner;
        MatchWinner = OneScore >= MatchTarget ? "one" : TwoScore >= MatchTarget ? "two" : null;
        Turn = winner;
        LastEvent = new PlusFourEvent(Guid.NewGuid().ToString("N"), winner, "round-win", card.ToPublic(), CurrentColor, null);
    }

    void DealRound(string startingSide)
    {
        drawPile.Clear();
        discardPile.Clear();
        hands["one"].Clear();
        hands["two"].Clear();
        drawPile.AddRange(BuildDeck().OrderBy(_ => Random.Shared.Next()));

        for (int i = 0; i < InitialHandSize; i++)
        {
            hands["one"].Add(DrawRaw());
            hands["two"].Add(DrawRaw());
        }

        PlusFourCard first;
        do
        {
            first = DrawRaw();
            if (first.Color == "wild")
                drawPile.Insert(0, first);
        } while (first.Color == "wild");

        discardPile.Add(first.Value == "Inverte" ? first with { PlayedColor = first.Color } : first);
        CurrentColor = first.Color;
        Turn = startingSide;
        RoundWinner = null;
        MatchWinner = null;
    }

    PlusFourCard DrawRaw()
    {
        PlusFourCard card = drawPile[^1];
        drawPile.RemoveAt(drawPile.Count - 1);
        return card;
    }

    void EnsureDrawPile()
    {
        if (drawPile.Count > 0 || discardPile.Count <= 1)
            return;

        PlusFourCard top = discardPile[^1];
        List<PlusFourCard> rest = discardPile.Take(discardPile.Count - 1).ToList();
        discardPile.Clear();
        discardPile.Add(top);
        drawPile.AddRange(rest.OrderBy(_ => Random.Shared.Next()));
    }

    bool CanPlay(PlusFourCard card)
    {
        PlusFourCard top = discardPile[^1];
        return card.Color == "wild" ||
            card.Value == "Inverte" ||
            card.Color == CurrentColor ||
            card.Value == top.Value;
    }

    string NextTurnAfter(PlusFourCard card, string side)
    {
        string other = Other(side);
        if (card.Value == "+2")
        {
            DrawCards(other, 2);
            return side;
        }

        if (card.Value == "+4")
        {
            DrawCards(other, 4);
            return side;
        }

        if (card.Value == "Pula")
            return side;

        if (card.Value == "Inverte")
            return side;

        return other;
    }

    void DrawCards(string side, int count)
    {
        for (int i = 0; i < count; i++)
        {
            EnsureDrawPile();
            if (drawPile.Count == 0)
                return;
            hands[side].Add(DrawRaw());
        }
    }

    PlusFourPublicState ToPublicState(string viewerSide)
    {
        string opponent = Other(viewerSide);
        PlusFourCard top = discardPile[^1];
        return new PlusFourPublicState(
            Code,
            IsReady,
            IsCanceled,
            CanceledBy,
            Turn,
            CurrentColor,
            Round,
            OneScore,
            TwoScore,
            RoundWinner,
            MatchWinner,
            drawPile.Count,
            top.ToPublic(),
            hands[viewerSide].Select(card => card.ToPublic()).ToList(),
            hands[opponent].Count,
            LastEvent);
    }

    string? SideForPlayer(string? playerId)
    {
        if (playerId == OnePlayerId) return "one";
        if (playerId == TwoPlayerId) return "two";
        return null;
    }

    string? PlayerIdForSide(string side) =>
        side == "one" ? OnePlayerId : TwoPlayerId;

    static string Other(string side) => side == "one" ? "two" : "one";

    static string? NormalizeColor(string? color)
    {
        color = (color ?? string.Empty).Trim().ToLowerInvariant();
        return Colors.Contains(color) ? color : null;
    }

    static int CardPoints(PlusFourCard card) => card.Value switch
    {
        "+4" => 50,
        "Cor" => 40,
        "+2" or "Pula" or "Inverte" => 20,
        _ => int.TryParse(card.Value, out int value) ? value : 10
    };

    static List<PlusFourCard> BuildDeck()
    {
        var deck = new List<PlusFourCard>();
        foreach (string color in Colors)
        {
            deck.Add(NewCard(color, "0"));
            for (int copy = 0; copy < 2; copy++)
            {
                for (int value = 1; value <= 9; value++)
                    deck.Add(NewCard(color, value.ToString()));
                deck.Add(NewCard(color, "Pula"));
                deck.Add(NewCard(color, "+2"));
                deck.Add(NewCard(color, "Inverte"));
            }
        }

        for (int i = 0; i < 4; i++)
        {
            deck.Add(NewCard("wild", "Cor"));
            deck.Add(NewCard("wild", "+4"));
        }

        return deck;
    }

    static PlusFourCard NewCard(string color, string value) =>
        new(Guid.NewGuid().ToString("N"), color, value, null);

    void Touch() => LastActivityAt = DateTimeOffset.UtcNow;
}

sealed record PlusFourJoinResult(
    string RoomCode,
    string? PlayerId,
    string? PlayerSide,
    bool Waiting,
    string? Error,
    PlusFourPublicState? State)
{
    public static PlusFourJoinResult Fail(string roomCode, string error) =>
        new(roomCode, null, null, false, error, null);
}

sealed record PlusFourPublicState(
    string Code,
    bool Ready,
    bool Canceled,
    string? CanceledBy,
    string Turn,
    string CurrentColor,
    int Round,
    int OneScore,
    int TwoScore,
    string? RoundWinner,
    string? MatchWinner,
    int DrawCount,
    PlusFourPublicCard TopCard,
    List<PlusFourPublicCard> Hand,
    int OpponentCount,
    PlusFourEvent? LastEvent);

sealed record PlusFourCard(string Id, string Color, string Value, string? PlayedColor)
{
    public PlusFourPublicCard ToPublic() => new(Id, Color, Value, PlayedColor);
}

sealed record PlusFourPublicCard(string Id, string Color, string Value, string? PlayedColor);
sealed record PlusFourAction(string PlayerId, string Type, string? CardId, string? Color);
sealed record PlusFourLeaveAction(string PlayerId);
sealed record PlusFourEvent(string Id, string PlayerSide, string Type, PlusFourPublicCard? Card, string? Color, string? NextTurn);
