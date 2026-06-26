namespace SolitaireNET;

public partial class MainPage : ContentPage
{
    const string SaveKey = "solitaire_save";

    public MainPage()
    {
        InitializeComponent();
    }

    async void Paciencia_Clicked(object sender, EventArgs e)
    {
        string salvo = Preferences.Get(SaveKey, "");

        if (!string.IsNullOrWhiteSpace(salvo))
        {
            string escolha = await DisplayActionSheet(
                "Paciência",
                "Cancelar",
                null,
                "Continuar jogo anterior",
                "Novo jogo");

            if (escolha == "Continuar jogo anterior")
            {
                await Navigation.PushAsync(new SolitairePage(salvo));
                return;
            }

            if (escolha == "Novo jogo")
            {
                Preferences.Remove(SaveKey);
                await Navigation.PushAsync(new SolitairePage());
                return;
            }

            return;
        }

        await Navigation.PushAsync(new SolitairePage());
    }

    async void Domino_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new DominoPage());
    }

    void Sair_Clicked(object sender, EventArgs e)
    {
        Application.Current?.Quit();
    }
}