using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Chess;
sealed class CleanupService(GameStore games, CheckersStore checkers, ChessStore chess, PlayerPresenceStore players) : BackgroundService
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            games.RemoveExpired(now);
            checkers.RemoveExpired(now);
            chess.RemoveExpired(now);
            players.RemoveExpired(now);
        }
    }
}
