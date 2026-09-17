using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>ESPN tennis rankings API (ATP and WTA singles).</summary>
public sealed class EspnTennisParser : JsonRankingParser<EspnTennisParser.Root>
{
    public override string SourceName => "EspnTennis";

    public sealed record Root(List<Ranking> Rankings);
    public sealed record Ranking(List<Rank> Ranks, string? Update);
    public sealed record Rank(short Current, Athlete Athlete, decimal? Points);
    public sealed record Athlete(string CitizenshipCountry, string? DisplayName);

    protected override IEnumerable<RankEntry> Map(Root root)
    {
        Ranking ranking = root.Rankings.FirstOrDefault()
            ?? throw new ParseException(SourceName, "no rankings in response");

        return ranking.Ranks.Select(r =>
            new RankEntry(r.Current, r.Athlete.CitizenshipCountry.Trim().ToUpperInvariant().IOCToISO3(), Competitor: r.Athlete.DisplayName, Points: r.Points));
    }

    protected override DateOnly? GetRankingDate(Root root) =>
        root.Rankings.FirstOrDefault()?.Update is string update ? IsoDate.Parse(SourceName, update) : null;
}
