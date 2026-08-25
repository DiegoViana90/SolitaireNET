namespace SolitaireNET;

public enum BlackjackRoundState
{
    WaitingBet,
    Dealing,
    PlayerTurn,
    DealerTurn,
    Finished
}

public sealed class BlackjackGame
{
    public const int DeckCount = 6;
    public const int CardsPerDeck = 52;
    public const int TotalShoeCards = DeckCount * CardsPerDeck;

    // Ao chegar a 25% ou menos, troca o shoe antes da próxima mão.
    const int ShuffleThreshold = TotalShoeCards / 4;

    readonly Random random = new();
    readonly List<BlackjackCard> shoe = new();

    bool shoeNeedsShuffle;

    public List<BlackjackCard> PlayerCards { get; } = new();
    public List<BlackjackCard> DealerCards { get; } = new();

    public int Chips { get; private set; } = 1000;
    public int Bet { get; private set; } = 25;

    public int CardsRemaining => shoe.Count;

    public string ShoeText =>
        $"Shoe: {CardsRemaining:N0} / {TotalShoeCards:N0}";

    public BlackjackRoundState State { get; private set; } =
        BlackjackRoundState.WaitingBet;

    public string Status { get; private set; } =
        "Escolha sua aposta e inicie a rodada.";

    public bool HideDealerSecondCard =>
        State is BlackjackRoundState.Dealing
            or BlackjackRoundState.PlayerTurn;

    public int PlayerScore =>
        CalculateScore(PlayerCards);

    public int DealerScore =>
        CalculateScore(DealerCards);

    public string PlayerScoreText =>
        FormatScore(PlayerCards);

    public string DealerScoreText =>
        FormatScore(DealerCards);

    public string VisibleDealerScoreText
    {
        get
        {
            if (DealerCards.Count == 0)
                return "0";

            if (!HideDealerSecondCard)
                return DealerScoreText;

            return FormatScore(
                DealerCards.Take(1));
        }
    }

    public bool PlayerHasBlackjack =>
        PlayerCards.Count == 2 &&
        PlayerScore == 21;

    public bool DealerHasBlackjack =>
        DealerCards.Count == 2 &&
        DealerScore == 21;

    public bool CanChangeBet =>
        State is BlackjackRoundState.WaitingBet
            or BlackjackRoundState.Finished;

    public bool CanStartRound =>
        CanChangeBet &&
        Chips >= Bet &&
        Bet >= 5;

    public bool IsPlayerTurn =>
        State == BlackjackRoundState.PlayerTurn;

    public event Action? StateChanged;

    public event Func<BlackjackCard, bool, Task>? CardDealing;

    public BlackjackGame()
    {
        BuildNewShoe();
    }

    public void IncreaseBet(int amount)
    {
        if (!CanChangeBet || Chips < 5)
            return;

        Bet = Math.Min(
            Chips,
            Bet + Math.Max(1, amount));

        Bet = Math.Max(5, Bet);

        Notify();
    }

    public void DecreaseBet(int amount)
    {
        if (!CanChangeBet)
            return;

        Bet = Math.Max(
            5,
            Bet - Math.Max(1, amount));

        if (Bet > Chips)
            Bet = Chips;

        Notify();
    }

    public void ResetGame()
    {
        Chips = 1000;
        Bet = 25;

        PlayerCards.Clear();
        DealerCards.Clear();

        BuildNewShoe();

        State = BlackjackRoundState.WaitingBet;
        Status =
            "Fichas e shoe reiniciados. Escolha sua aposta.";

        Notify();
    }

    public async Task StartRoundAsync()
    {
        if (!CanStartRound)
            return;

        if (shoeNeedsShuffle ||
            shoe.Count < 20)
        {
            State = BlackjackRoundState.Dealing;
            Status =
                "Substituindo e embaralhando os seis baralhos...";

            Notify();

            await Task.Delay(1300);

            BuildNewShoe();

            Status =
                "Novo shoe pronto: 312 cartas embaralhadas.";

            Notify();

            await Task.Delay(850);
        }

        Chips -= Bet;

        PlayerCards.Clear();
        DealerCards.Clear();

        State = BlackjackRoundState.Dealing;
        Status = $"Aposta de {Bet} fichas realizada.";

        Notify();

        await Task.Delay(550);

        // Distribuição real alternada.
        await DealCardAsync(
            PlayerCards,
            "Carta para você...");

        await DealCardAsync(
            DealerCards,
            "Carta para o dealer...");

        await DealCardAsync(
            PlayerCards,
            "Segunda carta para você...");

        await DealCardAsync(
            DealerCards,
            "Carta fechada para o dealer...");

        CheckCutCard();

        if (PlayerHasBlackjack ||
            DealerHasBlackjack)
        {
            await ResolveInitialBlackjackAsync();
            return;
        }

        State = BlackjackRoundState.PlayerTurn;
        Status =
            $"Sua vez. Pontuação: {PlayerScoreText}.";

        Notify();
    }

    public async Task HitAsync()
    {
        if (!IsPlayerTurn)
            return;

        await DealCardAsync(
            PlayerCards,
            "Você pediu uma carta...");

        CheckCutCard();

        if (PlayerScore > 21)
        {
            State = BlackjackRoundState.Finished;
            Status =
                $"Você estourou com {PlayerScore}. Dealer venceu.";

            Notify();
            return;
        }

        if (PlayerScore == 21)
        {
            Status =
                "Você chegou a 21. Vez do dealer.";

            Notify();

            await Task.Delay(700);
            await StandAsync();
            return;
        }

        Status =
            $"Você tem {PlayerScoreText}. Pedir ou parar?";

        Notify();
    }

