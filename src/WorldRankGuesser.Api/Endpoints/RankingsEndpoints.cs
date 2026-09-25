using System.Security.Cryptography;
using System.Text;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Endpoints;

/// <summary>
/// POST /api/rankings/refresh: re-read the rankings view and swap the in-memory snapshot in place, the same load the
/// background timer does, so a scrape reaches new boards at once with no restart (spec
/// docs/superpowers/specs/2026-09-25-rankings-refresh-notification-design.md). The scraper calls it at the end of a run
/// that stored a new release. Guarded by a shared token, compared in constant time before anything else; mapped
/// only when Rankings:RefreshToken is set, so a local run has no such route. The body names counts only, never a
/// country: the route sits on production's public ingress. Excluded from the OpenAPI document: not the front end's.
/// </summary>
public static class RankingsEndpoints
{
    public const string RefreshRoute = "/api/rankings/refresh";
    public const string TokenHeader = "X-Refresh-Token";

    public static IEndpointRouteBuilder MapRankingsEndpoints(this IEndpointRouteBuilder app, RankingsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RefreshToken))
        {
            return app;
        }

        var expected = Encoding.UTF8.GetBytes(options.RefreshToken);

        app.MapPost(RefreshRoute, async (HttpContext http, RankingsRefresher refresher, CancellationToken ct) =>
        {
            var given = Encoding.UTF8.GetBytes(http.Request.Headers[TokenHeader].ToString());
            if (!CryptographicOperations.FixedTimeEquals(given, expected))
            {
                return Results.Unauthorized();
            }

            var result = await refresher.RefreshAsync(ct);
            return result.Loaded
                ? Results.Ok(new { rows = result.Rows, drawableCountries = result.DrawableCountries, loadedAt = result.LoadedAt })
                : Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).ExcludeFromDescription();

        return app;
    }
}
