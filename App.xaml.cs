namespace SolitaireNET;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        MainPage = new NavigationPage(new MainPage())
        {
            BarBackgroundColor = Color.FromArgb("#0B6B3A"),
            BarTextColor = Colors.White
        };
    }
}