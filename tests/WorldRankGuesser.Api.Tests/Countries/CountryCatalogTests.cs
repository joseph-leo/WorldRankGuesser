using WorldRankGuesser.Api.Countries;

namespace WorldRankGuesser.Api.Tests.Countries;

public class CountryCatalogTests
{
    private static readonly CountryCatalog Catalog = CountryCatalog.LoadEmbedded();

    [Theory]
    [InlineData("DEU", "DE")]
    [InlineData("GBR", "GB")]
    [InlineData("TWN", "TW")]
    [InlineData("SXM", "SX")]
    [InlineData("XKX", "XK")]
    public void Maps_iso3_to_iso2(string iso3, string expected)
    {
        Assert.True(Catalog.TryGetIso2(iso3, out var iso2));
        Assert.Equal(expected, iso2);
    }

    [Theory]
    [InlineData("ENG")]
    [InlineData("WI")]
    [InlineData("???")]
    public void Unknown_codes_are_not_found(string iso3)
    {
        Assert.False(Catalog.TryGetIso2(iso3, out _));
    }
}
