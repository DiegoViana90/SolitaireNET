using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Chess;
sealed class RankingStore
{
    static readonly TimeSpan SnapshotLifetime = TimeSpan.FromMinutes(5);
    readonly object gate = new();
    readonly string? connectionString;
    readonly string databasePath;
    readonly bool usePostgres;
    RankingSnapshot? cachedSnapshot;

    public RankingStore(IConfiguration configuration)
    {
        connectionString = configuration["Ranking:ConnectionString"];
        usePostgres = !string.IsNullOrWhiteSpace(connectionString);
        databasePath = configuration["Ranking:DatabasePath"] ??
            Path.Combine(AppContext.BaseDirectory, "data", "ranking.db");
        EnsureDatabase();
    }

    public RankingSnapshot Snapshot()
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (cachedSnapshot != null && cachedSnapshot.ExpiresAt > now)
                return cachedSnapshot;

            using DbConnection connection = OpenConnection();
            using DbCommand command = connection.CreateCommand();
            string nameOrder = usePostgres
                ? "LOWER(display_name) ASC"
                : "display_name COLLATE NOCASE ASC";
            command.CommandText = $$"""
                SELECT display_name, games_started, wins, updated_at
                FROM ranking_players
                ORDER BY
                    wins DESC,
                    CASE WHEN games_started >= 3 THEN CAST(wins AS REAL) / games_started ELSE -1 END DESC,
                    games_started DESC,
                    {{nameOrder}}
                LIMIT 50;
                """;

            List<RankingEntry> entries = new();
            using (DbDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                    entries.Add(ReadEntry(reader));
            }

            RankingSummary summary = Summary(connection);

