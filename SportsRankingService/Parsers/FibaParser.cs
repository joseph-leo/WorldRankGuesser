using System.Text.RegularExpressions;
using HtmlAgilityPack;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>
/// FIBA ranking page. The table has no country code column and the country name is localized,
/// so the country is read from the English slug of the team link (/xx/teams/154-usa).
/// </summary>
public sealed partial class FibaParser : HtmlRankingParser
{
    public override string SourceName => "Fiba";

    protected override string RowXPath => "//table//tbody/tr";

    protected override RankEntry? MapRow(HtmlNode row)
    {
        HtmlNode? link = row.SelectSingleNode(".//a[contains(@href, '/teams/')]");
        if (link is null)
        {
            return null;
        }

        Match slug = TeamSlug().Match(link.GetAttributeValue("href", ""));
        if (!slug.Success)
        {
            return null;
        }

        string name = slug.Groups[1].Value.Replace('-', ' ');
        if (!CountryUtil.TryGetISO3FromCountry(name, out string? iso3))
        {
            throw new ParseException(SourceName, $"no ISO3 mapping for team slug '{slug.Groups[1].Value}'");
        }

        string positionText = row.SelectSingleNode("td")?.InnerText.Trim().TrimEnd('.') ?? "";
        short position = short.Parse(positionText);

        return new RankEntry(position, iso3!, link.InnerText.Trim());
    }

    [GeneratedRegex(@"/teams/\d+-([a-z0-9-]+)")]
    private static partial Regex TeamSlug();
}
