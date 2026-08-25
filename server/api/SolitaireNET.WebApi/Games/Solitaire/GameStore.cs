using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Chess;
sealed class GameStore
{
    static readonly TimeSpan InactiveGameLifetime = TimeSpan.FromHours(12);
    static readonly TimeSpan CompletedGameLifetime = TimeSpan.FromHours(1);
    readonly ConcurrentDictionary<string, GameSession> games = new();

    public int Count => games.Count;

    public GameSession Create(string? ownerUid)
    {
        var game = GameSession.New(ownerUid);
        games[game.Id] = game;
        return game;
    }

    public GameSession? Get(string id)
    {
        if (!games.TryGetValue(id, out GameSession? game))
            return null;

        game.Touch();
        return game;
    }

    public bool Remove(string id) => games.TryRemove(id, out _);

    public void RemoveExpired(DateTimeOffset now)
    {
        foreach ((string id, GameSession game) in games)
        {
            TimeSpan lifetime = game.IsCompleted ? CompletedGameLifetime : InactiveGameLifetime;
            if (now - game.LastActivityAt > lifetime)
                games.TryRemove(new KeyValuePair<string, GameSession>(id, game));
        }
    }
}
