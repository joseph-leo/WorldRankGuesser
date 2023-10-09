using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using SportsRankingService.Models;

namespace SportsRankingService.RankingsDb;

public partial class WorldRankGuesserContext : DbContext
{
    public WorldRankGuesserContext(DbContextOptions<WorldRankGuesserContext> options)
        : base(options)
    {
    }

    public virtual DbSet<SportsRanking> SportsRankings { get; set; }

    public virtual DbSet<SportsRankings_History> SportsRankings_Histories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SportsRanking>(entity =>
        {
            entity.HasKey(e => e.ID).HasName("PK_Ranking");

            entity.Property(e => e.ISO3).IsFixedLength();
        });

        modelBuilder.Entity<SportsRankings_History>(entity =>
        {
            entity.HasKey(e => e.ID).HasName("PK_Ranking_Hist");

            entity.Property(e => e.ISO3).IsFixedLength();
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
