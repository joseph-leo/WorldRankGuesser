using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>
/// WTA players API (used for women's doubles). The response is a top-level array, one page of the list
/// (serviceconfig.json pages it). A few players deep in the list carry no nationality: countryCode null, or
/// "NCD", which is neither an ISO nor an IOC code (both seen 2026-09-22). A row without a country cannot count
/// for one and is dropped, like BWF's neutral athletes.
/// </summary>
public sealed class WtaParser : JsonRankingParser<List<WtaParser.Entry>>
{
    public override string SourceName => "Wta";

    /// <summary>The feed's "no country declared" code.</summary>
    private const string NoCountry = "NCD";

    public sealed record Entry(short Ranking, Player Player, string? RankedAt, decimal? Points);
    public sealed record Player(string? CountryCode, string? FullName);

    protected override IEnumerable<RankEntry> Map(List<Entry> root) =>
        root.Where(e => HasCountry(e.Player.CountryCode))
            .Select(e => new RankEntry(e.Ranking, e.Player.CountryCode!.Trim().IOCToISO3(), Competitor: e.Player.FullName, Points: e.Points));

    private static bool HasCountry(string? countryCode) =>
        !string.IsNullOrWhiteSpace(countryCode) && !countryCode.Trim().Equals(NoCountry, StringComparison.OrdinalIgnoreCase);

    // Every entry carries the same rankedAt; the first is enough. An empty page has no date.
    protected override DateOnly? GetRankingDate(List<Entry> root) =>
        root.Count == 0 ? null : IsoDate.Parse(SourceName, root[0].RankedAt);
}
