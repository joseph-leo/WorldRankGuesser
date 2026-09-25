using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SportsRankingService.Persistence;
using SportsRankingService.Services;
using WorldRankGuesser.Api.Endpoints;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// POST /api/rankings/refresh re-reads the view and swaps the snapshot in place, guarded by a shared token. Without
/// a configured token the route does not exist. The body names counts only, never a country, because the route sits
/// on production's public ingress.
/// </summary>
[Collection("sql")]
public class RankingsRefreshEndpointTests(SqlServerFixture sql)
{
    private const string Token = "test-refresh-token";

    private static ApiFactory WithToken(string connectionString) =>
        new(connectionString, new Dictionary<string, string?> { ["Rankings:RefreshToken"] = Token });

    private static HttpRequestMessage Refresh(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RankingsEndpoints.RefreshRoute);
        if (token is not null)
        {
            request.Headers.Add(RankingsEndpoints.TokenHeader, token);
        }

        return request;
    }

    [Fact]
    public async Task Without_a_configured_token_the_route_does_not_exist()
    {
        await using var factory = new ApiFactory(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();
        var client = factory.CreateClient();

        var response = await client.SendAsync(Refresh(Token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_header_is_401()
    {
        await using var factory = WithToken(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();
        var before = factory.Services.GetRequiredService<IRankingsStore>().Current;

        var response = await factory.CreateClient().PostAsync(RankingsEndpoints.RefreshRoute, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Same(before, factory.Services.GetRequiredService<IRankingsStore>().Current);
    }

    [Fact]
    public async Task A_wrong_token_is_401_and_reads_nothing()
    {
        await using var factory = WithToken(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();
        var before = factory.Services.GetRequiredService<IRankingsStore>().Current;

        var response = await factory.CreateClient().SendAsync(Refresh("wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Same(before, factory.Services.GetRequiredService<IRankingsStore>().Current);
    }

    [Fact]
    public async Task The_right_token_reloads_the_snapshot_in_place_and_answers_the_counts()
    {
        await using var factory = WithToken(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();
        var store = factory.Services.GetRequiredService<IRankingsStore>();
        var before = store.Current!;
        var client = factory.CreateClient();

        var first = await client.SendAsync(Refresh(Token));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = (await first.Content.ReadFromJsonAsync<JsonElement>());
        var rowsBefore = firstBody.GetProperty("rows").GetInt32();
        Assert.Equal(before.DrawableCountries.Count, firstBody.GetProperty("drawableCountries").GetInt32());

        // A feed no category covers: one more row in the view, no change to any board, no other test disturbed.
        await using (var db = SqlServerFixture.CreateRankingsContext(sql.ConnectionString))
        {
            var outcome = await new RankingRepository(db, TimeProvider.System).SaveAsync(
                new RankingSnapshot("Chess", null, "Men", new DateOnly(2026, 9, 25), IsFederationDate: true,
                    [new RankingSnapshotEntry(1, "NOR", "Norway")]),
                CancellationToken.None);
            Assert.Equal(SaveOutcome.Inserted, outcome);
        }

        var second = await client.SendAsync(Refresh(Token));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(rowsBefore + 1, secondBody.GetProperty("rows").GetInt32());
        Assert.NotSame(before, store.Current);
        Assert.True(secondBody.GetProperty("loadedAt").GetDateTimeOffset() > before.LoadedAt);
        Assert.Equal(before.DrawableCountries.Select(c => c.Iso3), store.Current!.DrawableCountries.Select(c => c.Iso3));
    }

    [Fact]
    public async Task The_response_names_no_country()
    {
        await using var factory = WithToken(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();

        var body = await (await factory.CreateClient().SendAsync(Refresh(Token))).Content.ReadAsStringAsync();

        foreach (var iso3 in RankingsSeed.Countries)
        {
            Assert.DoesNotContain(iso3, body);
        }
    }

    [Fact]
    public async Task A_view_that_cannot_be_read_answers_503_and_keeps_the_snapshot()
    {
        await using var factory = WithToken(sql.EmptyConnectionString);
        var client = factory.CreateClient();

        var response = await client.SendAsync(Refresh(Token));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
        Assert.Null(factory.Services.GetRequiredService<IRankingsStore>().Current);
    }
}
