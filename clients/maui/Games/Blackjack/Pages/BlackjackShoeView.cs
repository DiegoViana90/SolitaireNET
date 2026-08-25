using Microsoft.Maui.Controls.Shapes;

namespace SolitaireNET;

public sealed class BlackjackShoeView : ContentView
{
    readonly Grid root = new();
    readonly Grid cardsStack = new();

    public BlackjackShoeView()
    {
        WidthRequest = 140;
        HeightRequest = 75;

        BuildVisual();

        Content = root;

        UpdateRemaining(
            BlackjackGame.TotalShoeCards,
            BlackjackGame.TotalShoeCards);
    }

    void BuildVisual()
    {
        root.Children.Clear();

        // Sombra inferior
        var shadow = new Border
        {
            WidthRequest = 126,
            HeightRequest = 14,
            TranslationY = 27,
            BackgroundColor = Color.FromArgb("#66000000"),
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(7)
            },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        root.Children.Add(shadow);

        // Base dourada
        var goldBase = new Border
        {
            WidthRequest = 128,
            HeightRequest = 8,
            TranslationY = 29,
            BackgroundColor = Color.FromArgb("#B98B2B"),
            Stroke = Color.FromArgb("#E4C65E"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(4)
            },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        root.Children.Add(goldBase);

        // Área onde ficam apenas as cartas
        cardsStack.WidthRequest = 116;
        cardsStack.HeightRequest = 54;
        cardsStack.TranslationX = -3;
        cardsStack.TranslationY = -3;
        cardsStack.HorizontalOptions = LayoutOptions.Center;
        cardsStack.VerticalOptions = LayoutOptions.Center;

        root.Children.Add(cardsStack);
    }

    public void UpdateRemaining(
        int remaining,
        int total)
    {
        cardsStack.Children.Clear();

        double percentage =
            total <= 0
                ? 0
                : (double)remaining / total;

        int visibleCards =
            Math.Clamp(
                (int)Math.Ceiling(percentage * 14),
                2,
                14);

        for (int i = 0; i < visibleCards; i++)
        {
            var card = new Border
            {
                WidthRequest = 72,
                HeightRequest = 46,
                TranslationX = i * 2.4,
                TranslationY = -i * 0.55,
                Rotation = -2,

                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),

                    GradientStops =
                    {
                        new GradientStop(
                            Color.FromArgb("#17377E"),
                            0),

                        new GradientStop(
                            Color.FromArgb("#2C58AA"),
                            1)
                    }
                },

                Stroke = Color.FromArgb("#D6E2FF"),
                StrokeThickness = 0.85,

                StrokeShape = new RoundRectangle
                {
                    CornerRadius = new CornerRadius(4)
                },

                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center
            };

            card.Content = new Grid
            {
                Children =
                {
                    new Label
                    {
                        Text = "◆",
                        TextColor = Color.FromArgb("#D9B955"),
                        FontSize = 13,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            };

            cardsStack.Children.Add(card);
        }
    }
}
