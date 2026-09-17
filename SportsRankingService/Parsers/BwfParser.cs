using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>
/// BWF ranking table API (vue-rankingtable). Countries come as names only. Doubles rows carry
/// two players and yield one entry per partner with the pair's position and points; each entry
/// names its own partner. Players listed as "Athlete Independent Neutral" have no country and
/// are dropped. Player names arrive as HTML ("&lt;span class="name-1"&gt;Jonatan&lt;/span&gt; ...").
/// </summary>
public sealed partial class BwfParser : JsonRankingParser<BwfParser.Root>
{
    public override string SourceName => "Bwf";

    public sealed record Root(Results Results);
    public sealed record Results(List<Row> Data, int? Total, [property: JsonPropertyName("last_page")] int? LastPage);
    public sealed record Row(
        short Rank,
        decimal? Points,
        [property: JsonPropertyName("player1_model")] Player? Player1,
        [property: JsonPropertyName("player2_model")] Player? Player2,
        [property: JsonPropertyName("p1_country_model")] Country? Player1Country,
        [property: JsonPropertyName("p2_country_model")] Country? Player2Country);
    public sealed record Player([property: JsonPropertyName("name_display_bold")] string? NameHtml);
    public sealed record Country(string? Name);

    protected override IEnumerable<RankEntry> Map(Root root)
    {
        foreach (Row row in root.Results.Data)
        {
            foreach ((Player? player, Country? country) in new[] { (row.Player1, row.Player1Country), (row.Player2, row.Player2Country) })
            {
                if (country?.Name is null || IsNeutral(country.Name))
                {
                    continue;
                }

                if (!CountryUtil.TryGetISO3FromCountry(country.Name, out string? iso3))
                {
                    throw new ParseException(SourceName, $"no ISO3 mapping for country name '{country.Name}'");
                }

                yield return new RankEntry(row.Rank, iso3!, country.Name, CleanName(player?.NameHtml), row.Points);
            }
        }
    }

    /// <summary>Strips the name's HTML markup and collapses whitespace; null when the feed gives none.</summary>
    private static string? CleanName(string? nameHtml)
    {
        if (nameHtml is null)
        {
            return null;
        }

        string name = Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(nameHtml, " ")), " ").Trim();
        return name.Length == 0 ? null : name;
    }

    private static bool IsNeutral(string countryName) =>
        countryName.Contains("Neutral", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
