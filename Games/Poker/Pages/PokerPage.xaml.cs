namespace SolitaireNET;

public partial class PokerPage : ContentPage
{
    readonly PokerGame game = new();

    bool processing;
    string lastPopupMessage = "";
    CancellationTokenSource? popupCancellation;

    public PokerPage()
    {
        InitializeComponent();

        game.StateChanged += UpdateScreen;

        UpdateScreen();
    }

    async void StartHand_Clicked(
        object sender,
        EventArgs e)
    {
        if (processing)
            return;

        processing = true;
        StartButton.IsEnabled = false;

        try
        {
            if (game.HandFinished ||
                game.Street == PokerStreet.Waiting)
            {
                await game.StartHandAsync();
            }
        }
        finally
        {
            processing = false;
            UpdateScreen();
        }
    }

    async void Fold_Clicked(
        object sender,
        EventArgs e)
    {
        if (processing)
            return;

        processing = true;
        SetActionsEnabled(false);

        try
        {
            await game.HumanFoldAsync();
        }
        finally
        {
            processing = false;
            UpdateScreen();
        }
    }

    async void CheckCall_Clicked(
        object sender,
        EventArgs e)
    {
        if (processing)
            return;

        processing = true;
        SetActionsEnabled(false);

        try
        {
            await game.HumanCheckOrCallAsync();
        }
        finally
        {
            processing = false;
            UpdateScreen();
        }
    }

    async void Raise_Clicked(
        object sender,
        EventArgs e)
    {
        if (processing)
            return;

        processing = true;
        SetActionsEnabled(false);

        try
        {
            await game.HumanRaiseAsync(10);
        }
        finally
        {
            processing = false;
            UpdateScreen();
        }
    }

    void UpdateScreen()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = game.Status;
            PotLabel.Text = $"Pote: {game.DisplayPot}";

            StreetLabel.Text = game.Street switch
            {
                PokerStreet.PreFlop => "PRÉ-FLOP",
                PokerStreet.Flop => "FLOP",
                PokerStreet.Turn => "TURN",
                PokerStreet.River => "RIVER",
                PokerStreet.Showdown => "SHOWDOWN",
                PokerStreet.Finished => "MÃO ENCERRADA",
                _ => "AGUARDANDO"
            };

            UpdateCommunityCards();

            UpdatePlayer(
                game.Players[0],
                HumanCards,
                HumanChips,
                HumanPosition,
                HumanBorder);

            UpdatePlayer(
                game.Players[1],
                Ai1Cards,
                Ai1Chips,
                Ai1Position,
                Ai1Border);

            UpdatePlayer(
                game.Players[2],
                Ai2Cards,
                Ai2Chips,
                Ai2Position,
                Ai2Border);

            UpdatePlayer(
                game.Players[3],
                Ai3Cards,
                Ai3Chips,
                Ai3Position,
                Ai3Border);

            UpdatePlayer(
                game.Players[4],
                Ai4Cards,
                Ai4Chips,
                Ai4Position,
                Ai4Border);

            HandStrengthLabel.Text =
                GetHumanHandDescription();

            UpdateTurnHighlight();

            bool showActions =
                game.IsHumanTurn &&
                !processing;

            ActionsContainer.IsVisible = showActions;

            if (showActions)
            {
                if (game.HumanCanCheck)
                {
                    CallInfoLabel.Text =
                        "Sua vez — você pode pedir mesa";

                    CheckCallButton.Text = "Mesa";
                }
                else
                {
                    CallInfoLabel.Text =
                        $"Para continuar: pagar {game.HumanCallAmount}";

                    CheckCallButton.Text =
                        $"Pagar {game.HumanCallAmount}";
                }

                RaiseButton.Text =
                    game.HumanCallAmount > 0
                        ? "Pagar + subir"
                        : "Aumentar +10";

                SetActionsEnabled(true);
            }
            else
            {
                SetActionsEnabled(false);
            }

            StartButton.IsVisible =
                game.HandFinished ||
                game.Street == PokerStreet.Waiting;

            StartButton.IsEnabled =
                !processing;

            StartButton.Text =
                game.Street == PokerStreet.Waiting
                    ? "Iniciar mão"
                    : "Próxima mão";

