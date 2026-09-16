using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>Volleyball World ranking API (indoor and beach, men and women).</summary>
public sealed class VolleyballWorldParser : JsonRankingParser<VolleyballWorldParser.Root>
{
    public override string SourceName => "VolleyballWorld";

    public sealed record Root(List<Team> Teams);
    public sealed record Team(short RankToDisplay, string FederationCode, string? FederationName);

    protected override IEnumerable<RankEntry> Map(Root root) =>
        root.Teams.Select(t => new RankEntry(t.RankToDisplay, t.FederationCode.Trim().IOCToISO3(), t.FederationName));
}
