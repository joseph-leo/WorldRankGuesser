using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

public class RankingMapperTests
{
    private static readonly RankingItem Item = new()
    {
        Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "http://x", Source = "Fih",
    };

    [Fact]
    public void Stamps_sport_event_and_gender_from_the_item()
    {
        SportsRanking row = RankingMapper.ToSportsRanking(new RankEntry(3, "DEU", "Germany"), Item);

        Assert.Equal("Field Hockey", row.Sport);
        Assert.Equal("Outdoor", row.Event);
        Assert.Equal("Men", row.Gender);
        Assert.Equal(3, row.Position);
        Assert.Equal("DEU", row.ISO3);
        Assert.Equal("Germany", row.TeamName);
    }

    [Fact]
    public void Rows_come_out_in_position_order()
    {
        RankEntry[] entries = [new(2, "B"), new(1, "A"), new(3, "C")];

        var rows = RankingMapper.ToSportsRankings(entries, Item);

        Assert.Equal(["A", "B", "C"], rows.Select(r => r.ISO3));
    }
}
