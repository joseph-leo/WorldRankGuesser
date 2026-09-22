using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Integration;

[Collection("sql")]
public class ReadinessTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Alive_at_once_and_ready_once_the_rankings_are_loaded()
    {
        await using var factory = new ApiFactory(sql.ConnectionString);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);   // the host started without waiting for a load

        await factory.WaitUntilReadyAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/readyz")).StatusCode);

        var snapshot = factory.Services.GetRequiredService<IRankingsStore>().Current;
        Assert.NotNull(snapshot);
        Assert.Equal(RankingsSeed.Countries, snapshot.DrawableCountries.Select(c => c.Iso3));
        Assert.Equal(RankingsSeed.PositionOf(0, 0), snapshot.Find("soccer", "AUS")!.BestByEntry.EntryRank);
    }

    [Fact]
    public async Task Alive_but_not_ready_when_the_rankings_view_is_missing()
    {
        await using var factory = new ApiFactory(sql.EmptyConnectionString);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/readyz")).StatusCode);
    }
}
