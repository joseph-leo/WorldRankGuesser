using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorldRankGuesser.Api.Games;
using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Players;

namespace WorldRankGuesser.Api.Tests.Integration;

[Collection("sql")]
public class GameServiceTests(SqlServerFixture sql) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(sql.ConnectionString);
        _ = _factory.Services;      // starts the host, which loads the rankings
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    private Task<Guid> NewPlayer() =>
        InScope(async s => (await s.GetRequiredService<PlayerService>().CreateAsync(default)).Id);

    private Task<GameStateDto> Start(Guid player) =>
        InScope(s => s.GetRequiredService<GameService>().StartPracticeAsync(player, default));

    private Task<PickResult> Pick(Guid player, Guid game, string category) =>
        InScope(s => s.GetRequiredService<GameService>().PickAsync(player, game, category, default));

    private async Task<Board> BoardOf(Guid gameId)
    {
        await using var db = sql.CreateContext();
        return (await db.Games.Include(g => g.Board).SingleAsync(g => g.Id == gameId)).Board;
    }

    [Fact]
    public async Task A_new_game_reveals_the_categories_and_only_the_first_country()
    {
        var state = await Start(await NewPlayer());
        var board = await BoardOf(state.Id);

        Assert.Equal("Practice", state.Mode);
        Assert.Equal("Country", state.RankMode);
        Assert.Equal(150, state.Cap);
        Assert.Equal(10, state.Categories.Count);
        Assert.Empty(state.Picks);
        Assert.Equal(board.Content.Countries[0].Iso3, state.CurrentCountry!.Iso3);
        Assert.Null(state.Deadline);
        Assert.False(state.IsComplete);
        Assert.Null(state.TotalScore);
        Assert.Null(state.OptimalScore);
        Assert.Null(state.Grid);
    }

    [Fact]
    public async Task A_pick_scores_the_boards_cell_for_the_current_country_and_advances_the_turn()
    {
        var player = await NewPlayer();
        var start = await Start(player);
        var board = await BoardOf(start.Id);
        var cricket = board.Content.Categories.ToList().FindIndex(c => c.Id == "cricket");

        var result = Assert.IsType<PickResult.Ok>(await Pick(player, start.Id, "cricket"));

        var pick = Assert.Single(result.State.Picks);
        Assert.Equal(0, pick.TurnIndex);
        Assert.Equal("cricket", pick.CategoryId);
        Assert.Equal(board.Content.Countries[0].Iso3, pick.Country.Iso3);
        Assert.Equal(board.Content.Cells[0][cricket].Score, pick.Score);
        Assert.Equal("Cricket", pick.Result.Sport);
        Assert.False(pick.WasLate);
        Assert.Equal(board.Content.Countries[1].Iso3, result.State.CurrentCountry!.Iso3);
    }

    [Fact]
    public async Task The_last_pick_completes_the_game_and_reveals_the_total_the_optimal_and_the_grid()
    {
        var player = await NewPlayer();
        var start = await Start(player);
        var board = await BoardOf(start.Id);

        GameStateDto state = start;
        foreach (var category in start.Categories)
        {
            state = Assert.IsType<PickResult.Ok>(await Pick(player, start.Id, category.Id)).State;
        }

        var diagonal = Enumerable.Range(0, 10).Sum(i => board.Content.Cells[i][i].Score);
        Assert.True(state.IsComplete);
        Assert.Null(state.CurrentCountry);
        Assert.Equal(diagonal, state.TotalScore);
        Assert.Equal(board.OptimalScore, state.OptimalScore);
        Assert.True(state.OptimalScore <= state.TotalScore);
        Assert.Equal(10, state.Grid!.Countries.Count);
        Assert.Equal(board.Content.Cells[3][7].Score, state.Grid.Cells[3][7].Score);

        var reloaded = await InScope(s => s.GetRequiredService<GameService>().GetAsync(player, start.Id, default));
        Assert.Equal(state.TotalScore, reloaded!.TotalScore);
    }

    [Fact]
    public async Task A_used_category_is_a_conflict_that_returns_the_current_state()
    {
        var player = await NewPlayer();
        var start = await Start(player);
        await Pick(player, start.Id, "soccer");

        var conflict = Assert.IsType<PickResult.Conflict>(await Pick(player, start.Id, "soccer"));

        Assert.Single(conflict.State.Picks);
    }

    [Fact]
    public async Task A_pick_on_a_completed_game_is_a_conflict()
    {
        var player = await NewPlayer();
        var start = await Start(player);
        foreach (var category in start.Categories) await Pick(player, start.Id, category.Id);

        Assert.IsType<PickResult.Conflict>(await Pick(player, start.Id, "soccer"));
    }

    [Fact]
    public async Task An_unknown_category_is_rejected_without_changing_the_game()
    {
        var player = await NewPlayer();
        var start = await Start(player);

        Assert.IsType<PickResult.UnknownCategory>(await Pick(player, start.Id, "curling"));

        var state = await InScope(s => s.GetRequiredService<GameService>().GetAsync(player, start.Id, default));
        Assert.Empty(state!.Picks);
    }

    [Fact]
    public async Task Another_players_game_does_not_exist()
    {
        var start = await Start(await NewPlayer());
        var stranger = await NewPlayer();

        Assert.IsType<PickResult.NotFound>(await Pick(stranger, start.Id, "soccer"));
        Assert.Null(await InScope(s => s.GetRequiredService<GameService>().GetAsync(stranger, start.Id, default)));
    }

    [Fact]
    public async Task Two_simultaneous_picks_of_one_category_yield_one_pick()
    {
        var player = await NewPlayer();
        var start = await Start(player);

        var results = await Task.WhenAll(Pick(player, start.Id, "soccer"), Pick(player, start.Id, "soccer"));

        Assert.Single(results.OfType<PickResult.Ok>());
        Assert.Single(results.OfType<PickResult.Conflict>());
    }

    [Fact]
    public async Task Simultaneous_picks_never_leave_the_game_inconsistent()
    {
        var player = await NewPlayer();
        var start = await Start(player);

        var results = await Task.WhenAll(start.Categories.Select(c => Pick(player, start.Id, c.Id)));

        var succeeded = results.OfType<PickResult.Ok>().Count();
        await using var db = sql.CreateContext();
        var game = await db.Games.Include(g => g.Picks).SingleAsync(g => g.Id == start.Id);

        Assert.InRange(succeeded, 1, 10);
        Assert.Equal(succeeded, game.TurnIndex);
        Assert.Equal(succeeded, game.Picks.Count);
        Assert.Equal(Enumerable.Range(0, succeeded), game.Picks.Select(p => p.TurnIndex).Order());
    }
}