            TryShowActionPopup(game.Status);
        });
    }

    void UpdateCommunityCards()
    {
        PokerCardView[] views =
        {
            CommunityCard1,
            CommunityCard2,
            CommunityCard3,
            CommunityCard4,
            CommunityCard5
        };

        for (int i = 0; i < views.Length; i++)
        {
            PokerCard? card =
                i < game.CommunityCards.Count
                    ? game.CommunityCards[i]
                    : null;

            views[i].SetCard(card);
        }
    }

    string GetHumanHandDescription()
    {
        PokerPlayer human = game.Players[0];

        if (human.HoleCards.Count == 0)
            return "Aguardando cartas";

        if (human.Folded)
            return "Você desistiu desta mão";

        var cards =
            human.HoleCards
                .Concat(game.CommunityCards)
                .ToList();

        if (cards.Count < 5)
        {
            var groups =
                cards
                    .GroupBy(c => c.Rank)
                    .OrderByDescending(g => g.Count())
                    .ThenByDescending(g => g.Key)
                    .ToList();

            if (groups[0].Count() == 2)
                return $"Um par de {RankPlural(groups[0].Key)}";

            int highest =
                cards.Max(c => c.Rank);

            return $"Carta alta: {RankName(highest)}";
        }

        PokerHandResult result =
            PokerHandEvaluator.Evaluate(cards);

        return BuildDetailedHandName(result.Name, cards);
    }

    static string BuildDetailedHandName(
        string resultName,
        List<PokerCard> cards)
    {
        var groups =
            cards
                .GroupBy(c => c.Rank)
                .Select(g => new
                {
                    Rank = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(g => g.Count)
                .ThenByDescending(g => g.Rank)
                .ToList();

        return resultName switch
        {
            "Carta alta" =>
                $"Carta alta: {RankName(cards.Max(c => c.Rank))}",

            "Um par" =>
                $"Um par de {RankPlural(groups.First(g => g.Count >= 2).Rank)}",

            "Dois pares" =>
                BuildTwoPairsDescription(groups),

            "Trinca" =>
                $"Trinca de {RankPlural(groups.First(g => g.Count >= 3).Rank)}",

            "Sequência" =>
                "Sequência",

            "Flush" =>
                BuildFlushDescription(cards),

            "Full House" =>
                "Full House",

            "Quadra" =>
                $"Quadra de {RankPlural(groups.First(g => g.Count >= 4).Rank)}",

            "Straight Flush" =>
                "Straight Flush",

            _ => resultName
        };
    }

    static string BuildTwoPairsDescription(
        IEnumerable<dynamic> groups)
    {
        var pairs =
            groups
                .Where(g => g.Count >= 2)
                .OrderByDescending(g => g.Rank)
                .Take(2)
                .ToList();

        if (pairs.Count < 2)
            return "Dois pares";

        return
            $"Dois pares: {RankPlural((int)pairs[0].Rank)} " +
            $"e {RankPlural((int)pairs[1].Rank)}";
    }

    static string BuildFlushDescription(
        IEnumerable<PokerCard> cards)
    {
        string? suit =
            cards
                .GroupBy(c => c.Suit)
                .Where(g => g.Count() >= 5)
                .Select(g => g.Key)
                .FirstOrDefault();

        return suit switch
        {
            "S" => "Flush de espadas",
            "H" => "Flush de copas",
            "D" => "Flush de ouros",
            "C" => "Flush de paus",
            _ => "Flush"
        };
    }

    static string RankName(int rank)
    {
        return rank switch
        {
            14 => "Ás",
            13 => "Rei",
            12 => "Dama",
            11 => "Valete",
            _ => rank.ToString()
        };
    }

    static string RankPlural(int rank)
    {
        return rank switch
        {
            14 => "ases",
            13 => "reis",
            12 => "damas",
            11 => "valetes",
            _ => rank.ToString()
        };
    }

    void UpdateTurnHighlight()
    {
        ResetBorder(Ai1Border, false);
        ResetBorder(Ai2Border, false);
        ResetBorder(Ai3Border, false);
        ResetBorder(Ai4Border, false);
        ResetBorder(HumanBorder, true);

        bool bettingStreet =
            game.Street is PokerStreet.PreFlop
                or PokerStreet.Flop
                or PokerStreet.Turn
                or PokerStreet.River;

        if (!bettingStreet)
            return;

        PokerPlayer current =
            game.CurrentPlayer;

        Border? currentBorder = null;

        if (ReferenceEquals(current, game.Players[0]))
            currentBorder = HumanBorder;
        else if (ReferenceEquals(current, game.Players[1]))
            currentBorder = Ai1Border;
        else if (ReferenceEquals(current, game.Players[2]))
            currentBorder = Ai2Border;
        else if (ReferenceEquals(current, game.Players[3]))
            currentBorder = Ai3Border;
        else if (ReferenceEquals(current, game.Players[4]))
            currentBorder = Ai4Border;

        if (currentBorder == null)
            return;

        currentBorder.Stroke =
            new SolidColorBrush(
                Color.FromArgb("#FF3030"));

        currentBorder.StrokeThickness = 5;
    }

    static void ResetBorder(
        Border border,
        bool isHuman)
    {
        border.Stroke =
            new SolidColorBrush(
                isHuman
                    ? Color.FromArgb("#FFD54F")
                    : Color.FromArgb("#22000000"));

        border.StrokeThickness = 3;
    }

    void UpdatePlayer(
        PokerPlayer player,
        Label cardsLabel,
        Label chipsLabel,
        Label positionLabel,
        Border border)
    {
        positionLabel.Text =
            player.PositionText;

        string status = "";

        if (player.Folded &&
            player.HoleCards.Count > 0)
        {
            status = " | desistiu";
            border.Opacity = 0.52;
        }
        else
        {
            border.Opacity = 1;
        }

        if (player.AllIn)
            status = " | ALL-IN";

        chipsLabel.Text =
            $"{player.Chips:N0} fichas" +
            (
                player.CurrentBet > 0
                    ? $" | aposta: {player.CurrentBet}"
                    : ""
            ) +
            status;

        if (player.HoleCards.Count == 0)
        {
            cardsLabel.Text =
                player.Chips <= 0
                    ? "ELIMINADO"
                    : "—  —";

            return;
        }

        if (player.IsHuman)
        {
            cardsLabel.Text =
                string.Join(
                    "   ",
                    player.HoleCards.Select(c => c.ToString()));

            return;
        }

        bool revealCards =
            game.Street is PokerStreet.Showdown
                or PokerStreet.Finished
            &&
            !player.Folded;

        cardsLabel.Text = revealCards
            ? string.Join(
                "   ",
                player.HoleCards.Select(c => c.ToString()))
            : string.Join(
                "   ",
                player.HoleCards.Select(_ => "🂠"));
    }

    void TryShowActionPopup(string message)
    {
        if (string.IsNullOrWhiteSpace(message) ||
            message == lastPopupMessage)
        {
            return;
        }

        bool isAction =
            message.Contains("está pensando",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains("aumentou",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains("pagou",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains("pediu mesa",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains("desistiu",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains("all-in",
                StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("Sua vez",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains("venceu",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Showdown",
                StringComparison.OrdinalIgnoreCase);

        if (!isAction)
            return;

        lastPopupMessage = message;

        _ = ShowActionPopupAsync(message);
    }

    async Task ShowActionPopupAsync(string message)
    {
        popupCancellation?.Cancel();
        popupCancellation?.Dispose();

        popupCancellation =
            new CancellationTokenSource();

        CancellationToken token =
            popupCancellation.Token;

        string playerName =
            ExtractPlayerName(message);

        string actionText =
            ExtractActionText(
                message,
                playerName);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ActionPopupPlayer.Text =
                playerName.ToUpperInvariant();

            ActionPopupText.Text =
                actionText.ToUpperInvariant();

            PopupOverlay.Opacity = 0;
            ActionPopup.Opacity = 0;
            ActionPopup.Scale = 0.84;

            PopupOverlay.IsVisible = true;
            ActionPopup.IsVisible = true;
        });

        try
        {
            await Task.WhenAll(
                PopupOverlay.FadeTo(1, 170),
                ActionPopup.FadeTo(1, 170),
                ActionPopup.ScaleTo(
                    1,
                    210,
                    Easing.CubicOut));

            int duration =
                message.Contains(
                    "está pensando",
                    StringComparison.OrdinalIgnoreCase)
                    ? 850
                    : 1300;

            await Task.Delay(
                duration,
                token);

            await Task.WhenAll(
                PopupOverlay.FadeTo(0, 180),
                ActionPopup.FadeTo(0, 180),
                ActionPopup.ScaleTo(
                    0.92,
                    180,
                    Easing.CubicIn));

            if (!token.IsCancellationRequested)
            {
                PopupOverlay.IsVisible = false;
                ActionPopup.IsVisible = false;
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    static string ExtractPlayerName(
        string message)
    {
        if (message.StartsWith(
            "Sua vez",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Você";
        }

        if (message.StartsWith(
            "Você ",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Você";
        }

        foreach (string name in new[]
        {
            "IA 1",
            "IA 2",
            "IA 3",
            "IA 4"
        })
        {
            if (message.StartsWith(
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        if (message.Contains(
            "Showdown",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Showdown";
        }

        return "Mesa";
    }

    static string ExtractActionText(
        string message,
        string playerName)
    {
        string result = message;

        if (playerName == "Você" &&
            result.StartsWith(
                "Você ",
                StringComparison.OrdinalIgnoreCase))
        {
            result =
                result["Você ".Length..];
        }
        else if (playerName.StartsWith("IA"))
        {
            string prefix =
                playerName + " ";

            if (result.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
            {
                result =
                    result[prefix.Length..];
            }
        }

        return result
            .Trim()
            .TrimEnd('.');
    }

    void SetActionsEnabled(bool enabled)
    {
        FoldButton.IsEnabled = enabled;
        CheckCallButton.IsEnabled = enabled;
        RaiseButton.IsEnabled = enabled;
    }
}
