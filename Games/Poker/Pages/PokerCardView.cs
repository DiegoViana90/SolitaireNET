using Microsoft.Maui.Controls.Shapes;

namespace SolitaireNET;

public sealed class PokerCardView : ContentView
{
    readonly Border cardBorder;
    readonly Label rankLabel;
    readonly Label centerSuitLabel;
    readonly Label bottomLabel;

    public PokerCardView()
    {
        WidthRequest = 47;
        HeightRequest = 68;

        rankLabel = new Label
        {
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(5, 2, 0, 0),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };

        centerSuitLabel = new Label
        {
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        bottomLabel = new Label
        {
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 0, 5, 2),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End
        };

        var grid = new Grid();

        grid.Children.Add(rankLabel);
        grid.Children.Add(centerSuitLabel);
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

        SetCard(null);
    }

    public void SetCard(PokerCard? card)
    {
        if (card == null)
        {
            cardBorder.BackgroundColor =
                Color.FromArgb("#22FFFFFF");

            cardBorder.Stroke =
                Color.FromArgb("#88FFFFFF");

            rankLabel.Text = "";
            centerSuitLabel.Text = "";
            bottomLabel.Text = "";

            Opacity = 0.70;
            return;
        }

        Color textColor = card.IsRed
            ? Color.FromArgb("#C62828")
            : Colors.Black;

        cardBorder.BackgroundColor = Colors.White;
        cardBorder.Stroke = Colors.Black;

        rankLabel.Text = card.RankText;
        rankLabel.TextColor = textColor;

        centerSuitLabel.Text = card.SuitText;
        centerSuitLabel.TextColor = textColor;

        bottomLabel.Text = card.RankText;
        bottomLabel.TextColor = textColor;

        Opacity = 1;
    }
}
