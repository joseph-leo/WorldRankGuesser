using Microsoft.EntityFrameworkCore;

namespace SportsRankingService.Persistence;

public sealed class RankingsDbContext(DbContextOptions<RankingsDbContext> options) : DbContext(options)
{
    public DbSet<RankingRelease> Releases => Set<RankingRelease>();

    public DbSet<RankingRow> Rows => Set<RankingRow>();

    /// <summary>The one place the provider is configured (Program, design time, tests).</summary>
    public static void Configure(DbContextOptionsBuilder options, string? connectionString) =>
        options.UseSqlServer(connectionString, sql => sql
            // A paused Azure SQL database answers 40613 while it resumes (about a minute in staging), which failed the
            // first save of a run. No one waits on the scraper, so it retries longer than the game. The repository
            // opens no transactions, so each SaveChanges retries on its own; a commit whose acknowledgement is lost
            // can store a release twice, which is harmless: the next run compares with the newest and finds it Unchanged.
            .EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null));

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RankingsDbContext).Assembly);
}
