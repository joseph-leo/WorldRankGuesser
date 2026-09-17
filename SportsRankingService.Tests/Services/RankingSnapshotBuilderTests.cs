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
        RankingSnapshot snapshot = Build(new RankEntry(3, "DEU", "Germany"));

        Assert.Equal("Field Hockey", snapshot.Sport);
        Assert.Equal("Outdoor", snapshot.Event);
        Assert.Equal("Men", snapshot.Gender);
        Assert.Equal([new RankingSnapshotEntry(3, "DEU", "Germany", null, null, 1)], snapshot.Entries);
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
    public void One_row_per_country_keeps_the_best_placed_entry_and_counts_the_rest()
    {
        RankingSnapshot snapshot = Build(
            new(1, "USA", Competitor: "Biles"),
            new(2, "JPN", Competitor: "Okamura"),
            new(3, "USA", Competitor: "Lee"),
            new(5, "USA", Competitor: "Jones"));

        Assert.Equal(["USA", "JPN"], snapshot.Entries.Select(e => e.ISO3));
        RankingSnapshotEntry usa = snapshot.Entries[0];
        Assert.Equal(1, usa.Position);
        Assert.Equal("Biles", usa.Competitor);
        Assert.Equal(3, usa.RankedEntrants);
        Assert.Equal(1, snapshot.Entries[1].RankedEntrants);
    }

    [Fact]
    public void A_country_tied_with_itself_keeps_its_first_feed_entry()
    {
        RankingSnapshot snapshot = Build(new(1, "KOR", "Korea", "Kim"), new(1, "KOR", "Korea", "Seo"));

        RankingSnapshotEntry korea = Assert.Single(snapshot.Entries);
        Assert.Equal("Kim", korea.Competitor);
        Assert.Equal(2, korea.RankedEntrants);
    }

    [Fact]
    public void Take_applies_by_position_after_the_country_collapse()
    {
        RankingItem item = new() { Sport = "Badminton", Event = "Singles", Gender = "Men", Url = "http://x", Source = "Bwf", Take = 2 };
        RankEntry[] entries = [new(1, "KOR"), new(2, "KOR"), new(2, "DNK"), new(3, "CHN"), new(4, "DNK")];

        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(item, new ParsedRanking(entries), null, Today);

        Assert.Equal(["KOR", "DNK"], snapshot.Entries.Select(e => e.ISO3));
        // Entrants are counted over the whole ranking, not the kept top.
        Assert.Equal([2, 2], snapshot.Entries.Select(e => e.RankedEntrants));
    }

    [Fact]
    public void The_country_name_is_filled_from_the_code_when_the_feed_gives_none()
    {
        RankingSnapshot snapshot = Build(new(1, "DEU"), new(2, "NLD", "Nederland"), new(3, "WI"));

        Assert.Equal(["Germany", "Nederland", null], snapshot.Entries.Select(e => e.TeamName));
    }

    [Fact]
    public void Competitor_and_points_are_carried_through()
    {
        RankingSnapshot snapshot = Build(new RankEntry(1, "ESP", "Spain", "Alcaraz", 11500m));

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
