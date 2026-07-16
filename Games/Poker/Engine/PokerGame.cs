namespace SolitaireNET;

public enum PokerStreet
{
    Waiting,
    PreFlop,
    Flop,
    Turn,
    River,
    Showdown,
    Finished
}

public sealed class PokerGame
{
    public const int SmallBlindValue = 2;
    public const int BigBlindValue = 5;

    readonly Random random = new();
    readonly List<PokerCard> deck = new();

    int dealerIndex = -1;
    int currentPlayerIndex;
    int currentBet;
    int minimumRaise = BigBlindValue;

    public List<PokerPlayer> Players { get; } = new()
    {
        new PokerPlayer("Você", true),
        new PokerPlayer("IA 1", false),
        new PokerPlayer("IA 2", false),
        new PokerPlayer("IA 3", false),
        new PokerPlayer("IA 4", false)
    };

    public List<PokerCard> CommunityCards { get; } = new();

    public PokerStreet Street { get; private set; } =
        PokerStreet.Waiting;

    public int Pot { get; private set; }

    public int DisplayPot =>
        Pot + Players.Sum(p => p.CurrentBet);

    public int DealerIndex { get; private set; } = -1;
    public int SmallBlindIndex { get; private set; } = -1;
    public int BigBlindIndex { get; private set; } = -1;

    public string Status { get; private set; } =
        "Clique em Iniciar mão.";

    public PokerPlayer CurrentPlayer =>
        Players[currentPlayerIndex];

    public bool IsHumanTurn =>
        Street is PokerStreet.PreFlop
            or PokerStreet.Flop
            or PokerStreet.Turn
            or PokerStreet.River
        &&
        CurrentPlayer.IsHuman
        &&
        CurrentPlayer.CanAct;

    public int HumanCallAmount =>
        Math.Max(0, currentBet - Players[0].CurrentBet);

    public bool HumanCanCheck =>
        HumanCallAmount == 0;

    public bool HandFinished =>
        Street == PokerStreet.Finished;

    public event Action? StateChanged;

    public async Task StartHandAsync()
    {
        if (Players.Count(p => p.Chips > 0) < 2)
        {
            Status =
                "A partida acabou. Reinicie para restaurar as fichas.";

            Street = PokerStreet.Finished;
            Notify();
            return;
        }

        ResetHand();
        BuildDeck();

        dealerIndex = NextSeatWithChips(dealerIndex);

        DealerIndex = dealerIndex;
        SmallBlindIndex = NextSeatWithChips(DealerIndex);
        BigBlindIndex = NextSeatWithChips(SmallBlindIndex);

        Players[DealerIndex].IsDealer = true;
        Players[SmallBlindIndex].IsSmallBlind = true;
        Players[BigBlindIndex].IsBigBlind = true;

        PostBlind(SmallBlindIndex, SmallBlindValue);
        PostBlind(BigBlindIndex, BigBlindValue);

        currentBet = Players.Max(p => p.CurrentBet);
        minimumRaise = BigBlindValue;

        Street = PokerStreet.PreFlop;

        Status =
            $"Dealer: {Players[DealerIndex].Name} | " +
            $"SB: {Players[SmallBlindIndex].Name} | " +
            $"BB: {Players[BigBlindIndex].Name}";

        Notify();

        await Task.Delay(1100);
        await DealHoleCardsAsync();

        currentPlayerIndex = NextPlayerInHand(BigBlindIndex);

        Status =
            $"Pré-flop. A ação começa em {Players[currentPlayerIndex].Name}.";

        Notify();

        await ProcessGameAsync();
    }

    public void ResetMatch()
    {
        foreach (var player in Players)
            player.Chips = 1000;

        dealerIndex = -1;
        Street = PokerStreet.Waiting;
        Pot = 0;
        CommunityCards.Clear();

        Status = "Partida reiniciada. Clique em Iniciar mão.";

        foreach (var player in Players)
        {
            player.HoleCards.Clear();
            player.CurrentBet = 0;
            player.Folded = false;
            player.AllIn = false;
            player.Acted = false;
            player.IsDealer = false;
            player.IsSmallBlind = false;
            player.IsBigBlind = false;
        }

        Notify();
    }

    public async Task HumanFoldAsync()
    {
        if (!IsHumanTurn)
            return;

        var human = Players[0];

        human.Folded = true;
        human.Acted = true;

        Status = "Você desistiu.";

        Notify();

        currentPlayerIndex =
            NextPlayerInHand(currentPlayerIndex);

        await ProcessGameAsync();
    }

