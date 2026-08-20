using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Chess;

var builder = WebApplication.CreateBuilder(args);
string? firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
bool firebaseAuthEnabled = !string.IsNullOrWhiteSpace(firebaseProjectId);
bool loadTestEnabled = builder.Configuration.GetValue<bool>("LoadTest:Enabled");

if (firebaseAuthEnabled)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://securetoken.google.com/{firebaseProjectId}";
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidAudience = firebaseProjectId,
                ValidIssuer = $"https://securetoken.google.com/{firebaseProjectId}"
            };
        });

    builder.Services.AddAuthorization();
}

builder.Services.AddSingleton<GameStore>();
builder.Services.AddSingleton<CheckersStore>();
builder.Services.AddSingleton<ChessStore>();
builder.Services.AddSingleton<PlusFourStore>();
builder.Services.AddSingleton<UsageMetrics>();
builder.Services.AddSingleton<PlayerPresenceStore>();
builder.Services.AddSingleton<RankingStore>();
builder.Services.AddHostedService<CleanupService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch
    {
        context.RequestServices.GetRequiredService<UsageMetrics>().RecordApiError();
        throw;
    }
});

if (firebaseAuthEnabled)
{
    app.UseAuthentication();
    app.Use(async (context, next) =>
    {
        string? authorization = context.Request.Headers.Authorization.FirstOrDefault();
        bool hasBearerToken = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true;

        if (hasBearerToken && context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Token de login invalido ou expirado." });
            return;
        }

        await next(context);
    });
    app.UseAuthorization();
}

app.MapGet("/api/health", (GameStore games, UsageMetrics metrics, PlayerPresenceStore players, RankingStore ranking) =>
    Results.Ok(new
    {
        ok = true,
        firebaseAuth = new
        {
            enabled = firebaseAuthEnabled
        },
        usage = metrics.Snapshot(games.Count, players.ActiveCount),
        ranking = ranking.Summary()
    }));

if (firebaseAuthEnabled)
{
    app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
        Results.Ok(FirebaseUser.FromClaims(user)))
        .RequireAuthorization();
}
else
{
    app.MapGet("/api/auth/me", () =>
        Results.Problem(
            "Firebase authentication is not configured.",
            statusCode: StatusCodes.Status501NotImplemented));
}

app.MapPost("/api/games", (HttpContext context, GameStore store, UsageMetrics metrics, PlayerPresenceStore players, RankingStore ranking) =>
{
    players.Record(context);
    FirebaseUser? user = FirebaseUser.TryFromClaims(context.User);
    GameSession game = store.Create(user?.Uid);
    metrics.RecordGameCreated();

    if (user != null)
        ranking.RecordGameStarted(user);

    return Results.Ok(game.ToPublicState());
});

app.MapGet("/api/games/{id}", (string id, HttpContext context, GameStore store, PlayerPresenceStore players) =>
{
    players.Record(context);
    GameSession? game = store.Get(id);

    if (game == null)
        return Results.NotFound(new { error = "Game not found" });

    return Results.Ok(game.ToPublicState());
});

app.MapDelete("/api/games/{id}", (string id, HttpContext context, GameStore store, PlayerPresenceStore players) =>
{
    players.Record(context);
    GameSession? game = store.Get(id);
    if (game == null)
        return Results.NoContent();

    store.Remove(id);
    return Results.NoContent();
});

