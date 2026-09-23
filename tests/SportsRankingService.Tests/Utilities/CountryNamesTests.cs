using System.Globalization;
using SportsRankingService.Utilities;

namespace SportsRankingService.Tests.Utilities;

public class CountryNamesTests
{
    [Theory]
    [InlineData("ARE", "United Arab Emirates")]
    [InlineData("ARG", "Argentina")]
    [InlineData("ATG", "Antigua and Barbuda")]
    [InlineData("AUS", "Australia")]
    [InlineData("BIH", "Bosnia and Herzegovina")]
    [InlineData("BRA", "Brazil")]
    [InlineData("CAF", "Central African Republic")]
    [InlineData("CAN", "Canada")]
    [InlineData("CHN", "China")]
    [InlineData("CIV", "Cote D'Ivoire")]
    [InlineData("COD", "Democratic Republic of Congo")]
    [InlineData("COG", "Republic of Congo")]
    [InlineData("CPV", "Cabo Verde")]
    [InlineData("DEU", "Germany")]
    [InlineData("ESP", "Spain")]
    [InlineData("FJI", "Fiji")]
    [InlineData("FRA", "France")]
    [InlineData("GBR", "United Kingdom")]
    [InlineData("GMB", "Gambia")]
    [InlineData("HKG", "Hong Kong")]
    [InlineData("IRL", "Ireland")]
    [InlineData("IRN", "Iran")]
    [InlineData("JPN", "Japan")]
    [InlineData("KEN", "Kenya")]
    [InlineData("KGZ", "Kyrgyzstan")]
    [InlineData("KNA", "Saint Kitts and Nevis")]
    [InlineData("KOR", "Korea")]
    [InlineData("LCA", "Saint Lucia")]
    [InlineData("MAC", "Macau")]
    [InlineData("NZL", "New Zealand")]
    [InlineData("PNG", "Papua New Guinea")]
    [InlineData("PRK", "North Korea")]
    [InlineData("PSE", "Palestine")]
    [InlineData("RUS", "Russia")]
    [InlineData("SWZ", "Eswatini")]
    [InlineData("SYR", "Syria")]
    [InlineData("TCA", "Turks and Caicos")]
    [InlineData("TTO", "Trinidad and Tobago")]
    [InlineData("TUR", "Türkiye")]
    [InlineData("TWN", "Taiwan")]
    [InlineData("URY", "Uruguay")]
    [InlineData("USA", "United States of America")]
    [InlineData("VCT", "Saint Vincent and the Grenadines")]
    [InlineData("VGB", "British Virgin Islands")]
    [InlineData("VIR", "U.S. Virgin Islands")]
    [InlineData("ZAF", "South Africa")]
    [InlineData("SHN", "Saint Helena")]   // ICC "St.Helena"; the runtime's name lists all three islands
    public void Names_are_the_agreed_spelling_for_every_country_the_feeds_disagreed_on(string iso3, string expected)
    {
        Assert.Equal(expected, CountryUtil.GetCountryName(iso3));
    }

    [Theory]
    [InlineData("XKX", "Kosovo")]            // no ISO code; XKX is the customary one
    [InlineData("WI", "West Indies")]        // ICC cricket
    [InlineData("ENG", "England")]           // FIFA home nations, kept as emitted
    [InlineData("SCO", "Scotland")]
    [InlineData("WAL", "Wales")]
    [InlineData("NIR", "Northern Ireland")]
    public void Codes_the_feeds_emit_that_are_not_ISO3_have_names_too(string code, string expected)
    {
        Assert.Equal(expected, CountryUtil.GetCountryName(code));
    }

    // [Fact]
    // public void Every_region_the_runtime_knows_has_a_name()
    // {
    //     IEnumerable<string> missing = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
    //         .Select(c => new RegionInfo(c.Name))
    //         .Where(r => !r.TwoLetterISORegionName.Any(char.IsDigit))
    //         .Select(r => r.ThreeLetterISORegionName)
    //         .Distinct()
    //         .Where(iso3 => iso3 != "XKK")   // the runtime's Kosovo; the pipeline uses XKX
    //         .Where(iso3 => !CountryUtil.TryGetCountryName(iso3, out _));
    //
    //     Assert.Empty(missing);
    // }

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("")]
    public void Unknown_codes_have_no_name(string code)
    {
        Assert.False(CountryUtil.TryGetCountryName(code, out _));
        Assert.Throws<ArgumentException>(() => CountryUtil.GetCountryName(code));
    }
}
