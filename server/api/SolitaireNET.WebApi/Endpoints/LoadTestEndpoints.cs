public static class LoadTestEndpoints
{
    public static void MapLoadTestEndpoints(this WebApplication app, bool enabled)
    {
        if (!enabled)
            return;

        app.MapPost("/api/loadtest/users", (LoadTestUserRequest request, RankingStore ranking) =>
        {
            if (!ranking.CreateLoadTestUser(request))
                return Results.Conflict(new { error = "User already exists." });

            return Results.Created($"/api/loadtest/users/{request.Uid}", request);
        });

        app.MapPost("/api/loadtest/users/{uid}/win", (string uid, RankingStore ranking) =>
        {
            if (!ranking.RecordLoadTestWin(uid))
                return Results.NotFound();

            return Results.Ok(new { uid, winAdded = true });
        });
    }
}
