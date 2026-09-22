using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// The view contract between the scraper and the game. The fixture applies the scraper's real migrations, so a
/// change to a column dbo.CurrentCountryRankings projects fails here, in the game's tests, before it reaches a database.
/// </summary>
[Collection("sql")]
public class ViewContractTests(SqlServerFixture sql)
{
    [Fact]
    public async Task The_scrapers_view_yields_the_seeded_rankings_with_every_mapped_column()
    {
        await using var db = sql.CreateContext();

        var rows = await new RankingsReader(db).ReadAsync(CancellationToken.None);

        var seeded = rows.Where(r => RankingsSeed.Feeds.Contains((r.Sport, r.Event, r.Gender))).ToList();
        Assert.Equal(RankingsSeed.Feeds.Length * RankingsSeed.Countries.Length, seeded.Count);

        for (var feed = 0; feed < RankingsSeed.Feeds.Length; feed++)
        for (var country = 0; country < RankingsSeed.Countries.Length; country++)
        {
            var (sport, ev, gender) = RankingsSeed.Feeds[feed];
            var row = Assert.Single(seeded, r => r.Sport == sport && r.Event == ev && r.Gender == gender && r.ISO3 == RankingsSeed.Countries[country]);

            Assert.Equal(RankingsSeed.PositionOf(country, feed), row.Position);
            Assert.Equal(new DateOnly(2026, 9, 14), row.RankingDate);
            Assert.True(row.IsFederationDate);
            Assert.False(string.IsNullOrEmpty(row.TeamName));
            Assert.Null(row.Competitor);
            Assert.Null(row.Points);
            Assert.Equal((int?)1, row.RankedEntrants); // one entry per country per feed in the seed
        }
    }
}
