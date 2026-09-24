using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Tests.Utilities;

/// <summary>
/// Every code a parser emits from a sample must have a name in <see cref="CountryNames"/>, otherwise the
/// builder fails that feed at run time. Failing here names the codes to map (usually a federation-only
/// code for the IOC table) before a live run finds them one feed at a time.
/// </summary>
public class SampleCountryCodesTests
{
    [Theory]
    [MemberData(nameof(Feeds.All), MemberType = typeof(Feeds))]
    public void Every_code_a_sample_yields_has_a_country_name(IRankingParser parser, string sample, string? selector)
    {
        IEnumerable<string> unnamed = parser.Parse(Sample.Read(sample), selector).Entries
            .Select(e => e.ISO3)
            .Distinct()
            .Where(code => !CountryUtil.TryGetCountryName(code, out _))
            .Order();

        Assert.True(!unnamed.Any(), $"{sample}: no country name for {string.Join(", ", unnamed)}");
    }
}
