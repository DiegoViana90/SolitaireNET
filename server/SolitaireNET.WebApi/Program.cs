using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
string? firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
bool firebaseAuthEnabled = !string.IsNullOrWhiteSpace(firebaseProjectId);

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

sealed class CheckersStore
{
    readonly object gate = new();
    readonly ConcurrentDictionary<string, CheckersSession> rooms = new();
    string? waitingRandomCode;

    public CheckersJoinResult CreatePrivateRoom()
    {
        CheckersSession room = CreateRoom();
        return room.AddPlayer();
    }

    public CheckersJoinResult JoinRoom(string code)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out CheckersSession? room))
            return CheckersJoinResult.Fail(code, "Sala nao encontrada.");

        return room.AddPlayer();
    }

    public CheckersJoinResult JoinRandomRoom()
    {
        lock (gate)
        {
            if (waitingRandomCode != null &&
                rooms.TryGetValue(waitingRandomCode, out CheckersSession? waitingRoom) &&
                waitingRoom.IsWaiting)
            {
                waitingRandomCode = null;
                return waitingRoom.AddPlayer();
            }

            CheckersSession room = CreateRoom();
            waitingRandomCode = room.Code;
            return room.AddPlayer();
        }
    }

    public CheckersJoinResult GetRoom(string code, string playerId)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out CheckersSession? room))
            return CheckersJoinResult.Fail(code, "Sala nao encontrada.");

        return room.ToJoinResult(playerId);
    }

    public CheckersJoinResult ApplyMove(string code, CheckersMoveAction action)
    {
        code = NormalizeCode(code);
        if (!rooms.TryGetValue(code, out CheckersSession? room))
            return CheckersJoinResult.Fail(code, "Sala nao encontrada.");

        return room.ApplyMove(action);
    }

    CheckersSession CreateRoom()
    {
        string code;
        do
        {
            code = CreateCode();
        } while (rooms.ContainsKey(code));

        var room = CheckersSession.New(code);
        rooms[code] = room;
        return room;
    }

    static string CreateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> value = stackalloc char[4];
        for (int i = 0; i < value.Length; i++)
            value[i] = chars[Random.Shared.Next(chars.Length)];
        return new string(value);
    }

    static string NormalizeCode(string code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();
}

sealed class CheckersSession
{
    readonly object gate = new();
    readonly CheckersPiece?[,] board = new CheckersPiece?[8, 8];

    CheckersSession(string code)
    {
        Code = code;
        Deal();
    }

    public string Code { get; }
    public string? LightPlayerId { get; private set; }
    public string? DarkPlayerId { get; private set; }
    public string Turn { get; private set; } = "light";
    public string? ForcedPieceId { get; private set; }
    public string? Winner { get; private set; }
    public bool IsWaiting => LightPlayerId != null && DarkPlayerId == null;

    public static CheckersSession New(string code) => new(code);

    public CheckersJoinResult AddPlayer()
    {
        lock (gate)
        {
            string playerId = Guid.NewGuid().ToString("N");
            string side;

            if (LightPlayerId == null)
            {
                LightPlayerId = playerId;
                side = "light";
            }
            else if (DarkPlayerId == null)
            {
                DarkPlayerId = playerId;
                side = "dark";
            }
            else
            {
                return CheckersJoinResult.Fail(Code, "Sala cheia.");
            }

            return BuildJoinResult(playerId, side);
        }
    }

    public CheckersJoinResult ToJoinResult(string playerId)
    {
        lock (gate)
        {
            string? side = SideFor(playerId);
            return side == null
                ? CheckersJoinResult.Fail(Code, "Jogador nao pertence a esta sala.")
                : BuildJoinResult(playerId, side);
        }
    }

