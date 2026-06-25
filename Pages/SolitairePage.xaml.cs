namespace SolitaireNET;

public partial class SolitairePage : ContentPage
{
    const string SaveKey = "solitaire_save";

    readonly SolitaireGame game = new();
    readonly string? saveToLoad;

    public SolitairePage(string? save = null)
    {
        InitializeComponent();

        saveToLoad = save;

        Mesa.Drawable = game;
        game.StatusChanged += AtualizarStatus;

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(saveToLoad))
                game.ImportState(saveToLoad);
            else
                game.NovoJogo();

            AtualizarStatus();
            Mesa.Invalidate();
        };

        SizeChanged += (_, _) =>
        {
            game.SetSize((float)Mesa.Width, (float)Mesa.Height);
            Mesa.Invalidate();
        };
    }

    async void NovoJogo_Clicked(object sender, EventArgs e)
    {
        bool confirmar = await DisplayAlert(
            "Novo jogo",
            "Deseja começar um novo jogo?",
            "Sim",
            "Cancelar");

        if (!confirmar)
            return;

        game.NovoJogo();
        SalvarJogo();
        AtualizarStatus();
        Mesa.Invalidate();
    }

    void Mesa_StartInteraction(object sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;

        game.TouchStart(e.Touches[0]);
        SalvarJogo();
        AtualizarStatus();
        Mesa.Invalidate();
    }

    void Mesa_DragInteraction(object sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;

        game.TouchMove(e.Touches[0]);
        Mesa.Invalidate();
    }

    void Mesa_EndInteraction(object sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;

        game.TouchEnd(e.Touches[0]);
        SalvarJogo();
        AtualizarStatus();
        Mesa.Invalidate();
    }

    void AtualizarStatus()
    {
        BtnSemSaida.IsVisible = game.SemSaida;
    }

    void SalvarJogo()
    {
        Preferences.Set(SaveKey, game.ExportState());
    }

    protected override bool OnBackButtonPressed()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            bool confirmar = await DisplayAlert(
                "Sair do jogo",
                "Deseja voltar ao menu? O jogo atual será salvo.",
                "Sim",
                "Cancelar");

            if (confirmar)
            {
                SalvarJogo();
                await Navigation.PopAsync();
            }
        });

        return true;
    }
}