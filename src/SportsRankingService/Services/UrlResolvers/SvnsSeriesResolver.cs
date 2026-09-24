using System.Text.Json;
using HtmlAgilityPack;
using SportsRankingService.Models;

namespace SportsRankingService.Services.UrlResolvers;

/// <summary>
/// The SVNS standings page carries the current season's World Rugby series ids (one per sex) in a
/// data attribute. Each series' metadata says whether it is men's ("mrs") or women's ("wrs"); the
/// one matching the item's gender is formatted into the item's URL ({0}).
/// </summary>
public sealed class SvnsSeriesResolver : IUrlResolver
{
    public const string ResolverName = "SvnsSeries";

    private const string StandingsPage = "https://www.svns.com/en/standings";
    private const string SeriesApi = "https://api.wr-rims-prod.pulselive.com/rugby/v3/series/{0}";

    public string Name => ResolverName;

    public async Task<ResolvedUrl> ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken)
    {
        string wanted = item.Gender == "Women" ? "wrs" : "mrs";

        string html = (await fetcher.GetStringAsync(StandingsPage, cancellationToken))
            ?? throw new InvalidOperationException($"SVNS standings page {StandingsPage} could not be fetched");

        foreach (string id in ExtractSeriesIds(html))
        {
            string? series = await fetcher.GetStringAsync(string.Format(SeriesApi, id), cancellationToken);
            if (series is not null && ReadSportCode(series) == wanted)
            {
                return new ResolvedUrl(string.Format(item.Url, id));
            }
        }

        throw new InvalidOperationException($"No SVNS series with sport code '{wanted}' found on {StandingsPage}");
    }

    internal static IReadOnlyList<string> ExtractSeriesIds(string html)
    {
        HtmlDocument document = new();
        document.LoadHtml(html);

        string? ids = document.DocumentNode.SelectSingleNode("//*[@data-series-ids]")?.GetAttributeValue("data-series-ids", null);
        if (string.IsNullOrWhiteSpace(ids))
        {
            throw new InvalidOperationException("SVNS standings page has no data-series-ids attribute");
        }

        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static string ReadSportCode(string seriesJson)
    {
        using JsonDocument document = JsonDocument.Parse(seriesJson);
        return document.RootElement.GetProperty("sport").GetString()
            ?? throw new InvalidOperationException("SVNS series metadata has no sport code");
    }
}
