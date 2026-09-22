using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Games;

namespace WorldRankGuesser.Api.Tests.Integration;

[Collection("sql")]
public class AntiCheatTests(SqlServerFixture sql) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(sql.ConnectionString);
        await _factory.WaitUntilReadyAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task No_response_names_a_future_country_or_reveals_the_grid_before_the_game_is_complete()
    {
        var client = _factory.CreateClient();
        var startResponse = await client.PostAsJsonAsync("/api/games", new StartGameRequest("practice"));
        var body = await startResponse.Content.ReadAsStringAsync();
        var state = (await startResponse.Content.ReadFromJsonAsync<GameStateDto>())!;

        await using var db = sql.CreateContext();
        var countries = (await db.Games.Include(g => g.Board).SingleAsync(g => g.Id == state.Id)).Board.Content.Countries;

        for (var turn = 0; turn < countries.Count; turn++)
        {
            // `body` is the response that revealed the country for `turn`.
            foreach (var future in countries.Skip(turn + 1))
            {
                Assert.DoesNotContain($"\"{future.Iso3}\"", body);
            }

            Assert.Contains("\"grid\":null", body);
            Assert.Contains("\"optimalScore\":null", body);

            var reload = await (await client.GetAsync($"/api/games/{state.Id}")).Content.ReadAsStringAsync();
            foreach (var future in countries.Skip(turn + 1))
            {
                Assert.DoesNotContain($"\"{future.Iso3}\"", reload);
            }

            var pick = await client.PostAsJsonAsync($"/api/games/{state.Id}/picks", new PickRequest(state.Categories[turn].Id));
            body = await pick.Content.ReadAsStringAsync();
        }

        Assert.DoesNotContain("\"grid\":null", body);     // complete: now everything is revealed
    }

    [Fact]
    public async Task No_response_reveals_what_a_pick_scored_before_the_game_is_complete()
    {
        var client = _factory.CreateClient();
        var state = (await (await client.PostAsJsonAsync("/api/games", new StartGameRequest("practice")))
            .Content.ReadFromJsonAsync<GameStateDto>())!;

        foreach (var category in state.Categories.SkipLast(1))
        {
            var pick = await client.PostAsJsonAsync($"/api/games/{state.Id}/picks", new PickRequest(category.Id));
            var reload = await client.GetAsync($"/api/games/{state.Id}");
            var conflict = await client.PostAsJsonAsync($"/api/games/{state.Id}/picks", new PickRequest(category.Id));
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            foreach (var response in new[] { pick, reload, conflict })
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotMatch("\"score\":\\s*\\d", body);
                Assert.DoesNotContain("\"countryRank\"", body);      // no board cell under any name
                Assert.DoesNotContain("\"entryRank\"", body);
                Assert.Contains("\"totalScore\":null", body);
            }
        }

        var last = await client.PostAsJsonAsync($"/api/games/{state.Id}/picks", new PickRequest(state.Categories[^1].Id));
        var final = (await last.Content.ReadFromJsonAsync<GameStateDto>())!;

        Assert.True(final.IsComplete);
        Assert.All(final.Picks, p =>
        {
            Assert.NotNull(p.Score);
            Assert.NotNull(p.Result);
        });
    }

    [Fact]
    public async Task Another_players_game_is_404_for_reads_and_picks()
    {
        var owner = _factory.CreateClient();
        var stranger = _factory.CreateClient();
        var game = (await (await owner.PostAsJsonAsync("/api/games", new StartGameRequest("practice")))
            .Content.ReadFromJsonAsync<GameStateDto>())!;
        await stranger.PostAsJsonAsync("/api/games", new StartGameRequest("practice"));      // the stranger has a cookie too

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/games/{game.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsJsonAsync($"/api/games/{game.Id}/picks", new PickRequest("soccer"))).StatusCode);
    }

    [Fact]
    public async Task A_caller_without_a_cookie_cannot_see_any_game()
    {
        var owner = _factory.CreateClient();
        var game = (await (await owner.PostAsJsonAsync("/api/games", new StartGameRequest("practice")))
            .Content.ReadFromJsonAsync<GameStateDto>())!;

        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/games/{game.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_request_cannot_choose_the_country_or_the_score()
    {
        var client = _factory.CreateClient();
        var game = (await (await client.PostAsJsonAsync("/api/games", new StartGameRequest("practice")))
            .Content.ReadFromJsonAsync<GameStateDto>())!;

        // Extra properties a cheating client might send are ignored: the server decides country and score.
        var response = await client.PostAsJsonAsync(
            $"/api/games/{game.Id}/picks", new { categoryId = "soccer", iso3 = "XXX", score = 1, turnIndex = 9 });
        var state = (await response.Content.ReadFromJsonAsync<GameStateDto>())!;

        var pick = Assert.Single(state.Picks);
        Assert.Equal(0, pick.TurnIndex);
        Assert.Equal(game.CurrentCountry!.Iso3, pick.Country.Iso3);

        // The score stays hidden until the game is complete, so finish it and compare with the stored board.
        foreach (var category in game.Categories.Where(c => c.Id != "soccer"))
        {
            response = await client.PostAsJsonAsync($"/api/games/{game.Id}/picks", new PickRequest(category.Id));
        }

        state = (await response.Content.ReadFromJsonAsync<GameStateDto>())!;
        await using var db = sql.CreateContext();
        var board = (await db.Games.Include(g => g.Board).SingleAsync(g => g.Id == game.Id)).Board.Content;
        var soccer = board.Categories.ToList().FindIndex(c => c.Id == "soccer");

        Assert.Equal(board.Cells[0][soccer].Score, state.Picks[0].Score);
    }
}
