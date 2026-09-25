# Rankings Refresh Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A scrape reaches players within seconds and without a restart: the game API gets a token-guarded refresh endpoint that swaps its in-memory rankings snapshot in place, and the scraper calls it once at the end of any run that stored a new release.

**Architecture:** The refresh logic leaves `RankingsRefreshService` for a shared singleton `RankingsRefresher` that both the timer and the new `POST /api/rankings/refresh` call under one lock. The scraper gains a `RefreshNotifier` run once after `UpdateAllAsync`. One GitHub secret, `RANKINGS_REFRESH_TOKEN`, reaches the game app and the Job as Container App secrets through the existing Bicep, and the Job's target URL is built from the environment's default domain. No shared code between the two programs; the view stays the only data link.

**Tech Stack:** ASP.NET Core minimal API on .NET 11 (`global.json`), `CryptographicOperations.FixedTimeEquals`, xUnit 2.9 with `WebApplicationFactory` and the Testcontainers SQL fixture; the scraper's generic host with `IHttpClientFactory` and `Microsoft.Extensions.Diagnostics.Testing`'s `FakeLogger`; Bicep 0.47.16 through Azure CLI, Microsoft.App API version `2026-01-01`; GitHub Actions on `ubuntu-24.04`; actionlint; the promotion guard's bash tests.

**Spec:** `docs/superpowers/specs/2026-09-25-rankings-refresh-notification-design.md`. Parent: `docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md`.

## Global Constraints

