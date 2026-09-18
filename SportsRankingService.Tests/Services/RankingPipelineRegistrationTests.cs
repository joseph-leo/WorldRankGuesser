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

        Assert.IsType<HttpFetcher>(provider.GetRequiredService<IHttpFetcher>());
    }
}
