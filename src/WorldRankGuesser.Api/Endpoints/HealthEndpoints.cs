using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

        app.MapGet("/readyz", async (IRankingsStore store, GameDbContext db, CancellationToken ct) =>
        {
            var snapshot = store.Current;
            if (snapshot is null || !await db.Database.CanConnectAsync(ct))
            {
                return Results.Json(new { status = "not ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new { status = "ready", rankingsLoadedAt = snapshot.LoadedAt });
        }).ExcludeFromDescription();

        return app;
    }
}
