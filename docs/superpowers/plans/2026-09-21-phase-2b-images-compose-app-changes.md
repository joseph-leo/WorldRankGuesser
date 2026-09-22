# Phase 2b: Images, Compose and App Changes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the two container images (the game with its front end, the scraper), a Compose stack that runs them the way production will, and the four app changes that hosting behind a proxy with a pausing database needs, so that from a clean clone `docker compose --profile stack up --build` serves a playable practice game on `localhost:8080`.

**Architecture:** The game image is three stages (Node build of the SvelteKit app → `dotnet publish` of the API with the web output in `wwwroot` → chiseled ASP.NET runtime); the scraper image is SDK publish → plain runtime plus `curl`. `docker-compose.yml` keeps the development SQL Server as it is and adds a `stack` profile: a one-shot `migrate` service (a fourth stage of the game's Dockerfile that applies the scraper's `dbo` migrations, then the game's), the `game`, and the `scraper` on demand. In the API, forwarded headers are honoured only behind the setting `Hosting:TrustForwardedHeaders`, SQL connections retry transient failures, and the rankings load in the background on a backoff instead of blocking host startup; the front end polls `/readyz` before offering a game. The Playwright config takes `E2E_BASE_URL` so the same tests run against the stack (here) and staging (plan 2c).

**Tech Stack:** Docker Desktop 29 / Compose v2.40, `mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1`, `mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1-resolute-chiseled-extra`, `mcr.microsoft.com/dotnet/runtime:11.0.0-rc.1-resolute`, `node:24`, .NET 11 RC1, EF Core 11 RC1, `Microsoft.Extensions.TimeProvider.Testing` 10.10.0, SvelteKit 2 / Svelte 5, Vitest 5, Playwright 1.63, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md`, sections 6 (images and compose; the benchmark paragraph was done in plan 2a) and 9 (app changes and testing, except the promotion-guard composite action, which is workflow code and belongs to plan 2c). Section 3 lists the three defects this plan fixes. Plan 2c covers going public, Azure and the pipelines.

## Global Constraints

- The coupling rule (spec section 2): `WorldRankGuesser.Api` never references `SportsRankingService`. The `dbo.CurrentCountryRankings` view is the only runtime link. The `migrate` stage builds both projects side by side; that is a deployment tool, not a reference.
- Base images, verified on MCR on 2026-09-21: SDK `mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1`; game runtime `mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1-resolute-chiseled-extra` ("extra" because `Microsoft.Data.SqlClient` refuses globalization-invariant mode and only "extra" ships ICU); scraper runtime `mcr.microsoft.com/dotnet/runtime:11.0.0-rc.1-resolute` (not chiseled: `CurlFetcher` starts `curl` as a process, which needs apt). Node `node:24`. **The .NET tags follow `global.json`: when it moves to RC2 or GA, both Dockerfiles move with it.**
- Both images build with the repo root as context. One `.dockerignore` (Task 6). The game listens on 8080, runs non-root, has no `HEALTHCHECK` (Container Apps probes the app). `ASPNETCORE_ENVIRONMENT=Production` in the stack, so the player cookie is `Secure`; browsers exempt `http://localhost`, which is why the local stack works without TLS.
- Forwarded headers: `X-Forwarded-For` and `X-Forwarded-Proto` only, forward limit 1, known proxies and networks cleared, all of it only when `Hosting:TrustForwardedHeaders` is `true`; the default is `false`.
- SQL retries: `EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: 30 s)`. Concurrency conflicts are not transient, so the `409` path never retries.
- Rankings refresh: `StartAsync` no longer loads; `ExecuteAsync` attempts at once, retries on a backoff of 5, 10, 20, 40, then 60 seconds (the last repeats) while no snapshot exists, then waits `Rankings:RefreshMinutes`. A load that yields fewer drawable countries than there are categories counts as failed (a board cannot be drawn from it). `Rankings:RefreshMinutes` stays 60 in `appsettings.json`; 720 is Azure's parameter and belongs to plan 2c.
- Front end: the start screen polls `/readyz` one call at a time, 2 seconds after each answer, for up to 90 seconds, then shows a retry message. `GameStore` gains no rule.
- The API contract does not change in this plan. `src/WorldRankGuesser.Web/openapi/WorldRankGuesser.Api.json` and `src/lib/api/schema.d.ts` must be unchanged after every build; a diff there is a defect.
- Compose: project name `worldrankguesser`, volume `worldrankguesser_sqldata`, container `worldrankguesser-sql` (unchanged), SA password `Rankings_Dev1!` (dev-only, bound to `127.0.0.1`). Development's `docker compose up -d --wait` starts only the SQL Server, as today.
- Every commit passes `dotnet build WorldRankGuesser.slnx`, `dotnet test WorldRankGuesser.slnx` (Docker Desktop running: Testcontainers), and, when the front end changed, `npm run check` and `npm test` in `src/WorldRankGuesser.Web`.
- Windows: run commands in PowerShell from the repo root unless a step says otherwise (`npm` commands run from `src/WorldRankGuesser.Web`). A PowerShell tool call keeps no state between calls, so each step's commands are self-contained.
- Commit messages: imperative subject, a wrapped body that says why, and the session's attribution trailers:

  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
  ```

**Precondition:** `git status --short` prints nothing on `main` at or after `5b50e2f`; Docker Desktop is running; the development database is up under the old Compose project name with rankings from plan 2a's live run (`docker compose ps` shows `worldrankguesser-sql` healthy; Task 8 copies its volume). Task 1 creates branch `phase-2b-images-compose-app` off `main`.

---

## File structure

**Created:**

| Path | Responsibility |
|---|---|
| `.dockerignore` | What neither image's build context carries (outputs, dependencies, tooling, tests, docs). |
| `Dockerfile` | The game image: stages `web`, `api`, `migrate`, `runtime`. The default target is `runtime`; `migrate` is built only by name, for compose. |
| `src/SportsRankingService/Dockerfile` | The scraper image: `build`, then a runtime with `curl` and `ca-certificates`, non-root. |
| `src/WorldRankGuesser.Api/Configuration/HostingOptions.cs` | The `Hosting` section: `TrustForwardedHeaders`. |
| `tests/WorldRankGuesser.Api.Tests/Integration/ForwardedHeadersTests.cs` | The per-IP limit keys on `X-Forwarded-For` only with the setting on. |
| `tests/WorldRankGuesser.Api.Tests/Rankings/RankingsRefreshServiceTests.cs` | The backoff, the periodic refresh, and the too-few-countries guard, on a fake clock. |
| `src/WorldRankGuesser.Web/src/lib/api/readiness.svelte.ts` | `ServerReadiness`: the waking-up state machine over `/readyz`. |
| `src/WorldRankGuesser.Web/src/lib/api/readiness.test.ts` | Vitest with fake timers for `ServerReadiness`. |

**Modified:**

| Path | Change |
|---|---|
| `src/WorldRankGuesser.Api/Persistence/GameDbContext.cs:31-32` | `EnableRetryOnFailure` in `Configure` (Task 1). |
| `tests/WorldRankGuesser.Api.Tests/Integration/PersistenceTests.cs` | Pins the retrying execution strategy (Task 1). |
| `src/WorldRankGuesser.Api/Rankings/RankingsRefreshService.cs` | Background load with backoff; no load in `StartAsync` (Task 2). |
| `tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj` | Adds `Microsoft.Extensions.TimeProvider.Testing` (Task 2). |
| `tests/WorldRankGuesser.Api.Tests/Integration/ApiFactory.cs` | `WaitUntilReadyAsync()` (Task 2). |
| `tests/WorldRankGuesser.Api.Tests/Integration/{AntiCheatTests,GameApiTests,GameServiceTests,ReadinessTests}.cs` | Wait for the rankings before starting games (Task 2). |
| `src/WorldRankGuesser.Api/Program.cs` | Forwarded headers behind the setting, first in the pipeline (Task 3). |
| `src/WorldRankGuesser.Web/src/lib/api/client.ts` | `isReady()` (Task 4). |
| `src/WorldRankGuesser.Web/src/routes/+page.svelte` | The waking-up state before the Practice button (Task 4). |
| `src/WorldRankGuesser.Web/playwright.config.ts` | `E2E_BASE_URL` (Task 5). |
| `docker-compose.yml` | Project name `worldrankguesser`; the `stack` profile (Task 8). |
| `.github/workflows/ci.yml` | Action majors on Node 24; the `images` job (Task 9). |
| `tests/SportsRankingService.Tests/Parsers/FigParserTests.cs:52` | xUnit2029 (Task 9). |
| `CLAUDE.md`, `README.md`, `src/SportsRankingService/README.md`, `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md` | Commands, hosting paragraph, stack, the spec's status line (Task 10). |

---

### Task 1: SQL connection retries

A paused Azure SQL database rejects the first connection while it resumes (spec section 3, defect 2). The code opens no explicit transactions, so a retrying execution strategy is safe. `SqlServerRetryingExecutionStrategy` retries only the provider's transient error numbers (40613 "database unavailable", 40197, 40501, 10928/10929, 1205 deadlock, ...); a unique-index violation or a row-version mismatch is not among them, so `GameService.PickAsync`'s `DbUpdateException` catch, and the `409` it produces, are unchanged.

**Files:**
- Modify: `src/WorldRankGuesser.Api/Persistence/GameDbContext.cs:31-32`
- Test: `tests/WorldRankGuesser.Api.Tests/Integration/PersistenceTests.cs`

**Interfaces:**
- Produces: `GameDbContext.Configure(DbContextOptionsBuilder, string)` keeps its signature; every context built through it (Program, the test fixture, design time, the `migrate` stage) retries.

- [ ] **Step 1: Branch**

```powershell
git status --short
git switch -c phase-2b-images-compose-app main
```

Expected: `git status --short` prints nothing first; if it prints anything, stop: the owner must commit or stash.

- [ ] **Step 2: Write the failing test**

Add to `tests/WorldRankGuesser.Api.Tests/Integration/PersistenceTests.cs`, as the first `[Fact]` of the class (before `Board_content_round_trips_as_json`):

```csharp
    [Fact]
    public void The_context_retries_transient_failures()
    {
        // A paused Azure SQL database refuses connections while it resumes; the first visitor must not get a 500.
        using var db = sql.CreateContext();

        Assert.IsType<SqlServerRetryingExecutionStrategy>(db.Database.CreateExecutionStrategy());
    }
```

`SqlServerRetryingExecutionStrategy` is in the `Microsoft.EntityFrameworkCore` namespace, which the file already imports.

- [ ] **Step 3: Run it and see it fail**

```powershell
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~PersistenceTests.The_context_retries_transient_failures"
```

Expected: FAIL: the strategy is `SqlServerExecutionStrategy` (the non-retrying default).

- [ ] **Step 4: Enable retries**

In `src/WorldRankGuesser.Api/Persistence/GameDbContext.cs`, replace

```csharp
    /// <summary>The one place the provider and the migrations history table are configured (Program, tests, design time).</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Schema));
```

with

```csharp
    /// <summary>The one place the provider and the migrations history table are configured (Program, tests, design time).</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql
            .MigrationsHistoryTable("__EFMigrationsHistory", Schema)
            // A paused Azure SQL database refuses connections while it resumes; retry instead of failing the first
            // visitor. Only the provider's transient errors retry: a unique index or a row version firing is not one,
            // so a losing simultaneous pick still becomes the 409 in GameService.
            .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null));
