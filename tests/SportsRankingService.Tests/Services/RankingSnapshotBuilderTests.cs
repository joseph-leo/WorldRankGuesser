using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

public class RankingSnapshotBuilderTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);

    private static readonly RankingItem Item = new()
    {
        Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "http://x", Source = "Fih",
    };

    private static RankingSnapshot Build(params RankEntry[] entries) =>
        RankingSnapshotBuilder.Build(Item, new ParsedRanking(entries), null, Today);

    [Fact]
    public void Stamps_sport_event_and_gender_from_the_item()
    {
        RankingSnapshot snapshot = Build(new RankEntry(3, "DEU"));

        Assert.Equal("Field Hockey", snapshot.Sport);
        Assert.Equal("Outdoor", snapshot.Event);
        Assert.Equal("Men", snapshot.Gender);
        Assert.Equal([new RankingSnapshotEntry(3, "DEU", "Germany")], snapshot.Entries);
    }

    [Fact]
    public void Entries_come_out_in_position_order()
    {
        RankingSnapshot snapshot = Build(new(2, "BEL"), new(1, "AUS"), new(3, "CAN"));

        Assert.Equal(["AUS", "BEL", "CAN"], snapshot.Entries.Select(e => e.ISO3));
    }

    [Fact]
    public void Entries_tied_on_position_keep_their_feed_order()
    {
        RankingSnapshot snapshot = Build(new(1, "KOR"), new(1, "DNK"), new(1, "CHN"));

        Assert.Equal(["KOR", "DNK", "CHN"], snapshot.Entries.Select(e => e.ISO3));
    }

    [Fact]
    public void Every_entry_is_kept_a_country_may_appear_many_times()
    {
        RankingSnapshot snapshot = Build(
            new(1, "USA", Competitor: "Biles"),
            new(3, "USA", Competitor: "Lee"),
            new(2, "JPN", Competitor: "Okamura"),
            new(5, "USA", Competitor: "Jones"));

        Assert.Equal(["Biles", "Okamura", "Lee", "Jones"], snapshot.Entries.Select(e => e.Competitor));
        Assert.Equal(["USA", "JPN", "USA", "USA"], snapshot.Entries.Select(e => e.ISO3));
    }

    [Fact]
    public void Partners_differing_only_by_competitor_are_both_kept()
    {
        RankingSnapshot snapshot = Build(
            new(1, "KOR", "KIM Won Ho", 114099m),
            new(1, "KOR", "SEO Seung Jae", 114099m));

        Assert.Equal(2, snapshot.Entries.Count);
    }

    [Fact]
    public void Equal_entries_fail_the_build_naming_the_entry()
    {
        var ex = Assert.Throws<ParseException>(() => Build(
            new(1, "KOR", "KIM Won Ho", 114099m),
            new(1, "KOR", "KIM Won Ho", 114099m)));

        Assert.Contains("KOR", ex.Message);
    }

    [Fact]
    public void The_country_name_always_comes_from_the_code_table()
    {
        RankingSnapshot snapshot = Build(new(1, "DEU"), new(2, "NLD"), new(3, "WI"), new(4, "USA"));

        Assert.Equal(["Germany", "Netherlands", "West Indies", "United States of America"], snapshot.Entries.Select(e => e.TeamName));
    }

    [Fact]
    public void An_unknown_code_fails_the_build_naming_the_code()
    {
        var ex = Assert.Throws<ParseException>(() => Build(new(1, "DEU"), new(2, "ZZZ")));

        Assert.Contains("ZZZ", ex.Message);
    }

    [Fact]
    public void Competitor_and_points_are_carried_through()
    {
        RankingSnapshot snapshot = Build(new RankEntry(1, "ESP", "Alcaraz", 11500m));

        RankingSnapshotEntry spain = Assert.Single(snapshot.Entries);
        Assert.Equal("Alcaraz", spain.Competitor);
        Assert.Equal(11500m, spain.Points);
    }

    [Fact]
    public void The_parsers_date_wins_and_is_flagged_as_the_federations()
    {
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking([new RankEntry(1, "DEU")], new DateOnly(2026, 9, 12)), new DateOnly(2026, 9, 1), Today);

        Assert.Equal(new DateOnly(2026, 9, 12), snapshot.RankingDate);
        Assert.True(snapshot.IsFederationDate);
    }

    [Fact]
    public void The_resolvers_date_is_used_when_the_parser_has_none()
    {
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking([new RankEntry(1, "DEU")]), new DateOnly(2026, 9, 1), Today);

        Assert.Equal(new DateOnly(2026, 9, 1), snapshot.RankingDate);
        Assert.True(snapshot.IsFederationDate);
    }

    [Fact]
    public void Today_is_the_fallback_and_is_flagged_as_not_the_federations()
    {
        RankingSnapshot snapshot = Build(new RankEntry(1, "DEU"));

        Assert.Equal(Today, snapshot.RankingDate);
        Assert.False(snapshot.IsFederationDate);
    }

    [Fact]
    public void Describe_joins_the_non_empty_parts()
    {
        RankingItem noEvent = new() { Sport = "Basketball", Gender = "Women", Url = "http://x", Source = "Fiba" };

        Assert.Equal("Field Hockey Outdoor Men", RankingSnapshotBuilder.Build(Item, new ParsedRanking([]), null, Today).Describe());
        Assert.Equal("Basketball Women", RankingSnapshotBuilder.Build(noEvent, new ParsedRanking([]), null, Today).Describe());
    }
}
