using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SportsRankingService.Configuration;
using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Tests.Configuration;

/// <summary>
/// The committed serviceconfig.json (copied next to the test binaries with the scraper's output) names only
/// registered parsers, resolvers and fetchers on its enabled items, so a typo fails here rather than in the weekly
/// run. A disabled item is kept as documentation and may name a parser that is gone (ATP doubles), as the runner
/// never sees it.
/// </summary>
public class ServiceConfigTests
{
    private static List<RankingItem> Items()
    {
        var options = new RankingSourcesOptions();
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "serviceconfig.json"))
            .Build()
            .Bind(options);
        return options.Rankings;
    }

    [Fact]
    public void Every_enabled_item_names_a_registered_parser_resolver_and_fetcher()
    {
        using ServiceProvider provider = new ServiceCollection().AddLogging().AddRankingPipeline().BuildServiceProvider();
        HashSet<string> parsers = provider.GetServices<IRankingParser>().Select(p => p.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> resolvers = provider.GetServices<IUrlResolver>().Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> fetchers = provider.GetServices<IHttpFetcher>().Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<RankingItem> items = Items().Where(i => i.Enabled).ToList();

        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            Assert.Contains(item.Source, parsers);
            Assert.Contains(item.UrlResolver, resolvers);
            Assert.Contains(item.Fetcher, fetchers);
        });
    }

    /// <summary>CloudFront refuses Azure addresses for www.wbsc.org (2026-09-24); the Worker in proxy/ is their egress.</summary>
    [Fact]
    public void The_five_wbsc_feeds_are_enabled_through_the_proxy()
    {
        List<RankingItem> wbsc = Items().Where(i => i.Source == "Wbsc").ToList();

        Assert.Equal(5, wbsc.Count);
        Assert.All(wbsc, item =>
        {
            Assert.True(item.Enabled, item.Describe());
            Assert.Equal(ProxyFetcher.FetcherName, item.Fetcher);
        });
    }
}
