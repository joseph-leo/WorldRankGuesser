using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Persistence;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public const string ConnectionStringName = "WorldRankGuesserConnection";

    public const string Schema = "game";

    private static readonly JsonSerializerOptions BoardJson = new(JsonSerializerDefaults.Web);

    public DbSet<Player> Players => Set<Player>();

    public DbSet<Board> Boards => Set<Board>();

    public DbSet<Game> Games => Set<Game>();

    public DbSet<Pick> Picks => Set<Pick>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Read-only: the scraper's view in dbo. Never part of this context's migrations.</summary>
    public DbSet<CountryRankingRow> CountryRankings => Set<CountryRankingRow>();

    /// <summary>The one place the provider and the migrations history table are configured (Program, tests, design time).</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql
            .MigrationsHistoryTable("__EFMigrationsHistory", Schema)
            // A paused Azure SQL database refuses connections while it resumes; retry instead of failing the first
            // visitor. Only the provider's transient errors retry: a unique index or a row version firing is not one,
            // so a losing simultaneous pick still becomes the 409 in GameService.
            .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null));

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);

        model.Entity<CountryRankingRow>(e =>
        {
            e.HasNoKey();
            e.ToView("CurrentCountryRankings", "dbo");
            e.Property(x => x.Points).HasPrecision(12, 3);
        });

        model.Entity<Player>(e =>
        {
            e.Property(x => x.Nickname).HasMaxLength(24);
            e.Property(x => x.ExternalProvider).HasMaxLength(32);
            e.Property(x => x.ExternalSubject).HasMaxLength(200);
            e.HasIndex(x => new { x.ExternalProvider, x.ExternalSubject })
                .IsUnique()
                .HasFilter("[ExternalProvider] IS NOT NULL AND [ExternalSubject] IS NOT NULL");
        });

        model.Entity<Board>(e =>
        {
            e.Property(x => x.RankMode).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Content)
                .HasColumnType("nvarchar(max)")
                .HasConversion(
                    content => JsonSerializer.Serialize(content, BoardJson),
                    json => JsonSerializer.Deserialize<BoardContent>(json, BoardJson)!);
            e.HasIndex(x => x.DailyDate).IsUnique().HasFilter("[DailyDate] IS NOT NULL");
        });

        model.Entity<Game>(e =>
        {
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Board).WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Picks).WithOne().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PlayerId, x.DailyDate }).IsUnique().HasFilter("[DailyDate] IS NOT NULL");
            e.HasIndex(x => new { x.DailyDate, x.TotalScore }).IncludeProperties(x => new { x.StartedAt, x.CompletedAt });
        });

        model.Entity<Pick>(e =>
        {
            e.HasKey(x => new { x.GameId, x.TurnIndex });
            e.Property(x => x.CategoryId).HasMaxLength(32);
            e.Property(x => x.ISO3).HasColumnName("ISO3").HasMaxLength(3).IsUnicode(false);
            e.HasIndex(x => new { x.GameId, x.CategoryId }).IsUnique();
        });
    }
}
