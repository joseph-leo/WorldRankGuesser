using HtmlAgilityPack;

namespace SportsRankingService.Parsing;

/// <summary>
/// Base for feeds that return an HTML page. Subclasses give the XPath that selects one node per
/// ranked row and map each row to an entry (or null to skip it).
/// </summary>
public abstract class HtmlRankingParser : IRankingParser
{
    public abstract string SourceName { get; }

    protected abstract string RowXPath { get; }

    protected abstract RankEntry? MapRow(HtmlNode row);

    public IReadOnlyList<RankEntry> Parse(string response)
    {
        HtmlDocument document = new();
        document.LoadHtml(response);

        HtmlNodeCollection rows = document.DocumentNode.SelectNodes(RowXPath)
            ?? throw new ParseException(SourceName, $"no rows matched {RowXPath}");

        return rows.Select(MapRow).OfType<RankEntry>().ToList();
    }
}