    public CheckersJoinResult ApplyMove(CheckersMoveAction action)
    {
        lock (gate)
        {
            string? side = SideFor(action.PlayerId);
            if (side == null)
                return CheckersJoinResult.Fail(Code, "Jogador nao pertence a esta sala.");

            if (!IsReady)
                return CheckersJoinResult.Fail(Code, "Aguardando segundo jogador.");

            if (Winner != null)
                return CheckersJoinResult.Fail(Code, "Partida finalizada.");

            if (side != Turn)
                return CheckersJoinResult.Fail(Code, "Aguarde sua vez.");

            CheckersPiece? piece = PieceAt(action.From.Row, action.From.Col);
            if (piece == null || piece.Owner != side)
                return CheckersJoinResult.Fail(Code, "Peca de origem invalida.");

            if (ForcedPieceId != null && piece.Id != ForcedPieceId)
                return CheckersJoinResult.Fail(Code, "Continue a captura com a mesma peca.");

            List<CheckersMove> legalMoves = GetMovesByPiece(side)
                .SelectMany(pair => pair.Value)
                .ToList();

            CheckersMove? move = legalMoves.FirstOrDefault(candidate =>
                candidate.From.Row == action.From.Row &&
                candidate.From.Col == action.From.Col &&
                candidate.To.Row == action.To.Row &&
                candidate.To.Col == action.To.Col);

            if (move == null)
                return CheckersJoinResult.Fail(Code, "Jogada invalida.");

            ApplyLegalMove(piece, move);
            return BuildJoinResult(action.PlayerId, side);
        }
    }

    bool IsReady => LightPlayerId != null && DarkPlayerId != null;

    void Deal()
    {
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (IsDarkSquare(row, col))
                    board[row, col] = new CheckersPiece($"dark-{row}-{col}", "dark", false);
            }
        }

        for (int row = 5; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (IsDarkSquare(row, col))
                    board[row, col] = new CheckersPiece($"light-{row}-{col}", "light", false);
            }
        }
    }

    void ApplyLegalMove(CheckersPiece piece, CheckersMove move)
    {
        board[move.From.Row, move.From.Col] = null;
        board[move.To.Row, move.To.Col] = piece;

        if (move.Captured != null)
            board[move.Captured.Row, move.Captured.Col] = null;

        PromoteIfNeeded(piece, move.To.Row);

        bool canContinueCapture =
            move.Captured != null &&
            GetMovesForPiece(move.To.Row, move.To.Col, onlyCaptures: true).Count > 0;

        if (canContinueCapture)
        {
            ForcedPieceId = piece.Id;
            return;
        }

        ForcedPieceId = null;
        Turn = Opponent(Turn);
        Winner = ResolveWinner();
    }

    void PromoteIfNeeded(CheckersPiece piece, int row)
    {
        if (piece.King)
            return;

        if ((piece.Owner == "light" && row == 0) ||
            (piece.Owner == "dark" && row == 7))
        {
            piece.King = true;
        }
    }

    Dictionary<string, List<CheckersMove>> GetMovesByPiece(string owner)
    {
        Dictionary<string, List<CheckersMove>> captures = new();
        Dictionary<string, List<CheckersMove>> regular = new();

        ForEachPiece((piece, row, col) =>
        {
            if (piece.Owner != owner)
                return;

            if (ForcedPieceId != null && piece.Id != ForcedPieceId)
                return;

            List<CheckersMove> captureMoves = GetMovesForPiece(row, col, onlyCaptures: true);
            if (captureMoves.Count > 0)
            {
                captures[piece.Id] = captureMoves;
                return;
            }

            List<CheckersMove> moves = GetMovesForPiece(row, col, onlyCaptures: false);
            if (moves.Count > 0)
                regular[piece.Id] = moves;
        });

        return captures.Count > 0 ? captures : regular;
    }

    List<CheckersMove> GetMovesForPiece(int row, int col, bool onlyCaptures)
    {
        CheckersPiece? piece = PieceAt(row, col);
        if (piece == null)
            return new List<CheckersMove>();

        List<CheckersMove> moves = new();
        IEnumerable<(int Row, int Col)> directions = onlyCaptures
            ? CaptureDirections(piece)
            : MoveDirections(piece);

        foreach ((int dr, int dc) in directions)
        {
            int stepRow = row + dr;
            int stepCol = col + dc;
            int jumpRow = row + dr * 2;
            int jumpCol = col + dc * 2;
            CheckersPiece? stepPiece = PieceAt(stepRow, stepCol);

            if (stepPiece != null &&
                stepPiece.Owner != piece.Owner &&
                IsInside(jumpRow, jumpCol) &&
                PieceAt(jumpRow, jumpCol) == null)
            {
                moves.Add(new CheckersMove(
                    new CheckersPosition(row, col),
                    new CheckersPosition(jumpRow, jumpCol),
                    new CheckersPosition(stepRow, stepCol)));
                continue;
            }

            if (!onlyCaptures && IsInside(stepRow, stepCol) && stepPiece == null)
            {
                moves.Add(new CheckersMove(
                    new CheckersPosition(row, col),
                    new CheckersPosition(stepRow, stepCol),
                    null));
            }
        }

        return moves;
    }

    string? ResolveWinner()
    {
        int light = 0;
        int dark = 0;
        ForEachPiece((piece, _, _) =>
        {
            if (piece.Owner == "light")
                light++;
            else
                dark++;
        });

        if (light == 0)
            return "dark";
        if (dark == 0)
            return "light";
        if (GetMovesByPiece(Turn).Count == 0)
            return Opponent(Turn);

        return null;
    }

    CheckersJoinResult BuildJoinResult(string playerId, string side) =>
        new(
            Code,
            playerId,
            side,
            !IsReady,
            null,
            ToPublicState(side));

    CheckersPublicState ToPublicState(string playerSide)
    {
        List<List<CheckersPublicPiece?>> rows = new();
        for (int row = 0; row < 8; row++)
        {
            List<CheckersPublicPiece?> current = new();
            for (int col = 0; col < 8; col++)
            {
                CheckersPiece? piece = board[row, col];
                current.Add(piece == null
                    ? null
                    : new CheckersPublicPiece(piece.Id, piece.Owner, piece.King));
            }
            rows.Add(current);
        }

        return new CheckersPublicState(
            rows,
            Turn,
            playerSide,
            IsReady,
            ForcedPieceId,
            Winner);
    }

    string? SideFor(string playerId)
    {
        if (playerId == LightPlayerId)
            return "light";
        if (playerId == DarkPlayerId)
            return "dark";
        return null;
    }

    CheckersPiece? PieceAt(int row, int col) =>
        IsInside(row, col) ? board[row, col] : null;

    void ForEachPiece(Action<CheckersPiece, int, int> callback)
    {
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
                if (board[row, col] is { } piece)
                    callback(piece, row, col);
    }

    static IEnumerable<(int Row, int Col)> MoveDirections(CheckersPiece piece)
    {
        if (piece.King)
            return AllDirections;

        int dr = piece.Owner == "light" ? -1 : 1;
        return new[] { (dr, 1), (dr, -1) };
    }

    static IEnumerable<(int Row, int Col)> CaptureDirections(CheckersPiece piece) =>
        piece.King ? AllDirections : AllDirections;

    static readonly (int Row, int Col)[] AllDirections =
    {
        (1, 1),
        (1, -1),
        (-1, 1),
        (-1, -1)
    };

    static string Opponent(string owner) => owner == "light" ? "dark" : "light";
    static bool IsInside(int row, int col) => row >= 0 && row < 8 && col >= 0 && col < 8;
    static bool IsDarkSquare(int row, int col) => (row + col) % 2 == 1;
}

