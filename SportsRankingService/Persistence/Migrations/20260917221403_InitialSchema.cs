using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsRankingService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RankingReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sport = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Event = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RankingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsFederationDate = table.Column<bool>(type: "bit", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingReleases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RankingRows",
                columns: table => new
                {
                    ReleaseId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<short>(type: "smallint", nullable: false),
                    ISO3 = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Competitor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Points = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingRows", x => new { x.ReleaseId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_RankingRows_RankingReleases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "RankingReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankingReleases_Feed",
                table: "RankingReleases",
                columns: new[] { "Sport", "Event", "Gender" });

            // Read models for consumers, raw SQL outside the EF model; a later migration that
            // changes a projected column must drop and re-create them itself.
            // CurrentRankings: every row of the newest release per feed.
            migrationBuilder.Sql("""
                CREATE VIEW dbo.CurrentRankings AS
                SELECT r.Sport, r.Event, r.Gender, r.RankingDate, r.IsFederationDate,
                       x.Ordinal, x.Position, x.ISO3, x.TeamName, x.Competitor, x.Points
                FROM dbo.RankingReleases r
                JOIN dbo.RankingRows x ON x.ReleaseId = r.Id
                WHERE r.Id = (
                    SELECT MAX(n.Id)
                    FROM dbo.RankingReleases n
                    WHERE n.Sport = r.Sport
                      AND n.Gender = r.Gender
                      AND (n.Event = r.Event OR (n.Event IS NULL AND r.Event IS NULL)));
                """);

            // CurrentCountryRankings: one row per country per feed — its best-placed entry plus how many
            // entries the country holds. MIN(Ordinal) is the best entry because Ordinal is assigned
            // after the stable position sort (ties resolved by feed order).
            migrationBuilder.Sql("""
                CREATE VIEW dbo.CurrentCountryRankings AS
                SELECT r.Sport, r.Event, r.Gender, r.RankingDate, r.IsFederationDate,
                       x.Position, x.ISO3, x.TeamName, x.Competitor, x.Points, c.RankedEntrants
                FROM dbo.RankingReleases r
                JOIN dbo.RankingRows x ON x.ReleaseId = r.Id
                JOIN (
                    SELECT ReleaseId, ISO3, COUNT(*) AS RankedEntrants, MIN(Ordinal) AS BestOrdinal
                    FROM dbo.RankingRows
                    GROUP BY ReleaseId, ISO3
                ) c ON c.ReleaseId = x.ReleaseId AND c.ISO3 = x.ISO3 AND c.BestOrdinal = x.Ordinal
                WHERE r.Id = (
                    SELECT MAX(n.Id)
                    FROM dbo.RankingReleases n
                    WHERE n.Sport = r.Sport
                      AND n.Gender = r.Gender
                      AND (n.Event = r.Event OR (n.Event IS NULL AND r.Event IS NULL)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW dbo.CurrentCountryRankings;");
            migrationBuilder.Sql("DROP VIEW dbo.CurrentRankings;");

            migrationBuilder.DropTable(
                name: "RankingRows");

            migrationBuilder.DropTable(
                name: "RankingReleases");
        }
    }
}