```

- [ ] **Step 5: Run the SQL collection**

```powershell
dotnet build WorldRankGuesser.slnx
dotnet test WorldRankGuesser.slnx --no-build --filter "FullyQualifiedName~Integration"
git status --short
```

Expected: every integration test passes, including `GameApiTests.A_used_category_returns_409_with_the_current_state` and `AntiCheatTests` (the conflict path is intact); `git status` shows only the two files of this task (the OpenAPI document is unchanged).

- [ ] **Step 6: Commit**

```powershell
git add src/WorldRankGuesser.Api/Persistence/GameDbContext.cs tests/WorldRankGuesser.Api.Tests/Integration/PersistenceTests.cs
git commit -m @'
Retry transient SQL failures

A paused Azure SQL database rejects the first connection while it
resumes, so the first visitor after idle got a 500. The context now
uses the retrying execution strategy (six attempts, 30 seconds maximum
delay). Concurrency conflicts are not transient and never retry, so
the 409 path is unchanged.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 2: Load the rankings in the background on a backoff

Today `RankingsRefreshService.StartAsync` loads once and blocks host startup on it; the next attempt is `RefreshMinutes` later (spec section 3, defect 3). A cold start usually meets a paused database, so the load fails and `/readyz` reports not-ready for an hour. The service already takes `TimeProvider`, so the new behaviour is unit-tested on a fake clock. Because a started host no longer has rankings at once, `ApiFactory` gains `WaitUntilReadyAsync()` and the integration tests that start games call it.

**Files:**
- Modify: `tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj`
- Create: `tests/WorldRankGuesser.Api.Tests/Rankings/RankingsRefreshServiceTests.cs`
- Modify: `src/WorldRankGuesser.Api/Rankings/RankingsRefreshService.cs`
- Modify: `tests/WorldRankGuesser.Api.Tests/Integration/ApiFactory.cs`
- Modify: `tests/WorldRankGuesser.Api.Tests/Integration/AntiCheatTests.cs:13-17`
- Modify: `tests/WorldRankGuesser.Api.Tests/Integration/GameApiTests.cs:12-16,145-177`
- Modify: `tests/WorldRankGuesser.Api.Tests/Integration/GameServiceTests.cs:14-19`
- Modify: `tests/WorldRankGuesser.Api.Tests/Integration/ReadinessTests.cs:10-23`

**Interfaces:**
- Consumes: `IRankingsReader.ReadAsync(CancellationToken)`, `RankingsSnapshotBuilder.Build(rows, GameOptions, int cap, CountryCatalog, DateTimeOffset)`, `IRankingsStore.Set(RankingsSnapshot)`, `RankingsSnapshot.DrawableCountries`, `RankingsSnapshot.Categories`, the test helpers `TestData.Row(...)`, `TestData.Options()`, `TestData.Catalog`, `TestData.LoadedAt`.
- Produces: `RankingsRefreshService.StartupBackoff` (`internal static readonly TimeSpan[]`); `ApiFactory.WaitUntilReadyAsync()` (`Task`, throws `TimeoutException` after 30 s), which Task 3's test and every later test that starts a game use.

- [ ] **Step 1: Add the fake clock package**

In `tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj`, add to the package `<ItemGroup>`, after `Microsoft.NET.Test.Sdk`:

```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
```

