using System.Text.Json.Serialization;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>
/// BWF ranking table API (vue-rankingtable). Countries come as names only. Doubles rows carry
/// two players and yield one entry per partner with the pair's position. Players listed as
/// "Athlete Independent Neutral" have no country and are dropped.
/// </summary>
public sealed class BwfParser : JsonRankingParser<BwfParser.Root>
{
    public override string SourceName => "Bwf";

    public sealed record Root(Results Results);
    public sealed record Results(List<Row> Data, int? Total, [property: JsonPropertyName("last_page")] int? LastPage);
    public sealed record Row(
        short Rank,
        [property: JsonPropertyName("p1_country_model")] Country? Player1Country,
        [property: JsonPropertyName("p2_country_model")] Country? Player2Country);
    public sealed record Country(string? Name);

    protected override IEnumerable<RankEntry> Map(Root root)
    {
        foreach (Row row in root.Results.Data)
        {
            foreach (Country? country in new[] { row.Player1Country, row.Player2Country })
            {
                if (country?.Name is null || IsNeutral(country.Name))
                {
                    continue;
                }

                if (!CountryUtil.TryGetISO3FromCountry(country.Name, out string? iso3))
                {
                    throw new ParseException(SourceName, $"no ISO3 mapping for country name '{country.Name}'");
                }

                yield return new RankEntry(row.Rank, iso3!, country.Name);
            }
        }
    }

    private static bool IsNeutral(string countryName) =>
        countryName.Contains("Neutral", StringComparison.OrdinalIgnoreCase);
}
