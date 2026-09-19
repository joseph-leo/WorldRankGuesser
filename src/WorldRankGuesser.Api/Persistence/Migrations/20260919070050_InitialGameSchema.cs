using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldRankGuesser.Api.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260919070050_InitialGameSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "game");

        migrationBuilder.CreateTable(
            name: "Boards",
            schema: "game",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                DailyDate = table.Column<DateOnly>(type: "date", nullable: true),
                RankMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                Cap = table.Column<int>(type: "int", nullable: false),
                RankingsLoadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                OptimalScore = table.Column<int>(type: "int", nullable: false),
                Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Boards", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DataProtectionKeys",
            schema: "game",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Players",
            schema: "game",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Nickname = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ExternalProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                ExternalSubject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                CurrentStreak = table.Column<int>(type: "int", nullable: false),
                BestStreak = table.Column<int>(type: "int", nullable: false),
                LastDailyDate = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Players", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Games",
            schema: "game",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BoardId = table.Column<long>(type: "bigint", nullable: false),
                Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                DailyDate = table.Column<DateOnly>(type: "date", nullable: true),
                TurnIndex = table.Column<int>(type: "int", nullable: false),
                TurnDeadline = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                TotalScore = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Games", x => x.Id);
                table.ForeignKey(
                    name: "FK_Games_Boards_BoardId",
                    column: x => x.BoardId,
                    principalSchema: "game",
                    principalTable: "Boards",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Games_Players_PlayerId",
                    column: x => x.PlayerId,
                    principalSchema: "game",
                    principalTable: "Players",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Picks",
            schema: "game",
            columns: table => new
            {
                GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TurnIndex = table.Column<int>(type: "int", nullable: false),
                CategoryId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ISO3 = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                Score = table.Column<int>(type: "int", nullable: false),
                WasLate = table.Column<bool>(type: "bit", nullable: false),
                PickedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Picks", x => new { x.GameId, x.TurnIndex });
                table.ForeignKey(
                    name: "FK_Picks_Games_GameId",
                    column: x => x.GameId,
                    principalSchema: "game",
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Boards_DailyDate",
            schema: "game",
            table: "Boards",
            column: "DailyDate",
            unique: true,
            filter: "[DailyDate] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Games_BoardId",
            schema: "game",
            table: "Games",
            column: "BoardId");

        migrationBuilder.CreateIndex(
            name: "IX_Games_DailyDate_TotalScore",
            schema: "game",
            table: "Games",
            columns: new[] { "DailyDate", "TotalScore" })
            .Annotation("SqlServer:Include", new[] { "StartedAt", "CompletedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Games_PlayerId_DailyDate",
            schema: "game",
            table: "Games",
            columns: new[] { "PlayerId", "DailyDate" },
            unique: true,
            filter: "[DailyDate] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Picks_GameId_CategoryId",
            schema: "game",
            table: "Picks",
            columns: new[] { "GameId", "CategoryId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Players_ExternalProvider_ExternalSubject",
            schema: "game",
            table: "Players",
            columns: new[] { "ExternalProvider", "ExternalSubject" },
            unique: true,
            filter: "[ExternalProvider] IS NOT NULL AND [ExternalSubject] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DataProtectionKeys",
            schema: "game");

        migrationBuilder.DropTable(
            name: "Picks",
            schema: "game");

        migrationBuilder.DropTable(
            name: "Games",
            schema: "game");

        migrationBuilder.DropTable(
            name: "Boards",
            schema: "game");

        migrationBuilder.DropTable(
            name: "Players",
            schema: "game");
    }
}