(10.10.0 is the version the scraper's tests use; no 11.0 build existed on 2026-09-21.)

- [ ] **Step 2: Write the failing tests**

Create `tests/WorldRankGuesser.Api.Tests/Rankings/RankingsRefreshServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Rankings;

/// <summary>
/// The refresh service on a fake clock: nothing here waits in real time except <see cref="Eventually"/>, which only
/// lets the service's thread-pool continuations run.
/// </summary>
public class RankingsRefreshServiceTests
{
    /// <summary>Answers each read from a queue: an <see cref="Exception"/> is thrown, rows are returned; an empty queue returns <see cref="Rows"/>.</summary>
    private sealed class ScriptedReader(params object[] answers) : IRankingsReader
    {
        private readonly Queue<object> _answers = new(answers);

        public int Attempts { get; private set; }

        public Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct)
        {
            Attempts++;
            var answer = _answers.Count > 0 ? _answers.Dequeue() : Rows;

            return answer is Exception error
                ? Task.FromException<IReadOnlyList<CountryRankingRow>>(error)
                : Task.FromResult((IReadOnlyList<CountryRankingRow>)answer);
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

    private static (RankingsRefreshService Service, RankingsStore Store) Create(ScriptedReader reader, FakeTimeProvider time, int refreshMinutes = 60)
    {
        var store = new RankingsStore();
        var scopes = new ServiceCollection()
            .AddScoped<IRankingsReader>(_ => reader)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var service = new RankingsRefreshService(
            scopes,
            store,
            Options.Create(TestData.Options()),
            Options.Create(new ScoringOptions()),
            Options.Create(new RankingsOptions { RefreshMinutes = refreshMinutes }),
            TestData.Catalog,
            time,
            NullLogger<RankingsRefreshService>.Instance);

        return (service, store);
    }

    /// <summary>Waits, briefly and in real time, for the service's continuations to reach a state.</summary>
    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The service did not reach the expected state.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Startup_does_not_wait_for_the_load_and_retries_on_a_backoff_until_it_succeeds()
    {
        var time = new FakeTimeProvider(TestData.LoadedAt);
        var reader = new ScriptedReader(Down, Down, Rows);
        var (service, store) = Create(reader, time);

        await service.StartAsync(CancellationToken.None);       // returns at once; the first attempt runs in the background
        await Eventually(() => reader.Attempts == 1);
        Assert.Null(store.Current);

        time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        Assert.Equal(1, reader.Attempts);                        // the second attempt waits the full 5 seconds

        time.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => reader.Attempts == 2);
        Assert.Null(store.Current);

        time.Advance(TimeSpan.FromSeconds(10));                  // 5, then 10: the third attempt lands 15 seconds in
        await Eventually(() => store.Current is not null);
        Assert.Equal(3, reader.Attempts);
        Assert.Equal(4, store.Current!.DrawableCountries.Count);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task After_the_first_load_it_refreshes_every_RefreshMinutes_and_a_failed_refresh_keeps_the_snapshot()
    {
        var time = new FakeTimeProvider(TestData.LoadedAt);
        var reader = new ScriptedReader(Rows, Down, Rows);
        var (service, store) = Create(reader, time, refreshMinutes: 60);

        await service.StartAsync(CancellationToken.None);
        await Eventually(() => store.Current is not null);
        var first = store.Current;

        time.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(50);
        Assert.Equal(1, reader.Attempts);                        // no backoff once a snapshot exists: the next try is on the hour

        time.Advance(TimeSpan.FromMinutes(1));
        await Eventually(() => reader.Attempts == 2);
        await Task.Delay(50);
        Assert.Same(first, store.Current);                       // the failed refresh kept the previous snapshot

        time.Advance(TimeSpan.FromMinutes(60));
        await Eventually(() => reader.Attempts == 3);
        await Eventually(() => !ReferenceEquals(first, store.Current));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_view_with_too_few_countries_to_draw_a_board_does_not_count_as_loaded()
    {
        var time = new FakeTimeProvider(TestData.LoadedAt);
        var reader = new ScriptedReader(Array.Empty<CountryRankingRow>(), Rows);   // the scraper has not run yet, then it has
        var (service, store) = Create(reader, time);

        await service.StartAsync(CancellationToken.None);
        await Eventually(() => reader.Attempts == 1);
        await Task.Delay(50);
        Assert.Null(store.Current);                              // /readyz stays 503 rather than offering an undrawable board

        time.Advance(TimeSpan.FromSeconds(5));
        await Eventually(() => store.Current is not null);
        Assert.Equal(2, reader.Attempts);

        await service.StopAsync(CancellationToken.None);
    }
}
```

- [ ] **Step 3: Run them and see them fail**

```powershell
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~RankingsRefreshServiceTests"
```

Expected: the first FAILS, timing out in `Eventually` at `reader.Attempts == 2` (today the next attempt after a failed startup load is an hour away); the third FAILS at the first `Assert.Null(store.Current)` (today an empty view counts as loaded). The second PASSES already: it pins the periodic behaviour the rewrite must keep.

- [ ] **Step 4: Rewrite the service**

Replace the whole of `src/WorldRankGuesser.Api/Rankings/RankingsRefreshService.cs` with

```csharp
using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>
/// Loads the rankings snapshot in the background: at once, then on a short backoff until the first load succeeds
/// (a cold start usually meets a database that is still resuming), then every Rankings:RefreshMinutes. A failed
/// refresh keeps the previous snapshot. Host startup never waits for a load, so a startup probe cannot trip on it;
/// /readyz reports not-ready until the first snapshot exists.
/// </summary>
public sealed class RankingsRefreshService(
    IServiceScopeFactory scopes,
    IRankingsStore store,
    IOptions<GameOptions> gameOptions,
    IOptions<ScoringOptions> scoringOptions,
    IOptions<RankingsOptions> rankingsOptions,
    CountryCatalog catalog,
    TimeProvider time,
    ILogger<RankingsRefreshService> logger) : BackgroundService
{
    /// <summary>Waits between attempts while there is no snapshot yet; the last one repeats.</summary>
    internal static readonly TimeSpan[] StartupBackoff =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 0; !await RefreshAsync(stoppingToken); attempt++)
        {
            await Task.Delay(StartupBackoff[Math.Min(attempt, StartupBackoff.Length - 1)], time, stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(rankingsOptions.Value.RefreshMinutes), time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    /// <summary>True when a snapshot was stored.</summary>
    private async Task<bool> RefreshAsync(CancellationToken ct)
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
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Keep serving the previous snapshot; /readyz reports not-ready only if there has never been one.
            logger.LogError(error, "Loading the rankings failed; keeping the previous snapshot.");
            return false;
        }
    }
}
```

`Task.Delay(TimeSpan, TimeProvider, CancellationToken)` and `PeriodicTimer(TimeSpan, TimeProvider)` are the .NET 8+ overloads the fake clock drives. On shutdown the stopping token cancels the delay or the tick; `BackgroundService` treats that cancellation as a normal stop, as it did with the timer alone.

- [ ] **Step 5: Run the unit tests (green)**

```powershell
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~RankingsRefreshServiceTests"
```

Expected: 3 passed.

- [ ] **Step 6: Give the integration tests a way to wait**

Replace the whole of `tests/WorldRankGuesser.Api.Tests/Integration/ApiFactory.cs` with

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Integration;

public sealed class ApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    /// <summary>
    /// The rankings load in the background after the host starts (RankingsRefreshService), so a test that needs
    /// them waits here first. A host whose database has no rankings never becomes ready; those tests do not wait.
    /// </summary>
    public async Task WaitUntilReadyAsync()
    {
        var store = Services.GetRequiredService<IRankingsStore>();     // resolving Services starts the host
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (store.Current is null)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The rankings did not load within 30 seconds; see the host's log.");
            await Task.Delay(25);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, so the player cookie is not Secure-only and works over the test server's http.
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{GameDbContext.ConnectionStringName}", connectionString);
        builder.UseSetting("RateLimits:GameStartsPerPlayerPerHour", "100000");
        builder.UseSetting("RateLimits:GameStartsPerIpPerHour", "100000");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }
}
```

- [ ] **Step 7: Make the tests that start games wait**

1. `tests/WorldRankGuesser.Api.Tests/Integration/AntiCheatTests.cs`: replace

   ```csharp
       public Task InitializeAsync()
       {
           _factory = new ApiFactory(sql.ConnectionString);
           return Task.CompletedTask;
       }
   ```

   with

   ```csharp
       public async Task InitializeAsync()
       {
           _factory = new ApiFactory(sql.ConnectionString);
           await _factory.WaitUntilReadyAsync();
       }
   ```

2. `tests/WorldRankGuesser.Api.Tests/Integration/GameApiTests.cs`: the same replacement of `InitializeAsync`; and in both `Game_starts_are_rate_limited_per_player` and `A_trailing_slash_does_not_bypass_the_game_start_rate_limit`, insert `await limited.WaitUntilReadyAsync();` as the line after the `await using var limited = new ApiFactory(...)` statement (after its closing `});`). `Starting_a_game_before_the_rankings_load_returns_503_and_creates_no_player` does not wait: its database has no view, so it never becomes ready.

3. `tests/WorldRankGuesser.Api.Tests/Integration/GameServiceTests.cs`: replace

   ```csharp
       public Task InitializeAsync()
       {
           _factory = new ApiFactory(sql.ConnectionString);
           _ = _factory.Services;      // starts the host, which loads the rankings
           return Task.CompletedTask;
       }
   ```

   with

   ```csharp
       public async Task InitializeAsync()
       {
           _factory = new ApiFactory(sql.ConnectionString);
           await _factory.WaitUntilReadyAsync();
       }
   ```

4. `tests/WorldRankGuesser.Api.Tests/Integration/ReadinessTests.cs`: replace `Ready_once_the_rankings_are_loaded` with

   ```csharp
       [Fact]
       public async Task Alive_at_once_and_ready_once_the_rankings_are_loaded()
       {
           await using var factory = new ApiFactory(sql.ConnectionString);
           var client = factory.CreateClient();

           Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);   // the host started without waiting for a load

           await factory.WaitUntilReadyAsync();
           Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/readyz")).StatusCode);

           var snapshot = factory.Services.GetRequiredService<IRankingsStore>().Current;
           Assert.NotNull(snapshot);
           Assert.Equal(RankingsSeed.Countries, snapshot.DrawableCountries.Select(c => c.Iso3));
           Assert.Equal(RankingsSeed.PositionOf(0, 0), snapshot.Find("soccer", "AUS")!.BestByEntry.EntryRank);
       }
   ```

   `Alive_but_not_ready_when_the_rankings_view_is_missing` stays as it is.

- [ ] **Step 8: Run the whole solution**

```powershell
dotnet build WorldRankGuesser.slnx
dotnet test WorldRankGuesser.slnx --no-build
git status --short
```

Expected: both test assemblies pass; `git status` shows only the eight files of this task.

- [ ] **Step 9: Commit**

```powershell
git add src/WorldRankGuesser.Api/Rankings/RankingsRefreshService.cs tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj tests/WorldRankGuesser.Api.Tests/Rankings/RankingsRefreshServiceTests.cs tests/WorldRankGuesser.Api.Tests/Integration/ApiFactory.cs tests/WorldRankGuesser.Api.Tests/Integration/AntiCheatTests.cs tests/WorldRankGuesser.Api.Tests/Integration/GameApiTests.cs tests/WorldRankGuesser.Api.Tests/Integration/GameServiceTests.cs tests/WorldRankGuesser.Api.Tests/Integration/ReadinessTests.cs
git commit -m @'
Load the rankings in the background on a backoff

StartAsync loaded once, blocking host startup, and the next attempt
was RefreshMinutes later; a cold start meeting a paused database left
/readyz not-ready for an hour. ExecuteAsync now attempts at once and
retries after 5, 10, 20, 40, then 60 seconds until the first snapshot,
then every RefreshMinutes. A view with fewer drawable countries than
categories does not count as loaded, because no board can be drawn
from it. Integration tests wait for readiness through ApiFactory.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 3: Forwarded headers behind a setting

Behind the Container Apps ingress every player arrives from the proxy's address, so the per-IP rate limit would be one limit shared by the whole world (spec section 3, defect 1). With `Hosting:TrustForwardedHeaders` on, the forwarded-headers middleware runs first in the pipeline for `X-Forwarded-For` and `X-Forwarded-Proto` with forward limit 1 (the ingress appends the caller last) and no known-proxy list (the container is reachable only through the ingress). Off, the default, a directly exposed container ignores the headers, so nobody can spoof an address.

**Files:**
- Create: `src/WorldRankGuesser.Api/Configuration/HostingOptions.cs`
- Modify: `src/WorldRankGuesser.Api/Program.cs`
- Test: `tests/WorldRankGuesser.Api.Tests/Integration/ForwardedHeadersTests.cs`

**Interfaces:**
- Consumes: `ApiFactory(string, IReadOnlyDictionary<string, string?>)`, `ApiFactory.WaitUntilReadyAsync()` (Task 2), `StartGameRequest(string Mode)` from `WorldRankGuesser.Api.Games`.
- Produces: configuration key `Hosting:TrustForwardedHeaders` (bool, default false), which plan 2c's Bicep sets to `true` and Task 8's Caddy comment names.

- [ ] **Step 1: Write the failing tests**

Create `tests/WorldRankGuesser.Api.Tests/Integration/ForwardedHeadersTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using WorldRankGuesser.Api.Games;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// Behind the ingress every player arrives from the proxy's address, so the per-IP limit must key on X-Forwarded-For,
/// but only when the app is told the header can be trusted; a directly exposed container must ignore it.
/// </summary>
[Collection("sql")]
public class ForwardedHeadersTests(SqlServerFixture sql)
{
    private static Task<HttpResponseMessage> StartFrom(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/games")
        {
            Content = JsonContent.Create(new StartGameRequest("practice")),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        return client.SendAsync(request);
    }

    private static Dictionary<string, string?> TwoStartsPerIp(bool trustForwardedHeaders) => new()
    {
        ["RateLimits:GameStartsPerIpPerHour"] = "2",
        ["Hosting:TrustForwardedHeaders"] = trustForwardedHeaders ? "true" : "false",
    };

    [Fact]
    public async Task With_the_setting_on_each_forwarded_address_has_its_own_limit()
    {
        await using var factory = new ApiFactory(sql.ConnectionString, TwoStartsPerIp(trustForwardedHeaders: true));
        await factory.WaitUntilReadyAsync();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await StartFrom(client, "203.0.113.1")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.2")).StatusCode);
    }

    [Fact]
    public async Task With_the_setting_off_the_header_is_ignored()
    {
        await using var factory = new ApiFactory(sql.ConnectionString, TwoStartsPerIp(trustForwardedHeaders: false));
        await factory.WaitUntilReadyAsync();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.1")).StatusCode);

        // The same connection as far as the app can see, so another header value is over the limit too.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await StartFrom(client, "203.0.113.2")).StatusCode);
    }
}
```

