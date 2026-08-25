using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SolitaireNET;

public sealed class SolitaireApiClient
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    readonly HttpClient http = new()
    {
        BaseAddress = new Uri("https://paciencia.net.br/api/")
    };

    public async Task<RemoteSolitaireState?> TryGetGameAsync(string id)
    {
        using HttpResponseMessage response =
            await http.GetAsync($"games/{Uri.EscapeDataString(id)}");

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RemoteSolitaireState>(JsonOptions);
    }

    public async Task<RemoteSolitaireState> CreateGameAsync()
    {
        using HttpResponseMessage response =
            await http.PostAsync("games", null);

        response.EnsureSuccessStatusCode();

        return await ReadStateAsync(response);
    }

    public async Task<RemoteSolitaireState> SendActionAsync(string gameId, RemoteGameAction action)
    {
        using HttpResponseMessage response =
            await http.PostAsJsonAsync($"games/{Uri.EscapeDataString(gameId)}/actions", action, JsonOptions);

        response.EnsureSuccessStatusCode();

        return await ReadStateAsync(response);
    }

    static async Task<RemoteSolitaireState> ReadStateAsync(HttpResponseMessage response)
    {
        RemoteSolitaireState? state =
            await response.Content.ReadFromJsonAsync<RemoteSolitaireState>(JsonOptions);

        return state ?? throw new InvalidOperationException("Servidor retornou uma partida vazia.");
    }
}

public sealed class RemoteSolitaireState
{
    public string Id { get; set; } = "";
    public int StockCount { get; set; }
    public int WasteCount { get; set; }
    public RemoteCard? WasteTop { get; set; }
    public List<List<RemoteCard>> Tableau { get; set; } = new();
    public List<RemoteCard?> Foundations { get; set; } = new();
    public bool Won { get; set; }
}

public sealed class RemoteCard
{
    public string? Id { get; set; }
    public int? Rank { get; set; }
    public string? Suit { get; set; }
    public bool FaceUp { get; set; }

    public bool IsRed => Suit is "H" or "D";

    public string RankText => Rank switch
    {
        1 => "A",
        11 => "J",
        12 => "Q",
        13 => "K",
        int value => value.ToString(),
        _ => ""
    };

    public string SuitText => Suit switch
    {
        "S" => "♠",
        "H" => "♥",
        "D" => "♦",
        "C" => "♣",
        _ => ""
    };
}

public sealed class RemoteGameAction
{
    public string Type { get; set; } = "";
    public RemotePileRef? Source { get; set; }
    public RemotePileRef? Target { get; set; }
}

public sealed class RemotePileRef
{
    public string Kind { get; set; } = "";
    public int Index { get; set; }
    public int? Row { get; set; }
}
