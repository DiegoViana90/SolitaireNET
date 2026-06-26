namespace SolitaireNET;

public partial class DominoPage : ContentPage
{
    readonly DominoGame game = new();

    public DominoPage()
    {
        InitializeComponent();

        Mesa.Drawable = game;

        Loaded += (_, _) =>
        {
            game.NewGame();
            Mesa.Invalidate();
        };
    }

    async void Mesa_StartInteraction(object sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
            return;

        int tileIndex = game.HitHandTile(e.Touches[0]);

        if (tileIndex < 0)
            return;

        string lado = await DisplayActionSheet(
            "Jogar peça",
            "Cancelar",
            null,
            "Esquerda",
            "Direita");

        if (lado == "Esquerda")
            game.PlayTile(tileIndex, true);

        if (lado == "Direita")
            game.PlayTile(tileIndex, false);

        Mesa.Invalidate();
    }

    async void Passar_Clicked(object sender, EventArgs e)
    {
        if (game.CurrentPlayerCanPlay())
        {
            await DisplayAlert("Dominó", "Você ainda tem peça para jogar.", "OK");
            return;
        }

        game.PassTurn();
        Mesa.Invalidate();
    }

    async void Novo_Clicked(object sender, EventArgs e)
    {
        bool confirmar = await DisplayAlert(
            "Novo jogo",
            "Deseja começar uma nova partida de dominó?",
            "Sim",
            "Cancelar");

        if (!confirmar)
            return;

        game.NewGame();
        Mesa.Invalidate();
    }
}