The per-player limit stays at `ApiFactory`'s 100000, so only the per-IP partition decides. The test server reports no remote address, which the middleware allows for the first forwarded entry.

- [ ] **Step 2: Run them and see the first fail**

```powershell
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~ForwardedHeadersTests"
```

Expected: `With_the_setting_on_each_forwarded_address_has_its_own_limit` FAILS at the last assertion (429 instead of 200: the header is ignored today); `With_the_setting_off_the_header_is_ignored` passes already (it describes today's behaviour, and must keep passing).

- [ ] **Step 3: The options class**

Create `src/WorldRankGuesser.Api/Configuration/HostingOptions.cs`:

```csharp
namespace WorldRankGuesser.Api.Configuration;

public sealed class HostingOptions
{
    public const string Section = "Hosting";

    /// <summary>
    /// On only behind a proxy that rewrites X-Forwarded-For and X-Forwarded-Proto for every request (the Container Apps
    /// ingress): the per-IP rate limit then keys on the caller's address instead of the proxy's. Off, the default, a
    /// directly exposed container ignores the headers, so no one can spoof an address.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }
}
```

- [ ] **Step 4: Wire the middleware**

In `src/WorldRankGuesser.Api/Program.cs`:

1. Add `using Microsoft.AspNetCore.HttpOverrides;` after `using Microsoft.AspNetCore.DataProtection;`.

2. After the `RateLimitOptions` registration (the block ending `.ValidateOnStart();` for `RateLimits`), add:

   ```csharp
   // ---- Hosting: forwarded headers only where a proxy is known to set them ------------------------------------------
   // Read once at startup: whether the middleware exists at all depends on it.
   var hosting = builder.Configuration.GetSection(HostingOptions.Section).Get<HostingOptions>() ?? new HostingOptions();
   if (hosting.TrustForwardedHeaders)
   {
       builder.Services.Configure<ForwardedHeadersOptions>(options =>
       {
           options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
           // The ingress appends the caller last, so only that one hop is believed.
           options.ForwardLimit = 1;
           // The container is reachable only through the environment's ingress, so every peer is that proxy.
           options.KnownIPNetworks.Clear();
           options.KnownProxies.Clear();
       });
   }
   ```

3. Replace

   ```csharp
   var app = builder.Build();

   app.UseExceptionHandler();
   ```

   with

   ```csharp
   var app = builder.Build();

   if (hosting.TrustForwardedHeaders)
   {
       app.UseForwardedHeaders();     // first, so everything after it sees the caller's address and scheme
   }

   app.UseExceptionHandler();
   ```

`KnownIPNetworks` (a `List<System.Net.IPNetwork>`) is the .NET 11 name; the obsolete `KnownNetworks` is a view over it (checked on 2026-09-21 with the RC1 runtime: both default to one loopback entry, and clearing `KnownIPNetworks` leaves both at zero), so nothing else needs clearing. With the lists empty the middleware trusts every source, which is the intent.

- [ ] **Step 5: Run the tests (green) and the whole solution**

```powershell
dotnet build WorldRankGuesser.slnx
dotnet test WorldRankGuesser.slnx --no-build
git status --short
```

Expected: both `ForwardedHeadersTests` pass; everything else still passes; `git status` shows only the three files of this task (the OpenAPI document is unchanged: no endpoint changed).

- [ ] **Step 6: Commit**

```powershell
git add src/WorldRankGuesser.Api/Configuration/HostingOptions.cs src/WorldRankGuesser.Api/Program.cs tests/WorldRankGuesser.Api.Tests/Integration/ForwardedHeadersTests.cs
git commit -m @'
Honour X-Forwarded-For behind Hosting:TrustForwardedHeaders

Behind the ingress every player has the proxy's address, so the
per-IP game-start limit would be one limit for the whole world. With
the setting on, the forwarded-headers middleware runs first for
X-Forwarded-For and X-Forwarded-Proto with forward limit 1 and no
known-proxy list; off, the default, the headers are ignored so a
directly exposed container cannot be spoofed.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 4: The front end waits for the server

Before offering a game, the start screen asks `/readyz`. A scaled-to-zero app serves the page itself, so what is usually asleep is the database; the first `/readyz` wakes it and, with Task 1's retries, may itself take up to a minute to answer. So the polling is sequential: the next call goes 2 seconds after the previous answer, for up to 90 seconds, after which a plain message with a retry button covers both a slow wake and a spent free allowance. This is server state, not a game rule, so it lives beside the API client and `GameStore` is untouched.

**Files:**
- Modify: `src/WorldRankGuesser.Web/src/lib/api/client.ts`
- Create: `src/WorldRankGuesser.Web/src/lib/api/readiness.svelte.ts`
- Create: `src/WorldRankGuesser.Web/src/lib/api/readiness.test.ts`
- Modify: `src/WorldRankGuesser.Web/src/routes/+page.svelte`

**Interfaces:**
- Consumes: `GET /readyz` (200 when ready, 503 otherwise; excluded from the OpenAPI document, so `schema.d.ts` does not change).
- Produces: `isReady(): Promise<boolean>` in `client.ts`; `class ServerReadiness { phase: 'checking' | 'waking' | 'ready' | 'unavailable'; wait(): Promise<void> }` with constructor `(check = isReady, { intervalMs = 2000, timeoutMs = 90_000 } = {})`.

All commands in this task run from `src/WorldRankGuesser.Web`.

- [ ] **Step 1: The readiness call**

Append to `src/WorldRankGuesser.Web/src/lib/api/client.ts`:

```ts

/** Whether the API has rankings and can reach its database (`/readyz`). False on any failure, a network error included. */
export async function isReady(): Promise<boolean> {
	try {
		return (await fetch('/readyz', { credentials: 'same-origin' })).ok;
	} catch {
		return false;
	}
}
```

- [ ] **Step 2: Write the failing tests**

Create `src/WorldRankGuesser.Web/src/lib/api/readiness.test.ts`:

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ServerReadiness } from './readiness.svelte';

describe('ServerReadiness', () => {
	beforeEach(() => vi.useFakeTimers());
	afterEach(() => vi.useRealTimers());

	it('is ready after one answer when the server is up', async () => {
		const check = vi.fn(async () => true);
		const server = new ServerReadiness(check);

		await server.wait();

		expect(server.phase).toBe('ready');
		expect(check).toHaveBeenCalledTimes(1);
	});

	it('says it is waking the server and asks again two seconds after each answer', async () => {
		const answers = [false, false, true];
		const check = vi.fn(async () => answers.shift() ?? true);
		const server = new ServerReadiness(check);

		const waiting = server.wait();
		await vi.advanceTimersByTimeAsync(0);
		expect(server.phase).toBe('waking');
		expect(check).toHaveBeenCalledTimes(1);

		await vi.advanceTimersByTimeAsync(2000);
		expect(check).toHaveBeenCalledTimes(2);
		expect(server.phase).toBe('waking');

		await vi.advanceTimersByTimeAsync(2000);
		await waiting;
		expect(server.phase).toBe('ready');
		expect(check).toHaveBeenCalledTimes(3);
	});

	it('gives up after 90 seconds and can be asked again', async () => {
		const check = vi.fn(async () => false);
		const server = new ServerReadiness(check);

		const waiting = server.wait();
		await vi.advanceTimersByTimeAsync(90_000);
		await waiting;

		expect(server.phase).toBe('unavailable');
		expect(check.mock.calls.length).toBeGreaterThanOrEqual(45);

		check.mockResolvedValue(true);
		await server.wait();
		expect(server.phase).toBe('ready');
	});

	it('waits for each answer before asking again, so a slow /readyz is never stacked', async () => {
		let answer!: (ready: boolean) => void;
		const check = vi.fn(() => new Promise<boolean>((resolve) => (answer = resolve)));
		const server = new ServerReadiness(check);

		const waiting = server.wait();
		await vi.advanceTimersByTimeAsync(10_000);
		expect(check).toHaveBeenCalledTimes(1); // still waiting on the first answer

		answer(true);
		await waiting;
		expect(server.phase).toBe('ready');
	});
});
```

- [ ] **Step 3: Run them and see them fail**

```powershell
npm test -- src/lib/api/readiness.test.ts
```

Expected: FAIL: `Failed to resolve import "./readiness.svelte"`.

- [ ] **Step 4: The state machine**

Create `src/WorldRankGuesser.Web/src/lib/api/readiness.svelte.ts`:

```ts
import * as api from './client';

export type ReadinessPhase = 'checking' | 'waking' | 'ready' | 'unavailable';

/**
 * Whether a game can be started: the start screen asks /readyz before offering one. A scaled-to-zero app and a
 * paused database take a while after a quiet spell, and one /readyz call can itself wait on the database, so the
 * calls go one at a time, `intervalMs` after each answer, for `timeoutMs`. This is server state, not a game rule.
 */
export class ServerReadiness {
	phase = $state<ReadinessPhase>('checking');

	readonly #check: () => Promise<boolean>;
	readonly #intervalMs: number;
	readonly #timeoutMs: number;

	constructor(check: () => Promise<boolean> = api.isReady, { intervalMs = 2000, timeoutMs = 90_000 } = {}) {
		this.#check = check;
		this.#intervalMs = intervalMs;
		this.#timeoutMs = timeoutMs;
	}

	/** Resolves when the server is ready or the wait has run out; `phase` says which. Call again to retry. */
	async wait(): Promise<void> {
		this.phase = 'checking';
		const deadline = Date.now() + this.#timeoutMs;

		while (!(await this.#check())) {
			if (Date.now() >= deadline) {
				this.phase = 'unavailable';
				return;
			}
			this.phase = 'waking';
			await new Promise((resolve) => setTimeout(resolve, this.#intervalMs));
		}

		this.phase = 'ready';
	}
}
```

- [ ] **Step 5: Run the tests (green)**

```powershell
npm test -- src/lib/api/readiness.test.ts
```

Expected: 4 passed.

- [ ] **Step 6: The start screen**

Replace the whole of `src/WorldRankGuesser.Web/src/routes/+page.svelte` with

```svelte
<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { ServerReadiness } from '$lib/api/readiness.svelte';
	import { GameStore } from '$lib/game/gameStore.svelte';

	const store = new GameStore();
	const server = new ServerReadiness();

	// Ask before offering a game: after a quiet spell the server and its database take a while to wake.
	onMount(() => {
		server.wait();
	});

	async function startPractice() {
		const id = await store.start();
		if (id) await goto(`/play/${id}`);
	}
</script>

<h1>Guess where they rank</h1>
<p class="muted">
	You are dealt ten countries, one at a time. Put each into a different sport. You score the country's world rank in
	that sport, and the lowest total wins. Unranked, or ranked below 150th, scores 150.
</p>

{#if server.phase === 'unavailable'}
	<p class="error" role="alert">
		The server is not answering. It may be resting until the 1st of the month; please try again in a moment.
	</p>
	<button class="primary" onclick={() => server.wait()}>Try again</button>
{:else}
	<button class="primary" disabled={store.busy || server.phase !== 'ready'} onclick={startPractice}>Practice game</button>
	{#if server.phase === 'waking'}
		<p class="muted" role="status">Waking up the server… this can take up to a minute.</p>
	{/if}
{/if}

{#if store.error}
	<p class="error" role="alert">{store.error}</p>
{/if}
```

The button exists from the first render (disabled until ready), so Playwright's `getByRole('button', { name: 'Practice game' }).click()` simply waits for it to become enabled, and the existing three tests stay as they are. The waking message appears only after the first unready answer, so a healthy server shows no flash of it.

- [ ] **Step 7: Check, unit tests, and the Playwright game**

The Playwright run starts the API and Vite itself, so the development database must be up with rankings (`docker compose up -d --wait` from the repo root if it is not).

```powershell
npm run check
npm test
npm run test:e2e
git status --short
```

Expected: `svelte-check found 0 errors and 0 warnings`; every Vitest file passes; Playwright reports `3 passed`; `git status` (from the repo root) shows only the four files of this task; `src/lib/api/schema.d.ts` is untouched.

- [ ] **Step 8: Commit**

```powershell
git add src/WorldRankGuesser.Web/src/lib/api/client.ts src/WorldRankGuesser.Web/src/lib/api/readiness.svelte.ts src/WorldRankGuesser.Web/src/lib/api/readiness.test.ts src/WorldRankGuesser.Web/src/routes/+page.svelte
git commit -m @'
Wait for the server before offering a game

The start screen polls /readyz one call at a time, two seconds after
each answer, for up to ninety seconds, then shows a retry message that
also covers a spent free allowance. Server state only; GameStore has
no new rule.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 5: Playwright against a deployed app

`playwright.config.ts` reads `E2E_BASE_URL`: when it is set, that is the `baseURL` and no local servers are started, so the same three tests run against the compose stack (Task 8) and, in plan 2c, against staging.

**Files:**
- Modify: `src/WorldRankGuesser.Web/playwright.config.ts`

**Interfaces:**
- Produces: environment variable `E2E_BASE_URL` (a URL with no trailing slash; e.g. `http://localhost:8080`), consumed by Task 8's acceptance step and plan 2c's staging smoke test.

All commands in this task run from `src/WorldRankGuesser.Web`.

- [ ] **Step 1: The config**

Replace the whole of `src/WorldRankGuesser.Web/playwright.config.ts` with

```ts
import { defineConfig } from '@playwright/test';

// With E2E_BASE_URL set, the tests run against an app that is already up (the compose stack, staging) and nothing
// is started here. Without it, the API and Vite start as in development.
const deployed = process.env.E2E_BASE_URL;

export default defineConfig({
	testDir: 'e2e',
	use: { baseURL: deployed ?? 'http://localhost:5173' },
	webServer: deployed
		? undefined
		: [
				{
					command: 'dotnet run --project ../WorldRankGuesser.Api',
					url: 'http://localhost:5170/readyz',
					reuseExistingServer: true,
					timeout: 120_000
				},
				{
					command: 'npm run dev -- --port 5173 --strictPort',
					url: 'http://localhost:5173',
					reuseExistingServer: true
				}
			]
});
```

- [ ] **Step 2: Prove both modes**

```powershell
npm run test:e2e
$env:E2E_BASE_URL = 'http://localhost:5999'; npm run test:e2e; Remove-Item Env:E2E_BASE_URL
```

Expected: the first run reports `3 passed` as before (it started the servers). The second fails within seconds with `net::ERR_CONNECTION_REFUSED at http://localhost:5999/` on each test and never prints a `[WebServer]` line: nothing was started. (The real run against a deployed app is Task 8's acceptance.)

- [ ] **Step 3: Commit**

```powershell
git add src/WorldRankGuesser.Web/playwright.config.ts
git commit -m @'
Let the Playwright game run against a deployed app

E2E_BASE_URL makes it the baseURL and starts no local servers, so the
same tests play the compose stack now and staging in plan 2c.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 6: The game image

Three stages with the repo root as context. `web` builds the SvelteKit app from the committed `schema.d.ts` (it never regenerates API types); `api` publishes the API with build-time OpenAPI generation off (it boots the app and writes into the Web folder) and copies the web output into `wwwroot`; `runtime` is the chiseled "extra" ASP.NET image: non-root, no shell, port 8080, no `HEALTHCHECK`. The `migrate` stage arrives in Task 8.

**Files:**
- Create: `.dockerignore`
- Create: `Dockerfile`

**Interfaces:**
- Consumes: `global.json`, `Directory.Build.props`, `src/WorldRankGuesser.Api/`, `src/WorldRankGuesser.Web/` (with `package-lock.json`, whose optional dependencies include the Linux native binaries of `lightningcss` and `rolldown`).
- Produces: image `worldrankguesser-game` (local tag), listening on 8080, configured through `ConnectionStrings__WorldRankGuesserConnection` and `ASPNETCORE_ENVIRONMENT`; Task 8's `game` service and Task 9's `images` job build it.

- [ ] **Step 1: The build context**

Create `.dockerignore` at the repo root:

```
# Both Dockerfiles build with the repo root as context; keep outputs, dependencies, tooling, tests and docs out of it.
.git
.claude
.github
.idea
.vs
docs
tests
tools
**/bin
**/obj
**/node_modules
**/build
**/.svelte-kit
**/test-results
**/playwright-report
**/BenchmarkDotNet.Artifacts
**/publish
```

(`**/` is needed: a bare `node_modules` would match only at the root.)

- [ ] **Step 2: The Dockerfile**

Create `Dockerfile` at the repo root:

```dockerfile
# The game image: the SvelteKit build served from the API's wwwroot. Build from the repo root:
#   docker build -t worldrankguesser-game .
# The .NET tags follow global.json: when it moves, they move.

FROM node:24 AS web
WORKDIR /web
COPY src/WorldRankGuesser.Web/package.json src/WorldRankGuesser.Web/package-lock.json ./
RUN npm ci
COPY src/WorldRankGuesser.Web/ ./
# Uses the committed src/lib/api/schema.d.ts; API types are never regenerated here (that needs the built API).
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS api
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/WorldRankGuesser.Api/ src/WorldRankGuesser.Api/
# Build-time OpenAPI generation boots the app and writes into the Web folder; the committed document is the contract.
RUN dotnet publish src/WorldRankGuesser.Api -c Release -o /app -p:OpenApiGenerateDocuments=false -p:UseAppHost=false
COPY --from=web /web/build /app/wwwroot

# "extra" because Microsoft.Data.SqlClient refuses globalization-invariant mode and only "extra" ships ICU.
# Non-root (user app), no shell, port 8080. No HEALTHCHECK: Container Apps probes the app itself.
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1-resolute-chiseled-extra AS runtime
WORKDIR /app
COPY --from=api /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "WorldRankGuesser.Api.dll"]
```

- [ ] **Step 3: Build it**

```powershell
docker build -t worldrankguesser-game .
docker inspect worldrankguesser-game --format '{{.Config.User}} {{.Config.ExposedPorts}}'
```

Expected: the build succeeds (the first run pulls three base images; `npm run build` prints the adapter-static output; `dotnet publish` prints no `GetDocument` line, because generation is off); `inspect` prints `1654 map[8080/tcp:{}]` (the chiseled image's non-root `app` user, by its uid, and the exposed port).

- [ ] **Step 4: Run it against the development database**

The container reaches the host's SQL Server as `host.docker.internal`. The development database already has the `game` schema and rankings.

```powershell
docker run --rm -d --name wrg-game-check -p 127.0.0.1:8080:8080 -e ASPNETCORE_ENVIRONMENT=Production -e "ConnectionStrings__WorldRankGuesserConnection=Server=host.docker.internal,1433;Database=WorldRankGuesser;User Id=sa;Password=Rankings_Dev1!;TrustServerCertificate=True" worldrankguesser-game
$deadline = (Get-Date).AddSeconds(120); $ready = $null; while (-not $ready -and (Get-Date) -lt $deadline) { try { $ready = (Invoke-WebRequest -UseBasicParsing http://localhost:8080/readyz).Content } catch { Start-Sleep 2 } }; $ready
(Invoke-WebRequest -UseBasicParsing http://localhost:8080/).Content.Substring(0, 40)
(Invoke-WebRequest -UseBasicParsing http://localhost:8080/play/anything).StatusCode
docker logs wrg-game-check
docker stop wrg-game-check
```

Expected: `$ready` is `{"status":"ready","rankingsLoadedAt":"..."}`; the root returns HTML starting `<!doctype html>`; the client-side route returns 200 (the `index.html` fallback); the log shows `Rankings loaded: N rows, M drawable countries.` and the data-protection warning about keys stored unencrypted (accepted, spec section 7). If `/readyz` never answers, read the log: a connection error means the host is not reachable as `host.docker.internal`; stop and report.

- [ ] **Step 5: Commit**

```powershell
git status --short
git add .dockerignore Dockerfile
git commit -m @'
Build the game image

Three stages from the repo root: the SvelteKit build on node:24, a
dotnet publish with build-time OpenAPI generation off and the web
output in wwwroot, and the chiseled "extra" ASP.NET runtime (ICU for
SqlClient), non-root on port 8080 with no HEALTHCHECK.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 7: The scraper image, and the curl check

SDK publish, then the plain .NET runtime image with `curl` and `ca-certificates` installed, because `CurlFetcher` starts `curl` as a process. Non-root. It runs once and exits; `Program.cs` returns 1 when any feed failed (verified: `return summary.Failed > 0 ? 1 : 0;`), which marks a Job run failed. This task also runs the spec's first scraper risk check: Windows curl uses Schannel, Linux curl uses OpenSSL, and BWF's Cloudflare rule judges the TLS fingerprint, so the seven curl-fetched feeds (five BWF, two ice hockey) must be seen to pass from inside the container.

**Files:**
- Create: `src/SportsRankingService/Dockerfile`

**Interfaces:**
- Consumes: `global.json`, `Directory.Build.props`, `src/SportsRankingService/` (whose `appsettings.json` and `serviceconfig.json` are copied next to the binaries by the csproj).
- Produces: image `worldrankguesser-scraper` (local tag); arguments after the image name are the scraper's (`--only ...`); Task 8's `scraper` service and Task 9's `images` job build it.

- [ ] **Step 1: The Dockerfile**

Create `src/SportsRankingService/Dockerfile`:

```dockerfile
# The scraper image. Build from the repo root:
#   docker build -f src/SportsRankingService/Dockerfile -t worldrankguesser-scraper .
# Runs every enabled feed once and exits 0, or 1 if any feed failed; arguments after the image name are the
# scraper's (`--only Soccer`). The .NET tags follow global.json: when it moves, they move.

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/SportsRankingService/ src/SportsRankingService/
RUN dotnet publish src/SportsRankingService -c Release -o /app -p:UseAppHost=false

# The plain runtime, not chiseled: CurlFetcher starts curl as a process, so the image needs curl and root CAs.
FROM mcr.microsoft.com/dotnet/runtime:11.0.0-rc.1-resolute
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "SportsRankingService.dll"]
```

- [ ] **Step 2: Build it**

```powershell
docker build -f src/SportsRankingService/Dockerfile -t worldrankguesser-scraper .
docker run --rm --entrypoint curl worldrankguesser-scraper --version
docker run --rm --entrypoint id worldrankguesser-scraper
```

Expected: the build succeeds; `curl --version` prints a Linux curl with `OpenSSL` in its first line (the fingerprint the next step tests); `id` prints `uid=1654(app)`.

- [ ] **Step 3: The seven curl-fetched feeds from inside the container (spec section 6, risk 1)**

Against the development database, live federation sites; give the command a 10-minute timeout.

```powershell
docker run --rm -e "ConnectionStrings__WorldRankGuesserConnection=Server=host.docker.internal,1433;Database=WorldRankGuesser;User Id=sa;Password=Rankings_Dev1!;TrustServerCertificate=True" worldrankguesser-scraper --only Badminton --only "Ice Hockey"
$LASTEXITCODE
```

Expected: the log shows `Fetching ... with curl` for the BWF and Wikipedia URLs, no `Fetch failed` line, and a summary for 7 feeds ending in `0 failed` (new versus unchanged depends on what the federations published); `$LASTEXITCODE` is `0`.

If a BWF feed fails with a curl exit code and a Cloudflare page, the Linux fingerprint is refused. **Stop and report** the exact `Fetch failed` lines: the spec says to try a curl build with another TLS backend first, and the scraper's rules forbid forging a browser fingerprint; that is the owner's call, and the rest of this plan does not depend on it (a failed feed keeps its previous release).

- [ ] **Step 4: Commit**

```powershell
git add src/SportsRankingService/Dockerfile
git commit -m @'
Build the scraper image

SDK publish, then the plain runtime with curl and root certificates
installed, because CurlFetcher starts curl as a process; runs as the
non-root app user, once, and exits 1 if any feed failed. All seven
curl-fetched feeds passed from inside the container on Linux curl.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

If Step 3 found a refused feed, say so in the body instead of the last sentence.

---

### Task 8: The Compose stack

One file with profiles. `sql` stays as it is (same container name, same healthcheck, `127.0.0.1:1433`), so `docker compose up -d --wait` remains the development command. The project name moves from the old repo's `sportsrankingservice` to `worldrankguesser` (decided 2026-09-21), with the volume copied so local data survives. Behind `profiles: ["stack"]`: `migrate`, a fourth stage of the game's Dockerfile that applies `dbo` then `game` and exits; `game` on `127.0.0.1:8080` as `Production`, after `migrate`; `scraper` on demand. A commented Caddy service shows a VPS how to put TLS in front, because the Production cookie is `Secure`.

The `migrate` stage is an SDK image with both projects and the tool manifest, running `dotnet ef database update` twice, rather than a bind mount of the source (Windows `obj/` folders would collide with a Linux build) or EF bundles (plan 2c's mechanism; its quirks belong there).

**Files:**
- Modify: `Dockerfile` (a `migrate` stage between `api` and `runtime`)
- Modify: `docker-compose.yml`

**Interfaces:**
- Consumes: the images of Tasks 6 and 7; `.config/dotnet-tools.json` (`dotnet-ef` 11.0.0-rc.1); `E2E_BASE_URL` (Task 5); `Hosting:TrustForwardedHeaders` (Task 3, named in the Caddy comment).
- Produces: `docker compose --profile stack up --build -d`, `docker compose --profile stack run --rm scraper [--only ...]`, `docker compose --profile stack down`; images `worldrankguesser-game:local`, `worldrankguesser-migrate:local`, `worldrankguesser-scraper:local`; volume `worldrankguesser_sqldata`. Task 10 documents these.

- [ ] **Step 1: Before touching the file, record the data and move the volume**

The project name is still `sportsrankingservice` here, so `down` addresses the running container.

```powershell
docker compose ps --format '{{.Name}} {{.Status}}'
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM WorldRankGuesser.dbo.RankingReleases"
docker compose down
docker volume create worldrankguesser_sqldata
docker run --rm -v sportsrankingservice_sqldata:/from:ro -v worldrankguesser_sqldata:/to alpine sh -c "cp -a /from/. /to/"
docker volume ls --format '{{.Name}}' | Select-String sqldata
```

Expected: `ps` shows `worldrankguesser-sql` healthy; the count is a positive number: **note it**; `down` removes the container and keeps the volume; the copy prints nothing; the last line lists both `sportsrankingservice_sqldata` and `worldrankguesser_sqldata`.

- [ ] **Step 2: The migrate stage**

In `Dockerfile`, insert between the `api` stage (after its `COPY --from=web ...` line) and the `runtime` stage:

```dockerfile

# docker-compose.yml's migrate service: applies the scraper's dbo migrations, then the game's, and exits. Built only
# by name (`docker build --target migrate .`); the game image above never includes it. Both projects sit side by
# side here as a deployment tool; the API itself still never references the scraper.
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS migrate
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY .config/ .config/
RUN dotnet tool restore
COPY src/SportsRankingService/ src/SportsRankingService/
COPY src/WorldRankGuesser.Api/ src/WorldRankGuesser.Api/
RUN dotnet build src/SportsRankingService -c Release \
    && dotnet build src/WorldRankGuesser.Api -c Release -p:OpenApiGenerateDocuments=false
# dbo first, as on every new database. The connection string comes from the environment.
ENTRYPOINT ["sh", "-c", "dotnet ef database update --no-build -c Release --project src/SportsRankingService && dotnet ef database update --no-build -c Release --project src/WorldRankGuesser.Api"]
```

`runtime` must stay the last stage: `docker build .` without `--target` builds the last one.

- [ ] **Step 3: The compose file**

Replace the whole of `docker-compose.yml` with

```yaml
# Local SQL Server for development: `docker compose up -d --wait` starts only it. Behind the `stack` profile, the
# production-shaped stack: migrations (dbo, then game), the game image on http://localhost:8080 as Production, and
# the scraper image on demand:
#   docker compose --profile stack up --build -d
#   docker compose --profile stack run --rm scraper            # or `... scraper --only Soccer`
#   docker compose --profile stack down                        # the database volume stays
# The SA password is a dev-only credential for this container; it is also in
# src/SportsRankingService/appsettings.json and src/WorldRankGuesser.Api/appsettings.Development.json. Override the
# connection string elsewhere with the ConnectionStrings__WorldRankGuesserConnection environment variable.
name: worldrankguesser

x-stack-connection: &stack-connection
  ConnectionStrings__WorldRankGuesserConnection: "Server=sql,1433;Database=WorldRankGuesser;User Id=sa;Password=Rankings_Dev1!;TrustServerCertificate=True"

services:
  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: worldrankguesser-sql
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "Rankings_Dev1!"
      MSSQL_PID: Developer
    ports:
      - "127.0.0.1:1433:1433"
    volumes:
      - sqldata:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -Q 'SELECT 1' || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 20s

  # One shot: applies the scraper's dbo migrations, then the game's, and exits; game waits for it.
  # On a VPS it is an explicit `docker compose --profile stack run --rm migrate` before each release.
  migrate:
    profiles: ["stack"]
    image: worldrankguesser-migrate:local
    build:
      context: .
      target: migrate
    environment:
      <<: *stack-connection
    depends_on:
      sql:
        condition: service_healthy

  game:
    profiles: ["stack"]
    image: worldrankguesser-game:local
    build:
      context: .
    ports:
      - "127.0.0.1:8080:8080"
    environment:
      <<: *stack-connection
      ASPNETCORE_ENVIRONMENT: Production
      # Hosting__TrustForwardedHeaders stays off: this container is reached directly, not through a proxy.
    depends_on:
      sql:
        condition: service_healthy
      migrate:
        condition: service_completed_successfully

  # Runs once and exits (1 if any feed failed).
  scraper:
    profiles: ["stack"]
    image: worldrankguesser-scraper:local
    build:
      context: .
      dockerfile: src/SportsRankingService/Dockerfile
    environment:
      <<: *stack-connection
    depends_on:
      sql:
        condition: service_healthy
      migrate:
        condition: service_completed_successfully

  # On a VPS the Production cookie is Secure (browsers exempt only localhost), so the game needs TLS in front of it.
  # Caddy obtains a certificate by itself. To use it: uncomment this service and the caddy_data volume, put the
  # hostname in, drop the game's `ports` (Caddy reaches it on the compose network), and set
  # `Hosting__TrustForwardedHeaders: "true"` on game so the per-IP rate limit keys on the caller's address.
  # caddy:
  #   profiles: ["stack"]
  #   image: caddy:2
  #   ports:
  #     - "80:80"
  #     - "443:443"
  #   command: caddy reverse-proxy --from game.example.com --to game:8080
  #   volumes:
  #     - caddy_data:/data
  #   depends_on:
  #     - game

volumes:
  sqldata:
  # caddy_data:
```

- [ ] **Step 4: The development database under the new name, and the old volume gone**

```powershell
docker compose up -d --wait
docker inspect worldrankguesser-sql --format '{{index .Config.Labels "com.docker.compose.project"}} {{range .Mounts}}{{.Name}}{{end}}'
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM WorldRankGuesser.dbo.RankingReleases"
docker volume rm sportsrankingservice_sqldata
docker volume ls --format '{{.Name}}' | Select-String sqldata
```

Expected: `up` starts `worldrankguesser-sql` healthy; `inspect` prints `worldrankguesser worldrankguesser_sqldata`; the count equals the one noted in Step 1; the old volume is removed (if Docker refuses because it is in use, stop and report; do not force); the last line lists only `worldrankguesser_sqldata`.

- [ ] **Step 5: The stack on the existing data**

```powershell
docker compose --profile stack up --build -d
docker compose --profile stack ps -a --format '{{.Name}} {{.Status}}'
docker compose --profile stack logs migrate
$deadline = (Get-Date).AddSeconds(120); $ready = $null; while (-not $ready -and (Get-Date) -lt $deadline) { try { $ready = (Invoke-WebRequest -UseBasicParsing http://localhost:8080/readyz).Content } catch { Start-Sleep 2 } }; $ready
```

Expected: `ps` shows `worldrankguesser-sql` healthy, `worldrankguesser-migrate-1 Exited (0)`, `worldrankguesser-game-1 Up`; the migrate log prints `No migrations were applied. The database is already up to date.` twice (the development database is migrated already); `$ready` is `{"status":"ready",...}`.

- [ ] **Step 6: Play the game through the stack, and run the scraper image through it**

```powershell
cd src/WorldRankGuesser.Web; $env:E2E_BASE_URL = 'http://localhost:8080'; npm run test:e2e; Remove-Item Env:E2E_BASE_URL; cd ../..
docker compose --profile stack run --rm scraper --only Soccer
$LASTEXITCODE
```

Expected: Playwright reports `3 passed` with no `[WebServer]` lines (a full practice game over the Production cookie on `http://localhost` through the image); the scraper container fetches the two soccer feeds (`FIFA`), prints a summary for 2 feeds with `0 failed`, and exits `0`. The games it played are in the development database's `game` schema, as `npm run test:e2e` always leaves them.

- [ ] **Step 7: From a clean clone (the spec's acceptance), under a throwaway project name**

A second project name gives an empty volume and rebuilds nothing (the images are named). The container name is pinned, so the development stack goes down first. The full scrape takes a few minutes: give it a 10-minute timeout.

```powershell
docker compose --profile stack down
docker compose -p wrgclean --profile stack up -d
docker compose -p wrgclean --profile stack logs migrate
try { (Invoke-WebRequest -UseBasicParsing http://localhost:8080/readyz).StatusCode } catch { $_.Exception.Response.StatusCode.value__ }
```

Expected: the migrate log shows both sets applied on the new database: `Applying migration '20260917221403_InitialSchema'.` then `Applying migration '20260919070050_InitialGameSchema'.`; `/readyz` answers `503`: the view exists but is empty, which Task 2 does not count as loaded, and the game keeps retrying on its backoff.

```powershell
docker compose -p wrgclean --profile stack run --rm scraper
$LASTEXITCODE
$deadline = (Get-Date).AddSeconds(90); $ready = $null; while (-not $ready -and (Get-Date) -lt $deadline) { try { $ready = (Invoke-WebRequest -UseBasicParsing http://localhost:8080/readyz).Content } catch { Start-Sleep 2 } }; $ready
cd src/WorldRankGuesser.Web; $env:E2E_BASE_URL = 'http://localhost:8080'; npm run test:e2e; Remove-Item Env:E2E_BASE_URL; cd ../..
docker compose -p wrgclean --profile stack down -v
docker compose up -d --wait
```

Expected: the scrape ends with a summary of 54 feeds and `0 failed` (a feed the federation changed since 2026-09-21 may fail: note it, it is not this plan's defect, and the game still becomes ready as long as at least ten countries are drawable); within 60 seconds of the scrape `/readyz` is `{"status":"ready",...}` without any restart (the backoff's last step); Playwright reports `3 passed`; `down -v` removes `wrgclean_sqldata`; the last command brings the development database back.

- [ ] **Step 8: Commit**

```powershell
git status --short
git add Dockerfile docker-compose.yml
git commit -m @'
Add the production-shaped stack to docker-compose.yml

The project is now named worldrankguesser (the volume was copied from
the old repo's project). Behind the stack profile: migrate, a stage of
the game's Dockerfile that applies dbo then game and exits; the game
image on 127.0.0.1:8080 as Production; the scraper image on demand;
and a commented Caddy service for a VPS, since the Production cookie
is Secure. From a clean clone the stack migrates an empty database,
the scraper fills the view, and the game goes ready on its own.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 9: CI builds both images; the action warnings

`ci.yml` gains an `images` job that builds the game image, the `migrate` target and the scraper image on every pull request without pushing (spec 8.2; the deploy workflows of plan 2c are what push). The same edit takes the three actions off Node 20, which GitHub's runner now warns about: `actions/checkout@v7`, `actions/setup-node@v7` and `actions/setup-dotnet@v6` are the current majors and each declares `using: node24` (checked in their `action.yml` on 2026-09-21). The `ubuntu-latest` notice (Ubuntu 26 from 2026-10-19) is informational and needs no change. The one compiler warning in the solution, xUnit2029 in a scraper test, is fixed here too.

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `tests/SportsRankingService.Tests/Parsers/FigParserTests.cs:52`

**Interfaces:** none new; the job runs the `docker build` commands of Tasks 6, 7 and 8 exactly.

- [ ] **Step 1: The warning**

In `tests/SportsRankingService.Tests/Parsers/FigParserTests.cs`, replace

```csharp
        Assert.Empty(rows.Where(r => r.Position == 2));
```

with

```csharp
        Assert.DoesNotContain(rows, r => r.Position == 2);
```

```powershell
dotnet build WorldRankGuesser.slnx | Select-String "warning" | Sort-Object -Unique
dotnet test tests/SportsRankingService.Tests --no-build --filter "FullyQualifiedName~FigParserTests"
```

Expected: no warning lines at all; the FIG tests pass.

- [ ] **Step 2: The workflow**

Replace the whole of `.github/workflows/ci.yml` with

```yaml
name: CI

on:
  pull_request:
  push:
    branches: [main]

jobs:
  api:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
      - run: dotnet build WorldRankGuesser.slnx
      - run: dotnet test WorldRankGuesser.slnx --no-build
      - name: The committed OpenAPI document matches the API
        run: git diff --exit-code -- src/WorldRankGuesser.Web/openapi

  web:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: src/WorldRankGuesser.Web
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-node@v7
        with:
          node-version: 24
          cache: npm
          cache-dependency-path: src/WorldRankGuesser.Web/package-lock.json
      - run: npm ci
      - name: The committed API types match the OpenAPI document
        run: npm run gen:api && git diff --exit-code -- src/lib/api/schema.d.ts
      - run: npm run check
      - run: npm test -- --passWithNoTests
      - run: npm run build

  # Both images, and the compose stack's migrate target, build on every pull request; nothing is pushed here.
  # The deploy workflows are what push (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md, section 8).
  images:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - name: The game image
        run: docker build -t worldrankguesser-game .
      - name: The migrate target
        run: docker build --target migrate -t worldrankguesser-migrate .
      - name: The scraper image
        run: docker build -f src/SportsRankingService/Dockerfile -t worldrankguesser-scraper .
```

- [ ] **Step 3: Run the three builds the job runs**

```powershell
docker build -t worldrankguesser-game .
docker build --target migrate -t worldrankguesser-migrate .
docker build -f src/SportsRankingService/Dockerfile -t worldrankguesser-scraper .
```

Expected: each succeeds (cached from Tasks 6 to 8). The job itself is proven when the pull request runs (Task 10).

- [ ] **Step 4: Commit**

```powershell
git add .github/workflows/ci.yml tests/SportsRankingService.Tests/Parsers/FigParserTests.cs
git commit -m @'
Build both images in CI; move the actions off Node 20

An images job builds the game image, the migrate target and the
scraper image on every pull request without pushing. checkout, setup-
node and setup-dotnet move to the majors that run on Node 24, which
ends the runner's deprecation warnings. The one solution warning,
xUnit2029 in FigParserTests, is fixed.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 10: Documents, the branch, the pull request

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `src/SportsRankingService/README.md`
- Modify: `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md:3-4`

**Interfaces:** none; documentation only.

- [ ] **Step 1: CLAUDE.md**

1. In "Commands", after the line `dotnet run tools/SimulateBoards/simulate.cs -- 20000 ...`, add:

   ```
   docker compose --profile stack up --build -d                             # the production-shaped stack: migrate (dbo, then game), then the game image on http://localhost:8080 as Production
   docker compose --profile stack run --rm scraper                          # the scraper image once against the stack's database; append --only ... for a subset
   docker compose --profile stack down                                      # stop the stack; the database volume stays
   docker build -t worldrankguesser-game . ; docker build -f src/SportsRankingService/Dockerfile -t worldrankguesser-scraper .   # the two images (CI also builds `--target migrate`)
   ```

   and, after the `npm run gen:api` line:

   ```
   $env:E2E_BASE_URL='http://localhost:8080'; npm run test:e2e   # the same Playwright game against a deployed app (the stack, staging); starts no local servers
   ```

2. In "Rankings pipeline", replace `refreshed at startup and hourly by `RankingsRefreshService`; a failed refresh keeps the previous snapshot.` with:

   ```
   refreshed in the background by `RankingsRefreshService`: at once after the host starts, then on a 5/10/20/40/60 s backoff until the first snapshot exists (a cold start usually meets a paused database), then every `Rankings:RefreshMinutes`; a failed refresh keeps the previous snapshot, and a view with fewer drawable countries than categories does not count as loaded, because no board can be drawn from it. Host startup never waits for a load: `/readyz` is 503 until the first snapshot, `/healthz` is 200 at once.
   ```

3. Add a paragraph after "Persistence" (before "Two migration sets"):

   ```
   **Hosting.** `GameDbContext.Configure` retries transient SQL failures (six attempts, 30 s maximum delay): a paused Azure SQL database refuses connections while it resumes. Concurrency conflicts are not transient, so the `409` path never retries. `Hosting:TrustForwardedHeaders` (off by default) puts the forwarded-headers middleware first in the pipeline for `X-Forwarded-For` and `X-Forwarded-Proto`, forward limit 1, every proxy trusted; on only behind an ingress that rewrites those headers, so a directly exposed container cannot be spoofed. Images: the root `Dockerfile` (SvelteKit build → `dotnet publish` with build-time OpenAPI generation off and the web output in `wwwroot` → chiseled "extra" ASP.NET runtime: ICU for SqlClient, non-root, port 8080, no `HEALTHCHECK`; plus a `migrate` target for compose that applies `dbo` then `game`) and `src/SportsRankingService/Dockerfile` (plain runtime with `curl`). Both build from the repo root (`.dockerignore`), and **their .NET tags follow `global.json`: when it moves, they move.** `docker-compose.yml` is the development SQL Server plus the `stack` profile; in the stack `ASPNETCORE_ENVIRONMENT` is `Production`, so the player cookie is `Secure`, which browsers exempt on `localhost`; a VPS needs TLS in front (the commented Caddy service).
   ```

4. In "Front end", after the sentence ending `and lands when the next country arrives.`, add:

   ```
   Before a game can start, the start screen polls `/readyz` through `ServerReadiness` (`src/lib/api/readiness.svelte.ts`): one call at a time, 2 s after each answer, for 90 s, then a retry message; server state, not a game rule.
   ```

5. In "Tests", after `Pure logic (scoring, snapshot builder, board generator, optimal assignment) is unit-tested without a database.`, add: `` `RankingsRefreshServiceTests` drives the backoff on a `FakeTimeProvider`. `` And at the end of the section add: `` Integration tests that start games call `ApiFactory.WaitUntilReadyAsync()`, because the rankings load after the host starts. ``

- [ ] **Step 2: README.md**

After step 4 of "Running locally", add:

```

Or everything in containers, the way it is deployed: `docker compose --profile stack up --build -d` (SQL Server, both migration sets, then the game on http://localhost:8080) and `docker compose --profile stack run --rm scraper` once, so it has rankings.
```

- [ ] **Step 3: The scraper's README**

In `src/SportsRankingService/README.md`:

1. Replace the "Setup" code block (its paths predate the import) with:

   ```powershell
   docker compose up -d --wait                              # SQL Server 2022 on localhost,1433, loopback-only (sa / Rankings_Dev1!); the repo root's compose file
   dotnet tool restore                                      # dotnet-ef
   dotnet ef database update --project src/SportsRankingService   # create the database and the dbo schema
   dotnet run --project src/SportsRankingService            # fetch every enabled feed once and exit
   dotnet run --project src/SportsRankingService -- --only "Cricket ODI Women" --only Soccer   # rerun a subset
   dotnet test tests/SportsRankingService.Tests             # fixture-based tests, no external network or database (needs curl on the PATH)
   ```

2. In "Scheduling a weekly run", before the `Windows Task Scheduler` paragraph, add:

   ```
   As a container (`src/SportsRankingService/Dockerfile`, built from the repo root; it carries curl and runs as a
   non-root user; the exit code is the process's, so a scheduler or a Container Apps Job sees a failed feed):

   ```powershell
   docker build -f src/SportsRankingService/Dockerfile -t worldrankguesser-scraper .
   docker run --rm -e "ConnectionStrings__WorldRankGuesserConnection=Server=...;Database=WorldRankGuesser;..." worldrankguesser-scraper                # every enabled feed
   docker run --rm -e "ConnectionStrings__WorldRankGuesserConnection=..." worldrankguesser-scraper --only Soccer   # a subset
   ```

   In this repo's compose stack it is `docker compose --profile stack run --rm scraper`.

   ```

3. In "Configuration", the sentence `(slim container images may` / `not: `apt-get install -y curl`)` wraps across two lines; replace its second line's `not: `apt-get install -y curl`)` with `not; the Dockerfile installs it)`.

- [ ] **Step 4: The spec's status line**

In `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md`, replace

```
Status: **approved design.** Every section was approved in discussion and the owner's decisions are made (section 2). No implementation plan exists yet.
```

with

```
Status: **approved design.** Every section was approved in discussion and the owner's decisions are made (section 2). Plans: 2a `docs/superpowers/plans/2026-09-21-phase-2a-scraper-import.md` (merged 2026-09-21), 2b `docs/superpowers/plans/2026-09-21-phase-2b-images-compose-app-changes.md` (sections 6 and 9; the Compose project was renamed `worldrankguesser`), 2c not yet written (sections 7 and 8, and the promotion-guard action of section 9).
```

- [ ] **Step 5: Commit and push**

```powershell
dotnet build WorldRankGuesser.slnx
git status --short
git add CLAUDE.md README.md src/SportsRankingService/README.md docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md
git commit -m @'
Document the images, the stack and the hosting settings

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
git push -u origin phase-2b-images-compose-app
```

Expected: the build changes nothing under `src/WorldRankGuesser.Web/openapi`; `git status` before the add shows only the four documents.

- [ ] **Step 6: The pull request**

Open a pull request from `phase-2b-images-compose-app` to `main` on GitHub (`gh` is not installed on this machine; if it is later, `gh pr create` with the same text) titled "Build the images and the compose stack; fix the three hosting defects" with this body:

```
Phase 2b of docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md (sections 6 and 9).

- Game image (SvelteKit build → dotnet publish → chiseled "extra" runtime) and scraper image (runtime + curl); all seven curl-fetched feeds pass from inside the container.
- docker-compose.yml: project renamed worldrankguesser; a `stack` profile with migrate (dbo, then game), the game on :8080 as Production, and the scraper on demand. From a clean clone the stack migrates, the scraper fills the view, and the game goes ready by itself.
- SQL retries, forwarded headers behind Hosting:TrustForwardedHeaders, rankings loaded in the background on a backoff, and a waking-up state on the start screen.
- Playwright takes E2E_BASE_URL; it played the stack.
- CI builds both images and the migrate target on every PR; the actions move to their Node 24 majors.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
```

Expected: all three CI jobs green (`api`, `web`, `images`), with no Node 20 deprecation annotations. Fix a first-run failure on this branch.

Plan 2b is complete when the pull request is merged: `docker compose --profile stack up --build -d` from a clean clone serves a playable practice game on `localhost:8080` once the scraper has run, `docker compose up -d --wait` still starts only the development database, and CI builds both images on every pull request.
