using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>World Rugby union rankings API (mru / wru).</summary>
public sealed class WorldRugbyParser : JsonRankingParser<WorldRugbyParser.Root>
{
    public override string SourceName => "WorldRugby";

    public sealed record Root(List<Entry> Entries, Effective? Effective);
    public sealed record Entry(short Pos, Team Team, decimal? Pts);
    public sealed record Team(string CountryCode, string? Name);
    public sealed record Effective(string? Label);

    protected override IEnumerable<RankEntry> Map(Root root) =>
        root.Entries.Select(e => new RankEntry(e.Pos, e.Team.CountryCode.Trim().IOCToISO3(), e.Team.Name, Points: e.Pts));

    protected override DateOnly? GetRankingDate(Root root) =>
        root.Effective?.Label is string label ? IsoDate.Parse(SourceName, label) : null;
}
