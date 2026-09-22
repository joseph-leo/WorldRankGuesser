using Microsoft.EntityFrameworkCore;

namespace SportsRankingService.Persistence;

public sealed class RankingsDbContext(DbContextOptions<RankingsDbContext> options) : DbContext(options)
{
    public DbSet<RankingRelease> Releases => Set<RankingRelease>();

    public DbSet<RankingRow> Rows => Set<RankingRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RankingsDbContext).Assembly);
}