    public async Task HumanCheckOrCallAsync()
    {
        if (!IsHumanTurn)
            return;

        var human = Players[0];

        int amountToCall =
            Math.Max(0, currentBet - human.CurrentBet);

        int paid =
            Math.Min(amountToCall, human.Chips);

        CommitChips(human, paid);
        human.Acted = true;

        Status = paid == 0
            ? "Você pediu mesa."
            : $"Você pagou {paid}.";

        Notify();

        currentPlayerIndex =
            NextPlayerInHand(currentPlayerIndex);

        await ProcessGameAsync();
    }

    public async Task HumanRaiseAsync(int raiseAmount)
    {
        if (!IsHumanTurn)
            return;

        var human = Players[0];

        int callAmount =
            Math.Max(0, currentBet - human.CurrentBet);

        if (human.Chips <= callAmount)
        {
            await HumanCheckOrCallAsync();
            return;
        }

        int requestedRaise =
            Math.Max(minimumRaise, raiseAmount);

        int totalToPay =
            Math.Min(
                human.Chips,
                callAmount + requestedRaise);

        int previousCurrentBet = currentBet;

        CommitChips(human, totalToPay);

        if (human.CurrentBet > previousCurrentBet)
        {
            minimumRaise =
                Math.Max(
                    BigBlindValue,
                    human.CurrentBet - previousCurrentBet);

            currentBet = human.CurrentBet;

            ResetActedAfterRaise(human);

            Status = human.AllIn
                ? $"Você foi all-in em {human.CurrentBet}."
                : $"Você aumentou para {human.CurrentBet}.";
        }
        else
        {
            Status = $"Você pagou {totalToPay}.";
        }

        human.Acted = true;

        Notify();

        currentPlayerIndex =
            NextPlayerInHand(currentPlayerIndex);

        await ProcessGameAsync();
    }

    async Task ProcessGameAsync()
    {
        int guard = 0;

        while (guard++ < 300)
        {
            var contenders =
                Players
                    .Where(p => p.IsInHand)
                    .ToList();

            if (contenders.Count == 1)
            {
                AwardPot(
                    contenders[0],
                    $"{contenders[0].Name} venceu porque todos desistiram.");

                return;
            }

            if (BettingRoundComplete())
            {
                CollectCurrentBets();

                if (Street == PokerStreet.River)
                {
                    await ShowdownAsync();
                    return;
                }

                if (Players.Count(p => p.CanAct) <= 1)
                {
                    await RunBoardToRiverAsync();
                    await ShowdownAsync();
                    return;
                }

                await AdvanceStreetAsync();
                continue;
            }

            var player = CurrentPlayer;

            if (!player.CanAct)
            {
                currentPlayerIndex =
                    NextPlayerInHand(currentPlayerIndex);

                continue;
            }

            if (player.IsHuman)
            {
                Status = HumanCallAmount == 0
                    ? "Sua vez. Você pode pedir mesa."
                    : $"Sua vez. Pague {HumanCallAmount} ou desista.";

                Notify();
                return;
            }

            await ExecuteAiActionAsync(player);

            currentPlayerIndex =
                NextPlayerInHand(currentPlayerIndex);
        }
    }

    async Task ExecuteAiActionAsync(PokerPlayer player)
    {
        Status = $"{player.Name} está pensando...";
        Notify();

        await Task.Delay(random.Next(1200, 1900));

        int callAmount =
            Math.Max(0, currentBet - player.CurrentBet);

        double strength =
            EstimateStrength(player);

        double pressure =
            player.Chips <= 0
                ? 1
                : (double)callAmount / Math.Max(1, player.Chips);

        double foldChance = 0;

        if (callAmount > 0)
        {
            if (strength < 0.22)
                foldChance = 0.38;
            else if (strength < 0.34)
                foldChance = 0.24;
            else if (strength < 0.46)
                foldChance = 0.10;

            // Quanto maior a aposta em relação às fichas,
            // maior a chance de desistir.
            foldChance += pressure switch
            {
                > 0.45 => 0.40,
                > 0.25 => 0.25,
                > 0.12 => 0.12,
                _ => 0
            };

            // Às vezes a IA paga mesmo sem ter jogo.
            // Isso evita que todo aumento cause fold automático.
            if (random.NextDouble() < 0.12)
                foldChance = 0;

            foldChance = Math.Clamp(foldChance, 0, 0.90);
        }

        bool shouldFold =
            callAmount > 0 &&
            random.NextDouble() < foldChance;

        if (shouldFold)
        {
            player.Folded = true;
            player.Acted = true;

            Status = $"{player.Name} desistiu.";
            Notify();

            await Task.Delay(900);
            return;
        }

        bool canRaise =
            player.Chips > callAmount + minimumRaise;

        bool bluff =
            canRaise &&
            strength < 0.45 &&
            random.NextDouble() < 0.06;

        bool shouldRaise =
            canRaise &&
            (
                strength > 0.72 &&
                random.NextDouble() < 0.65
                ||
                strength > 0.53 &&
                random.NextDouble() < 0.22
                ||
                bluff
            );

        if (shouldRaise)
        {
            int raiseAmount =
                minimumRaise * random.Next(1, 4);

            int totalToPay =
                Math.Min(
                    player.Chips,
                    callAmount + raiseAmount);

            int previousCurrentBet = currentBet;

            CommitChips(player, totalToPay);

            if (player.CurrentBet > previousCurrentBet)
            {
                minimumRaise =
                    Math.Max(
                        BigBlindValue,
                        player.CurrentBet - previousCurrentBet);

                currentBet = player.CurrentBet;

                ResetActedAfterRaise(player);
            }

            player.Acted = true;

            Status = player.AllIn
                ? $"{player.Name} foi all-in em {player.CurrentBet}."
                : $"{player.Name} aumentou para {player.CurrentBet}.";

            Notify();

            await Task.Delay(1100);
            return;
        }

        int paid =
            Math.Min(callAmount, player.Chips);

        CommitChips(player, paid);
        player.Acted = true;

        Status = paid == 0
            ? $"{player.Name} pediu mesa."
            : $"{player.Name} pagou {paid}.";

        Notify();

        await Task.Delay(1000);
    }

