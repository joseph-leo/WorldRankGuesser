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

    [Fact]
    public void Stamps_sport_event_and_gender_from_the_item()
    {
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking([new RankEntry(3, "DEU", "Germany")]), null, Today);

        Assert.Equal("Field Hockey", snapshot.Sport);
        Assert.Equal("Outdoor", snapshot.Event);
        Assert.Equal("Men", snapshot.Gender);
        Assert.Equal([new RankEntry(3, "DEU", "Germany")], snapshot.Entries);
    }

    [Fact]
    public void Entries_come_out_in_position_order()
    {
        RankEntry[] entries = [new(2, "B"), new(1, "A"), new(3, "C")];

        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking(entries), null, Today);

        Assert.Equal(["A", "B", "C"], snapshot.Entries.Select(e => e.ISO3));
    }

    [Fact]
    public void Take_keeps_every_entry_up_to_that_position_including_ties()
    {
        RankingItem item = new() { Sport = "Badminton", Event = "Doubles", Gender = "Men", Url = "http://x", Source = "Bwf", Take = 2 };
        RankEntry[] entries = [new(1, "KOR"), new(1, "KOR"), new(2, "DNK"), new(2, "DNK"), new(3, "CHN"), new(3, "CHN")];

        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(item, new ParsedRanking(entries), null, Today);

        Assert.Equal(4, snapshot.Entries.Count);
        Assert.All(snapshot.Entries, e => Assert.True(e.Position <= 2));
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
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking([new RankEntry(1, "DEU")]), null, Today);

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
