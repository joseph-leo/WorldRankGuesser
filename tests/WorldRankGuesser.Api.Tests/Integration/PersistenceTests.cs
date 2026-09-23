using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Tests.Integration;

[Collection("sql")]
public class PersistenceTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static Board NewBoard(DateOnly? dailyDate = null) => new()
    {
        DailyDate = dailyDate,
        RankMode = RankMode.Country,
        Cap = 150,
        RankingsLoadedAt = Now,
        OptimalScore = 3,
        CreatedAt = Now,
        Content = new BoardContent(
            [new BoardCategory("soccer", "Soccer")],
            [new BoardCountry("JPN", "JP", "Japan")],
            [[new BoardCell(3, 3, 17, false, "Soccer", null, "Men", "Someone")]]),
    };

    private static Player NewPlayer() => new() { Id = Guid.NewGuid(), CreatedAt = Now, LastSeenAt = Now };

    private static Game NewGame(Player player, Board board, DateOnly? dailyDate = null) => new()
    {
        Id = Guid.NewGuid(),
        Player = player,
        Board = board,
        Mode = dailyDate is null ? GameMode.Practice : GameMode.Daily,
        DailyDate = dailyDate,
        StartedAt = Now,
    };

    [Fact]
    public void The_context_retries_transient_failures()
    {
        // A paused Azure SQL database refuses connections while it resumes; the first visitor must not get a 500.
        using var db = sql.CreateContext();

        Assert.IsType<SqlServerRetryingExecutionStrategy>(db.Database.CreateExecutionStrategy());
    }

    [Fact]
    public void The_entra_authentication_provider_ships_with_the_api()
    {
        // Since SqlClient 7 the Entra modes (Active Directory Managed Identity in Azure) live in a separate package,
        // without which the first connection in Azure fails; referencing it is the only wiring it needs.
        var provider = Type.GetType("Microsoft.Data.SqlClient.ActiveDirectoryAuthenticationProvider, Microsoft.Data.SqlClient.Extensions.Azure");

        Assert.NotNull(provider);
    }

    [Fact]
    public async Task Board_content_round_trips_as_json()
    {
        var board = NewBoard();
        await using (var db = sql.CreateContext())
        {
            db.Boards.Add(board);
            await db.SaveChangesAsync();
        }

        await using var read = sql.CreateContext();
        var loaded = await read.Boards.SingleAsync(b => b.Id == board.Id);

        Assert.Equal(RankMode.Country, loaded.RankMode);
        Assert.Equal("Japan", loaded.Content.Countries[0].Name);
        Assert.Equal(new BoardCell(3, 3, 17, false, "Soccer", null, "Men", "Someone"), loaded.Content.Cells[0][0]);
    }

    [Fact]
    public async Task Only_one_board_per_daily_date_but_any_number_of_practice_boards()
    {
        var date = new DateOnly(2031, 1, 1);
        await using var db = sql.CreateContext();
        db.Boards.AddRange(NewBoard(), NewBoard(), NewBoard(date));
        await db.SaveChangesAsync();

        db.Boards.Add(NewBoard(date));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task One_daily_game_per_player_per_date_but_any_number_of_practice_games()
    {
        var date = new DateOnly(2031, 1, 2);
        var player = NewPlayer();
        var board = NewBoard(date);
        await using var db = sql.CreateContext();
        db.Games.AddRange(NewGame(player, board), NewGame(player, board), NewGame(player, board, date));
        await db.SaveChangesAsync();

        db.Games.Add(NewGame(player, board, date));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_category_cannot_be_picked_twice_in_one_game()
    {
        var game = NewGame(NewPlayer(), NewBoard());
        game.Picks.Add(new Pick { TurnIndex = 0, CategoryId = "soccer", ISO3 = "JPN", Score = 3, PickedAt = Now });
        await using var db = sql.CreateContext();
        db.Games.Add(game);
        await db.SaveChangesAsync();

        game.Picks.Add(new Pick { TurnIndex = 1, CategoryId = "soccer", ISO3 = "AUS", Score = 9, PickedAt = Now });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_stale_game_update_is_rejected()
    {
        var game = NewGame(NewPlayer(), NewBoard());
        await using (var db = sql.CreateContext())
        {
            db.Games.Add(game);
            await db.SaveChangesAsync();
        }

        await using var first = sql.CreateContext();
        await using var second = sql.CreateContext();
        var a = await first.Games.SingleAsync(g => g.Id == game.Id);
        var b = await second.Games.SingleAsync(g => g.Id == game.Id);

        a.TurnIndex = 1;
        await first.SaveChangesAsync();
        b.TurnIndex = 1;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task The_rankings_view_is_readable_through_the_context()
    {
        await using var db = sql.CreateContext();

        var rows = await db.CountryRankings.AsNoTracking().Where(r => r.Sport == "Soccer").ToListAsync();

        Assert.Equal(12, rows.Count);
        Assert.Equal(RankingsSeed.PositionOf(0, 0), rows.Single(r => r.ISO3 == "AUS").Position);
    }
}
