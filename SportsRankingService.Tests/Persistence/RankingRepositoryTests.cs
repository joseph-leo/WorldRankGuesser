using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SportsRankingService.Parsing;
using SportsRankingService.Persistence;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Persistence;

/// <summary>
/// Repository behaviour on an in-memory SQLite database created from the EF model (EnsureCreated).
/// This covers the tables and the insert-on-change rule; the CurrentRankings view lives only in
/// the SQL Server migration and is verified against the Docker instance instead.
/// </summary>
public sealed class RankingRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<RankingsDbContext> _options;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));

    public RankingRepositoryTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<RankingsDbContext>().UseSqlite(_connection).Options;

        using RankingsDbContext db = new(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private async Task<SaveOutcome> SaveAsync(RankingSnapshot snapshot)
    {
        // A fresh context per call, like the updater's per-feed scope.
        using RankingsDbContext db = new(_options);
        return await new RankingRepository(db, _clock).SaveAsync(snapshot, CancellationToken.None);
    }

    private List<RankingRelease> Releases()
    {
        using RankingsDbContext db = new(_options);
        return db.Releases.Include(r => r.Rows.OrderBy(x => x.Ordinal)).OrderBy(r => r.Id).ToList();
    }

    private static RankingSnapshot Hockey(DateOnly date, bool federation, params RankingSnapshotEntry[] entries) =>
        new("Field Hockey", "Outdoor", "Men", date, federation, entries);

    [Fact]
    public async Task First_save_inserts_a_release_with_one_row_per_entry()
    {
        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 15), false, new(1, "DEU", "Germany"), new(2, "NLD", "Netherlands")));

        Assert.Equal(SaveOutcome.Inserted, outcome);
        RankingRelease release = Assert.Single(Releases());
        Assert.Equal("Field Hockey", release.Sport);
        Assert.Equal("Outdoor", release.Event);
        Assert.Equal("Men", release.Gender);
        Assert.Equal(new DateOnly(2026, 9, 15), release.RankingDate);
        Assert.False(release.IsFederationDate);
        Assert.Equal(_clock.GetUtcNow(), release.FirstSeenAt);
        Assert.Equal(_clock.GetUtcNow(), release.LastSeenAt);
        Assert.Equal(32, release.ContentHash.Length);
        Assert.Equal([(0, 1, "DEU", "Germany"), (1, 2, "NLD", "Netherlands")], release.Rows.Select(x => (x.Ordinal, (int)x.Position, x.ISO3, x.TeamName)));
    }

    [Fact]
    public async Task An_identical_save_only_bumps_LastSeenAt()
    {
        await SaveAsync(Hockey(new(2026, 9, 15), false, new(1, "DEU"), new(2, "NLD")));
        _clock.Advance(TimeSpan.FromDays(7));

        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 22), false, new(1, "DEU"), new(2, "NLD")));

        Assert.Equal(SaveOutcome.Unchanged, outcome);
        RankingRelease release = Assert.Single(Releases());
        Assert.Equal(new DateOnly(2026, 9, 15), release.RankingDate);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero), release.FirstSeenAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero), release.LastSeenAt);
    }

    [Fact]
    public async Task Changed_rows_insert_a_second_release_and_keep_the_first()
    {
        await SaveAsync(Hockey(new(2026, 9, 15), false, new(1, "DEU"), new(2, "NLD")));

        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 22), false, new(1, "NLD"), new(2, "DEU")));

        Assert.Equal(SaveOutcome.Inserted, outcome);
        List<RankingRelease> releases = Releases();
        Assert.Equal(2, releases.Count);
        Assert.Equal("DEU", releases[0].Rows[0].ISO3);
        Assert.Equal("NLD", releases[1].Rows[0].ISO3);
    }

    [Fact]
    public async Task Same_rows_under_a_new_federation_date_insert_a_release()
    {
        await SaveAsync(Hockey(new(2026, 9, 12), true, new RankingSnapshotEntry(1, "DEU")));

        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 19), true, new RankingSnapshotEntry(1, "DEU")));

        Assert.Equal(SaveOutcome.Inserted, outcome);
        Assert.Equal([new DateOnly(2026, 9, 12), new DateOnly(2026, 9, 19)], Releases().Select(r => r.RankingDate));
    }

    [Fact]
    public async Task Reverting_to_older_content_is_still_a_new_release()
    {
        await SaveAsync(Hockey(new(2026, 9, 15), false, new RankingSnapshotEntry(1, "DEU")));
        await SaveAsync(Hockey(new(2026, 9, 15), false, new RankingSnapshotEntry(1, "NLD")));

        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 15), false, new RankingSnapshotEntry(1, "DEU")));

        Assert.Equal(SaveOutcome.Inserted, outcome);
        Assert.Equal(3, Releases().Count);
    }

    [Fact]
    public async Task Two_entries_at_the_same_position_are_both_stored()
    {
        RankingSnapshot doubles = new("Badminton", "Doubles", "Men", new(2026, 9, 15), false,
            [new(1, "KOR", "Korea", "KIM Won Ho"), new(1, "KOR", "Korea", "SEO Seung Jae")]);

        await SaveAsync(doubles);

        RankingRelease release = Assert.Single(Releases());
        Assert.Equal(["KIM Won Ho", "SEO Seung Jae"], release.Rows.Select(x => x.Competitor));
        Assert.All(release.Rows, x => Assert.Equal(1, x.Position));
    }

    [Fact]
    public async Task Feeds_are_independent_including_a_null_event()
    {
        RankingSnapshot basketballMen = new("Basketball", null, "Men", new(2026, 9, 1), true, [new(1, "USA")]);
        RankingSnapshot basketballWomen = new("Basketball", null, "Women", new(2026, 9, 1), true, [new(1, "USA")]);

        Assert.Equal(SaveOutcome.Inserted, await SaveAsync(basketballMen));
        Assert.Equal(SaveOutcome.Inserted, await SaveAsync(basketballWomen));
        Assert.Equal(SaveOutcome.Unchanged, await SaveAsync(basketballMen));
        Assert.Equal(SaveOutcome.Unchanged, await SaveAsync(basketballWomen));
        Assert.Equal(2, Releases().Count);
    }

    [Fact]
    public async Task Stores_the_country_name_competitor_and_points()
    {
        await SaveAsync(new RankingSnapshot("Tennis", "Singles", "Men", new(2026, 9, 10), true,
            [new(1, "ITA", "Italy", "Jannik Sinner", 11500m), new(2, "ESP", "Spain", "Carlos Alcaraz", 9000.5m)]));

        RankingRelease release = Assert.Single(Releases());
        Assert.Equal(
            [("ITA", "Italy", "Jannik Sinner", 11500m), ("ESP", "Spain", "Carlos Alcaraz", 9000.5m)],
            release.Rows.Select(x => (x.ISO3, x.TeamName, x.Competitor, x.Points)));
    }

    [Fact]
    public async Task A_new_best_athlete_at_the_same_position_is_a_new_release()
    {
        await SaveAsync(Hockey(new(2026, 9, 15), false, new RankingSnapshotEntry(1, "DEU", "Germany", "Anna")));

        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 15), false, new RankingSnapshotEntry(1, "DEU", "Germany", "Berta")));

        Assert.Equal(SaveOutcome.Inserted, outcome);
        Assert.Equal(2, Releases().Count);
    }
}
