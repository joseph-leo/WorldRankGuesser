using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>
/// Volleyball World ranking API, indoor and beach. Indoor rows are national teams with a decimal
/// score (<c>decimalPoints</c>; <c>points</c> is the rounded value). Beach rows are player pairs:
/// <c>name</c> is the pair ("Hölting Nilsson/Andersson, E"), <c>player1Name</c> is present, and only
/// the integer <c>points</c> exists.
/// </summary>
public sealed class VolleyballWorldParser : JsonRankingParser<VolleyballWorldParser.Root>
{
    public override string SourceName => "VolleyballWorld";

    public sealed record Root(List<Team> Teams);
    public sealed record Team(
        short RankToDisplay,
        string FederationCode,
        string? FederationName,
        string? Name,
        string? Player1Name,
        decimal? DecimalPoints,
        decimal? Points);

    protected override IEnumerable<RankEntry> Map(Root root) =>
        root.Teams.Select(t => new RankEntry(
            t.RankToDisplay,
            t.FederationCode.Trim().IOCToISO3(),
            t.FederationName,
            Competitor: t.Player1Name is null ? null : t.Name,
            Points: t.DecimalPoints ?? t.Points));
}
