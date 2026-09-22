using System.Text.Json.Serialization;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>ICC team rankings feed (Test, ODI, T20I; men and women).</summary>
public sealed class IccParser : JsonRankingParser<IccParser.Root>
{
    public override string SourceName => "Icc";

    public sealed record Root(Data Data);
    public sealed record Data([property: JsonPropertyName("bat-rank")] BatRank BatRank);
    public sealed record BatRank(List<Entry> Rank, [property: JsonPropertyName("rank_date")] string? RankDate);
    public sealed record Entry(short No, string Shortname, decimal? Rating);

    protected override IEnumerable<RankEntry> Map(Root root) =>
        root.Data.BatRank.Rank.Select(e => new RankEntry(e.No, ToISO3(e.Shortname), Points: e.Rating));   // the ICC ranks by Rating, not Points

    protected override DateOnly? GetRankingDate(Root root) => IsoDate.Parse(SourceName, root.Data.BatRank.RankDate);

    /// <summary>
    /// ICC "shortname" is a team code, not a country code: women's teams carry a "-W" suffix
    /// and a few teams use two-letter abbreviations. West Indies (WI) has no ISO3 code and is kept as-is.
    /// Sri Lanka is SL and Sierra Leone is SRL, which the IOC table would otherwise leave alone.
    /// </summary>
    internal static string ToISO3(string iccShortName)
    {
        string code = iccShortName.Trim().ToUpperInvariant();

        int suffix = code.IndexOf('-');
        if (suffix > 0)
        {
            code = code[..suffix];
        }

        return IccCodes.TryGetValue(code, out string? iso3) ? iso3 : code.IOCToISO3();
    }

    private static readonly Dictionary<string, string> IccCodes = new()
    {
        { "SA", "ZAF" },
        { "NZ", "NZL" },
        { "SL", "LKA" },
        { "HK", "HKG" },
        { "SRL", "SLE" },
    };
}
