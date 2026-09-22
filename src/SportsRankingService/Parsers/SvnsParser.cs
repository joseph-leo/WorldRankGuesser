using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>World Rugby series standings API, used for the SVNS (sevens) series. Team codes are IOC-style.</summary>
public sealed class SvnsParser : JsonRankingParser<SvnsParser.Root>
{
    public override string SourceName => "Svns";

    public sealed record Root(List<Entry> Entries);
    public sealed record Entry(short Position, Team Team, decimal? TotalPoints);
    public sealed record Team(string Abbreviation);

    protected override IEnumerable<RankEntry> Map(Root root) =>
        root.Entries.Select(e => new RankEntry(e.Position, e.Team.Abbreviation.Trim().IOCToISO3(), Points: e.TotalPoints));
}