sealed record CheckersJoinResult(
    string RoomCode,
    string? PlayerId,
    string? PlayerSide,
    bool Waiting,
    string? Error,
    CheckersPublicState? State)
{
    public static CheckersJoinResult Fail(string roomCode, string error) =>
        new(roomCode, null, null, false, error, null);
}

sealed record CheckersPublicState(
    List<List<CheckersPublicPiece?>> Board,
    string Turn,
    string PlayerSide,
    bool Ready,
    string? ForcedPieceId,
    string? Winner);

sealed record CheckersPublicPiece(string Id, string Owner, bool King);
sealed record CheckersMoveAction(string PlayerId, CheckersPosition From, CheckersPosition To);
sealed record CheckersMove(CheckersPosition From, CheckersPosition To, CheckersPosition? Captured);
sealed record CheckersPosition(int Row, int Col);

sealed class CheckersPiece(string id, string owner, bool king)
{
    public string Id { get; } = id;
    public string Owner { get; } = owner;
    public bool King { get; set; } = king;
}

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

sealed class CleanupService(GameStore games, PlayerPresenceStore players) : BackgroundService
{
    static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            games.RemoveExpired(now);
            players.RemoveExpired(now);
        }
    }
}

sealed class GameSession
{
    readonly object gate = new();
    readonly List<Card> stock = new();
    readonly List<Card> waste = new();
    readonly List<Card>[] tableau = Enumerable.Range(0, 7).Select(_ => new List<Card>()).ToArray();
    readonly List<Card>[] foundations = Enumerable.Range(0, 4).Select(_ => new List<Card>()).ToArray();

