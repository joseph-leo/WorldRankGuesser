using SportsRankingService.Configuration;
using SportsRankingService.Models;

namespace SportsRankingService.Tests.Configuration;

public class FeedFilterTests
{
    private static RankingItem Item(string sport, string? @event, string gender) => new()
    {
        Sport = sport, Event = @event, Gender = gender, Url = "http://x", Source = "Fih",
    };

    private static readonly RankingItem CricketOdiWomen = Item("Cricket", "ODI", "Women");
    private static readonly RankingItem CricketOdiMen = Item("Cricket", "ODI", "Men");
    private static readonly RankingItem CricketTestMen = Item("Cricket", "Test", "Men");
    private static readonly RankingItem Baseball5Mixed = Item("Baseball5", null, "Mixed");
    private static readonly RankingItem BaseballMen = Item("Baseball", null, "Men");

    [Fact]
    public void No_arguments_selects_every_feed()
    {
        FeedFilter filter = FeedFilter.Parse([]);

        Assert.Empty(filter.Patterns);
        Assert.True(filter.Matches(CricketOdiWomen));
        Assert.True(filter.Matches(Baseball5Mixed));
    }

    [Fact]
    public void Only_can_be_repeated_and_each_value_becomes_a_pattern()
    {
        FeedFilter filter = FeedFilter.Parse(["--only", "Cricket", "--only", "Rugby Sevens"]);

        Assert.Equal(["Cricket", "Rugby Sevens"], filter.Patterns);
    }

    [Fact]
    public void Only_without_a_value_is_rejected()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => FeedFilter.Parse(["--only"]));

        Assert.Contains("--only", ex.Message);
    }

    [Fact]
    public void An_unknown_argument_is_rejected_by_name()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => FeedFilter.Parse(["--sport", "Cricket"]));

        Assert.Contains("--sport", ex.Message);
    }

    [Fact]
    public void A_sport_pattern_matches_every_feed_of_that_sport()
    {
        FeedFilter filter = new(["Cricket"]);

        Assert.True(filter.Matches(CricketOdiWomen));
        Assert.True(filter.Matches(CricketOdiMen));
        Assert.True(filter.Matches(CricketTestMen));
        Assert.False(filter.Matches(BaseballMen));
    }

    [Fact]
    public void A_sport_and_event_pattern_matches_both_genders_of_that_event()
    {
        FeedFilter filter = new(["Cricket ODI"]);

        Assert.True(filter.Matches(CricketOdiWomen));
        Assert.True(filter.Matches(CricketOdiMen));
        Assert.False(filter.Matches(CricketTestMen));
    }

    [Fact]
    public void A_full_name_matches_exactly_one_feed()
    {
        FeedFilter filter = new(["Cricket ODI Women"]);

        Assert.True(filter.Matches(CricketOdiWomen));
        Assert.False(filter.Matches(CricketOdiMen));
    }

    [Fact]
    public void A_sport_and_gender_pattern_skips_the_event_and_matches_every_event_of_that_gender()
    {
        FeedFilter filter = new(["Cricket Women"]);

        Assert.True(filter.Matches(CricketOdiWomen));
        Assert.False(filter.Matches(CricketOdiMen));
        Assert.False(filter.Matches(CricketTestMen));
    }

    [Fact]
    public void A_pattern_may_start_in_the_middle_of_the_name()
    {
        FeedFilter filter = new(["Gymnastics"]);

        Assert.True(filter.Matches(Item("Artistic Gymnastics", "Vault", "Men")));
        Assert.True(filter.Matches(Item("Rhythmic Gymnastics", "Hoop", "Women")));
        Assert.False(filter.Matches(CricketOdiMen));
    }

    [Fact]
    public void Pattern_words_must_appear_in_the_name_in_order()
    {
        FeedFilter filter = new(["Women Cricket"]);

        Assert.False(filter.Matches(CricketOdiWomen));
    }

    [Fact]
    public void A_pattern_longer_than_the_name_matches_nothing()
    {
        FeedFilter filter = new(["Cricket ODI Women Extra"]);

        Assert.False(filter.Matches(CricketOdiWomen));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        FeedFilter filter = new(["cricket odi"]);

        Assert.True(filter.Matches(CricketOdiWomen));
    }

    [Fact]
    public void Matching_is_by_whole_word_so_Baseball_does_not_match_Baseball5()
    {
        FeedFilter filter = new(["Baseball"]);

        Assert.True(filter.Matches(BaseballMen));
        Assert.False(filter.Matches(Baseball5Mixed));
    }

    [Fact]
    public void A_feed_runs_when_any_pattern_matches_it()
    {
        FeedFilter filter = new(["Baseball", "Cricket Test"]);

        Assert.True(filter.Matches(BaseballMen));
        Assert.True(filter.Matches(CricketTestMen));
        Assert.False(filter.Matches(CricketOdiMen));
    }

    [Fact]
    public void Describe_is_the_sport_event_and_gender_with_a_missing_event_left_out()
    {
        Assert.Equal("Cricket ODI Women", CricketOdiWomen.Describe());
        Assert.Equal("Baseball Men", BaseballMen.Describe());
    }
}
