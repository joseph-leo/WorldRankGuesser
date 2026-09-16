using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SportsRankingService.Persistence;

public sealed class RankingRowConfiguration : IEntityTypeConfiguration<RankingRow>
{
    public void Configure(EntityTypeBuilder<RankingRow> row)
    {
        row.ToTable("RankingRows");
        row.HasKey(x => new { x.ReleaseId, x.Ordinal });
        // varchar(3), not char(3): West Indies is "WI".
        row.Property(x => x.ISO3).HasMaxLength(3).IsUnicode(false);
        row.Property(x => x.TeamName).HasMaxLength(100);
    }
}
