using Microsoft.Extensions.DependencyInjection;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

public class RankingPipelineRegistrationTests
{
    private static ServiceProvider Provider() =>
        new ServiceCollection().AddLogging().AddRankingPipeline().BuildServiceProvider();

    [Fact]
    public void Every_fetcher_is_registered_under_its_name()
    {
        using ServiceProvider provider = Provider();

        IEnumerable<string> names = provider.GetServices<IHttpFetcher>().Select(f => f.Name);

        Assert.Equal(["Curl", "Http", "Proxy"], names.Order());
    }

    /// <summary>
    /// Feeds sharing a page (FIG, Wikipedia IIHF) and resolvers sharing a preliminary page (WBSC, SVNS)
    /// request it once per run, and only because every fetcher they can be handed is the caching one.
    /// </summary>
    [Fact]
    public void Every_fetcher_is_handed_out_behind_the_cache()
    {
        using ServiceProvider provider = Provider();

        Assert.All(provider.GetServices<IHttpFetcher>(), fetcher => Assert.IsType<CachingFetcher>(fetcher));
    }

    /// <summary>The game may wait for its paused database to resume before answering (about 40 seconds on staging), so the notifier waits longer than a fetch.</summary>
    [Fact]
    public void The_notify_client_waits_two_minutes()
    {
        using ServiceProvider provider = Provider();

        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(RefreshNotifier.ClientName);

        Assert.Equal(TimeSpan.FromSeconds(120), client.Timeout);
    }
}
