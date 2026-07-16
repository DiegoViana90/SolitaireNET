using Microsoft.Maui.Controls.Shapes;

namespace SolitaireNET;

public sealed class BlackjackCardView : ContentView
{
    readonly Border cardBorder;
    readonly Label topLabel;
    readonly Label suitLabel;
    readonly Label bottomLabel;

    public BlackjackCardView()
    {
        WidthRequest = 58;
        HeightRequest = 84;

        topLabel = new Label
        {
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(5, 2, 0, 0),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };

        suitLabel = new Label
        {
            FontSize = 31,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        bottomLabel = new Label
        {
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 0, 5, 2),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End
        };

        var grid = new Grid();

        grid.Children.Add(topLabel);
        grid.Children.Add(suitLabel);
        grid.Children.Add(bottomLabel);

        cardBorder = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Colors.Black,
            StrokeThickness = 1.3,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(7)
            },
            Content = grid
        };

        Content = cardBorder;

        SetEmpty();
    }

    public void SetCard(
        BlackjackCard? card,
        bool hidden = false)
    {
        if (hidden)
        {
            cardBorder.BackgroundColor =
                Color.FromArgb("#183B85");

            cardBorder.Stroke =
                Colors.White;

            topLabel.Text = "";
            bottomLabel.Text = "";
            suitLabel.Text = "◆";
            suitLabel.TextColor =
                Color.FromArgb("#90CAF9");

            Opacity = 1;
            return;
        }

        if (card == null)
        {
            SetEmpty();
            return;
        }

        Color textColor =
            card.IsRed
                ? Color.FromArgb("#C62828")
                : Colors.Black;

        cardBorder.BackgroundColor =
            Colors.White;

        cardBorder.Stroke =
            Colors.Black;

        topLabel.Text =
            card.RankText;

        topLabel.TextColor =
            textColor;

        suitLabel.Text =
            card.SuitText;

        suitLabel.TextColor =
            textColor;

        bottomLabel.Text =
            card.RankText;

        bottomLabel.TextColor =
            textColor;

        Opacity = 1;
    }

    void SetEmpty()
    {
        cardBorder.BackgroundColor =
            Color.FromArgb("#22FFFFFF");

        cardBorder.Stroke =
            Color.FromArgb("#77FFFFFF");

        topLabel.Text = "";
        suitLabel.Text = "";
        bottomLabel.Text = "";

        Opacity = 0.65;
    }
}
