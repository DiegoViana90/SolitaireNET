using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GameStore>();

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

app.MapPost("/api/games", (GameStore store) =>
{
    GameSession game = store.Create();
    return Results.Ok(game.ToPublicState());
});

app.MapGet("/api/games/{id}", (string id, GameStore store) =>
{
    GameSession? game = store.Get(id);
    return game == null
        ? Results.NotFound(new { error = "Game not found" })
        : Results.Ok(game.ToPublicState());
});

app.MapPost("/api/games/{id}/actions", (string id, GameAction action, GameStore store) =>
{
    GameSession? game = store.Get(id);
    if (game == null)
        return Results.NotFound(new { error = "Game not found" });

    MoveResult result = game.Apply(action);
    return result.Ok
        ? Results.Ok(game.ToPublicState())
        : Results.BadRequest(new { error = result.Error });
});

app.Run();

sealed class GameStore
{
    readonly ConcurrentDictionary<string, GameSession> games = new();

    public GameSession Create()
    {
        var game = GameSession.New();
        games[game.Id] = game;
        return game;
    }

    public GameSession? Get(string id)
    {
        return games.TryGetValue(id, out GameSession? game)
            ? game
            : null;
    }
}

sealed class GameSession
{
    readonly object gate = new();
    readonly List<Card> stock = new();
    readonly List<Card> waste = new();
    readonly List<Card>[] tableau = Enumerable.Range(0, 7).Select(_ => new List<Card>()).ToArray();
    readonly List<Card>[] foundations = Enumerable.Range(0, 4).Select(_ => new List<Card>()).ToArray();

