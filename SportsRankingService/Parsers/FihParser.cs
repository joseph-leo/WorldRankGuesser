using System.Text.Json.Serialization;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>International Hockey Federation static JSON feeds (indoor and outdoor, men and women).</summary>
public sealed class FihParser : JsonRankingParser<FihParser.Root>
{
    public override string SourceName => "Fih";

    public sealed record Root(List<Team> Ranks);
    public sealed record Team(
        short Rank,
        [property: JsonPropertyName("team_short_code")] string TeamShortCode,
        [property: JsonPropertyName("team")] string? Name,
        decimal? Points);

    protected override IEnumerable<RankEntry> Map(Root root) =>
        root.Ranks.Select(t => new RankEntry(t.Rank, t.TeamShortCode.Trim().IOCToISO3(), t.Name, Points: t.Points));
}