            cachedSnapshot = new RankingSnapshot(
                now,
                now.Add(SnapshotLifetime),
                entries,
                summary.GamesStarted,
                summary.Wins);
            return cachedSnapshot;
        }
    }

    public RankingSummary Summary()
    {
        lock (gate)
        {
            using DbConnection connection = OpenConnection();
            return Summary(connection);
        }
    }

    public RankingEntry? GetPlayer(string uid)
    {
        lock (gate)
        {
            using DbConnection connection = OpenConnection();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT display_name, games_started, wins, updated_at
                FROM ranking_players
                WHERE uid = @uid;
                """;
            AddParameter(command, "@uid", uid);

            using DbDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadEntry(reader) : null;
        }
    }

    public bool CreateLoadTestUser(LoadTestUserRequest request)
    {
        lock (gate)
        {
            using DbConnection connection = OpenConnection();
            using DbCommand existsCommand = connection.CreateCommand();
            existsCommand.CommandText = "SELECT 1 FROM ranking_players WHERE uid = @uid;";
            AddParameter(existsCommand, "@uid", request.Uid);
            if (existsCommand.ExecuteScalar() != null)
                return false;

            using DbCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ranking_players
                    (uid, display_name, picture, games_started, wins, created_at, updated_at)
                VALUES
                    (@uid, @displayName, @picture, @gamesStarted, @wins, @createdAt, @updatedAt);
                """;
            AddParameter(command, "@uid", request.Uid);
            AddParameter(command, "@displayName", request.DisplayName);
            AddParameter(command, "@picture", request.Picture ?? (object)DBNull.Value);
            AddParameter(command, "@gamesStarted", request.GamesStarted);
            AddParameter(command, "@wins", request.Wins);
            AddParameter(command, "@createdAt", request.CreatedAt.ToString("O"));
            AddParameter(command, "@updatedAt", request.UpdatedAt.ToString("O"));
            command.ExecuteNonQuery();
            return true;
        }
    }

    public void RecordGameStarted(FirebaseUser user)
    {
        lock (gate)
        {
            using DbConnection connection = OpenConnection();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ranking_players
                    (uid, display_name, games_started, wins, created_at, updated_at)
                VALUES
                    (@uid, @displayName, 1, 0, @now, @now)
                ON CONFLICT(uid) DO UPDATE SET
                    display_name = CASE
                        WHEN excluded.display_name <> '' THEN excluded.display_name
                        ELSE ranking_players.display_name
                    END,
                    games_started = ranking_players.games_started + 1,
                    updated_at = excluded.updated_at;
                """;
            AddPlayerParameters(command, user, DateTimeOffset.UtcNow);
            command.ExecuteNonQuery();
        }
    }

    public void RecordWin(string uid)
    {
        lock (gate)
        {
            using DbConnection connection = OpenConnection();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ranking_players
                SET wins = wins + 1,
                    updated_at = @now
                WHERE uid = @uid;
                """;
            AddParameter(command, "@uid", uid);
            AddParameter(command, "@now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public bool RecordLoadTestWin(string uid)
    {
        lock (gate)
        {
            using DbConnection connection = OpenConnection();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ranking_players
                SET games_started = games_started + 1,
                    wins = wins + 1,
                    updated_at = @updatedAt
                WHERE uid = @uid;
                """;
            AddParameter(command, "@uid", uid);
            AddParameter(command, "@updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            return command.ExecuteNonQuery() > 0;
        }
    }

    void EnsureDatabase()
    {
        lock (gate)
        {
            string? directory = usePostgres ? null : Path.GetDirectoryName(databasePath);
            if (directory != null && !string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using DbConnection connection = OpenConnection();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = usePostgres
                ? """
                CREATE TABLE IF NOT EXISTS ranking_players (
                    uid TEXT PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    picture TEXT NULL,
                    games_started BIGINT NOT NULL DEFAULT 0,
                    wins BIGINT NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_ranking_players_order
                ON ranking_players (wins DESC, games_started DESC, display_name ASC);
                """
                : """
                CREATE TABLE IF NOT EXISTS ranking_players (
                    uid TEXT PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    picture TEXT NULL,
                    games_started INTEGER NOT NULL DEFAULT 0,
                    wins INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_ranking_players_order
                ON ranking_players (wins DESC, games_started DESC, display_name COLLATE NOCASE ASC);
                """;
            command.ExecuteNonQuery();
        }
    }

    DbConnection OpenConnection()
    {
        DbConnection connection;
        if (usePostgres)
        {
            connection = new NpgsqlConnection(connectionString);
        }
        else
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = databasePath
            };
            connection = new SqliteConnection(builder.ToString());
        }

        connection.Open();
        return connection;
    }

    static RankingSummary Summary(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*),
                COALESCE(SUM(games_started), 0),
                COALESCE(SUM(wins), 0)
            FROM ranking_players;
            """;

        using DbDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return new RankingSummary(0, 0, 0);

        return new RankingSummary(
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt64(reader.GetValue(1)),
            Convert.ToInt64(reader.GetValue(2)));
    }

    static RankingEntry ReadEntry(DbDataReader reader)
    {
        long gamesStarted = Convert.ToInt64(reader.GetValue(1));
        long wins = Convert.ToInt64(reader.GetValue(2));

        return new RankingEntry(
            reader.GetString(0),
            gamesStarted,
            wins,
            gamesStarted == 0 ? 0 : Math.Round((double)wins / gamesStarted, 3),
            DateTimeOffset.Parse(reader.GetString(3)));
    }

    static void AddPlayerParameters(DbCommand command, FirebaseUser user, DateTimeOffset now)
    {
        AddParameter(command, "@uid", user.Uid);
        AddParameter(command, "@displayName", CleanName(user.Name) ?? "");
        AddParameter(command, "@now", now.ToString("O"));
    }

    static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    static string? CleanName(string? value)
    {
        string? name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (name == null)
            return null;

        string firstName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return firstName.Length <= 40 ? firstName : firstName[..40];
    }
}

sealed record LoadTestUserRequest(
    string Uid,
    string DisplayName,
    string? Picture,
    long GamesStarted,
    long Wins,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

sealed record RankingSnapshot(
    DateTimeOffset GeneratedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<RankingEntry> Players,
    long GamesStarted,
    long Wins);

sealed record RankingSummary(
    int Players,
    long GamesStarted,
    long Wins);

sealed record RankingEntry(
    string DisplayName,
    long GamesStarted,
    long Wins,
    double WinRate,
    [property: JsonIgnore] DateTimeOffset UpdatedAt);

sealed record FirebaseUser(
    string Uid,
    string? Name)
{
    public static string? UidFromClaims(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        static string? Claim(ClaimsPrincipal user, string type) =>
            user.Claims.FirstOrDefault(claim => claim.Type == type)?.Value;

        string? uid =
            Claim(user, "user_id") ??
            Claim(user, ClaimTypes.NameIdentifier) ??
            Claim(user, "sub");

        return string.IsNullOrWhiteSpace(uid) ? null : uid;
    }

    public static FirebaseUser FromClaims(ClaimsPrincipal user)
    {
        static string? Claim(ClaimsPrincipal user, string type) =>
            user.Claims.FirstOrDefault(claim => claim.Type == type)?.Value;

        string uid = UidFromClaims(user) ?? "";

        return new FirebaseUser(
            uid,
            Claim(user, "name") ?? Claim(user, ClaimTypes.Name));
    }

    public static FirebaseUser? TryFromClaims(ClaimsPrincipal user)
    {
        string? uid = UidFromClaims(user);
        return uid == null ? null : FromClaims(user);
    }
}
