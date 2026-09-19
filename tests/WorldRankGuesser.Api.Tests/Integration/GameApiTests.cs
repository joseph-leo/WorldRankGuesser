using System.Net;
using System.Net.Http.Json;
using WorldRankGuesser.Api.Games;

namespace WorldRankGuesser.Api.Tests.Integration;

[Collection("sql")]
public class GameApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(sql.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<GameStateDto> Start(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/games", new StartGameRequest("practice"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameStateDto>())!;
    }

    [Fact]
    public async Task Starting_a_game_creates_a_player_and_sets_an_http_only_cookie()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/games", new StartGameRequest("practice"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("wrg_player="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_cookie_identifies_the_player_on_later_requests()
    {
        var client = _factory.CreateClient();
        var game = await Start(client);

        var second = await client.PostAsJsonAsync("/api/games", new StartGameRequest("practice"));
        var reloaded = await client.GetAsync($"/api/games/{game.Id}");

        Assert.False(second.Headers.Contains("Set-Cookie"));     // same player, no new cookie
        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("ranked")]
    [InlineData(null)]
    public async Task Only_practice_mode_exists_in_this_phase(string? mode)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/games", new StartGameRequest(mode));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));    // a rejected request creates no player
    }

    [Fact]
    public async Task A_full_game_over_http()
    {
        var client = _factory.CreateClient();
        var state = await Start(client);

        foreach (var category in state.Categories)
        {
            var response = await client.PostAsJsonAsync($"/api/games/{state.Id}/picks", new PickRequest(category.Id));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            state = (await response.Content.ReadFromJsonAsync<GameStateDto>())!;
        }

        Assert.True(state.IsComplete);
        Assert.Equal(state.Picks.Sum(p => p.Score), state.TotalScore);
        Assert.NotNull(state.Grid);
    }

    [Fact]
    public async Task A_used_category_returns_409_with_the_current_state()
    {
        var client = _factory.CreateClient();
        var game = await Start(client);
        await client.PostAsJsonAsync($"/api/games/{game.Id}/picks", new PickRequest("soccer"));

        var response = await client.PostAsJsonAsync($"/api/games/{game.Id}/picks", new PickRequest("soccer"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<GameStateDto>();
        Assert.Single(state!.Picks);
    }

    [Theory]
    [InlineData("curling")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_unknown_or_missing_category_returns_400(string? categoryId)
    {
        var client = _factory.CreateClient();
        var game = await Start(client);

        var response = await client.PostAsJsonAsync($"/api/games/{game.Id}/picks", new PickRequest(categoryId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_api_routes_are_404_not_the_front_end()
    {
        var response = await _factory.CreateClient().GetAsync("/api/rankings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Game_starts_are_rate_limited_per_player()
    {
        await using var limited = new ApiFactory(sql.ConnectionString, new Dictionary<string, string?>
        {
            ["RateLimits:GameStartsPerPlayerPerHour"] = "2",
        });
        var client = limited.CreateClient();

        await Start(client);      // creates the player; counted against the anonymous partition
        await Start(client);
        await Start(client);
        var blocked = await client.PostAsJsonAsync("/api/games", new StartGameRequest("practice"));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task A_trailing_slash_does_not_bypass_the_game_start_rate_limit()
    {
        await using var limited = new ApiFactory(sql.ConnectionString, new Dictionary<string, string?>
        {
            ["RateLimits:GameStartsPerPlayerPerHour"] = "2",
        });
        var client = limited.CreateClient();

        await Start(client);      // creates the player; counted against the anonymous partition
        await Start(client);
        await Start(client);      // exhausts the per-player limit, as in the sibling test above
        var blocked = await client.PostAsJsonAsync("/api/games/", new StartGameRequest("practice"));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task Starting_a_game_before_the_rankings_load_returns_503_and_creates_no_player()
    {
        await using var notReady = new ApiFactory(sql.EmptyConnectionString);

        var response = await notReady.CreateClient().PostAsJsonAsync("/api/games", new StartGameRequest("practice"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }
}