    GameSession(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public static GameSession New()
    {
        var game = new GameSession(Guid.NewGuid().ToString("N"));
        game.Deal();
        return game;
    }

    public PublicGameState ToPublicState()
    {
        lock (gate)
        {
            return new PublicGameState(
                Id,
                stock.Count,
                waste.Count,
                waste.Count > 0 ? PublicCard.FromVisible(waste[^1]) : null,
                tableau.Select(pile => pile.Select(PublicCard.From).ToList()).ToList(),
                foundations.Select(pile => pile.Count > 0 ? PublicCard.FromVisible(pile[^1]) : null).ToList(),
                foundations.Sum(pile => pile.Count) == 52);
        }
    }

    public MoveResult Apply(GameAction action)
    {
        lock (gate)
        {
            return action.Type switch
            {
                "drawStock" => DrawStock(),
                "resetStock" => ResetStock(),
                "flipTableau" => FlipTableau(action.Source?.Index ?? -1),
                "move" => Move(action.Source, action.Target),
                _ => MoveResult.Fail("Unknown action")
            };
        }
    }

    void Deal()
    {
        List<Card> deck = new();
        foreach (string suit in new[] { "S", "H", "D", "C" })
        {
            for (int rank = 1; rank <= 13; rank++)
                deck.Add(new Card(rank, suit));
        }

        Shuffle(deck);
        stock.AddRange(deck);

        for (int col = 0; col < 7; col++)
        {
            for (int row = 0; row <= col; row++)
            {
                Card card = stock[^1];
                stock.RemoveAt(stock.Count - 1);
                card.FaceUp = row == col;
                tableau[col].Add(card);
            }
        }
    }

    static void Shuffle(List<Card> deck)
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    MoveResult DrawStock()
    {
        if (stock.Count == 0)
            return MoveResult.Fail("Stock is empty");

        Card card = stock[^1];
        stock.RemoveAt(stock.Count - 1);
        card.FaceUp = true;
        waste.Add(card);

        return MoveResult.Success();
    }

    MoveResult ResetStock()
    {
        if (stock.Count > 0)
            return MoveResult.Fail("Stock is not empty");

        if (waste.Count == 0)
            return MoveResult.Fail("Waste is empty");

        while (waste.Count > 0)
        {
            Card card = waste[^1];
            waste.RemoveAt(waste.Count - 1);
            card.FaceUp = false;
            stock.Add(card);
        }

        return MoveResult.Success();
    }

    MoveResult FlipTableau(int column)
    {
        if (column < 0 || column >= tableau.Length)
            return MoveResult.Fail("Invalid tableau column");

        List<Card> pile = tableau[column];
        if (pile.Count == 0 || pile[^1].FaceUp)
            return MoveResult.Fail("No hidden top card to flip");

        pile[^1].FaceUp = true;
        return MoveResult.Success();
    }

    MoveResult Move(PileRef? source, PileRef? target)
    {
        if (source == null || target == null)
            return MoveResult.Fail("Missing source or target");

        MoveSelection? selection = TryBuildSelection(source);
        if (selection == null)
            return MoveResult.Fail("Invalid source");

        Card first = selection.Cards[0];

        bool canMove = target.Kind switch
        {
            "foundation" => selection.Cards.Count == 1 &&
                            target.Index >= 0 &&
                            target.Index < foundations.Length &&
                            CanMoveToFoundation(first, foundations[target.Index]),

            "tableau" => target.Index >= 0 &&
                         target.Index < tableau.Length &&
                         CanMoveToTableau(first, tableau[target.Index]),

            _ => false
        };

        if (!canMove)
            return MoveResult.Fail("Move is not allowed");

        RemoveSelection(selection);
        FlipExposedTableauTop(selection.Source);

        if (target.Kind == "foundation")
            foundations[target.Index].AddRange(selection.Cards);

        if (target.Kind == "tableau")
            tableau[target.Index].AddRange(selection.Cards);

        return MoveResult.Success();
    }

    MoveSelection? TryBuildSelection(PileRef source)
    {
        if (source.Kind == "waste")
        {
            if (waste.Count == 0)
                return null;

            return new MoveSelection(source, new List<Card> { waste[^1] });
        }

        if (source.Kind == "foundation")
        {
            if (source.Index < 0 || source.Index >= foundations.Length || foundations[source.Index].Count == 0)
                return null;

            return new MoveSelection(source, new List<Card> { foundations[source.Index][^1] });
        }

        if (source.Kind == "tableau")
        {
            if (source.Index < 0 || source.Index >= tableau.Length)
                return null;

            List<Card> pile = tableau[source.Index];
            int row = source.Row ?? -1;
            if (row < 0 || row >= pile.Count || !pile[row].FaceUp)
                return null;

            return new MoveSelection(source, pile.Skip(row).ToList());
        }

        return null;
    }

    void RemoveSelection(MoveSelection selection)
    {
        PileRef source = selection.Source;

        if (source.Kind == "waste")
            waste.RemoveAt(waste.Count - 1);

        if (source.Kind == "foundation")
            foundations[source.Index].RemoveAt(foundations[source.Index].Count - 1);

        if (source.Kind == "tableau")
            tableau[source.Index].RemoveRange(source.Row!.Value, tableau[source.Index].Count - source.Row.Value);
    }

    void FlipExposedTableauTop(PileRef source)
    {
        if (source.Kind != "tableau")
            return;

        List<Card> pile = tableau[source.Index];
        if (pile.Count > 0 && !pile[^1].FaceUp)
            pile[^1].FaceUp = true;
    }

    static bool CanMoveToFoundation(Card card, List<Card> foundation)
    {
        if (foundation.Count == 0)
            return card.Rank == 1;

        Card top = foundation[^1];
        return top.Suit == card.Suit && card.Rank == top.Rank + 1;
    }

    static bool CanMoveToTableau(Card card, List<Card> pile)
    {
        if (pile.Count == 0)
            return card.Rank == 13;

        Card top = pile[^1];
        return top.FaceUp &&
               top.IsRed != card.IsRed &&
               card.Rank == top.Rank - 1;
    }

}

sealed record MoveSelection(PileRef Source, List<Card> Cards);

sealed record GameAction(string Type, PileRef? Source, PileRef? Target);

sealed record PileRef(string Kind, int Index, int? Row);

sealed record PublicGameState(
    string Id,
    int StockCount,
    int WasteCount,
    PublicCard? WasteTop,
    List<List<PublicCard>> Tableau,
    List<PublicCard?> Foundations,
    bool Won);

sealed record PublicCard(string? Id, int? Rank, string? Suit, bool FaceUp)
{
    public static PublicCard From(Card card)
    {
        return card.FaceUp
            ? FromVisible(card)
            : new PublicCard(null, null, null, false);
    }

    public static PublicCard FromVisible(Card card)
    {
        return new PublicCard(card.Id, card.Rank, card.Suit, true);
    }
}

sealed record MoveResult(bool Ok, string? Error)
{
    public static MoveResult Success() => new(true, null);
    public static MoveResult Fail(string error) => new(false, error);
}

sealed class Card
{
    public Card(int rank, string suit)
    {
        Rank = rank;
        Suit = suit;
        Id = $"{suit}{rank}";
    }

    public string Id { get; }
    public int Rank { get; }
    public string Suit { get; }
    public bool FaceUp { get; set; }
    public bool IsRed => Suit is "H" or "D";
}
