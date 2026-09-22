using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Scoring;

/// <summary>The scoring rule, and the only place it lives: score = min(rank in the mode, cap); unranked = cap.</summary>
public static class ScoringEngine
{
    public static BoardCell Score(CategoryRank? rank, RankMode mode, int cap)
    {
        if (rank is null)
        {
            return new BoardCell(cap, null, null, Unranked: true, null, null, null, null);
        }

        var feed = mode == RankMode.Entry ? rank.BestByEntry : rank.BestByCountry;
        var value = mode == RankMode.Entry ? feed.EntryRank : feed.CountryRank;

        return new BoardCell(
            Math.Min(value, cap),
            feed.CountryRank,
            feed.EntryRank,
            Unranked: false,
            feed.Sport,
            feed.Event,
            feed.Gender,
            feed.Competitor,
            feed.RankedAs);
    }
}
