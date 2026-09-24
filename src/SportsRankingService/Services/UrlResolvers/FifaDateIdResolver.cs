using System.Globalization;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;

namespace SportsRankingService.Services.UrlResolvers;

/// <summary>
/// FIFA's ranking API needs a ranking-schedule id. The men's and women's ranking pages embed the
/// available dates as Next.js page data; the newest one is formatted into the item's URL ({0}).
/// </summary>
public sealed class FifaDateIdResolver : IUrlResolver
{
    public const string ResolverName = "FifaDateId";

    private const string MenPage = "https://inside.fifa.com/fifa-rankings/world-ranking/men";
    private const string WomenPage = "https://inside.fifa.com/fifa-rankings/world-ranking/women";

    public string Name => ResolverName;

    public async Task<ResolvedUrl> ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken)
    {
        string page = item.Gender == "Women" ? WomenPage : MenPage;
        string html = (await fetcher.GetStringAsync(page, cancellationToken))
            ?? throw new InvalidOperationException($"FIFA ranking page {page} could not be fetched");

        SoccerRankDate latest = ExtractLatestDate(html);

        return new ResolvedUrl(string.Format(CultureInfo.InvariantCulture, item.Url, latest.id), ToRankingDate(latest));
    }

    /// <summary>
    /// Reads props.pageProps.pageData.ranking.dates (grouped by year) from the __NEXT_DATA__ script
    /// and returns the newest entry by ISO timestamp. Throws if the page has no such data.
    /// </summary>
    internal static SoccerRankDate ExtractLatestDate(string html)
    {
        HtmlDocument document = new();
        document.LoadHtml(html);

        string pageData = document.DocumentNode.SelectSingleNode("//script[@id=\"__NEXT_DATA__\"]").NotNullOrEmpty().InnerText;

        JToken yearGroups = JObject.Parse(pageData)
            .SelectToken("props.pageProps.pageData.ranking.dates")
            .NotNullOrEmpty();

        List<SoccerRankDate> dates = yearGroups
            .SelectMany(year => year["dates"]?.ToObject<List<SoccerRankDate>>() ?? [])
            .ToList()
            .NotNullOrEmpty();

        return dates.MaxBy(d => DateTimeOffset.Parse(d.iso, CultureInfo.InvariantCulture))!;
    }

    internal static string ExtractLatestDateId(string html) => ExtractLatestDate(html).id;

    /// <summary>The date FIFA displays for a release is its iso timestamp's UTC date; the digits in the id are not the ranking date.</summary>
    internal static DateOnly ToRankingDate(SoccerRankDate date) =>
        DateOnly.FromDateTime(DateTimeOffset.Parse(date.iso, CultureInfo.InvariantCulture).UtcDateTime);
}
