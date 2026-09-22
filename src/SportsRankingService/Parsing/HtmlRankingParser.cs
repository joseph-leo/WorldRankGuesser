using HtmlAgilityPack;

namespace SportsRankingService.Parsing;

/// <summary>
/// Base for feeds that return an HTML page. Subclasses give the XPath that selects one node per
/// ranked row, map each row to an entry (or null to skip it), and may override
/// <see cref="GetRankingDate"/> when the page shows the ranking date.
/// </summary>
public abstract class HtmlRankingParser : IRankingParser
{
    public abstract string SourceName { get; }

    protected abstract string RowXPath { get; }

    protected abstract RankEntry? MapRow(HtmlNode row);

    /// <summary>The federation's ranking date, or null when the page has none. Throw <see cref="ParseException"/> for an unreadable value.</summary>
    protected virtual DateOnly? GetRankingDate(HtmlDocument document) => null;

    public ParsedRanking Parse(string response, string? selector = null)
    {
        HtmlDocument document = new();
        document.LoadHtml(response);

        HtmlNodeCollection rows = document.DocumentNode.SelectNodes(RowXPath)
            ?? throw new ParseException(SourceName, $"no rows matched {RowXPath}");

        return new ParsedRanking(rows.Select(MapRow).OfType<RankEntry>().ToList(), GetRankingDate(document));
    }
}
