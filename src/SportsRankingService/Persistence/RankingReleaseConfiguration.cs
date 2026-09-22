using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SportsRankingService.Persistence;

public sealed class RankingReleaseConfiguration : IEntityTypeConfiguration<RankingRelease>
{
    public void Configure(EntityTypeBuilder<RankingRelease> release)
    {
        release.ToTable("RankingReleases");
        release.HasKey(r => r.Id);
        release.Property(r => r.Sport).HasMaxLength(50);
        release.Property(r => r.Event).HasMaxLength(50);
        release.Property(r => r.Gender).HasMaxLength(10);
        release.Property(r => r.ContentHash).HasMaxLength(32).IsFixedLength();

        // "Newest release for a feed" is the lookup the repository and the CurrentRankings view both do.
        release.HasIndex(r => new { r.Sport, r.Event, r.Gender }).HasDatabaseName("IX_RankingReleases_Feed");

        release.HasMany(r => r.Rows)
            .WithOne(x => x.Release)
            .HasForeignKey(x => x.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
