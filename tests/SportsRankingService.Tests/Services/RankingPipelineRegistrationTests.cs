using Microsoft.Extensions.DependencyInjection;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

public class RankingPipelineRegistrationTests
{
    private static ServiceProvider Provider() =>
        new ServiceCollection().AddLogging().AddRankingPipeline().BuildServiceProvider();

    [Fact]
    public void Both_fetchers_are_registered_under_their_names()
    {
        using ServiceProvider provider = Provider();

        IEnumerable<string> names = provider.GetServices<IHttpFetcher>().Select(f => f.Name);

        Assert.Equal(["Curl", "Http"], names.Order());
    }

    /// <summary>The URL resolvers inject one <see cref="IHttpFetcher"/>; their preliminary requests must not start needing curl.</summary>
    [Fact]
    public void A_single_fetcher_dependency_is_the_HttpClient_one()
    {
        using ServiceProvider provider = Provider();

        Assert.Equal(HttpFetcher.FetcherName, provider.GetRequiredService<IHttpFetcher>().Name);
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
        Assert.Same(provider.GetRequiredService<IHttpFetcher>(), provider.GetServices<IHttpFetcher>().Single(f => f.Name == HttpFetcher.FetcherName));
    }
}
