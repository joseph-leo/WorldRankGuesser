using SportsRankingService.Parsers;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Tests.Utilities;

/// <summary>
/// Every code a parser emits from a captured feed must have a name in <see cref="CountryNames"/>,
/// otherwise the builder fails that feed at run time. Failing here names the codes to map (usually a
/// federation-only code for the IOC table) before a live run finds them one feed at a time.
/// </summary>
public class FixtureCountryCodesTests
{
    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void Every_code_a_fixture_yields_has_a_country_name(IRankingParser parser, string fixture, string? selector)
    {
        IEnumerable<string> unnamed = parser.Parse(Fixture.Read(fixture), selector).Entries
            .Select(e => e.ISO3)
            .Distinct()
            .Where(code => !CountryUtil.TryGetCountryName(code, out _))
            .Order();

        Assert.True(!unnamed.Any(), $"{fixture}: no country name for {string.Join(", ", unnamed)}");
    }

    public static TheoryData<IRankingParser, string, string?> EveryFixture()
    {
        TheoryData<IRankingParser, string, string?> data = new()
        {
            { new BwfParser(), "Bwf_MensDoubles.json", null },
            { new BwfParser(), "Bwf_MensSingles.json", null },
            { new EspnTennisParser(), "Espn_Atp_Singles.json", null },
            { new FibaParser(), "Fiba_Ranking_Men.html", null },
            { new FifaV3Parser(), "Fifa_V3_Men_FRS_20260611.json", null },
            { new FifaV3Parser(), "Fifa_V3_Women_FRS_20260419.json", null },
            { new FihParser(), "Fih_Outdoor_Men.json", null },
            { new IccParser(), "Icc_T20_Men.json", null },
            { new IccParser(), "Icc_T20_Women.json", null },
            { new IccParser(), "Icc_Test_Men.json", null },
            { new SvnsParser(), "Svns_Standings_Men.json", null },
            { new SvnsParser(), "Svns_Standings_Women.json", null },
            { new VolleyballWorldParser(), "VolleyballWorld_Beach_Men.json", null },
            { new VolleyballWorldParser(), "VolleyballWorld_Men.json", null },
            { new WbscParser(), "Wbsc_Baseball_Men.json", null },
            { new WorldRugbyParser(), "WorldRugby_Union_Men.json", null },
            { new WtaParser(), "Wta_Doubles.json", null },
        };

        // FIG: every apparatus table of both series on each page.
        var figPages = new (string Fixture, string[] Apparatus)[]
        {
            ("Fig_Artistic_Men.html", ["Floor Exercise", "Pommel Horse", "Still Rings", "Vault", "Parallel Bars", "Horizontal Bar"]),
            ("Fig_Artistic_Women.html", ["Vault", "Uneven Bars", "Balance Beam", "Floor Exercise"]),
            ("Fig_Rhythmic_Women.html", ["Individual All-Around", "Hoop", "Ball", "Clubs", "Ribbon", "Group All-Around", "Group 5x", "Group 3x+2x"]),
        };
        foreach (var (fixture, apparatus) in figPages)
        {
            foreach (string series in new[] { "World Cup", "World Challenge Cup" })
            {
                foreach (string a in apparatus)
                {
                    data.Add(new FigParser(), fixture, $"{series} / {a}");
                }
            }
        }

        return data;
    }
}
