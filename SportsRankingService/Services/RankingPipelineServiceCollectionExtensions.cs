using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SportsRankingService.Parsers;
using SportsRankingService.Parsing;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Services;

public static class RankingPipelineServiceCollectionExtensions
{
    // Several federation sites (ATP, FIBA, IIHF) return 403 to a request with no browser User-Agent.
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36";

    /// <summary>Registers the fetcher, every parser and URL resolver, and the runner that ties them together.</summary>
    public static IServiceCollection AddRankingPipeline(this IServiceCollection services)
    {
        services.AddHttpClient(HttpFetcher.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        });
        // The runner indexes the fetchers by Name. HttpFetcher stays last: a single IHttpFetcher dependency
        // (the URL resolvers) gets the last registration, and their requests must not start needing curl.
        services.AddSingleton<IHttpFetcher, CurlFetcher>();
        services.AddSingleton<IHttpFetcher, HttpFetcher>();

        // Parsers are stateless; the runner indexes them by SourceName.
        services.AddSingleton<IRankingParser, BwfParser>();
        services.AddSingleton<IRankingParser, EspnTennisParser>();
        services.AddSingleton<IRankingParser, FibaParser>();
        services.AddSingleton<IRankingParser, FigParser>();
        services.AddSingleton<IRankingParser, FifaV3Parser>();
        services.AddSingleton<IRankingParser, FihParser>();
        services.AddSingleton<IRankingParser, IccParser>();
        services.AddSingleton<IRankingParser, SvnsParser>();
        services.AddSingleton<IRankingParser, VolleyballWorldParser>();
        services.AddSingleton<IRankingParser, WbscParser>();
        services.AddSingleton<IRankingParser, WikipediaIihfParser>();
        services.AddSingleton<IRankingParser, WorldRugbyParser>();
        services.AddSingleton<IRankingParser, WtaParser>();

        services.AddSingleton<IUrlResolver, IdentityUrlResolver>();
        services.AddSingleton<IUrlResolver, FifaDateIdResolver>();
        services.AddSingleton<IUrlResolver, SvnsSeriesResolver>();
        services.AddSingleton<IUrlResolver, WbscReleaseDateResolver>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RankingSourceRunner>();
        services.AddSingleton<IRankingSourceRunner>(sp => sp.GetRequiredService<RankingSourceRunner>());
        services.AddTransient<IRankingUpdater, RankingUpdater>();

        return services;
    }
}
