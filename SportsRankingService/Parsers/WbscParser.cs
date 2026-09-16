using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>WBSC rankings API (baseball, softball, Baseball5). Requires an exact release date in the URL.</summary>
public sealed class WbscParser : JsonRankingParser<WbscParser.Root>
{
    public override string SourceName => "Wbsc";

    public sealed record Root(List<Row> Rankings);
    public sealed record Row(short Position, string Ioc, string? Date);

    protected override IEnumerable<RankEntry> Map(Root root) =>
        root.Rankings.Select(r => new RankEntry(r.Position, r.Ioc.Trim().IOCToISO3()));

    // The API only answers for an exact release date, so every row carries that same date.
    protected override DateOnly? GetRankingDate(Root root) =>
        root.Rankings.Count == 0 ? null : IsoDate.Parse(SourceName, root.Rankings[0].Date);
}
