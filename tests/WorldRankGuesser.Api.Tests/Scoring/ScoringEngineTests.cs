using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;
using WorldRankGuesser.Api.Scoring;

namespace WorldRankGuesser.Api.Tests.Scoring;

public class ScoringEngineTests
{
    // Denmark: best player is world #3 in singles (2nd-best nation there),
    // and its doubles pair is world #5 but the best nation in doubles.
    private static readonly FeedRank Singles = new(EntryRank: 3, CountryRank: 2, "Badminton", "Singles", "Men", "Viktor Axelsen");
    private static readonly FeedRank Doubles = new(EntryRank: 5, CountryRank: 1, "Badminton", "Doubles", "Men", "Kim Astrup");
    private static readonly CategoryRank Denmark = new(BestByEntry: Singles, BestByCountry: Doubles);

    [Fact]
    public void Unranked_scores_the_cap()
    {
        var cell = ScoringEngine.Score(null, RankMode.Country, cap: 150);

        Assert.Equal(150, cell.Score);
        Assert.True(cell.Unranked);
        Assert.Null(cell.CountryRank);
        Assert.Null(cell.EntryRank);
        Assert.Null(cell.Sport);
    }

    [Fact]
    public void Entry_mode_scores_the_entry_rank_of_the_best_entry_feed()
    {
        var cell = ScoringEngine.Score(Denmark, RankMode.Entry, cap: 150);

        Assert.Equal(3, cell.Score);
        Assert.Equal(3, cell.EntryRank);
        Assert.Equal(2, cell.CountryRank);
        Assert.Equal("Singles", cell.Event);
        Assert.Equal("Viktor Axelsen", cell.Competitor);
        Assert.False(cell.Unranked);
    }

    [Fact]
    public void Country_mode_scores_the_country_rank_of_the_best_country_feed()
    {
        var cell = ScoringEngine.Score(Denmark, RankMode.Country, cap: 150);

        Assert.Equal(1, cell.Score);
        Assert.Equal(1, cell.CountryRank);
        Assert.Equal(5, cell.EntryRank);
        Assert.Equal("Doubles", cell.Event);
    }

    [Fact]
    public void A_rank_below_the_cap_scores_the_cap_but_stays_ranked()
    {
        var feed = new FeedRank(EntryRank: 180, CountryRank: 180, "Soccer", null, "Men", null);
        var cell = ScoringEngine.Score(new CategoryRank(feed, feed), RankMode.Country, cap: 150);

        Assert.Equal(150, cell.Score);
        Assert.False(cell.Unranked);
        Assert.Equal(180, cell.CountryRank);
    }

    [Fact]
    public void An_inherited_rank_keeps_the_name_of_the_team_it_came_from()
    {
        var feed = new FeedRank(EntryRank: 9, CountryRank: 9, "Cricket", "ODI", "Men", null, RankedAs: "West Indies");
        var cell = ScoringEngine.Score(new CategoryRank(feed, feed), RankMode.Country, cap: 150);

        Assert.Equal("West Indies", cell.RankedAs);
    }
}
