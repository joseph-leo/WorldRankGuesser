using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>
/// FIG discipline ranking pages (ranking_mag_table.php, ranking_wag_table.php, ranking_rg_table.php).
/// One page holds several rankings: an h4 heading per series ("Apparatus World Cup Ranking List 2026",
/// "World Challenge Cup Ranking List 2026"), followed by a tab strip with one tab per apparatus whose
/// pane holds that ranking's table. The selector "&lt;series&gt; / &lt;apparatus&gt;" names one table.
/// Rows are athletes, or national groups for the rhythmic group events, with the IOC code in the
/// flag image's alt attribute and the series total in the Total column. FIG publishes no ranking
/// date on the page.
/// </summary>
public sealed partial class FigParser : IRankingParser
{
    public string SourceName => "Fig";

    public ParsedRanking Parse(string response, string? selector = null)
    {
        (string series, string apparatus) = SplitSelector(selector);

        HtmlDocument document = new();
        document.LoadHtml(response);

        HtmlNode heading = document.DocumentNode.SelectNodes("//h4")
            ?.FirstOrDefault(h => Clean(h.InnerText).Contains(series, StringComparison.OrdinalIgnoreCase))
            ?? throw new ParseException(SourceName, $"no series heading contains '{series}'");

        HtmlNode tab = heading.SelectNodes("following::ul[contains(@class, 'nav-tabs')][1]//a")
            ?.FirstOrDefault(a => Clean(a.InnerText).Equals(apparatus, StringComparison.OrdinalIgnoreCase))
            ?? throw new ParseException(SourceName, $"no '{apparatus}' tab under the '{series}' series");

        string paneId = tab.GetAttributeValue("href", "").TrimStart('#');
        HtmlNode pane = document.GetElementbyId(paneId)
            ?? throw new ParseException(SourceName, $"no pane '{paneId}' for the '{apparatus}' tab");

        HtmlNodeCollection rows = pane.SelectNodes(".//tr[td[@data-label='Rank']]")
            ?? throw new ParseException(SourceName, $"no ranked rows in '{selector}'");

        return new ParsedRanking(rows.Select(MapRow).ToList());
    }

    private (string Series, string Apparatus) SplitSelector(string? selector)
    {
        string[] parts = (selector ?? "").Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts.Any(string.IsNullOrEmpty))
        {
            throw new ParseException(SourceName, $"selector '{selector}' must be '<series> / <apparatus>', e.g. 'World Cup / Vault'");
        }

        return (parts[0], parts[1]);
    }

    private RankEntry MapRow(HtmlNode row)
    {
        string positionText = Clean(row.SelectSingleNode("td[@data-label='Rank']").InnerText);
        if (!short.TryParse(positionText, out short position))
        {
            throw new ParseException(SourceName, $"unreadable rank '{positionText}'");
        }

        string ioc = row.SelectSingleNode("td[@data-label='NF']//img")?.GetAttributeValue("alt", "") ?? "";
        if (ioc.Length == 0)
        {
            throw new ParseException(SourceName, $"row at rank {position} has no country code");
        }

        // Athletes carry a Name cell. Rhythmic groups are national teams: no Name cell (the second NF
        // cell holds the country name, which the pipeline derives from the code instead).
        string? athlete = Text(row.SelectSingleNode("td[@data-label='Name']"));

        string? totalText = Text(row.SelectSingleNode("td[@data-label='Total']"));
        decimal? points = totalText is not null && decimal.TryParse(totalText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal total) ? total : null;

        return new RankEntry(position, ioc.IOCToISO3(), athlete, points);
    }

    private static string? Text(HtmlNode? cell)
    {
        if (cell is null)
        {
            return null;
        }

        string text = Clean(cell.InnerText);
        return text.Length == 0 ? null : text;
    }

    /// <summary>Decodes entities (the names use &amp;nbsp;) and collapses runs of whitespace to one space.</summary>
    private static string Clean(string text) => Whitespace().Replace(WebUtility.HtmlDecode(text), " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