    double EstimateStrength(PokerPlayer player)
    {
        if (CommunityCards.Count == 0)
        {
            var first = player.HoleCards[0];
            var second = player.HoleCards[1];

            bool pair =
                first.Rank == second.Rank;

            bool suited =
                first.Suit == second.Suit;

            int high =
                Math.Max(first.Rank, second.Rank);

            int low =
                Math.Min(first.Rank, second.Rank);

            double strength =
                (high + low) / 30.0;

            if (pair)
                strength += 0.28 + high / 55.0;

            if (suited)
                strength += 0.07;

            if (Math.Abs(high - low) <= 2)
                strength += 0.05;

            if (high >= 13)
                strength += 0.08;

            return Math.Clamp(strength, 0, 1);
        }

        var knownCards =
            player.HoleCards
                .Concat(CommunityCards)
                .ToList();

        if (knownCards.Count >= 5)
        {
            var result =
                PokerHandEvaluator.Evaluate(knownCards);

            long divisor = 1;

            for (int i = 0; i < 5; i++)
                divisor *= 15;

            int category =
                (int)(result.Score / divisor);

            double strength =
                category / 8.0;

            strength +=
                player.HoleCards.Max(c => c.Rank) / 110.0;

            return Math.Clamp(strength, 0, 1);
        }

        return 0.45;
    }

    async Task AdvanceStreetAsync()
    {
        ResetBettingRound();

        switch (Street)
        {
            case PokerStreet.PreFlop:
                BurnCard();

                await DealCommunityCardAsync();
                await DealCommunityCardAsync();
                await DealCommunityCardAsync();

                Street = PokerStreet.Flop;
                Status = "Flop aberto.";
                break;

            case PokerStreet.Flop:
                BurnCard();

                await DealCommunityCardAsync();

                Street = PokerStreet.Turn;
                Status = "Turn aberto.";
                break;

            case PokerStreet.Turn:
                BurnCard();

                await DealCommunityCardAsync();

                Street = PokerStreet.River;
                Status = "River aberto.";
                break;
        }

        currentPlayerIndex =
            NextPlayerInHand(DealerIndex);

        Notify();

        await Task.Delay(900);
    }

    async Task RunBoardToRiverAsync()
    {
        while (CommunityCards.Count < 5)
        {
            BurnCard();

            if (CommunityCards.Count == 0)
            {
                await DealCommunityCardAsync();
                await DealCommunityCardAsync();
                await DealCommunityCardAsync();

                Street = PokerStreet.Flop;
            }
            else if (CommunityCards.Count == 3)
            {
                await DealCommunityCardAsync();
                Street = PokerStreet.Turn;
            }
            else
            {
                await DealCommunityCardAsync();
                Street = PokerStreet.River;
            }
        }
    }

    async Task DealCommunityCardAsync()
    {
        CommunityCards.Add(DrawCard());

        Notify();

        await Task.Delay(1000);
    }

    async Task ShowdownAsync()
    {
        CollectCurrentBets();

        Street = PokerStreet.Showdown;

        Status = "Showdown! Revelando cartas...";
        Notify();

        await Task.Delay(1400);

        var results =
            Players
                .Where(p => p.IsInHand)
                .Select(p => new
                {
                    Player = p,
                    Result = PokerHandEvaluator.Evaluate(
                        p.HoleCards
                            .Concat(CommunityCards)
                            .ToList())
                })
                .OrderByDescending(x => x.Result.Score)
                .ToList();

        long winningScore =
            results[0].Result.Score;

        var winners =
            results
                .Where(x => x.Result.Score == winningScore)
                .ToList();

        int share =
            Pot / winners.Count;

        int remainder =
            Pot % winners.Count;

        foreach (var winner in winners)
            winner.Player.Chips += share;

        winners[0].Player.Chips += remainder;

        string winnerNames =
            string.Join(
                ", ",
                winners.Select(w => w.Player.Name));

        string handName =
            winners[0].Result.Name;

        Pot = 0;
        Street = PokerStreet.Finished;

        Status = winners.Count == 1
            ? $"{winnerNames} venceu com {handName}."
            : $"Empate entre {winnerNames} com {handName}.";

        Notify();
    }