    public async Task StandAsync()
    {
        if (!IsPlayerTurn)
            return;

        State = BlackjackRoundState.DealerTurn;
        Status =
            $"Dealer revelou a carta: {DealerScoreText}.";

        Notify();

        await Task.Delay(1100);

        while (DealerScore < 17)
        {
            Status =
                $"Dealer tem {DealerScoreText} e precisa comprar.";

            Notify();

            await Task.Delay(950);

            await DealCardAsync(
                DealerCards,
                "Dealer comprou uma carta...");

            CheckCutCard();

            await Task.Delay(800);
        }

        ResolveRound();
    }

    async Task ResolveInitialBlackjackAsync()
    {
        State = BlackjackRoundState.DealerTurn;
        Status = "Verificando blackjack...";

        Notify();

        await Task.Delay(1100);

        if (PlayerHasBlackjack &&
            DealerHasBlackjack)
        {
            Chips += Bet;

            State = BlackjackRoundState.Finished;
            Status =
                "Os dois têm blackjack. Aposta devolvida.";

            Notify();
            return;
        }

        if (PlayerHasBlackjack)
        {
            int profit =
                (int)Math.Round(
                    Bet * 1.5,
                    MidpointRounding.AwayFromZero);

            Chips += Bet + profit;

            State = BlackjackRoundState.Finished;
            Status =
                $"Blackjack! Você ganhou {profit} fichas.";

            Notify();
            return;
        }

        State = BlackjackRoundState.Finished;
        Status =
            "Dealer fez blackjack. Você perdeu a aposta.";

        Notify();
    }

    void ResolveRound()
    {
        int playerScore = PlayerScore;
        int dealerScore = DealerScore;

        State = BlackjackRoundState.Finished;

        if (dealerScore > 21)
        {
            Chips += Bet * 2;

            Status =
                $"Dealer estourou com {dealerScore}. " +
                $"Você ganhou {Bet} fichas.";

            Notify();
            return;
        }

        if (playerScore > dealerScore)
        {
            Chips += Bet * 2;

            Status =
                $"Você venceu: {playerScore} a {dealerScore}. " +
                $"Lucro de {Bet} fichas.";

            Notify();
            return;
        }

        if (playerScore == dealerScore)
        {
            Chips += Bet;

            Status =
                $"Empate em {playerScore}. Aposta devolvida.";

            Notify();
            return;
        }

        Status =
            $"Dealer venceu: {dealerScore} a {playerScore}.";

        Notify();
    }

    async Task DealCardAsync(
        List<BlackjackCard> target,
        string message)
    {
        Status = message;
        Notify();

        await Task.Delay(350);

        if (shoe.Count == 0)
            BuildNewShoe();

        BlackjackCard card =
            DrawCard();

        bool goesToDealer =
            ReferenceEquals(
                target,
                DealerCards);

        if (CardDealing != null)
        {
            await CardDealing.Invoke(
                card,
                goesToDealer);
        }

        target.Add(card);

        Notify();

        await Task.Delay(250);
    }

    void BuildNewShoe()
    {
        shoe.Clear();

        string[] suits =
        {
            "S",
            "H",
            "D",
            "C"
        };

        for (int deckNumber = 0;
             deckNumber < DeckCount;
             deckNumber++)
        {
            foreach (string suit in suits)
            {
                for (int rank = 2;
                     rank <= 14;
                     rank++)
                {
                    shoe.Add(
                        new BlackjackCard(
                            rank,
                            suit));
                }
            }
        }

        // Fisher-Yates.
        for (int i = shoe.Count - 1;
             i > 0;
             i--)
        {
            int other =
                random.Next(i + 1);

            (shoe[i], shoe[other]) =
                (shoe[other], shoe[i]);
        }

        shoeNeedsShuffle = false;
    }

    BlackjackCard DrawCard()
    {
        BlackjackCard card =
            shoe[^1];

        shoe.RemoveAt(
            shoe.Count - 1);

        return card;
    }

    void CheckCutCard()
    {
        if (shoe.Count <= ShuffleThreshold)
            shoeNeedsShuffle = true;
    }

    static int CalculateScore(
        IEnumerable<BlackjackCard> cards)
    {
        int total =
            cards.Sum(c => c.BlackjackValue);

        int aces =
            cards.Count(c => c.Rank == 14);

        while (total > 21 &&
               aces > 0)
        {
            total -= 10;
            aces--;
        }

        return total;
    }

    static string FormatScore(
        IEnumerable<BlackjackCard> source)
    {
        var cards = source.ToList();

        if (cards.Count == 0)
            return "0";

        int hardScore =
            cards.Sum(card =>
                card.Rank == 14
                    ? 1
                    : card.BlackjackValue);

        bool hasAce =
            cards.Any(card =>
                card.Rank == 14);

        int softScore =
            hasAce
                ? hardScore + 10
                : hardScore;

        if (hasAce &&
            softScore <= 21 &&
            softScore != hardScore)
        {
            return $"{hardScore} / {softScore}";
        }

        return hardScore.ToString();
    }

    void Notify()
    {
        StateChanged?.Invoke();
    }
}