    GameSession(string id, string? ownerUid)
    {
        Id = id;
        OwnerUid = ownerUid;
        CreatedAt = DateTimeOffset.UtcNow;
        LastActivityAt = CreatedAt;
    }

    public string Id { get; }
    public string? OwnerUid { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool IsCompleted => CompletedAt.HasValue;

    public void Touch()
    {
        lock (gate)
            LastActivityAt = DateTimeOffset.UtcNow;
    }

    public static GameSession New(string? ownerUid)
    {
        var game = new GameSession(Guid.NewGuid().ToString("N"), ownerUid);
        game.Deal();
        return game;
    }

    public PublicGameState ToPublicState()
    {
        lock (gate)
        {
            return new PublicGameState(
                Id,
                stock.Count,
                waste.Count,
                waste.Count > 0 ? PublicCard.FromVisible(waste[^1]) : null,
                tableau.Select(pile => pile.Select(PublicCard.From).ToList()).ToList(),
                foundations.Select(pile => pile.Count > 0 ? PublicCard.FromVisible(pile[^1]) : null).ToList(),
                foundations.Sum(pile => pile.Count) == 52,
                OwnerUid != null);
        }
    }

    public bool IsOwnedByDifferentUser(string? uid)
    {
        lock (gate)
            return OwnerUid != null && uid != null && OwnerUid != uid;
    }

    public void DisableRankingIfSignedOut(string? uid)
    {
        lock (gate)
        {
            if (OwnerUid != null && uid == null)
                OwnerUid = null;
        }
    }

    public MoveResult Apply(GameAction action)
    {
        lock (gate)
        {
            bool wasWon = IsWon();
            MoveResult result = action.Type switch
            {
                "drawStock" => DrawStock(),
                "resetStock" => ResetStock(),
                "flipTableau" => FlipTableau(action.Source?.Index ?? -1),
                "move" => Move(action.Source, action.Target),
                _ => MoveResult.Fail("Unknown action")
            };

            if (!result.Ok)
                return result;

            LastActivityAt = DateTimeOffset.UtcNow;
            bool wonNow = !wasWon && IsWon();
            if (wonNow)
                CompletedAt = LastActivityAt;

            return result with { WonNow = wonNow };
        }
    }

    bool IsWon() => foundations.Sum(pile => pile.Count) == 52;

    void Deal()
    {
        List<Card> deck = new();
        foreach (string suit in new[] { "S", "H", "D", "C" })
        {
            for (int rank = 1; rank <= 13; rank++)
                deck.Add(new Card(rank, suit));
        }

        Shuffle(deck);
        stock.AddRange(deck);

        for (int col = 0; col < 7; col++)
        {
            for (int row = 0; row <= col; row++)
            {
                Card card = stock[^1];
                stock.RemoveAt(stock.Count - 1);
                card.FaceUp = row == col;
                tableau[col].Add(card);
            }
        }
    }

    static void Shuffle(List<Card> deck)
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    MoveResult DrawStock()
    {
        if (stock.Count == 0)
            return MoveResult.Fail("Stock is empty");

        Card card = stock[^1];
        stock.RemoveAt(stock.Count - 1);
        card.FaceUp = true;
        waste.Add(card);

        return MoveResult.Success();
    }

    MoveResult ResetStock()
    {
        if (stock.Count > 0)
            return MoveResult.Fail("Stock is not empty");

        if (waste.Count == 0)
            return MoveResult.Fail("Waste is empty");

        while (waste.Count > 0)
        {
            Card card = waste[^1];
            waste.RemoveAt(waste.Count - 1);
            card.FaceUp = false;
            stock.Add(card);
        }

        return MoveResult.Success();
    }

    MoveResult FlipTableau(int column)
    {
        if (column < 0 || column >= tableau.Length)
            return MoveResult.Fail("Invalid tableau column");

        List<Card> pile = tableau[column];
        if (pile.Count == 0 || pile[^1].FaceUp)
            return MoveResult.Fail("No hidden top card to flip");

        pile[^1].FaceUp = true;
        return MoveResult.Success();
    }

    MoveResult Move(PileRef? source, PileRef? target)
    {
        if (source == null || target == null)
            return MoveResult.Fail("Missing source or target");

        MoveSelection? selection = TryBuildSelection(source);
        if (selection == null)
            return MoveResult.Fail("Invalid source");

        Card first = selection.Cards[0];

        bool canMove = target.Kind switch
        {
            "foundation" => selection.Cards.Count == 1 &&
                            target.Index >= 0 &&
                            target.Index < foundations.Length &&
                            CanMoveToFoundation(first, foundations[target.Index]),

            "tableau" => target.Index >= 0 &&
                         target.Index < tableau.Length &&
                         CanMoveToTableau(first, tableau[target.Index]),

            _ => false
        };

        if (!canMove)
            return MoveResult.Fail("Move is not allowed");

        RemoveSelection(selection);
        FlipExposedTableauTop(selection.Source);

        if (target.Kind == "foundation")
            foundations[target.Index].AddRange(selection.Cards);

        if (target.Kind == "tableau")
            tableau[target.Index].AddRange(selection.Cards);

        return MoveResult.Success();
    }

    MoveSelection? TryBuildSelection(PileRef source)
    {
        if (source.Kind == "waste")
        {
            if (waste.Count == 0)
                return null;

            return new MoveSelection(source, new List<Card> { waste[^1] });
        }

        if (source.Kind == "foundation")
        {
            if (source.Index < 0 || source.Index >= foundations.Length || foundations[source.Index].Count == 0)
                return null;

            return new MoveSelection(source, new List<Card> { foundations[source.Index][^1] });
        }

        if (source.Kind == "tableau")
        {
            if (source.Index < 0 || source.Index >= tableau.Length)
                return null;

            List<Card> pile = tableau[source.Index];
            int row = source.Row ?? -1;
            if (row < 0 || row >= pile.Count || !pile[row].FaceUp)
                return null;

            return new MoveSelection(source, pile.Skip(row).ToList());
        }

        return null;
    }

    void RemoveSelection(MoveSelection selection)
    {
        PileRef source = selection.Source;

        if (source.Kind == "waste")
            waste.RemoveAt(waste.Count - 1);

        if (source.Kind == "foundation")
            foundations[source.Index].RemoveAt(foundations[source.Index].Count - 1);

        if (source.Kind == "tableau")
            tableau[source.Index].RemoveRange(source.Row!.Value, tableau[source.Index].Count - source.Row.Value);
    }

    void FlipExposedTableauTop(PileRef source)
    {
        if (source.Kind != "tableau")
            return;

        List<Card> pile = tableau[source.Index];
        if (pile.Count > 0 && !pile[^1].FaceUp)
            pile[^1].FaceUp = true;
    }

    static bool CanMoveToFoundation(Card card, List<Card> foundation)
    {
        if (foundation.Count == 0)
            return card.Rank == 1;

        Card top = foundation[^1];
        return top.Suit == card.Suit && card.Rank == top.Rank + 1;
    }

    static bool CanMoveToTableau(Card card, List<Card> pile)
    {
        if (pile.Count == 0)
            return card.Rank == 13;

        Card top = pile[^1];
        return top.FaceUp &&
               top.IsRed != card.IsRed &&
               card.Rank == top.Rank - 1;
    }

}

sealed record MoveSelection(PileRef Source, List<Card> Cards);

sealed record GameAction(string Type, PileRef? Source, PileRef? Target);

sealed record PileRef(string Kind, int Index, int? Row);

sealed record PublicGameState(
    string Id,
    int StockCount,
    int WasteCount,
    PublicCard? WasteTop,
    List<List<PublicCard>> Tableau,
    List<PublicCard?> Foundations,
    bool Won,
    bool Ranked);

sealed record PublicCard(string? Id, int? Rank, string? Suit, bool FaceUp)
{
    public static PublicCard From(Card card)
    {
        return card.FaceUp
            ? FromVisible(card)
            : new PublicCard(null, null, null, false);
    }

    public static PublicCard FromVisible(Card card)
    {
        return new PublicCard(card.Id, card.Rank, card.Suit, true);
    }
}

sealed record MoveResult(bool Ok, string? Error, bool WonNow = false)
{
    public static MoveResult Success() => new(true, null);
    public static MoveResult Fail(string error) => new(false, error);
}

sealed class Card
{
    public Card(int rank, string suit)
    {
        Rank = rank;
        Suit = suit;
        Id = $"{suit}{rank}";
    }

    public string Id { get; }
    public int Rank { get; }
    public string Suit { get; }
    public bool FaceUp { get; set; }
    public bool IsRed => Suit is "H" or "D";
}
