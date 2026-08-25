namespace SolitaireNET;

public partial class BlackjackPage : ContentPage
{
    readonly BlackjackGame game = new();

    bool processing;

    public BlackjackPage()
    {
        InitializeComponent();

        game.StateChanged += UpdateScreen;
        game.CardDealing += AnimateCardAsync;

        UpdateScreen();
    }

    async void StartRound_Clicked(
        object sender,
        EventArgs e)
    {
        if (processing ||
            !game.CanStartRound)
        {
            return;
        }

        processing = true;
        UpdateScreen();

        try
        {
            await game.StartRoundAsync();
        }
        finally
        {
            processing = false;
            UpdateScreen();
        }
    }

    async void Hit_Clicked(
        object sender,
        EventArgs e)
    {
        if (processing ||
            !game.IsPlayerTurn)
        {
            return;
        }

        processing = true;
        UpdateScreen();

        try
        {
            await game.HitAsync();
        }
        finally
        {
            processing = false;
            UpdateScreen();
        }
    }

    async void Stand_Clicked(
        object sender,
        EventArgs e)
    {
        if (processing ||
            !game.IsPlayerTurn)
        {
            return;
        }

        processing = true;
        UpdateScreen();

        try
        {
            await game.StandAsync();
        }
        finally
        {
            processing = false;
            UpdateScreen();
        }
    }

    void BetMinus25_Clicked(
        object sender,
        EventArgs e)
    {
        game.DecreaseBet(25);
    }

    void BetMinus5_Clicked(
        object sender,
        EventArgs e)
    {
        game.DecreaseBet(5);
    }

    void BetPlus5_Clicked(
        object sender,
        EventArgs e)
    {
        game.IncreaseBet(5);
    }

    void BetPlus25_Clicked(
        object sender,
        EventArgs e)
    {
        game.IncreaseBet(25);
    }

    async void Reset_Clicked(
        object sender,
        EventArgs e)
    {
        bool confirm =
            await DisplayAlert(
                "Reiniciar",
                "Restaurar 1.000 fichas e colocar um novo shoe?",
                "Sim",
                "Cancelar");

        if (!confirm)
            return;

        game.ResetGame();
    }

    async Task AnimateCardAsync(
        BlackjackCard card,
        bool goesToDealer)
    {
        await MainThread.InvokeOnMainThreadAsync(
            async () =>
            {
                FlyingCard.SetCard(
                    card,
                    hidden: true);

                Point shoePosition =
                    GetCenterRelativeToRoot(
                        ShoeView);

                VisualElement destination =
                    goesToDealer
                        ? DealerCardsPanel
                        : PlayerCardsPanel;

                Point destinationPosition =
                    GetCenterRelativeToRoot(
                        destination);

                FlyingCard.TranslationX =
                    shoePosition.X -
                    FlyingCard.WidthRequest / 2;

                FlyingCard.TranslationY =
                    shoePosition.Y -
                    FlyingCard.HeightRequest / 2;

                FlyingCard.Scale = 0.85;
                FlyingCard.Rotation = -8;
                FlyingCard.Opacity = 1;
                FlyingCard.IsVisible = true;

                ShoeView.UpdateRemaining(
                    game.CardsRemaining,
                    BlackjackGame.TotalShoeCards);

                double targetX =
                    destinationPosition.X -
                    FlyingCard.WidthRequest / 2;

                double targetY =
                    destinationPosition.Y -
                    FlyingCard.HeightRequest / 2;

                uint duration = 520;

                await Task.WhenAll(
                    FlyingCard.TranslateTo(
                        targetX,
                        targetY,
                        duration,
                        Easing.CubicInOut),

                    FlyingCard.ScaleTo(
                        1,
                        duration,
                        Easing.CubicOut),

                    FlyingCard.RotateTo(
                        0,
                        duration,
                        Easing.CubicOut));

                await FlyingCard.FadeTo(
                    0,
                    100);

                FlyingCard.IsVisible = false;
                FlyingCard.Opacity = 1;
            });
    }

