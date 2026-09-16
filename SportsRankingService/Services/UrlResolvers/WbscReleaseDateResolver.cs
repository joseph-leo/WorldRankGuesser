using System.Globalization;
using System.Text.RegularExpressions;
using SportsRankingService.Models;

namespace SportsRankingService.Services.UrlResolvers;

/// <summary>
/// The WBSC rankings API only answers for an exact release date. rankings.wbsc.org embeds its
/// date dropdown as {"date","sport","year","formatted"} objects; the newest for the item's sportId
/// (read from the item's URL query) is formatted into the URL ({0}).
/// </summary>
public sealed partial class WbscReleaseDateResolver(IHttpFetcher fetcher) : IUrlResolver
{
    public const string ResolverName = "WbscReleaseDate";

    private const string RankingsPage = "https://rankings.wbsc.org/";

    public string Name => ResolverName;

    public async Task<string> ResolveAsync(RankingItem item, CancellationToken cancellationToken)
    {
        Match sport = SportIdQuery().Match(item.Url);
        if (!sport.Success)
        {
            throw new InvalidOperationException($"WBSC URL has no sportId query parameter: {item.Url}");
        }

        string html = (await fetcher.GetStringAsync(RankingsPage, cancellationToken))
            ?? throw new InvalidOperationException($"WBSC rankings page {RankingsPage} could not be fetched");

        return string.Format(CultureInfo.InvariantCulture, item.Url, ExtractLatestReleaseDate(html, sport.Groups[1].Value));
    }

    internal static string ExtractLatestReleaseDate(string html, string sportId)
    {
        string? latest = ReleaseDate().Matches(html)
            .Where(m => m.Groups[2].Value == sportId)
            .Select(m => m.Groups[1].Value)
            .Max();

        return latest ?? throw new InvalidOperationException($"WBSC rankings page lists no release dates for sport '{sportId}'");
    }

    [GeneratedRegex(@"\{""date"":""(\d{4}-\d{2}-\d{2})"",""sport"":""([a-z0-9-]+)""")]
    private static partial Regex ReleaseDate();

    [GeneratedRegex(@"[?&]sportId=([a-z0-9-]+)")]
    private static partial Regex SportIdQuery();
}
