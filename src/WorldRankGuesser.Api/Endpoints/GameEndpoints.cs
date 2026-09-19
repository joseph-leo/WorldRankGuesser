using Microsoft.AspNetCore.Http.HttpResults;
using WorldRankGuesser.Api.Games;
using WorldRankGuesser.Api.Players;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Endpoints;

public static class GameEndpoints
{
    public const string StartGameRoute = "/api/games";

    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(StartGameRoute, StartGame).WithName("StartGame");
        app.MapGet("/api/games/{id:guid}", GetGame).WithName("GetGame");
        app.MapPost("/api/games/{id:guid}/picks", Pick).WithName("Pick");

        // Anything else under /api is a 404, never the front end's index.html.
        app.Map("/api/{**rest}", () => Results.NotFound()).ExcludeFromDescription();

        return app;
    }

    private static async Task<Results<Ok<GameStateDto>, ValidationProblem, ProblemHttpResult>> StartGame(
        StartGameRequest request,
        HttpContext http,
        IRankingsStore rankings,
        PlayerService players,
        GameService games,
        CancellationToken ct)
    {
        if (request.Mode != "practice")
        {
            var message = request.Mode == "daily" ? "The daily challenge is not available yet." : "mode must be \"practice\".";
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = [message] });
        }

        // Checked before a player is created, so a not-ready API leaves no orphan players behind.
        if (rankings.Current is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The rankings are not loaded yet.");
        }

        var playerId = await PlayerIdentity.EnsurePlayerAsync(http, players, ct);

        return TypedResults.Ok(await games.StartPracticeAsync(playerId, ct));
    }

    private static async Task<Results<Ok<GameStateDto>, NotFound>> GetGame(
        Guid id, HttpContext http, GameService games, CancellationToken ct)
    {
        if (http.User.GetPlayerId() is not { } playerId) return TypedResults.NotFound();

        var state = await games.GetAsync(playerId, id, ct);

        return state is null ? TypedResults.NotFound() : TypedResults.Ok(state);
    }

    private static async Task<Results<Ok<GameStateDto>, NotFound, Conflict<GameStateDto>, ValidationProblem>> Pick(
        Guid id, PickRequest request, HttpContext http, GameService games, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CategoryId)) return UnknownCategory();
        if (http.User.GetPlayerId() is not { } playerId) return TypedResults.NotFound();

        return await games.PickAsync(playerId, id, request.CategoryId, ct) switch
        {
            PickResult.Ok ok => TypedResults.Ok(ok.State),
            PickResult.Conflict conflict => TypedResults.Conflict(conflict.State),
            PickResult.UnknownCategory => UnknownCategory(),
            _ => TypedResults.NotFound(),
        };

        static ValidationProblem UnknownCategory() =>
            TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["categoryId"] = ["Unknown category."] });
    }
}
