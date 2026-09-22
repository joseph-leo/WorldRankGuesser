using System.Globalization;
using HtmlAgilityPack;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>
/// Wikipedia's "IIHF World Ranking" article as served by the MediaWiki REST API (/w/rest.php/v1/page/IIHF_World_Ranking/html).
/// It stands in for iihf.com, which answers every scripted client with a Cloudflare challenge. One page holds both
/// rankings: a section per gender, headed by an h2 with a stable id, whose sortable table has the columns
/// "current rank, previous rank, team, ..., current total (bold), ...". The selector "Men" or "Women" names the table,
/// and the table is only looked for inside its own section so a missing one can never be answered with the other.
/// A team the IIHF lists without a rank shows "NR" (suspended or inactive) or "new" (no results yet) and is skipped. The country is the text of the team link;
/// the article states no ranking date.
/// </summary>
public sealed class WikipediaIihfParser : IRankingParser
{
    public string SourceName => "WikipediaIihf";

    public ParsedRanking Parse(string response, string? selector = null)
    {
        string headingId = selector switch
        {
            "Men" => "Men's_rankings",
            "Women" => "Women's_rankings",
            _ => throw new ParseException(SourceName, $"selector '{selector}' must be 'Men' or 'Women'"),
        };

        HtmlDocument document = new();
        document.LoadHtml(response);

        HtmlNode section = document.GetElementbyId(headingId)?.ParentNode
            ?? throw new ParseException(SourceName, $"no heading with id \"{headingId}\"");

        HtmlNodeCollection rows = section.SelectNodes(".//table[contains(@class, 'sortable')][1]//tr[td]")
            ?? throw new ParseException(SourceName, $"no sortable table in the section of \"{headingId}\"");

        return new ParsedRanking(rows.Select(MapRow).OfType<RankEntry>().ToList());
    }

    private RankEntry? MapRow(HtmlNode row)
    {
        string rank = Clean(row.SelectSingleNode("td").InnerText);
        if (rank is "NR" or "new")
        {
            return null;
        }

        if (!short.TryParse(rank, NumberStyles.None, CultureInfo.InvariantCulture, out short position))
        {
            throw new ParseException(SourceName, $"rank '{rank}' is not a number, NR or new");
        }

        string team = Clean(row.SelectSingleNode(".//a[contains(@href, 'national_ice_hockey_team')]")?.InnerText);
        if (!CountryUtil.TryGetISO3FromCountry(team, out string? iso3))
        {
            throw new ParseException(SourceName, $"no ISO3 mapping for team '{team}'");
        }

        string total = Clean(row.SelectSingleNode("td/b")?.InnerText);
        if (!decimal.TryParse(total, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal points))
        {
            throw new ParseException(SourceName, $"no points total for '{team}' (rank {position})");
        }

        return new RankEntry(position, iso3!, Points: points);
    }

    private static string Clean(string? text) => HtmlEntity.DeEntitize(text ?? "").Trim();
}