    void AwardPot(
        PokerPlayer winner,
        string message)
    {
        CollectCurrentBets();

        winner.Chips += Pot;

        Pot = 0;
        Street = PokerStreet.Finished;
        Status = message;

        Notify();
    }

    bool BettingRoundComplete()
    {
        var playersWhoCanAct =
            Players
                .Where(p => p.CanAct)
                .ToList();

        if (playersWhoCanAct.Count == 0)
            return true;

        return playersWhoCanAct.All(
            p =>
                p.Acted &&
                p.CurrentBet == currentBet);
    }

    void ResetBettingRound()
    {
        foreach (var player in Players)
        {
            player.CurrentBet = 0;
            player.Acted = false;
        }

        currentBet = 0;
        minimumRaise = BigBlindValue;
    }

    void ResetActedAfterRaise(PokerPlayer raiser)
    {
        foreach (var player in Players)
        {
            if (!ReferenceEquals(player, raiser) &&
                player.IsInHand &&
                !player.AllIn)
            {
                player.Acted = false;
            }
        }
    }

    async Task DealHoleCardsAsync()
    {
        int activePlayers =
            Players.Count(p => p.Chips > 0);

        for (int round = 0; round < 2; round++)
        {
            int currentIndex =
                SmallBlindIndex;

            for (int dealt = 0;
                 dealt < activePlayers;
                 dealt++)
            {
                var player =
                    Players[currentIndex];

                player.HoleCards.Add(DrawCard());

                Status =
                    $"Dando carta para {player.Name} " +
                    $"({round + 1}ª volta)...";

                Notify();

                await Task.Delay(500);

                currentIndex =
                    NextSeatWithChips(currentIndex);
            }
        }
    }

    void ResetHand()
    {
        Pot = 0;
        CommunityCards.Clear();

        foreach (var player in Players)
        {
            player.HoleCards.Clear();
            player.CurrentBet = 0;
            player.Folded = player.Chips <= 0;
            player.AllIn = false;
            player.Acted = false;

            player.IsDealer = false;
            player.IsSmallBlind = false;
            player.IsBigBlind = false;
        }
    }

    void BuildDeck()
    {
        deck.Clear();

        string[] suits =
        {
            "S",
            "H",
            "D",
            "C"
        };

        foreach (string suit in suits)
        {
            for (int rank = 2; rank <= 14; rank++)
                deck.Add(new PokerCard(rank, suit));
        }

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int other =
                random.Next(i + 1);

            (deck[i], deck[other]) =
                (deck[other], deck[i]);
        }
    }

    PokerCard DrawCard()
    {
        var card = deck[^1];

        deck.RemoveAt(deck.Count - 1);

        return card;
    }

    void BurnCard()
    {
        if (deck.Count > 0)
            deck.RemoveAt(deck.Count - 1);
    }

    void PostBlind(
        int playerIndex,
        int value)
    {
        var player =
            Players[playerIndex];

        int paid =
            Math.Min(value, player.Chips);

        CommitChips(player, paid);
    }

    void CommitChips(
        PokerPlayer player,
        int amount)
    {
        amount =
            Math.Clamp(
                amount,
                0,
                player.Chips);

        player.Chips -= amount;
        player.CurrentBet += amount;

        if (player.Chips == 0)
            player.AllIn = true;
    }

    void CollectCurrentBets()
    {
        foreach (var player in Players)
        {
            Pot += player.CurrentBet;
            player.CurrentBet = 0;
        }
    }

    int NextSeatWithChips(int currentIndex)
    {
        int index = currentIndex;

        for (int attempt = 0;
             attempt < Players.Count;
             attempt++)
        {
            index =
                (index + 1) % Players.Count;

            if (Players[index].Chips > 0)
                return index;
        }

        return currentIndex;
    }

    int NextPlayerInHand(int currentIndex)
    {
        int index = currentIndex;

        for (int attempt = 0;
             attempt < Players.Count;
             attempt++)
        {
            index =
                (index + 1) % Players.Count;

            if (Players[index].IsInHand)
                return index;
        }

        return currentIndex;
    }

    void Notify()
    {
        StateChanged?.Invoke();
    }
}



