using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Tests;

/// <summary>
/// The optional pass over real responses saved under Captures/ (git-ignored; see Samples/SAMPLES.md): every
/// parser still reads the live format, a real list is long, and every code it yields has a name. Skipped when
/// the folder is absent. A capture that is missing while others exist fails its case with the file name.
/// </summary>
public class CaptureTests
{
    [CaptureTheory]
    [MemberData(nameof(Feeds.All), MemberType = typeof(Feeds))]
    public void A_real_response_parses_to_a_full_list_of_named_countries(IRankingParser parser, string capture, string? selector)
    {
        var rows = parser.Parse(Capture.Read(capture), selector).Entries;

        Assert.True(rows.Count >= 10, $"{capture} {selector}: expected a real list, got {rows.Count} rows");
        Assert.All(rows, r => Assert.Matches("^[A-Z]{2,3}$", r.ISO3));

        IEnumerable<string> unnamed = rows.Select(r => r.ISO3).Distinct().Where(code => !CountryUtil.TryGetCountryName(code, out _)).Order();
        Assert.True(!unnamed.Any(), $"{capture}: no country name for {string.Join(", ", unnamed)}");
    }
}
