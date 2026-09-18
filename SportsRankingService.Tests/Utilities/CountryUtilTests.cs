using SportsRankingService.Utilities;

namespace SportsRankingService.Tests.Utilities;

public class CountryUtilTests
{
    [Theory]
    [InlineData("GER", "DEU")]
    [InlineData("NED", "NLD")]
    [InlineData("RSA", "ZAF")]
    [InlineData("ROM", "ROU")]   // ICC, ESPN
    [InlineData("SER", "SRB")]   // ICC, ESPN
    [InlineData("KOS", "XKX")]   // FIFA, WBSC; Kosovo has no ISO code, XKX is the customary one
    [InlineData("USA", "USA")]   // already ISO3
    [InlineData("ENG", "ENG")]   // not an IOC code; passes through unchanged
    [InlineData("AGU", "AIA")]   // Volleyball World
    [InlineData("CUR", "CUW")]   // Volleyball World
    [InlineData("FAR", "FRO")]   // Volleyball World
    [InlineData("MSH", "MHL")]   // Volleyball World
    [InlineData("MLD", "MDA")]   // Volleyball World
    [InlineData("PAU", "PLW")]   // Volleyball World
    [InlineData("GDP", "GLP")]   // Volleyball World
    [InlineData("MQE", "MTQ")]   // Volleyball World
    [InlineData("NMI", "MNP")]   // Volleyball World
    [InlineData("JSY", "JEY")]   // ICC
    [InlineData("GSY", "GGY")]   // ICC
    [InlineData("IOM", "IMN")]   // ICC
    [InlineData("STH", "SHN")]   // ICC
    [InlineData("CTA", "CAF")]   // FIFA
    [InlineData("EQG", "GNQ")]   // FIFA
    [InlineData("TAH", "PYF")]   // FIFA
    [InlineData("ESW", "SWZ")]   // ICC, FIH
    [InlineData("SDA", "SAU")]   // ICC
    public void IOCToISO3_maps_IOC_codes_and_passes_others_through(string ioc, string expected)
    {
        Assert.Equal(expected, ioc.IOCToISO3());
    }

    [Theory]
    [InlineData("Germany", "DEU")]
    [InlineData("usa", "USA")]
    [InlineData("Korea", "KOR")]
    [InlineData("Hong Kong China", "HKG")]
    [InlineData("Chinese Taipei", "TWN")]
    [InlineData("England", "GBR")]
    [InlineData("Scotland", "GBR")]
    public void TryGetISO3FromCountry_resolves_names_through_the_region_mapping(string name, string expected)
    {
        Assert.True(CountryUtil.TryGetISO3FromCountry(name, out string? iso3));
        Assert.Equal(expected, iso3);
    }

    [Theory]
    [InlineData("Athlete Independent Neutral")]
    [InlineData("")]
    [InlineData("Not A Country")]
    public void TryGetISO3FromCountry_returns_false_for_unknown_names(string name)
    {
        Assert.False(CountryUtil.TryGetISO3FromCountry(name, out _));
    }
}
