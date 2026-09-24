using System.Globalization;
using System.Text.RegularExpressions;
using SportsRankingService.Models;

namespace SportsRankingService.Services.UrlResolvers;

/// <summary>
/// The WBSC rankings API only answers for an exact release date. www.wbsc.org/en/rankings embeds its
/// date dropdown as {"date","sport","year","formatted"} objects; the newest for the item's sportId
/// (read from the item's URL query) is formatted into the URL ({0}).
/// </summary>
public sealed partial class WbscReleaseDateResolver : IUrlResolver
{
    public const string ResolverName = "WbscReleaseDate";

    // rankings.wbsc.org redirects here permanently since 2026-09-24; the dropdown objects are embedded in this page.
    private const string RankingsPage = "https://www.wbsc.org/en/rankings";

    public string Name => ResolverName;

    public async Task<ResolvedUrl> ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken)
    {
        Match sport = SportIdQuery().Match(item.Url);
        if (!sport.Success)
        {
            throw new InvalidOperationException($"WBSC URL has no sportId query parameter: {item.Url}");
        }

        string html = (await fetcher.GetStringAsync(RankingsPage, cancellationToken))
            ?? throw new InvalidOperationException($"WBSC rankings page {RankingsPage} could not be fetched");

        string releaseDate = ExtractLatestReleaseDate(html, sport.Groups[1].Value);

        return new ResolvedUrl(
            string.Format(CultureInfo.InvariantCulture, item.Url, releaseDate),
            DateOnly.ParseExact(releaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture));
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
