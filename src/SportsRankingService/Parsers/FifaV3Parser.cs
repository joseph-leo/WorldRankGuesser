using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>
/// FIFA v3 rankings API (rankingsbyschedule). Country codes are FIFA's own, which are mostly ISO3
/// or IOC (a few are FIFA-only: CTA, EQG, TAH) and include the home nations (ENG, SCO, WAL, NIR),
/// which are kept as emitted.
/// </summary>
public sealed class FifaV3Parser : JsonRankingParser<FifaV3Parser.Root>
{
    public override string SourceName => "FifaV3";

    public sealed record Root(List<Result> Results);
    /// <param name="Rank">Null for teams that are listed but currently unranked (too few rated matches).</param>
    public sealed record Result(short? Rank, string IdCountry, decimal? TotalPoints);

    protected override IEnumerable<RankEntry> Map(Root root) =>
        root.Results
            .Where(r => r.Rank is not null)
            .Select(r => new RankEntry(r.Rank!.Value, r.IdCountry.Trim().IOCToISO3(), Points: r.TotalPoints));
}
