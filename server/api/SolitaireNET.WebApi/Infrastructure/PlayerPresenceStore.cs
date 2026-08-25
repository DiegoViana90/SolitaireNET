using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Chess;
sealed class PlayerPresenceStore
{
    public const string HeaderName = "X-Solitaire-Player";
    static readonly TimeSpan ActiveLifetime = TimeSpan.FromMinutes(5);
    readonly ConcurrentDictionary<string, DateTimeOffset> players = new();

    public int ActiveCount => players.Count;

    public bool Record(HttpContext context)
    {
        string? playerId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!Guid.TryParse(playerId, out Guid parsed) || parsed == Guid.Empty)
            return false;

        players[parsed.ToString("N")] = DateTimeOffset.UtcNow;
        return true;
    }

    public void RemoveExpired(DateTimeOffset now)
    {
        foreach ((string id, DateTimeOffset lastSeenAt) in players)
        {
            if (now - lastSeenAt > ActiveLifetime)
                players.TryRemove(new KeyValuePair<string, DateTimeOffset>(id, lastSeenAt));
        }
    }
}
