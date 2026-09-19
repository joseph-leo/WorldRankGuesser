using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Rankings;

internal static class TestData
{
    public static readonly DateTimeOffset LoadedAt = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    public static readonly CountryCatalog Catalog = new(new Dictionary<string, string>
    {
        ["DNK"] = "DK", ["CHN"] = "CN", ["IDN"] = "ID", ["JPN"] = "JP",
        ["GBR"] = "GB", ["JAM"] = "JM", ["IND"] = "IN", ["AUS"] = "AU",
    });

    public static CountryRankingRow Row(string sport, string? ev, string gender, short position, string iso3, string? competitor = null) =>
        new()
        {
            Sport = sport,
            Event = ev,
            Gender = gender,
            RankingDate = new DateOnly(2026, 9, 14),
            IsFederationDate = true,
            Position = position,
            ISO3 = iso3,
            TeamName = Names.GetValueOrDefault(iso3, iso3),
            Competitor = competitor,
        };

    private static readonly Dictionary<string, string> Names = new()
    {
        ["DNK"] = "Denmark", ["CHN"] = "China", ["IDN"] = "Indonesia", ["JPN"] = "Japan",
        ["GBR"] = "United Kingdom", ["JAM"] = "Jamaica", ["IND"] = "India", ["AUS"] = "Australia",
        ["ENG"] = "England", ["SCO"] = "Scotland", ["WI"] = "West Indies", ["ZZZ"] = "Nowhere",
    };

    public static GameOptions Options(int minCategoriesRanked = 1) => new()
    {
        MinCategoriesRanked = minCategoriesRanked,
        Categories =
        [
            new() { Id = "soccer", Name = "Soccer", Sports = ["Soccer"] },
            new() { Id = "cricket", Name = "Cricket", Sports = ["Cricket"] },
            new() { Id = "badminton", Name = "Badminton", Sports = ["Badminton"] },
            new() { Id = "hockey", Name = "Hockey", Sports = ["Field Hockey", "Ice Hockey"] },
        ],
        Aliases =
        [
            new() { Sources = ["ENG", "SCO"], Targets = ["GBR"], Categories = [] },
            new() { Sources = ["WI"], Targets = ["JAM"], Categories = ["cricket"] },
        ],
        NotDrawable = ["ENG", "SCO", "WI"],
    };
}