- **Names.** Route `POST /api/rankings/refresh`; header `X-Refresh-Token`; API setting `Rankings:RefreshToken` (environment `Rankings__RefreshToken`); scraper settings `Notify:Url` and `Notify:Token` (environment `Notify__Url`, `Notify__Token`); GitHub repository secret `RANKINGS_REFRESH_TOKEN`; Container App secret name `rankings-refresh-token` on both the game app and the Job; Bicep parameter `rankingsRefreshToken` on both templates.
- **Without a token, nothing exists:** the route is not mapped (404 like any unknown `/api` path) and the notifier only logs. Tests that do not set the token must see no change in behaviour.
- **The token check comes first** and is constant-time; 401 with no body on a miss. The response of a refresh names no country: only `rows`, `drawableCountries`, `loadedAt`, or `error`.
- **One refresh at a time:** a call during a refresh awaits that refresh's result.
- **A failed notification never changes the scraper's exit code**; the notifier never throws. It notifies only when `Inserted > 0`.
- **The route stays out of the OpenAPI document** (`ExcludeFromDescription`), so `src/WorldRankGuesser.Web/openapi/*.json` and `schema.d.ts` never change in this plan; CI fails if they do.
- **The Job's URL** is `'https://ca-wrg-${env}-game.${cae.properties.defaultDomain}/api/rankings/refresh'` in `scraper.bicep`, never a literal hostname in a parameter file.
- **`env.PATHS` of both deploy workflows does not change**; the guard's tests pin them.
- **Every commit passes** `dotnet build WorldRankGuesser.slnx` (no warnings from the changed projects) and the relevant suites: `dotnet test tests/SportsRankingService.Tests` (no Docker), `dotnet test WorldRankGuesser.slnx` (Docker Desktop running, for the API's SQL tests); `actionlint` and `bash .github/actions/promotion-guard/test.sh` when `.github/` changed; `az bicep build`/`build-params`/`lint` with no output when `infra/` changed. No attribution lines in commits.
- **Branch:** `rankings-refresh`, already created from `origin/main` with the spec committed; work in a worktree of it.

## Review Focus

- A request with no `X-Refresh-Token` header must answer 401 without throwing on the comparison (an absent header is compared as empty). Test: Task 2 (`A_missing_header_is_401`).
- Two refresh calls at the same moment must read the view once and both get that read's result, or the paused database is woken twice for nothing. Test: Task 1 (`Concurrent_calls_share_one_read`).
- A refresh while the view cannot be read must answer 503 and leave the previous snapshot in place, so a scrape's notification arriving during a database hiccup never blanks the game. Tests: Task 1 (`A_failed_read_keeps_the_previous_snapshot_and_reports_the_error`), Task 2 (`A_view_that_cannot_be_read_answers_503_and_keeps_the_snapshot`).
- The response body must never carry a country code, since the route sits on production's public ingress and only the token guards it. Test: Task 2 (`The_response_names_no_country`).
- The scraper's notification must survive a slow refresh: the game may wait tens of seconds for the paused database, so the client timeout is 120 seconds, and a timeout is a warning, not a failure. Tests: Task 3 (`A_transport_failure_warns_and_does_not_throw`), and the client's timeout asserted in `RankingPipelineRegistrationTests`.

---

### Task 1: The shared refresher

**Files:**
- Create: `src/WorldRankGuesser.Api/Rankings/RankingsRefresher.cs`
- Modify: `src/WorldRankGuesser.Api/Rankings/RankingsRefreshService.cs` (the whole class)
- Modify: `src/WorldRankGuesser.Api/Configuration/RankingsOptions.cs`
- Modify: `src/WorldRankGuesser.Api/Program.cs:77-83` (register the refresher)
- Test: `tests/WorldRankGuesser.Api.Tests/Rankings/RankingsRefresherTests.cs` (new), `tests/WorldRankGuesser.Api.Tests/Rankings/RankingsRefreshServiceTests.cs:50-66` (the `Create` helper)

**Interfaces:**
- Consumes: `IRankingsReader`, `IRankingsStore`, `RankingsSnapshotBuilder.Build(rows, gameOptions, cap, catalog, now)`, `CountryCatalog`.
- Produces: `public sealed record RefreshResult(bool Loaded, int Rows, int DrawableCountries, DateTimeOffset LoadedAt, string? Error)`; `public sealed class RankingsRefresher` with `Task<RefreshResult> RefreshAsync(CancellationToken ct)`; `RankingsOptions.RefreshToken` (`string?`, default null); `RankingsRefreshService(RankingsRefresher refresher, IOptions<RankingsOptions> rankingsOptions, TimeProvider time)`.

- [ ] **Step 1: Write the failing tests**

`tests/WorldRankGuesser.Api.Tests/Rankings/RankingsRefresherTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Rankings;

/// <summary>
/// The one place a snapshot is read, built and stored: the background timer and the refresh endpoint both call it.
/// A failed read keeps the previous snapshot; two callers at once share one read.
/// </summary>
public class RankingsRefresherTests
{
    /// <summary>Answers each read from a queue: an <see cref="Exception"/> is thrown, rows are returned; an empty queue returns <see cref="Rows"/>. A gate, when set, holds every read open until released.</summary>
    private sealed class ScriptedReader(params object[] answers) : IRankingsReader
    {
        private readonly Queue<object> _answers = new(answers);

        public int Attempts { get; private set; }

        public TaskCompletionSource? Gate { get; init; }

        public async Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct)
        {
            Attempts++;
            if (Gate is not null)
            {
                await Gate.Task;
            }

            var answer = _answers.Count > 0 ? _answers.Dequeue() : Rows;
            return answer is Exception error ? throw error : (IReadOnlyList<CountryRankingRow>)answer;
        }
    }

    /// <summary>One country per category of TestData.Options(), so the snapshot can fill a board.</summary>
    private static readonly CountryRankingRow[] Rows =
    [
        TestData.Row("Soccer", null, "Men", 1, "JPN"),
        TestData.Row("Cricket", "ODI", "Men", 1, "IND"),
        TestData.Row("Badminton", "Singles", "Men", 1, "DNK"),
        TestData.Row("Field Hockey", "Outdoor", "Men", 1, "AUS"),
    ];

    private static readonly Exception Down = new InvalidOperationException("The database is resuming.");

    private static (RankingsRefresher Refresher, RankingsStore Store) Create(IRankingsReader reader, FakeTimeProvider? time = null)
    {
        var store = new RankingsStore();
        var scopes = new ServiceCollection()
            .AddScoped<IRankingsReader>(_ => reader)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var refresher = new RankingsRefresher(
            scopes,
            store,
            Options.Create(TestData.Options()),
            Options.Create(new ScoringOptions()),
            TestData.Catalog,
            time ?? new FakeTimeProvider(TestData.LoadedAt),
            NullLogger<RankingsRefresher>.Instance);

        return (refresher, store);
    }

    [Fact]
    public async Task A_load_stores_the_snapshot_and_reports_its_counts()
    {
        var (refresher, store) = Create(new ScriptedReader(Rows));

        var result = await refresher.RefreshAsync(CancellationToken.None);

        Assert.True(result.Loaded);
        Assert.Equal(4, result.Rows);
        Assert.Equal(4, result.DrawableCountries);
        Assert.Equal(TestData.LoadedAt, result.LoadedAt);
        Assert.Null(result.Error);
        Assert.Equal(4, store.Current!.DrawableCountries.Count);
    }

    [Fact]
    public async Task A_failed_read_keeps_the_previous_snapshot_and_reports_the_error()
    {
        var (refresher, store) = Create(new ScriptedReader(Rows, Down));

        await refresher.RefreshAsync(CancellationToken.None);
        var first = store.Current;
        var result = await refresher.RefreshAsync(CancellationToken.None);

        Assert.False(result.Loaded);
        Assert.Contains("resuming", result.Error);
        Assert.Same(first, store.Current);
    }

    [Fact]
    public async Task A_first_read_that_fails_leaves_no_snapshot()
    {
        var (refresher, store) = Create(new ScriptedReader(Down));

        var result = await refresher.RefreshAsync(CancellationToken.None);

        Assert.False(result.Loaded);
        Assert.Null(store.Current);
    }

    /// <summary>A view with fewer drawable countries than categories cannot draw a board, so it does not count as loaded.</summary>
    [Fact]
    public async Task A_view_too_thin_for_a_board_is_refused()
    {
        var (refresher, store) = Create(new ScriptedReader(new[] { Rows[0], Rows[1] }));

        var result = await refresher.RefreshAsync(CancellationToken.None);

        Assert.False(result.Loaded);
        Assert.Contains("drawable countries", result.Error);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Concurrent_calls_share_one_read()
    {
        var reader = new ScriptedReader(Rows) { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var (refresher, _) = Create(reader);

        var first = refresher.RefreshAsync(CancellationToken.None);
        var second = refresher.RefreshAsync(CancellationToken.None);
        reader.Gate.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, reader.Attempts);
        Assert.All(results, r => Assert.True(r.Loaded));
        Assert.Equal(results[0].LoadedAt, results[1].LoadedAt);
    }

    [Fact]
    public async Task A_call_after_a_finished_refresh_reads_again()
    {
        var reader = new ScriptedReader(Rows, Rows);
        var (refresher, _) = Create(reader);

        await refresher.RefreshAsync(CancellationToken.None);
        await refresher.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, reader.Attempts);
    }
}
```

In `tests/WorldRankGuesser.Api.Tests/Rankings/RankingsRefreshServiceTests.cs`, replace the `Create` helper (lines 50 to 66, from `private static (RankingsRefreshService Service, RankingsStore Store) Create(` to the closing brace after `return (service, store);`) with:

```csharp
    private static (RankingsRefreshService Service, RankingsStore Store) Create(IRankingsReader reader, FakeTimeProvider time, int refreshMinutes = 60)
    {
        var store = new RankingsStore();
        var scopes = new ServiceCollection()
            .AddScoped<IRankingsReader>(_ => reader)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var refresher = new RankingsRefresher(
            scopes,
            store,
            Options.Create(TestData.Options()),
            Options.Create(new ScoringOptions()),
            TestData.Catalog,
            time,
            NullLogger<RankingsRefresher>.Instance);

        var service = new RankingsRefreshService(
            refresher,
            Options.Create(new RankingsOptions { RefreshMinutes = refreshMinutes }),
            time);

        return (service, store);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~RankingsRefresherTests|FullyQualifiedName~RankingsRefreshServiceTests"`

Expected: the build fails: `RankingsRefresher` and `RefreshResult` do not exist, and `RankingsRefreshService` has no three-argument constructor.

- [ ] **Step 3: Write the refresher and rewire the service**

`src/WorldRankGuesser.Api/Rankings/RankingsRefresher.cs`:

```csharp
using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>What one refresh did. <see cref="Error"/> is set only when <see cref="Loaded"/> is false; the previous snapshot then stays.</summary>
public sealed record RefreshResult(bool Loaded, int Rows, int DrawableCountries, DateTimeOffset LoadedAt, string? Error);

/// <summary>
/// The one place the rankings view is read into a snapshot and stored: the background timer
/// (<see cref="RankingsRefreshService"/>) and the refresh endpoint both call it. A failed read keeps the previous
/// snapshot, so a refresh can never blank the game. One refresh runs at a time: a call that arrives while one is in
/// flight awaits that refresh and returns its result, so two callers never wake the paused database twice for nothing.
/// </summary>
public sealed class RankingsRefresher(
    IServiceScopeFactory scopes,
    IRankingsStore store,
    IOptions<GameOptions> gameOptions,
    IOptions<ScoringOptions> scoringOptions,
    CountryCatalog catalog,
    TimeProvider time,
    ILogger<RankingsRefresher> logger)
{
    private readonly object _gate = new();
    private Task<RefreshResult>? _inFlight;

    public Task<RefreshResult> RefreshAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            // The in-flight refresh runs on its first caller's token; a second caller only observes it.
            _inFlight ??= RunAsync(ct).ContinueWith(t =>
            {
                lock (_gate)
                {
                    _inFlight = null;
                }

                return t.GetAwaiter().GetResult();
            }, TaskContinuationOptions.ExecuteSynchronously);

            return _inFlight;
        }
    }

    private async Task<RefreshResult> RunAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IRankingsReader>().ReadAsync(ct);
            var snapshot = RankingsSnapshotBuilder.Build(rows, gameOptions.Value, scoringOptions.Value.Cap, catalog, time.GetUtcNow());

            // A view the scraper has not filled yet cannot draw a board (BoardGenerator needs one country per category);
            // treating it as loaded would make /readyz say ready and every game start fail.
            if (snapshot.DrawableCountries.Count < snapshot.Categories.Count)
            {
                throw new InvalidOperationException(
                    $"The rankings view yields {snapshot.DrawableCountries.Count} drawable countries; a board needs {snapshot.Categories.Count}.");
            }

            store.Set(snapshot);
            logger.LogInformation(
                "Rankings loaded: {Rows} rows, {Countries} drawable countries.", rows.Count, snapshot.DrawableCountries.Count);
            return new RefreshResult(true, rows.Count, snapshot.DrawableCountries.Count, snapshot.LoadedAt, null);
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A cold start meeting a resuming database is expected, so those attempts log a warning without
            // claiming a previous snapshot; a failure after a snapshot exists is not expected and stays an error.
            if (store.Current is null)
            {
                logger.LogWarning(error, "Loading the rankings failed; no snapshot yet, trying again.");
            }
            else
            {
                logger.LogError(error, "Loading the rankings failed; keeping the previous snapshot.");
            }

            return new RefreshResult(false, 0, 0, store.Current?.LoadedAt ?? default, error.Message);
        }
    }
}
```

`src/WorldRankGuesser.Api/Rankings/RankingsRefreshService.cs` becomes:

```csharp
using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>
/// Loads the rankings snapshot in the background: at once, then on a short backoff until the first load succeeds
/// (a cold start usually meets a database that is still resuming), then every Rankings:RefreshMinutes. Host startup
/// never waits for a load, so a startup probe cannot trip on it; /readyz reports not-ready until the first snapshot
/// exists. The load itself is <see cref="RankingsRefresher"/>, shared with the refresh endpoint.
/// </summary>
public sealed class RankingsRefreshService(
    RankingsRefresher refresher,
    IOptions<RankingsOptions> rankingsOptions,
    TimeProvider time) : BackgroundService
{
    /// <summary>Waits between attempts while there is no snapshot yet; the last one repeats.</summary>
    private static readonly TimeSpan[] StartupBackoff =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 0; !(await refresher.RefreshAsync(stoppingToken)).Loaded; attempt++)
        {
            await Task.Delay(StartupBackoff[Math.Min(attempt, StartupBackoff.Length - 1)], time, stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(rankingsOptions.Value.RefreshMinutes), time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await refresher.RefreshAsync(stoppingToken);
        }
    }
}
```

`src/WorldRankGuesser.Api/Configuration/RankingsOptions.cs`:

```csharp
namespace WorldRankGuesser.Api.Configuration;

public sealed class RankingsOptions
{
    public const string Section = "Rankings";

    public int RefreshMinutes { get; set; } = 60;

    /// <summary>
    /// The shared secret that POST /api/rankings/refresh requires in X-Refresh-Token. Null (the default, and every
    /// local run) leaves the route unmapped. In Azure the Container App secret behind Rankings__RefreshToken.
    /// </summary>
    public string? RefreshToken { get; set; }
}
```

In `src/WorldRankGuesser.Api/Program.cs`, after `builder.Services.AddScoped<IRankingsReader, RankingsReader>();` add:

```csharp
builder.Services.AddSingleton<RankingsRefresher>();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build WorldRankGuesser.slnx; dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~RankingsRefresherTests|FullyQualifiedName~RankingsRefreshServiceTests"`

Expected: build with no warnings from the API project; 6 refresher tests and every refresh-service test pass. Then `git status` shows no change under `src/WorldRankGuesser.Web/openapi` (the build regenerates the document; a line-ending-only change is restored with `git checkout -- src/WorldRankGuesser.Web/openapi`).

- [ ] **Step 5: Commit**

```powershell
git add src/WorldRankGuesser.Api tests/WorldRankGuesser.Api.Tests
git commit -m "Rankings refresher: one shared, locked load for the timer and the coming endpoint"
```

---

### Task 2: The refresh endpoint

**Files:**
- Create: `src/WorldRankGuesser.Api/Endpoints/RankingsEndpoints.cs`
- Modify: `src/WorldRankGuesser.Api/Program.cs:181-182` (map it before the game endpoints, whose `/api/{**rest}` catch-all must not shadow it)
- Test: `tests/WorldRankGuesser.Api.Tests/Integration/RankingsRefreshEndpointTests.cs` (new)

**Interfaces:**
- Consumes: `RankingsRefresher.RefreshAsync(ct)` returning `RefreshResult(Loaded, Rows, DrawableCountries, LoadedAt, Error)`; `RankingsOptions.RefreshToken` (Task 1).
- Produces: `RankingsEndpoints.MapRankingsEndpoints(this IEndpointRouteBuilder app, RankingsOptions options)`; the route `POST /api/rankings/refresh` answering 401 / 200 `{ rows, drawableCountries, loadedAt }` / 503 `{ error }`; constants `RankingsEndpoints.RefreshRoute` and `RankingsEndpoints.TokenHeader` (`"X-Refresh-Token"`).

- [ ] **Step 1: Write the failing tests**

`tests/WorldRankGuesser.Api.Tests/Integration/RankingsRefreshEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SportsRankingService.Persistence;
using SportsRankingService.Services;
using WorldRankGuesser.Api.Endpoints;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// POST /api/rankings/refresh re-reads the view and swaps the snapshot in place, guarded by a shared token. Without
/// a configured token the route does not exist. The body names counts only, never a country, because the route sits
/// on production's public ingress.
/// </summary>
[Collection("sql")]
public class RankingsRefreshEndpointTests(SqlServerFixture sql)
{
    private const string Token = "test-refresh-token";

    private static ApiFactory WithToken(string connectionString) =>
        new(connectionString, new Dictionary<string, string?> { ["Rankings:RefreshToken"] = Token });

    private static HttpRequestMessage Refresh(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RankingsEndpoints.RefreshRoute);
        if (token is not null)
        {
            request.Headers.Add(RankingsEndpoints.TokenHeader, token);
        }

        return request;
    }

    [Fact]
    public async Task Without_a_configured_token_the_route_does_not_exist()
    {
        await using var factory = new ApiFactory(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();
        var client = factory.CreateClient();

        var response = await client.SendAsync(Refresh(Token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_header_is_401()
    {
        await using var factory = WithToken(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();
        var before = factory.Services.GetRequiredService<IRankingsStore>().Current;

        var response = await factory.CreateClient().PostAsync(RankingsEndpoints.RefreshRoute, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Same(before, factory.Services.GetRequiredService<IRankingsStore>().Current);
    }

    [Fact]
    public async Task A_wrong_token_is_401_and_reads_nothing()
    {
        await using var factory = WithToken(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();
        var before = factory.Services.GetRequiredService<IRankingsStore>().Current;

        var response = await factory.CreateClient().SendAsync(Refresh("wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Same(before, factory.Services.GetRequiredService<IRankingsStore>().Current);
    }

    [Fact]
    public async Task The_right_token_reloads_the_snapshot_in_place_and_answers_the_counts()
    {
        await using var factory = WithToken(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();
        var store = factory.Services.GetRequiredService<IRankingsStore>();
        var before = store.Current!;
        var client = factory.CreateClient();

        var first = await client.SendAsync(Refresh(Token));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = (await first.Content.ReadFromJsonAsync<JsonElement>());
        var rowsBefore = firstBody.GetProperty("rows").GetInt32();
        Assert.Equal(before.DrawableCountries.Count, firstBody.GetProperty("drawableCountries").GetInt32());

        // A feed no category covers: one more row in the view, no change to any board, no other test disturbed.
        await using (var db = SqlServerFixture.CreateRankingsContext(sql.ConnectionString))
        {
            var outcome = await new RankingRepository(db, TimeProvider.System).SaveAsync(
                new RankingSnapshot("Chess", null, "Men", new DateOnly(2026, 9, 25), IsFederationDate: true,
                    [new RankingSnapshotEntry(1, "NOR", "Norway")]),
                CancellationToken.None);
            Assert.Equal(SaveOutcome.Inserted, outcome);
        }

        var second = await client.SendAsync(Refresh(Token));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(rowsBefore + 1, secondBody.GetProperty("rows").GetInt32());
        Assert.NotSame(before, store.Current);
        Assert.True(secondBody.GetProperty("loadedAt").GetDateTimeOffset() > before.LoadedAt);
        Assert.Equal(before.DrawableCountries.Select(c => c.Iso3), store.Current!.DrawableCountries.Select(c => c.Iso3));
    }

    [Fact]
    public async Task The_response_names_no_country()
    {
        await using var factory = WithToken(sql.ConnectionString);
        await factory.WaitUntilReadyAsync();

        var body = await (await factory.CreateClient().SendAsync(Refresh(Token))).Content.ReadAsStringAsync();

        foreach (var iso3 in RankingsSeed.Countries)
        {
            Assert.DoesNotContain(iso3, body);
        }
    }

    [Fact]
    public async Task A_view_that_cannot_be_read_answers_503_and_keeps_the_snapshot()
    {
        await using var factory = WithToken(sql.EmptyConnectionString);
        var client = factory.CreateClient();

        var response = await client.SendAsync(Refresh(Token));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
        Assert.Null(factory.Services.GetRequiredService<IRankingsStore>().Current);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~RankingsRefreshEndpointTests"`

Expected: the build fails on `RankingsEndpoints` not existing.

- [ ] **Step 3: Write the endpoint and map it**

`src/WorldRankGuesser.Api/Endpoints/RankingsEndpoints.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Endpoints;

/// <summary>
/// POST /api/rankings/refresh: re-read the rankings view and swap the in-memory snapshot in place, the same load the
/// background timer does, so a scrape reaches new boards at once with no restart (spec
/// docs/superpowers/specs/2026-09-25-rankings-refresh-notification-design.md). The scraper calls it at the end of a run
/// that stored a new release. Guarded by a shared token, compared in constant time before anything else; mapped
/// only when Rankings:RefreshToken is set, so a local run has no such route. The body names counts only, never a
/// country: the route sits on production's public ingress. Excluded from the OpenAPI document: not the front end's.
/// </summary>
public static class RankingsEndpoints
{
    public const string RefreshRoute = "/api/rankings/refresh";
    public const string TokenHeader = "X-Refresh-Token";

    public static IEndpointRouteBuilder MapRankingsEndpoints(this IEndpointRouteBuilder app, RankingsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RefreshToken))
        {
            return app;
        }

        var expected = Encoding.UTF8.GetBytes(options.RefreshToken);

        app.MapPost(RefreshRoute, async (HttpContext http, RankingsRefresher refresher, CancellationToken ct) =>
        {
            var given = Encoding.UTF8.GetBytes(http.Request.Headers[TokenHeader].ToString());
            if (!CryptographicOperations.FixedTimeEquals(given, expected))
            {
                return Results.Unauthorized();
            }

            var result = await refresher.RefreshAsync(ct);
            return result.Loaded
                ? Results.Ok(new { rows = result.Rows, drawableCountries = result.DrawableCountries, loadedAt = result.LoadedAt })
                : Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).ExcludeFromDescription();

        return app;
    }
}
```

In `src/WorldRankGuesser.Api/Program.cs`, the two mapping lines become three, the rankings route before the game endpoints' `/api/{**rest}` catch-all:

```csharp
app.MapHealthEndpoints();
app.MapRankingsEndpoints(app.Services.GetRequiredService<IOptions<RankingsOptions>>().Value);
app.MapGameEndpoints();
```

If `IOptions` is not already in scope in `Program.cs`, add `using Microsoft.Extensions.Options;` at the top.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build WorldRankGuesser.slnx; dotnet test WorldRankGuesser.slnx`

Expected: no warnings from the API project; every test passes, the six new ones included; `AntiCheatTests` untouched and green. `git status` shows no change under `src/WorldRankGuesser.Web/openapi` beyond line endings (the route is excluded from the document).

- [ ] **Step 5: Commit**

```powershell
git add src/WorldRankGuesser.Api tests/WorldRankGuesser.Api.Tests
git commit -m "POST /api/rankings/refresh: a token-guarded in-place reload of the rankings snapshot"
```

---

### Task 3: The scraper's notifier

**Files:**
- Create: `src/SportsRankingService/Configuration/NotifyOptions.cs`
- Create: `src/SportsRankingService/Services/RefreshNotifier.cs`
- Modify: `src/SportsRankingService/Services/RankingPipelineServiceCollectionExtensions.cs` (a named client and the notifier)
- Modify: `src/SportsRankingService/Program.cs` (bind the options; call the notifier after the run)
- Test: `tests/SportsRankingService.Tests/Services/RefreshNotifierTests.cs` (new), `tests/SportsRankingService.Tests/Services/RankingPipelineRegistrationTests.cs` (the client's timeout)

**Interfaces:**
- Consumes: `UpdateSummary(Feeds, Inserted, Unchanged, Failed)`; the endpoint contract of Task 2 (`POST <Url>`, header `X-Refresh-Token`, 200 body `{ rows, drawableCountries, loadedAt }`).
- Produces: `NotifyOptions { const string SectionName = "Notify"; string? Url; string? Token }`; `RefreshNotifier` with `Task NotifyAsync(UpdateSummary summary, CancellationToken ct)`, `RefreshNotifier.ClientName == "notify"`, `RefreshNotifier.TokenHeader == "X-Refresh-Token"`.

- [ ] **Step 1: Write the failing tests**

`tests/SportsRankingService.Tests/Services/RefreshNotifierTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SportsRankingService.Configuration;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// At the end of a run that stored a new release, one POST tells the game to re-read the view. Nothing configured,
/// or nothing new: no request. A failure is a warning and never an exception, because the game's own timer covers it.
/// </summary>
public class RefreshNotifierTests
{
    private const string Url = "https://ca-wrg-staging-game.example.azurecontainerapps.io/api/rankings/refresh";

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        public Exception? Throws { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (Throws is not null)
            {
                throw Throws;
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(RefreshNotifier.ClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private static (RefreshNotifier Notifier, RecordingHandler Handler, FakeLogger<RefreshNotifier> Log) Build(
        NotifyOptions options, HttpStatusCode status = HttpStatusCode.OK, string body = "{\"rows\":3587,\"drawableCountries\":218,\"loadedAt\":\"2026-09-25T00:59:53Z\"}", Exception? throws = null)
    {
        var handler = new RecordingHandler(status, body) { Throws = throws };
        var log = new FakeLogger<RefreshNotifier>();
        return (new RefreshNotifier(new Factory(handler), options, log), handler, log);
    }

    private static readonly UpdateSummary Inserted = new(Feeds: 54, Inserted: 5, Unchanged: 49, Failed: 0);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Without_a_url_it_sends_nothing_and_says_so_once(string? url)
    {
        var (notifier, handler, log) = Build(new NotifyOptions { Url = url, Token = "t0k" });

        await notifier.NotifyAsync(Inserted, CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains("Notify:Url"));
    }

    [Fact]
    public async Task A_run_with_nothing_new_sends_nothing()
    {
        var (notifier, handler, _) = Build(new NotifyOptions { Url = Url, Token = "t0k" });

        await notifier.NotifyAsync(new UpdateSummary(54, Inserted: 0, Unchanged: 54, Failed: 0), CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_run_with_a_new_release_posts_once_with_the_token_and_logs_the_counts()
    {
        var (notifier, handler, log) = Build(new NotifyOptions { Url = Url, Token = "t0k" });

        await notifier.NotifyAsync(Inserted, CancellationToken.None);

        var (request, _) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Url, request.RequestUri!.ToString());
        Assert.Equal(["t0k"], request.Headers.GetValues(RefreshNotifier.TokenHeader));
        FakeLogRecord line = Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Information);
        Assert.Contains("3587", line.Message);
        Assert.Contains("218", line.Message);
    }

    /// <summary>Some feeds failed but others stored a new release: the view changed, so the game is told.</summary>
    [Fact]
    public async Task A_run_with_failures_and_a_new_release_still_posts()
    {
        var (notifier, handler, _) = Build(new NotifyOptions { Url = Url, Token = "t0k" });

        await notifier.NotifyAsync(new UpdateSummary(54, Inserted: 1, Unchanged: 48, Failed: 5), CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_non_success_status_warns_with_the_status_and_does_not_throw()
    {
        var (notifier, _, log) = Build(new NotifyOptions { Url = Url, Token = "t0k" }, HttpStatusCode.Unauthorized, "");

        await notifier.NotifyAsync(Inserted, CancellationToken.None);

        FakeLogRecord warning = Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Contains("401", warning.Message);
    }

    [Fact]
    public async Task A_transport_failure_warns_and_does_not_throw()
    {
        var (notifier, _, log) = Build(new NotifyOptions { Url = Url, Token = "t0k" }, throws: new TaskCanceledException("timed out"));

        await notifier.NotifyAsync(Inserted, CancellationToken.None);

        Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
    }
}
```

In `tests/SportsRankingService.Tests/Services/RankingPipelineRegistrationTests.cs` add:

```csharp
    /// <summary>The game may wait for its paused database to resume before answering (about 40 seconds on staging), so the notifier waits longer than a fetch.</summary>
    [Fact]
    public void The_notify_client_waits_two_minutes()
    {
        using ServiceProvider provider = Provider();

        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(RefreshNotifier.ClientName);

        Assert.Equal(TimeSpan.FromSeconds(120), client.Timeout);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~RefreshNotifierTests|FullyQualifiedName~RankingPipelineRegistrationTests"`

Expected: the build fails on `NotifyOptions` and `RefreshNotifier` not existing.

- [ ] **Step 3: Write the options, the notifier, the registration and the call**

`src/SportsRankingService/Configuration/NotifyOptions.cs`:

```csharp
namespace SportsRankingService.Configuration;

/// <summary>
/// The game to tell after a run that stored a new release: its POST /api/rankings/refresh URL and the shared token
/// it checks. Bound from the <c>Notify</c> section: in Azure the Job's environment variables <c>Notify__Url</c> and
/// <c>Notify__Token</c>; unset locally, which sends nothing.
/// </summary>
public sealed class NotifyOptions
{
    public const string SectionName = "Notify";

    /// <summary>Absolute URL of the game's refresh endpoint. Null, empty or blank: no notification.</summary>
    public string? Url { get; set; }

    /// <summary>The token sent in X-Refresh-Token.</summary>
    public string? Token { get; set; }
}
```

`src/SportsRankingService/Services/RefreshNotifier.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;

namespace SportsRankingService.Services;

/// <summary>
/// After a run that stored at least one new release, one POST tells the game to re-read the rankings view, so a
/// scrape reaches new boards within seconds instead of at the game's next 12-hour refresh
/// (docs/superpowers/specs/2026-09-25-rankings-refresh-notification-design.md). The only link to the game besides the
/// view, and it carries no data: "read again". A failure is a warning and never changes the run's exit code, since
/// the game's timer still covers it; the client waits two minutes because the game may first have to wake its paused
/// database. Nothing configured: nothing sent, said once.
/// </summary>
public sealed class RefreshNotifier
{
    public const string ClientName = "notify";
    public const string TokenHeader = "X-Refresh-Token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NotifyOptions _options;
    private readonly ILogger<RefreshNotifier> _logger;

    public RefreshNotifier(IHttpClientFactory httpClientFactory, IOptions<NotifyOptions> options, ILogger<RefreshNotifier> logger)
        : this(httpClientFactory, options.Value, logger)
    {
    }

    internal RefreshNotifier(IHttpClientFactory httpClientFactory, NotifyOptions options, ILogger<RefreshNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyAsync(UpdateSummary summary, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            _logger.LogInformation("Notify:Url is not configured: no game is told about new releases");
            return;
        }

        if (summary.Inserted == 0)
        {
            return;
        }

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(ClientName);
            using HttpRequestMessage request = new(HttpMethod.Post, _options.Url);
            request.Headers.TryAddWithoutValidation(TokenHeader, _options.Token ?? "");
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("The game answered {StatusCode} to the refresh notification; its next timed refresh covers it", (int)response.StatusCode);
                return;
            }

            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            _logger.LogInformation("Game notified: {Rows} rows, {Countries} drawable countries",
                body.RootElement.GetProperty("rows").GetInt32(), body.RootElement.GetProperty("drawableCountries").GetInt32());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "The refresh notification to the game failed; its next timed refresh covers it");
        }
    }
}
```

In `src/SportsRankingService/Services/RankingPipelineServiceCollectionExtensions.cs`, after the proxy client's `AddHttpClient` line add:

```csharp
        // The game may wait for its paused database before answering a refresh (about 40 seconds on staging).
        services.AddHttpClient(RefreshNotifier.ClientName, client => client.Timeout = TimeSpan.FromSeconds(120));
```

and after `services.AddTransient<IRankingUpdater, RankingUpdater>();` add:

```csharp
        services.AddSingleton<RefreshNotifier>();
```

In `src/SportsRankingService/Program.cs`, after the `ProxyOptions` `Configure` line add:

```csharp
// Unset locally (no game is told); the Job sets Notify__Url and Notify__Token (infra/scraper.bicep).
builder.Services.Configure<NotifyOptions>(builder.Configuration.GetSection(NotifyOptions.SectionName));
```

and in the `try` block, replace

```csharp
    UpdateSummary summary = await updater.UpdateAllAsync(filter, shutdown.Token);

    return summary.Failed > 0 ? 1 : 0;
```

with

```csharp
    UpdateSummary summary = await updater.UpdateAllAsync(filter, shutdown.Token);

    // Tells the game to re-read the view when a release was stored; a failure here is logged and never fails the run.
    await host.Services.GetRequiredService<RefreshNotifier>().NotifyAsync(summary, shutdown.Token);

    return summary.Failed > 0 ? 1 : 0;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build WorldRankGuesser.slnx; dotnet test tests/SportsRankingService.Tests`

Expected: no warnings from the scraper; every test passes, the seven new ones included.

- [ ] **Step 5: Commit**

```powershell
git add src/SportsRankingService tests/SportsRankingService.Tests
git commit -m "Scraper: tell the game to re-read the view after a run that stored a new release"
```

---

### Task 4: The secret through Bicep and the deploy workflows

**Files:**
- Modify: `infra/game.bicep` (a secure parameter, a secret, an env entry), `infra/staging/game.bicepparam`, `infra/production/game.bicepparam`
- Modify: `infra/scraper.bicep` (a secure parameter, a secret, two env entries), `infra/staging/scraper.bicepparam`, `infra/production/scraper.bicepparam`
- Modify: `.github/workflows/deploy-game.yml` (both deploy steps), `.github/workflows/deploy-scraper.yml` (both deploy steps)

**Interfaces:**
- Consumes: `Rankings__RefreshToken` (Task 1), `Notify__Url` and `Notify__Token` (Task 3); `.github/scripts/require-env.sh NAME "consequence"`.
- Produces: Bicep parameter `rankingsRefreshToken` on both templates; the GitHub secret name `RANKINGS_REFRESH_TOKEN` read by both workflows.

- [ ] **Step 1: The game template and its parameter files**

In `infra/game.bicep`, after the `refreshMinutes` parameter add:

```bicep
// The token POST /api/rankings/refresh requires, from the GitHub secret RANKINGS_REFRESH_TOKEN, which the scraper's
// Job also gets (docs/superpowers/specs/2026-09-25-rankings-refresh-notification-design.md).
@secure()
param rankingsRefreshToken string
```

In the app's `configuration` block, after `activeRevisionsMode: 'Single'` add:

```bicep
      secrets: [
        {
          name: 'rankings-refresh-token'
          value: rankingsRefreshToken
        }
      ]
```

In the container's `env` array, after the `Rankings__RefreshMinutes` entry add:

```bicep
            {
              name: 'Rankings__RefreshToken'
              secretRef: 'rankings-refresh-token'
            }
```

Append to both `infra/staging/game.bicepparam` and `infra/production/game.bicepparam`:

```bicep
// The refresh token comes from the deploy workflow's RANKINGS_REFRESH_TOKEN; the placeholder only lets
// `az bicep build-params` run without it.
param rankingsRefreshToken = readEnvironmentVariable('RANKINGS_REFRESH_TOKEN', 'placeholder')
```

- [ ] **Step 2: The scraper template and its parameter files**

In `infra/scraper.bicep`, after the `proxyToken` parameter add:

```bicep
// The same token the game checks on POST /api/rankings/refresh; the Job calls it after a run that stored a release.
@secure()
param rankingsRefreshToken string
```

In the job's `configuration.secrets` array, after the `proxy-token` entry add:

```bicep
        {
          name: 'rankings-refresh-token'
          value: rankingsRefreshToken
        }
```

In the container's `env` array, after the `Proxy__Token` entry add:

```bicep
            {
              // The game's default hostname: its app name under the environment's default domain, so nothing here
              // depends on the game app existing, and the first-deploy order (scraper, then game) stands.
              name: 'Notify__Url'
              value: 'https://ca-wrg-${env}-game.${cae.properties.defaultDomain}/api/rankings/refresh'
            }
            {
              name: 'Notify__Token'
              secretRef: 'rankings-refresh-token'
            }
```

Append to both `infra/staging/scraper.bicepparam` and `infra/production/scraper.bicepparam`:

```bicep
param rankingsRefreshToken = readEnvironmentVariable('RANKINGS_REFRESH_TOKEN', 'placeholder')
```

- [ ] **Step 3: Build and lint every changed template and parameter file**

Run:

```powershell
foreach ($f in 'infra/game.bicep', 'infra/scraper.bicep') { az bicep build --file $f --stdout | Out-Null; az bicep lint --file $f }
foreach ($f in 'infra/staging/game.bicepparam', 'infra/production/game.bicepparam', 'infra/staging/scraper.bicepparam', 'infra/production/scraper.bicepparam') { az bicep build-params --file $f --stdout | Out-Null }
```

Expected: no output at all. A warning is a failure in CI's `infra` job.

- [ ] **Step 4: Export and guard the secret in both workflows**

In `.github/workflows/deploy-game.yml`, before each of the two `Deploy` steps that run `az deployment group create` with `infra/game.bicep` (the staging one and the production one), add a guard step, and add `RANKINGS_REFRESH_TOKEN` to that deploy step's `env` next to `GAME_IMAGE`:

```yaml
      - name: The rankings refresh token secret exists
        env:
          RANKINGS_REFRESH_TOKEN: ${{ secrets.RANKINGS_REFRESH_TOKEN }}
        run: .github/scripts/require-env.sh RANKINGS_REFRESH_TOKEN "the game would get an empty refresh token and every notification from the scraper would fail with 401"
```

```yaml
          RANKINGS_REFRESH_TOKEN: ${{ secrets.RANKINGS_REFRESH_TOKEN }}
```

Read the file first to find the exact step names: `grep -n "Deploy\|GAME_IMAGE" .github/workflows/deploy-game.yml`. The staging step's `env` already holds `GAME_IMAGE` and `ALLOWED_IPS`; production's holds `GAME_IMAGE`.

In `.github/workflows/deploy-scraper.yml`, the existing `The proxy token secret exists` steps (staging and production) each gain a second line, and both `Deploy the Job by digest` steps' `env` gains the variable:

```yaml
      - name: The proxy and refresh token secrets exist
        env:
          PROXY_TOKEN: ${{ secrets.PROXY_TOKEN }}
          RANKINGS_REFRESH_TOKEN: ${{ secrets.RANKINGS_REFRESH_TOKEN }}
        run: |
          .github/scripts/require-env.sh PROXY_TOKEN "the Job would get an empty proxy token and every WBSC feed would fail with 401"
          .github/scripts/require-env.sh RANKINGS_REFRESH_TOKEN "the Job would get an empty refresh token and its notification to the game would fail with 401"
```

```yaml
          RANKINGS_REFRESH_TOKEN: ${{ secrets.RANKINGS_REFRESH_TOKEN }}
```

- [ ] **Step 5: Lint the workflows and run the guard's tests**

Run (Git Bash): `actionlint && bash .github/actions/promotion-guard/test.sh`

Expected: actionlint prints nothing; the guard's tests pass, both push filters still equal to their `env.PATHS`.

- [ ] **Step 6: Commit**

```powershell
git add infra .github/workflows/deploy-game.yml .github/workflows/deploy-scraper.yml
git commit -m "The rankings refresh token: a secret on the game app and the Job, exported and guarded by both deploys"
```

---

### Task 5: Docs

**Files:**
- Modify: `infra/README.md` (the Cloudflare step gains the new secret; the "Scrape on demand" bullet; the rotation bullet)
- Modify: `CLAUDE.md` (the coupling rule in "What this is"; the Rankings pipeline paragraph; the Deployment paragraph)
- Modify: `src/SportsRankingService/CLAUDE.md` (Architecture point 1; "Current state"), `src/SportsRankingService/README.md`

**Interfaces:** none.

- [ ] **Step 1: The runbook**

In `infra/README.md`, step 5 (Cloudflare) gets a fourth command in its fenced block and one sentence after it:

```bash
   openssl rand -hex 32 | gh secret set RANKINGS_REFRESH_TOKEN
```

`RANKINGS_REFRESH_TOKEN` is the token the game's `POST /api/rankings/refresh` checks and the Job sends after a run that stored a release; both deploys carry it.

The "Scrape on demand" bullet's sentence starting `The game reads the view into memory on startup` and everything after it in that bullet becomes:

```markdown
  The game reads the view into memory on startup and every 12 hours (`Rankings__RefreshMinutes=720`), and the Job
  tells it to read again at the end of any run that stored a new release (`POST /api/rankings/refresh` with the
  shared token; the Job's log says "Game notified", the game's "Rankings loaded"), so a scrape reaches new boards
  within seconds and games in progress keep their boards. A restart is never needed; if a notification failed (the
  Job's log says so), the timer covers it within 12 hours, or call the endpoint by hand with the secret's value
  (GitHub never shows it again; keep it in a password manager):
  `curl -X POST -H "X-Refresh-Token: <the secret>" https://<the game's hostname>/api/rankings/refresh`
  (from an address staging admits).
```

The "Rotate the proxy token" bullet gets a sibling after it:

```markdown
- **Rotate the refresh token:** `openssl rand -hex 32 | gh secret set RANKINGS_REFRESH_TOKEN`, then dispatch
  `deploy-game.yml` and `deploy-scraper.yml` on `main` (and on `prod`). Until both have run for an environment, the
  Job's notification there fails with 401 and the 12-hour timer covers.
```

- [ ] **Step 2: The root notes**

In `CLAUDE.md`:

- "What this is", the coupling rule: `Coupling rule: WorldRankGuesser.Api never references SportsRankingService; the view is the only runtime link, and only the SQL test fixture may use the scraper's migrations.` becomes `Coupling rule: WorldRankGuesser.Api never references SportsRankingService; the view is the only data link, plus one one-way call with no shared code (the scraper's Job POSTs to the game's /api/rankings/refresh after a run that stored a release, so the game re-reads the view at once), and only the SQL test fixture may use the scraper's migrations.`
- "Rankings pipeline" paragraph, after `then every Rankings:RefreshMinutes;` insert: `and at once on POST /api/rankings/refresh with the Rankings:RefreshToken in X-Refresh-Token (RankingsRefresher: one load at a time, shared by the timer and the endpoint; the route is unmapped without a token and excluded from the OpenAPI document; the body carries counts only);`.
- "Deployment" paragraph, after the WBSC proxy sentence add: `The refresh token is the repository secret RANKINGS_REFRESH_TOKEN, carried to the game app (Rankings__RefreshToken) and the Job (Notify__Token, with Notify__Url built from the environment's default domain) by both deploys.`

- [ ] **Step 3: The scraper's notes**

In `src/SportsRankingService/CLAUDE.md`, Architecture point 1, after `and returns exit code 1 if any feed failed.` add: `After the run, RefreshNotifier POSTs once to the game's /api/rankings/refresh (Notify:Url, token in X-Refresh-Token) when Inserted > 0, so the game re-reads the view at once; unset locally (nothing sent, said once); a failure is a warning and never the exit code (the game's 12-hour timer covers); the client waits 120 s because the game may first wake its paused database. RefreshNotifierTests drive it on a recording handler.`

In "Current state", after the sentence ending `which the resolver now reads directly).` add: `Since 2026-09-25 the Job notifies the game after a run with new releases (spec docs/superpowers/specs/2026-09-25-rankings-refresh-notification-design.md), after a merge deployed the game a minute before the Job saved and baseball stayed unranked until a restart.`

In `src/SportsRankingService/README.md`, after the paragraph on the Proxy fetcher add:

```markdown
After a run that stored at least one new release, the scraper POSTs once to the game's `/api/rankings/refresh`
(`Notify__Url`, with the shared token in `X-Refresh-Token` from `Notify__Token`), so the game re-reads the view
at once instead of at its next 12-hour refresh. Unset locally, nothing is sent; a failed notification is a warning
and never fails the run.
```

- [ ] **Step 4: Commit**

```powershell
git add infra/README.md CLAUDE.md src/SportsRankingService/CLAUDE.md src/SportsRankingService/README.md
git commit -m "Runbook and notes: the refresh endpoint and the scraper's notification"
```

---

### Task 6: Checkpoint: the two programs together on this machine

Needs the local database (`docker compose up -d --wait`) with both migration sets applied and rankings in it (it has them from earlier runs). Git Bash throughout; the token is any string here, both programs read it from the environment.

**Files:** none.

- [ ] **Step 1: Run the API with a token**

In one terminal:

```bash
Rankings__RefreshToken=local-test-token dotnet run --project src/WorldRankGuesser.Api
```

Expected: the API listens on http://localhost:5170 and logs "Rankings loaded: N rows, M drawable countries." once.

- [ ] **Step 2: Probe the endpoint**

```bash
curl -s -o /dev/null -w "no token: %{http_code}\n" -X POST http://localhost:5170/api/rankings/refresh
curl -s -w "\nright token: %{http_code}\n" -X POST -H "X-Refresh-Token: local-test-token" http://localhost:5170/api/rankings/refresh
```

Expected: `no token: 401`; then a JSON body with `rows`, `drawableCountries`, `loadedAt` and `right token: 200`, and a second "Rankings loaded" line in the API's terminal.

- [ ] **Step 3: Make one feed insert, then run the scraper with the notifier configured**

Delete the newest local release of one small feed so the next run stores it again (the local development database only):

```bash
docker exec worldrankguesser-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -d WorldRankGuesser -Q "DECLARE @id int = (SELECT MAX(Id) FROM dbo.RankingReleases WHERE Sport = 'Baseball' AND Gender = 'Women'); DELETE FROM dbo.RankingRows WHERE ReleaseId = @id; DELETE FROM dbo.RankingReleases WHERE Id = @id;"
```

(Git Bash rewrites `/opt/...`; if the exec fails with a `C:/Program Files/Git/opt` path, prefix the command with `MSYS_NO_PATHCONV=1`.) Then:

```bash
Notify__Url=http://localhost:5170/api/rankings/refresh Notify__Token=local-test-token dotnet run --project src/SportsRankingService -- --only "Baseball Women"
```

Expected: the scraper logs `Baseball Women: Inserted (28 rows, ...)`, `1 feeds: 1 new releases, 0 unchanged, 0 failed`, then `Game notified: N rows, M drawable countries`; the API's terminal shows a third "Rankings loaded" line; exit code 0.

- [ ] **Step 4: The same run with nothing new, and with no game configured**

```bash
Notify__Url=http://localhost:5170/api/rankings/refresh Notify__Token=local-test-token dotnet run --project src/SportsRankingService -- --only "Baseball Women"
dotnet run --project src/SportsRankingService -- --only "Baseball Women"
```

Expected: the first logs `Unchanged` and no "Game notified" line and no request reaches the API; the second logs `Notify:Url is not configured` once. Stop the API with Ctrl+C.

Nothing to commit.

---

### Task 7: Checkpoint: staging through the pipeline

Owner-run: the secret, the merge, the verification, and the one fact the spec leaves to verify.

**Files:** possibly `infra/staging/game.bicepparam` (only if the fallback is needed).

- [ ] **Step 1: The secret, before the merge**

```bash
openssl rand -hex 32 | gh secret set RANKINGS_REFRESH_TOKEN
gh secret list | grep RANKINGS_REFRESH_TOKEN
```

- [ ] **Step 2: Merge and watch**

Push the branch, open the PR, merge it. `gh run list --limit 6`: `CI`, `Deploy game` and `Deploy scraper` (staging) all end green. The scraper deploy's Job run stores nothing new (a run minutes after the last one), so its log shows no "Game notified"; that is expected.

- [ ] **Step 3: Force a notification in staging**

A run only notifies when it stored a new release, so make one feed store again: in the portal, open staging's SQL database, Query editor, sign in with Entra, and delete the newest release of one small feed (the local Task 6 did the same):

```sql
DECLARE @id int = (SELECT MAX(Id) FROM dbo.RankingReleases WHERE Sport = 'Baseball' AND Gender = 'Women');
DELETE FROM dbo.RankingRows WHERE ReleaseId = @id;
DELETE FROM dbo.RankingReleases WHERE Id = @id;
```

Then start the Job and wait for it:

```bash
bash .github/scripts/run-job.sh caj-wrg-staging-scraper rg-wrg-staging 3900
```

Then Log Analytics, workspace `log-wrg-staging`:

```
ContainerAppConsoleLogs_CL | where TimeGenerated > ago(1h) | where Log_s has "Game notified" or Log_s has "Rankings loaded" or Log_s has "refresh notification" or Log_s has "Application started" | project TimeGenerated, ContainerAppName_s, Log_s | order by TimeGenerated asc
```

Expected, on a run with a new release: the Job's "Game notified: N rows, M drawable countries" and, within seconds, the game's "Rankings loaded: N rows" with no "Application started" between them.

- [ ] **Step 4: If the Job's log says the notification failed with 403 or a timeout**

Staging's allow-list refused the environment's own traffic (spec section 7). Read the environment's outbound addresses and add them to staging's game parameters, then dispatch the game deploy:

```bash
az containerapp env show --name cae-wrg-staging --resource-group rg-wrg-staging --query "properties.staticIp" -o tsv
```

In `infra/staging/game.bicepparam`, `allowedIps` becomes the union of the secret's list and that address: `param allowedIps = union(json(readEnvironmentVariable('ALLOWED_IPS', '[]')), ['<staticIp>/32'])`, committed with a comment saying why; production has no allow-list and needs nothing. Re-run Step 3.

- [ ] **Step 5: Promotion**

When production exists (phase 2c Part C), the promotion carries both images; production's first Job run notifies the production game over its default hostname.

Nothing to commit except Step 4's parameter change, if it was needed. Update the spec's status line to implemented when Step 3 has passed.
