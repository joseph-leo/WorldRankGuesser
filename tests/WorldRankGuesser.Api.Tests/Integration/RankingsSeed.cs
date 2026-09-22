using SportsRankingService.Persistence;
using SportsRankingService.Services;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// Rankings for the tests, written the way the scraper writes them: one release per feed through
/// <see cref="RankingRepository"/>, so the real dbo.CurrentCountryRankings view serves them. 12 countries ranked 1-12
/// in one feed of each of the 10 categories, deterministic, so tests can predict every score.
/// </summary>
internal static class RankingsSeed
{
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

    public static async Task SaveAsync(string connectionString)
    {
        await using var db = SqlServerFixture.CreateRankingsContext(connectionString);
        var repository = new RankingRepository(db, TimeProvider.System);

        for (var feed = 0; feed < Feeds.Length; feed++)
        {
            var (sport, ev, gender) = Feeds[feed];
            var entries = Enumerable.Range(0, Countries.Length)
                .Select(country => new RankingSnapshotEntry(PositionOf(country, feed), Countries[country], Names[country]))
                .OrderBy(entry => entry.Position)
                .ToList();

            var outcome = await repository.SaveAsync(
                new RankingSnapshot(sport, ev, gender, new DateOnly(2026, 9, 14), IsFederationDate: true, entries),
                CancellationToken.None);

            if (outcome != SaveOutcome.Inserted)
                throw new InvalidOperationException($"Seeding {sport} {ev} {gender}: expected Inserted, got {outcome}.");
        }
    }
}
