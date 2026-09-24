using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    /// <summary>
    /// Data protection registers a hosted service that preloads its key ring at startup. With the keys in the database,
    /// that preload holds host startup for as long as a paused Azure SQL database takes to resume (about 40 seconds on
    /// staging, 2026-09-24); the web server starts only after the hosted services, so the startup probe killed the
    /// container before it listened. The preload is removed: the key ring loads on first use, at the first game start.
    /// </summary>
    [Fact]
    public async Task Startup_does_not_preload_the_data_protection_key_ring()
    {
        await using var factory = new ApiFactory(sql.ConnectionString);
        _ = factory.CreateClient();                              // starts the host

        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), s => s.GetType().Name == "DataProtectionHostedService");
    }
}
