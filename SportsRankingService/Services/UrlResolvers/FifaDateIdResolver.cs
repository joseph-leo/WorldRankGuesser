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
public sealed class FifaDateIdResolver(IHttpFetcher fetcher) : IUrlResolver
{
    public const string ResolverName = "FifaDateId";

    private const string MenPage = "https://inside.fifa.com/fifa-rankings/world-ranking/men";
    private const string WomenPage = "https://inside.fifa.com/fifa-rankings/world-ranking/women";

    public string Name => ResolverName;

    public async Task<string> ResolveAsync(RankingItem item, CancellationToken cancellationToken)
    {
        string page = item.Gender == "Women" ? WomenPage : MenPage;
        string html = (await fetcher.GetStringAsync(page, cancellationToken))
            ?? throw new InvalidOperationException($"FIFA ranking page {page} could not be fetched");

        return string.Format(CultureInfo.InvariantCulture, item.Url, ExtractLatestDateId(html));
    }

    /// <summary>
    /// Reads props.pageProps.pageData.ranking.dates (grouped by year) from the __NEXT_DATA__ script
    /// and returns the id of the newest entry by ISO timestamp. Throws if the page has no such data.
    /// </summary>
    internal static string ExtractLatestDateId(string html)
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

        return dates.MaxBy(d => DateTimeOffset.Parse(d.iso, CultureInfo.InvariantCulture))!.id;
    }
}
