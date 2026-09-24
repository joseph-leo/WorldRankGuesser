using SportsRankingService.Parsers;
using SportsRankingService.Parsing;

namespace SportsRankingService.Tests;

/// <summary>
/// Every feed shape the scraper parses: the parser, the sample file in Samples/ (and, when present, the
/// real capture of the same name in Captures/), and the selector for a page that holds several rankings.
/// </summary>
internal static class Feeds
{
    public static TheoryData<IRankingParser, string, string?> All()
    {
        TheoryData<IRankingParser, string, string?> data = new()
        {
            { new BwfParser(), "Bwf_MensDoubles.json", null },
            { new BwfParser(), "Bwf_MensSingles.json", null },
            { new EspnTennisParser(), "Espn_Atp_Singles.json", null },
            { new FibaParser(), "Fiba_Ranking_Men.html", null },
            { new FifaV3Parser(), "Fifa_V3_Men.json", null },
            { new FifaV3Parser(), "Fifa_V3_Women.json", null },
            { new FihParser(), "Fih_Outdoor_Men.json", null },
            { new IccParser(), "Icc_T20_Men.json", null },
            { new IccParser(), "Icc_T20_Women.json", null },
            { new IccParser(), "Icc_Test_Men.json", null },
            { new SvnsParser(), "Svns_Standings_Men.json", null },
            { new SvnsParser(), "Svns_Standings_Women.json", null },
            { new VolleyballWorldParser(), "VolleyballWorld_Beach_Men.json", null },
            { new VolleyballWorldParser(), "VolleyballWorld_Men.json", null },
            { new WbscParser(), "Wbsc_Baseball_Men.json", null },
            { new WikipediaIihfParser(), "Wikipedia_IihfWorldRanking.html", "Men" },
            { new WikipediaIihfParser(), "Wikipedia_IihfWorldRanking.html", "Women" },
            { new WorldRugbyParser(), "WorldRugby_Union_Men.json", null },
            { new WtaParser(), "Wta_Doubles.json", null },
        };

        foreach ((string page, string[] apparatus) in FigPages)
        {
            foreach (string series in FigSeries)
            {
                foreach (string a in apparatus)
                {
                    data.Add(new FigParser(), page, $"{series} / {a}");
                }
            }
        }

        return data;
    }

    public static readonly string[] FigSeries = ["World Cup", "World Challenge Cup"];

    /// <summary>Each FIG page and its apparatus tabs; every series has a tab per apparatus.</summary>
    public static readonly (string Page, string[] Apparatus)[] FigPages =
    [
        ("Fig_Artistic_Men.html", ["Floor Exercise", "Pommel Horse", "Still Rings", "Vault", "Parallel Bars", "Horizontal Bar"]),
        ("Fig_Artistic_Women.html", ["Vault", "Uneven Bars", "Balance Beam", "Floor Exercise"]),
        ("Fig_Rhythmic_Women.html", ["Individual All-Around", "Hoop", "Ball", "Clubs", "Ribbon", "Group All-Around", "Group 5x", "Group 3x+2x"]),
    ];
}
