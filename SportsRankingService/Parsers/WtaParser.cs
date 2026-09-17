using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>WTA players API (used for women's doubles). The response is a top-level array.</summary>
public sealed class WtaParser : JsonRankingParser<List<WtaParser.Entry>>
{
    public override string SourceName => "Wta";

    public sealed record Entry(short Ranking, Player Player, string? RankedAt, decimal? Points);
    public sealed record Player(string CountryCode, string? FullName);

    protected override IEnumerable<RankEntry> Map(List<Entry> root) =>
        root.Select(e => new RankEntry(e.Ranking, e.Player.CountryCode.Trim().IOCToISO3(), Competitor: e.Player.FullName, Points: e.Points));

    // Every entry carries the same rankedAt; the first is enough. An empty page has no date.
    protected override DateOnly? GetRankingDate(List<Entry> root) =>
        root.Count == 0 ? null : IsoDate.Parse(SourceName, root[0].RankedAt);
}
