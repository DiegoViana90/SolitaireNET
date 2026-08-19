using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Chess;
sealed class UsageMetrics
{
    readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    long gamesCreated;
    long actionsAttempted;
    long actionsAccepted;
    long invalidActions;
    long apiErrors;
    long wins;

    public void RecordGameCreated() => Interlocked.Increment(ref gamesCreated);
    public void RecordActionAttempted() => Interlocked.Increment(ref actionsAttempted);
    public void RecordActionAccepted() => Interlocked.Increment(ref actionsAccepted);
    public void RecordInvalidAction() => Interlocked.Increment(ref invalidActions);
    public void RecordApiError() => Interlocked.Increment(ref apiErrors);
    public void RecordWin() => Interlocked.Increment(ref wins);

    public UsageSnapshot Snapshot(int gamesInMemory, int activePlayers) => new(
        startedAt,
        activePlayers,
        gamesInMemory,
        Interlocked.Read(ref gamesCreated),
        Interlocked.Read(ref actionsAttempted),
        Interlocked.Read(ref actionsAccepted),
        Interlocked.Read(ref invalidActions),
        Interlocked.Read(ref apiErrors),
        Interlocked.Read(ref wins));
}

sealed record UsageSnapshot(
    DateTimeOffset StartedAt,
    int ActivePlayers,
    int GamesInMemory,
    long GamesCreated,
    long ActionsAttempted,
    long ActionsAccepted,
    long InvalidActions,
    long ApiErrors,
    long Wins);
