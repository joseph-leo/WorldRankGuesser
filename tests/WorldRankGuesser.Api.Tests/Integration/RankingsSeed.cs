using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// A stand-in for the scraper's view: a table with the view's exact columns, filled with 12 countries ranked 1-12
/// in one feed of each of the 10 categories. Deterministic, so tests can predict every score.
/// </summary>
internal static class RankingsSeed
{
    public const string CreateTableSql = """
        CREATE TABLE dbo.CurrentCountryRankings (
            Sport nvarchar(100) NOT NULL,
            Event nvarchar(100) NULL,
            Gender nvarchar(20) NOT NULL,
            RankingDate date NOT NULL,
            IsFederationDate bit NOT NULL,
            Position smallint NOT NULL,
            ISO3 varchar(3) NOT NULL,
            TeamName nvarchar(200) NULL,
            Competitor nvarchar(200) NULL,
            Points decimal(12,3) NULL,
            RankedEntrants int NULL);
        """;

    public static readonly string[] Countries =
        ["AUS", "BRA", "CAN", "DEU", "ESP", "FRA", "IND", "ITA", "JPN", "NZL", "USA", "ZAF"];

    private static readonly string[] Names =
        ["Australia", "Brazil", "Canada", "Germany", "Spain", "France", "India", "Italy", "Japan", "New Zealand", "United States of America", "South Africa"];

    public static readonly (string Sport, string? Event, string Gender)[] Feeds =
    [
        ("Soccer", null, "Men"), ("Basketball", null, "Men"), ("Cricket", "ODI", "Men"), ("Rugby", "Union", "Men"),
        ("Volleyball", null, "Men"), ("Tennis", "Singles", "Men"), ("Badminton", "Singles", "Men"), ("Baseball", null, "Men"),
        ("Field Hockey", "Outdoor", "Men"), ("Artistic Gymnastics", "Vault", "Men"),
    ];

    /// <summary>Each feed is a rotation of the country list, so every country holds every position 1-12 somewhere.</summary>
    public static short PositionOf(int countryIndex, int feedIndex) => (short)((countryIndex + feedIndex * 5) % Countries.Length + 1);

    public static async Task InsertAsync(GameDbContext db)
    {
        for (var feed = 0; feed < Feeds.Length; feed++)
        for (var country = 0; country < Countries.Length; country++)
        {
            var (sport, ev, gender) = Feeds[feed];
            var position = PositionOf(country, feed);
            var iso3 = Countries[country];
            var name = Names[country];

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.CurrentCountryRankings
                    (Sport, Event, Gender, RankingDate, IsFederationDate, Position, ISO3, TeamName, Competitor, Points, RankedEntrants)
                VALUES ({sport}, {ev}, {gender}, '2026-09-14', 1, {position}, {iso3}, {name}, NULL, NULL, 1)
                """);
        }
    }
}
