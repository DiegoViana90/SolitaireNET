namespace SolitaireNET;

public partial class SolitairePage : ContentPage
{
    const string SaveKey = "solitaire_server_game_id";

    readonly SolitaireApiClient api = new();
    readonly RemoteSolitaireGame game = new();
    readonly string? saveToLoad;
    bool loaded;

    public SolitairePage(string? save = null)
    {
        InitializeComponent();

        saveToLoad = save;

        Mesa.Drawable = game;
        game.StatusChanged += AtualizarStatus;

        Loaded += async (_, _) =>
        {
            if (loaded)
                return;

            loaded = true;
            await CarregarJogoAsync();
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

        await CriarNovoJogoAsync();
    }

    async void Mesa_StartInteraction(object sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;

        RemoteGameAction? action = game.TouchStart(e.Touches[0]);
        Mesa.Invalidate();

        if (action != null)
            await EnviarAcaoAsync(action);
    }

    void Mesa_DragInteraction(object sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;

        game.TouchMove(e.Touches[0]);
        Mesa.Invalidate();
    }

    async void Mesa_EndInteraction(object sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;

        RemoteGameAction? action = game.TouchEnd(e.Touches[0]);
        Mesa.Invalidate();

        if (action != null)
            await EnviarAcaoAsync(action);
    }

    void AtualizarStatus()
    {
        BtnSemSaida.IsVisible = false;
    }

    async Task CarregarJogoAsync()
    {
        game.SetBusy(true);
        Mesa.Invalidate();

        try
        {
            string gameId = IsGameId(saveToLoad)
                ? saveToLoad!
                : Preferences.Get(SaveKey, "");

            RemoteSolitaireState? state =
                IsGameId(gameId)
                    ? await api.TryGetGameAsync(gameId)
                    : null;

            if (state == null)
                state = await api.CreateGameAsync();

            Preferences.Set(SaveKey, state.Id);
            game.SetState(state);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Servidor indisponivel",
                $"Nao consegui carregar o jogo agora.\n\n{ex.Message}",
                "OK");
        }
        finally
        {
            game.SetBusy(false);
            Mesa.Invalidate();
        }
    }

    async Task CriarNovoJogoAsync()
    {
        game.SetBusy(true);
        Mesa.Invalidate();

        try
        {
            RemoteSolitaireState state = await api.CreateGameAsync();
            Preferences.Set(SaveKey, state.Id);
            game.SetState(state);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Servidor indisponivel",
                $"Nao consegui criar um jogo novo agora.\n\n{ex.Message}",
                "OK");
        }
        finally
        {
            game.SetBusy(false);
            Mesa.Invalidate();
        }
    }

    async Task EnviarAcaoAsync(RemoteGameAction action)
    {
        if (game.IsBusy)
            return;

        string gameId = Preferences.Get(SaveKey, "");

        if (!IsGameId(gameId))
            return;

        game.SetBusy(true);
        Mesa.Invalidate();

        try
        {
            RemoteSolitaireState state = await api.SendActionAsync(gameId, action);
            Preferences.Set(SaveKey, state.Id);
            game.SetState(state);
        }
        catch
        {
            try
            {
                RemoteSolitaireState? state = await api.TryGetGameAsync(gameId);
                if (state == null)
                    state = await api.CreateGameAsync();

                Preferences.Set(SaveKey, state.Id);
                game.SetState(state);
            }
            catch
            {
                // The next user action will try to sync again.
            }
        }
        finally
        {
            game.SetBusy(false);
            Mesa.Invalidate();
        }
    }

    static bool IsGameId(string? value)
    {
        return value?.Length == 32 &&
               value.All(Uri.IsHexDigit) == true;
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
                await Navigation.PopAsync();
            }
        });

        return true;
    }
}