app.MapPost("/api/games/{id}/actions", (string id, GameAction action, HttpContext context, GameStore store, UsageMetrics metrics, PlayerPresenceStore players, RankingStore ranking) =>
{
    players.Record(context);
    metrics.RecordActionAttempted();

    GameSession? game = store.Get(id);
    if (game == null)
        return Results.NotFound(new { error = "Game not found" });

    string? uid = FirebaseUser.UidFromClaims(context.User);
    if (game.IsOwnedByDifferentUser(uid))
    {
        return Results.Problem(
            "Esta partida pertence a outra conta.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    game.DisableRankingIfSignedOut(uid);

    MoveResult result = game.Apply(action);
    if (!result.Ok)
    {
        metrics.RecordInvalidAction();
        return Results.BadRequest(new { error = result.Error });
    }

    metrics.RecordActionAccepted();
    if (result.WonNow)
    {
        metrics.RecordWin();
        if (game.OwnerUid != null)
            ranking.RecordWin(game.OwnerUid);
    }

    return Results.Ok(game.ToPublicState());
});

app.MapPost("/api/presence", (HttpContext context, PlayerPresenceStore players) =>
{
    return players.Record(context)
        ? Results.NoContent()
        : Results.BadRequest(new { error = $"Missing or invalid {PlayerPresenceStore.HeaderName} header" });
});

app.MapGet("/api/usage", (GameStore games, UsageMetrics metrics, PlayerPresenceStore players) =>
    Results.Ok(metrics.Snapshot(games.Count, players.ActiveCount)));

app.MapGet("/api/ranking", (RankingStore ranking) =>
    Results.Ok(ranking.Snapshot()));

app.MapLoadTestEndpoints(loadTestEnabled);

app.MapPost("/api/checkers/rooms", (CheckersStore store) =>
    Results.Ok(store.CreatePrivateRoom()));

app.MapPost("/api/checkers/rooms/{code}/join", (string code, CheckersStore store) =>
{
    CheckersJoinResult result = store.JoinRoom(code);
    return result.Error == null
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/checkers/matchmaking", (CheckersStore store) =>
    Results.Ok(store.JoinRandomRoom()));

app.MapGet("/api/checkers/rooms/{code}", (string code, string playerId, CheckersStore store) =>
{
    CheckersJoinResult result = store.GetRoom(code, playerId);
    return result.Error == null
        ? Results.Ok(result)
        : Results.NotFound(new { error = result.Error });
});

app.MapPost("/api/checkers/rooms/{code}/actions", (string code, CheckersMoveAction action, CheckersStore store) =>
{
    CheckersJoinResult result = store.ApplyMove(code, action);
    return result.Error == null
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/checkers/rooms/{code}/leave", (string code, CheckersLeaveAction action, CheckersStore store) =>
{
    CheckersJoinResult result = store.LeaveRoom(code, action.PlayerId);
    return result.Error == null
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/chess/rooms", (ChessStore store) =>
    Results.Ok(store.CreatePrivateRoom()));

app.MapPost("/api/chess/rooms/{code}/join", (string code, ChessStore store) =>
{
    ChessJoinResult result = store.JoinRoom(code);
    return result.Error == null
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/chess/matchmaking", (ChessStore store) =>
    Results.Ok(store.JoinRandomRoom()));

app.MapGet("/api/chess/rooms/{code}", (string code, string playerId, ChessStore store) =>
{
    ChessJoinResult result = store.GetRoom(code, playerId);
    return result.Error == null
        ? Results.Ok(result)
        : Results.NotFound(new { error = result.Error });
});

app.MapPost("/api/chess/rooms/{code}/actions", (string code, ChessMoveAction action, ChessStore store) =>
{
    ChessJoinResult result = store.ApplyMove(code, action);
    return result.Error == null
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/chess/rooms/{code}/leave", (string code, ChessLeaveAction action, ChessStore store) =>
{
    ChessJoinResult result = store.LeaveRoom(code, action.PlayerId);
    return result.Error == null
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/plus-four/rooms", (PlusFourStore store) =>
    Results.Ok(store.CreatePrivateRoom()));

app.MapPost("/api/plus-four/rooms/{code}/join", (string code, PlusFourStore store) =>
{
    PlusFourJoinResult result = store.JoinRoom(code);
    return result.Error == null
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/plus-four/matchmaking", (PlusFourStore store) =>
    Results.Ok(store.JoinRandomRoom()));

app.MapGet("/api/plus-four/rooms/{code}", (string code, string playerId, PlusFourStore store) =>
{
    PlusFourJoinResult result = store.GetRoom(code, playerId);
    return result.Error == null
        ? Results.Ok(result)
        : Results.NotFound(new { error = result.Error });
});

app.MapGet("/api/plus-four/rooms/{code}/events", async (string code, string playerId, HttpContext context, PlusFourStore store) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.Headers["X-Accel-Buffering"] = "no";
    await context.Response.StartAsync(context.RequestAborted);

    while (!context.RequestAborted.IsCancellationRequested)
    {
        PlusFourJoinResult result = store.GetRoom(code, playerId);
        if (result.Error != null)
            break;

        string payload = System.Text.Json.JsonSerializer.Serialize(result);
        await context.Response.WriteAsync($"data: {payload}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        if (!await store.WaitForChange(code, playerId, context.RequestAborted))
            break;
    }
});

app.MapPost("/api/plus-four/rooms/{code}/actions", (string code, PlusFourAction action, PlusFourStore store) =>
{
    PlusFourJoinResult result = store.ApplyAction(code, action);
    return result.Error == null
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/plus-four/rooms/{code}/leave", (string code, PlusFourLeaveAction action, PlusFourStore store) =>
{
    PlusFourJoinResult result = store.LeaveRoom(code, action.PlayerId);
    return result.Error == null
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Error });
});

if (firebaseAuthEnabled)
{
    app.MapGet("/api/ranking/me", (ClaimsPrincipal user, RankingStore ranking) =>
    {
        string? uid = FirebaseUser.UidFromClaims(user);
        return uid == null
            ? Results.Unauthorized()
            : Results.Ok(ranking.GetPlayer(uid));
    }).RequireAuthorization();
}
else
{
    app.MapGet("/api/ranking/me", () =>
        Results.Problem(
            "Firebase authentication is not configured.",
            statusCode: StatusCodes.Status501NotImplemented));
}

app.Run();