    Point GetCenterRelativeToRoot(
        VisualElement element)
    {
        double x =
            element.X +
            element.Width / 2;

        double y =
            element.Y +
            element.Height / 2;

        Element? parent =
            element.Parent;

        while (parent is VisualElement visual &&
               !ReferenceEquals(
                   visual,
                   RootGrid))
        {
            x += visual.X;
            y += visual.Y;

            parent = visual.Parent;
        }

        return new Point(x, y);
    }

    void UpdateScreen()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text =
                game.Status;

            ChipsLabel.Text =
                game.Chips.ToString("N0");

            BetLabel.Text =
                game.Bet.ToString("N0");

            ShoeView.UpdateRemaining(
                game.CardsRemaining,
                BlackjackGame.TotalShoeCards);

            PlayerScoreLabel.Text =
                $"Pontuação: {game.PlayerScoreText}";

            DealerScoreLabel.Text =
                game.HideDealerSecondCard
                    ? $"Pontuação visível: {game.VisibleDealerScoreText}"
                    : $"Pontuação: {game.DealerScoreText}";

            RoundPhaseLabel.Text =
                game.State switch
                {
                    BlackjackRoundState.WaitingBet =>
                        "AGUARDANDO APOSTA",

                    BlackjackRoundState.Dealing =>
                        "DISTRIBUINDO",

                    BlackjackRoundState.PlayerTurn =>
                        "SUA VEZ",

                    BlackjackRoundState.DealerTurn =>
                        "VEZ DO DEALER",

                    BlackjackRoundState.Finished =>
                        "RODADA ENCERRADA",

                    _ => "BLACKJACK"
                };

            RenderCards();

            bool canChangeBet =
                game.CanChangeBet &&
                !processing;

            BetMinus25Button.IsEnabled =
                canChangeBet &&
                game.Bet > 5;

            BetMinus5Button.IsEnabled =
                canChangeBet &&
                game.Bet > 5;

            BetPlus5Button.IsEnabled =
                canChangeBet &&
                game.Bet < game.Chips;

            BetPlus25Button.IsEnabled =
                canChangeBet &&
                game.Bet < game.Chips;

            StartButton.IsEnabled =
                game.CanStartRound &&
                !processing;

            StartButton.Text =
                game.State == BlackjackRoundState.Finished
                    ? "Nova rodada"
                    : "Iniciar rodada";

            HitButton.IsEnabled =
                game.IsPlayerTurn &&
                !processing;

            StandButton.IsEnabled =
                game.IsPlayerTurn &&
                !processing;

            UpdateActionButtonStyle();
        });
    }

    void RenderCards()
    {
        PlayerCardsPanel.Children.Clear();
        DealerCardsPanel.Children.Clear();

        foreach (BlackjackCard card
                 in game.PlayerCards)
        {
            var view =
                new BlackjackCardView();

            view.SetCard(card);

            PlayerCardsPanel.Children.Add(view);
        }

        for (int i = 0;
             i < game.DealerCards.Count;
             i++)
        {
            var view =
                new BlackjackCardView();

            bool hidden =
                game.HideDealerSecondCard &&
                i == 1;

            view.SetCard(
                game.DealerCards[i],
                hidden);

            DealerCardsPanel.Children.Add(view);
        }


    }

    static void AddEmptyCard(
        HorizontalStackLayout panel)
    {
        var view =
            new BlackjackCardView();

        view.SetCard(null);

        panel.Children.Add(view);
    }

    void UpdateActionButtonStyle()
    {
        bool canPlay =
            game.IsPlayerTurn &&
            !processing;

        if (canPlay)
        {
            HitButton.BackgroundColor =
                Color.FromArgb("#17653A");

            HitButton.TextColor =
                Colors.White;

            HitButton.Opacity = 1;

            StandButton.BackgroundColor =
                Color.FromArgb("#B3261E");

            StandButton.TextColor =
                Colors.White;

            StandButton.Opacity = 1;
        }
        else
        {
            HitButton.BackgroundColor =
                Color.FromArgb("#454A47");

            HitButton.TextColor =
                Color.FromArgb("#AAAAAA");

            HitButton.Opacity = 0.72;

            StandButton.BackgroundColor =
                Color.FromArgb("#454A47");

            StandButton.TextColor =
                Color.FromArgb("#AAAAAA");

            StandButton.Opacity = 0.72;
        }
    }}





