# Phase 0 + 1: Reset and Practice Game — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Blazor Server app with an ASP.NET Core API and a SvelteKit front end that together play a complete, server-authoritative **practice** game against the rankings database.

**Architecture:** The API loads `dbo.CurrentCountryRankings` into an immutable in-memory snapshot, generates an immutable 10×10 *board* per game, and resolves every pick as a lookup on that board. The browser only ever sends a category ID and only ever learns the current country. The SvelteKit app is a static single-page app that renders server state; in development Vite proxies `/api` to the API.

**Tech Stack:** .NET 11 (RC 1), ASP.NET Core minimal APIs, EF Core 11 on SQL Server, xUnit 2, Testcontainers, SvelteKit 2 / Svelte 5 / TypeScript, Vite, Vitest, Playwright, `openapi-typescript`, `flag-icons`.

**Spec:** `docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md` — read it first. This plan covers spec phases 0 and 1 only.

## Global Constraints

- Target framework is `net11.0`. SDK pinned in `global.json` to `11.0.100-rc.1.26425.128`, `rollForward: latestFeature`, `allowPrerelease: true`.
- Every `Microsoft.EntityFrameworkCore.*`, `Microsoft.AspNetCore.*` and `Microsoft.Extensions.ApiDescription.Server` package uses version `11.0.0-rc.1.26425.128`. `dotnet-ef` uses the same version.
- Test stack: `xunit` 2.9.3 (the SDK template's version; `IAsyncLifetime` methods return `Task`), `Testcontainers.MsSql` 4.15.0, `Microsoft.AspNetCore.Mvc.Testing` at the version above.
- Solution file is `WorldRankGuesser.slnx` (the .NET 11 SDK's default format).
- Node 24, npm. Front-end packages: `svelte` ^5.57, `@sveltejs/kit` ^2.70, `@sveltejs/adapter-static` ^3.0, `vite` ^8, `vitest` ^5, `openapi-typescript` ^7.13, `flag-icons` ^7.5, `@playwright/test` ^1.63.
- The API owns SQL schema `game` only, with its migrations history table at `game.__EFMigrationsHistory`. It never creates, alters or migrates anything in `dbo`. The API never migrates itself at startup; tests and developers apply migrations explicitly.
- The connection string is named `WorldRankGuesserConnection` (same name the scraper uses).
- Local development uses the SQL Server container from the **SportsRankingService** repo (`docker compose up -d --wait` there; `127.0.0.1,1433`, `sa` / `Rankings_Dev1!`, database `WorldRankGuesser`, already populated by the scraper). This repo does not start a second SQL Server. The spec's `docker-compose.yml` is deferred to phase 2, where it ships with the Dockerfile; a compose file here today would only collide with that container on port 1433.
- Scoring rule, verbatim from the spec: `score = min(rank in the configured mode, Cap)`; unranked → `Cap`. Defaults: `RankMode` `Country`, `Cap` `150`.
- Country rank uses standard competition ranking: tied countries share a rank and the next rank skips (1, 2, 2, 4).
- Category IDs are exactly: `soccer`, `basketball`, `cricket`, `rugby`, `volleyball`, `tennis`, `badminton`, `baseball`, `hockey`, `gymnastics`. The category count is never hardcoded in code; a game has as many turns as its board has categories.
- `ENG`, `SCO`, `WAL`, `NIR`, `WI` are never drawable.
- A response never contains a country beyond the current turn, and never contains the grid or the optimal score before the game is complete.
- A game that is not the caller's returns `404`, never `403`.
- Phase 1 implements **practice** mode only. `POST /api/games` with `mode: "daily"` returns `400`. The timer, streaks, nicknames, history, leaderboards and sign-in are later phases; their columns exist in the schema but no code reads or writes them beyond defaults.
- Commit after every task. Work on branch `rebuild`. Commit messages are one short imperative sentence (repo style).

## File Structure

```
global.json
Directory.Build.props                       TargetFramework, Nullable, ImplicitUsings for every project
WorldRankGuesser.slnx
.config/dotnet-tools.json                   dotnet-ef
.github/workflows/ci.yml
tools/GenerateCountryCatalog/generate.cs    one-off generator for the ISO3→ISO2 table (file-based app)

src/WorldRankGuesser.Api/
  Program.cs                                composition root only
  appsettings.json                          Scoring, Game (categories, aliases), Rankings, RateLimits
  appsettings.Development.json              dev connection string
  Configuration/ScoringOptions.cs           RankMode, Cap
  Configuration/GameOptions.cs              categories, aliases, not-drawable, MinCategoriesRanked
  Configuration/RankingsOptions.cs          RefreshMinutes
  Configuration/RateLimitOptions.cs
  Countries/CountryCatalog.cs               ISO3 → ISO2 lookup
  Countries/countries.json                  generated, committed, embedded
  Rankings/CountryRankingRow.cs             one row of the view
  Rankings/FeedRank.cs                      FeedRank, CategoryRank
  Rankings/RankingsSnapshot.cs              immutable read model
  Rankings/RankingsSnapshotBuilder.cs       rows → snapshot (country rank, best across feeds, aliases, pool)
  Rankings/IRankingsReader.cs, RankingsReader.cs
  Rankings/RankingsStore.cs                 holds the current snapshot
  Rankings/RankingsRefreshService.cs        load at startup, then hourly
  Scoring/ScoringEngine.cs                  CategoryRank + mode + cap → BoardCell
  Boards/BoardContent.cs                    BoardCategory, BoardCountry, BoardCell, BoardContent
  Boards/OptimalAssignment.cs               bitmask DP
  Boards/BoardGenerator.cs                  draw + grid + optimal
  Persistence/Player.cs, Board.cs, Game.cs, Pick.cs, GameMode.cs
  Persistence/GameDbContext.cs
  Persistence/Migrations/
  Players/PlayerService.cs, PlayerIdentity.cs
  Games/GameDtos.cs                         GameStateDto and friends, request records
  Games/GameStateMapper.cs                  Game → GameStateDto (the only place that decides what is revealed)
  Games/GameService.cs                      start, get, pick
  Endpoints/GameEndpoints.cs, HealthEndpoints.cs

tests/WorldRankGuesser.Api.Tests/
  Countries/CountryCatalogTests.cs
  Scoring/ScoringEngineTests.cs
  Rankings/RankingsSnapshotBuilderTests.cs, TestData.cs
  Boards/OptimalAssignmentTests.cs, BoardGeneratorTests.cs
  Integration/SqlServerFixture.cs, RankingsSeed.cs, ApiFactory.cs
  Integration/PersistenceTests.cs, ReadinessTests.cs, GameServiceTests.cs, GameApiTests.cs, AntiCheatTests.cs

src/WorldRankGuesser.Web/
  svelte.config.js, vite.config.ts, playwright.config.ts, package.json
  openapi/WorldRankGuesser.Api.json         generated by the API build, committed
  src/lib/api/schema.d.ts                   generated by openapi-typescript, committed
  src/lib/api/client.ts                     typed fetch wrappers
  src/lib/countries/iso2.json               generated decoy list (no ranks)
  src/lib/game/gameStore.svelte.ts          the only holder of game state
  src/lib/game/spin.ts                      pure spin-sequence builder
  src/lib/components/Flag.svelte, CategoryCard.svelte, CountrySpinner.svelte
  src/routes/+layout.ts, +layout.svelte, +page.svelte
  src/routes/play/[gameId]/+page.svelte
  src/routes/results/[gameId]/+page.svelte
  e2e/practice-game.test.ts
```

---

### Task 1: Reset the repository

Removes the Blazor app and leaves a building, tested, CI-checked skeleton.

**Files:**
- Delete: `WorldRankGuesser/` (whole folder), `WorldRankGuesser.sln`
- Create: `global.json`, `Directory.Build.props`, `WorldRankGuesser.slnx`, `.github/workflows/ci.yml`
- Create: `src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj`, `Program.cs`, `Properties/launchSettings.json`, `appsettings.json`, `appsettings.Development.json`
- Create: `tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj`, `HealthzTests.cs`

**Interfaces:**
- Produces: `public partial class Program` (so tests can use `WebApplicationFactory<Program>`); `GET /healthz` → `200 {"status":"ok"}`; the API listens on `http://localhost:5170`.

- [ ] **Step 1: Remove the Blazor app**

```bash
git rm -r -q WorldRankGuesser WorldRankGuesser.sln
rm -rf WorldRankGuesser .vs
```

Expected: `git status --short` shows only deletions; the `WorldRankGuesser/` folder (including ignored `bin/` and `obj/`) no longer exists.

- [ ] **Step 2: Create `global.json` and `Directory.Build.props`**

`global.json`:

```json
{
  "sdk": {
    "version": "11.0.100-rc.1.26425.128",
    "rollForward": "latestFeature",
    "allowPrerelease": true
  }
}
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Scaffold the solution and projects**

```bash
dotnet new sln -n WorldRankGuesser
dotnet new web -n WorldRankGuesser.Api -o src/WorldRankGuesser.Api
dotnet new xunit -n WorldRankGuesser.Api.Tests -o tests/WorldRankGuesser.Api.Tests
dotnet sln WorldRankGuesser.slnx add src/WorldRankGuesser.Api tests/WorldRankGuesser.Api.Tests
dotnet add tests/WorldRankGuesser.Api.Tests reference src/WorldRankGuesser.Api
dotnet add tests/WorldRankGuesser.Api.Tests package Microsoft.AspNetCore.Mvc.Testing --version 11.0.0-rc.1.26425.128
rm tests/WorldRankGuesser.Api.Tests/UnitTest1.cs
```

Replace `src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <InternalsVisibleTo Include="WorldRankGuesser.Api.Tests" />
  </ItemGroup>

</Project>
```

In `tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj`, delete the `<TargetFramework>`, `<ImplicitUsings>` and `<Nullable>` lines (they come from `Directory.Build.props`); keep everything else.

Replace `src/WorldRankGuesser.Api/Properties/launchSettings.json` with:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5170",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
    }
  }
}
```

- [ ] **Step 4: Write the failing test**

`tests/WorldRankGuesser.Api.Tests/HealthzTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WorldRankGuesser.Api.Tests;

public class HealthzTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Healthz_returns_ok()
    {
        var response = await factory.CreateClient().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 5: Run it to see it fail**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: FAIL to compile — `Program` is inaccessible (the template's `Program` is an internal top-level class).

- [ ] **Step 6: Write `Program.cs`**

Replace `src/WorldRankGuesser.Api/Program.cs` with:

```csharp
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
```

- [ ] **Step 7: Run the test to see it pass**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 8: Add CI**

`.github/workflows/ci.yml`:

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
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet build WorldRankGuesser.slnx
      - run: dotnet test WorldRankGuesser.slnx --no-build
```

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "Replace the Blazor app with an API and test skeleton on net11.0"
```

---

### Task 2: Country catalog (ISO3 → ISO2)

The rankings view only has ISO3 codes; flags are keyed by ISO2. The table is generated once from .NET's `RegionInfo`, committed, and embedded, so runtime never depends on the host's ICU data. Verified on 2026-09-19: of the 230 codes in the live view, the only drawable one `RegionInfo` lacks is `XKX` (Kosovo), which the generator adds.

**Files:**
- Create: `tools/GenerateCountryCatalog/generate.cs`
- Create (generated): `src/WorldRankGuesser.Api/Countries/countries.json`
- Create: `src/WorldRankGuesser.Api/Countries/CountryCatalog.cs`
- Modify: `src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj`
- Test: `tests/WorldRankGuesser.Api.Tests/Countries/CountryCatalogTests.cs`

**Interfaces:**
- Produces:
  - `CountryCatalog.LoadEmbedded()` → `CountryCatalog`
  - `new CountryCatalog(IReadOnlyDictionary<string, string> iso2ByIso3)` (for tests)
  - `bool TryGetIso2(string iso3, out string iso2)`
  - Generator usage: `dotnet run tools/GenerateCountryCatalog/generate.cs -- <countries.json path> [<iso2.json path>]`. The optional second file is a JSON array of ISO2 codes for the front end (Task 11).

- [ ] **Step 1: Write the failing tests**

`tests/WorldRankGuesser.Api.Tests/Countries/CountryCatalogTests.cs`:

```csharp
using WorldRankGuesser.Api.Countries;

namespace WorldRankGuesser.Api.Tests.Countries;

public class CountryCatalogTests
{
    private static readonly CountryCatalog Catalog = CountryCatalog.LoadEmbedded();

    [Theory]
    [InlineData("DEU", "DE")]
    [InlineData("GBR", "GB")]
    [InlineData("TWN", "TW")]
    [InlineData("SXM", "SX")]
    [InlineData("XKX", "XK")]
    public void Maps_iso3_to_iso2(string iso3, string expected)
    {
        Assert.True(Catalog.TryGetIso2(iso3, out var iso2));
        Assert.Equal(expected, iso2);
    }

    [Theory]
    [InlineData("ENG")]
    [InlineData("WI")]
    [InlineData("???")]
    public void Unknown_codes_are_not_found(string iso3)
    {
        Assert.False(Catalog.TryGetIso2(iso3, out _));
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: FAIL to compile — namespace `WorldRankGuesser.Api.Countries` does not exist.

- [ ] **Step 3: Write the generator**

`tools/GenerateCountryCatalog/generate.cs`:

```csharp
// One-off generator for the committed ISO3 -> ISO2 table. Re-run only to add countries.
// Usage: dotnet run tools/GenerateCountryCatalog/generate.cs -- <countries.json> [<iso2.json>]
using System.Globalization;
using System.Text.Json;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: generate.cs -- <countries.json> [<iso2.json>]");
    return 1;
}

var iso2ByIso3 = new SortedDictionary<string, string>(StringComparer.Ordinal);

foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
{
    var region = new RegionInfo(culture.Name);
    var iso2 = region.TwoLetterISORegionName;
    if (iso2.Length != 2 || iso2.Any(char.IsDigit)) continue;   // skips "001", "150", "419"
    iso2ByIso3.TryAdd(region.ThreeLetterISORegionName, iso2);
}

// RegionInfo calls Kosovo XKK; the scraper and the federations use XKX.
iso2ByIso3["XKX"] = "XK";

var options = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(args[0], JsonSerializer.Serialize(iso2ByIso3, options) + "\n");
Console.WriteLine($"{iso2ByIso3.Count} countries -> {args[0]}");

if (args.Length == 2)
{
    var iso2List = iso2ByIso3.Values.Distinct().Order(StringComparer.Ordinal).ToArray();
    File.WriteAllText(args[1], JsonSerializer.Serialize(iso2List) + "\n");
    Console.WriteLine($"{iso2List.Length} ISO2 codes -> {args[1]}");
}

return 0;
```

- [ ] **Step 4: Generate the table**

```bash
mkdir -p src/WorldRankGuesser.Api/Countries
dotnet run tools/GenerateCountryCatalog/generate.cs -- src/WorldRankGuesser.Api/Countries/countries.json
```

Expected: prints about `245 countries -> ...` (the exact count depends on the machine's ICU data; anything from 240 to 255 is fine). Open the file and confirm it contains `"DEU": "DE"` and `"XKX": "XK"`.

- [ ] **Step 5: Embed it and write the catalog**

Add to `src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj`, inside `<Project>`:

```xml
  <ItemGroup>
    <Content Remove="Countries\countries.json" />
    <EmbeddedResource Include="Countries\countries.json" LogicalName="countries.json" />
  </ItemGroup>
```

`src/WorldRankGuesser.Api/Countries/CountryCatalog.cs`:

```csharp
using System.Text.Json;

namespace WorldRankGuesser.Api.Countries;

/// <summary>ISO 3166 alpha-3 to alpha-2, from the committed countries.json. Flags are keyed by alpha-2.</summary>
public sealed class CountryCatalog(IReadOnlyDictionary<string, string> iso2ByIso3)
{
    public static CountryCatalog LoadEmbedded()
    {
        using var stream = typeof(CountryCatalog).Assembly.GetManifestResourceStream("countries.json")
            ?? throw new InvalidOperationException("Embedded resource countries.json is missing.");

        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("countries.json is empty.");

        return new CountryCatalog(map);
    }

    public bool TryGetIso2(string iso3, out string iso2)
    {
        if (iso2ByIso3.TryGetValue(iso3, out var found))
        {
            iso2 = found;
            return true;
        }

        iso2 = string.Empty;
        return false;
    }
}
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: all pass (9 tests).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Add the committed ISO3 to ISO2 country catalog"
```

---

### Task 3: Options and the scoring engine

**Files:**
- Create: `src/WorldRankGuesser.Api/Configuration/ScoringOptions.cs`, `Configuration/GameOptions.cs`
- Create: `src/WorldRankGuesser.Api/Rankings/FeedRank.cs`
- Create: `src/WorldRankGuesser.Api/Boards/BoardContent.cs`
- Create: `src/WorldRankGuesser.Api/Scoring/ScoringEngine.cs`
- Modify: `src/WorldRankGuesser.Api/appsettings.json`, `Program.cs`
- Test: `tests/WorldRankGuesser.Api.Tests/Scoring/ScoringEngineTests.cs`

**Interfaces:**
- Produces:
  - `enum RankMode { Entry, Country }`
  - `ScoringOptions { RankMode RankMode; int Cap; }`, section `"Scoring"`
  - `GameOptions { int MinCategoriesRanked; List<CategoryDefinition> Categories; List<AliasRule> Aliases; List<string> NotDrawable; static bool IsValid(GameOptions) }`, section `"Game"`
  - `CategoryDefinition { string Id; string Name; List<string> Sports; }`
  - `AliasRule { List<string> Sources; List<string> Targets; List<string> Categories; }` — every target inherits the best result of the sources, in the listed categories (empty list = all categories)
  - `record FeedRank(int EntryRank, int CountryRank, string Sport, string? Event, string Gender, string? Competitor)`
  - `record CategoryRank(FeedRank BestByEntry, FeedRank BestByCountry)`
  - `record BoardCategory(string Id, string Name)`, `record BoardCountry(string Iso3, string Iso2, string Name)`
  - `record BoardCell(int Score, int? CountryRank, int? EntryRank, bool Unranked, string? Sport, string? Event, string? Gender, string? Competitor)`
  - `record BoardContent(IReadOnlyList<BoardCategory> Categories, IReadOnlyList<BoardCountry> Countries, IReadOnlyList<IReadOnlyList<BoardCell>> Cells)` — `Cells[countryIndex][categoryIndex]`
  - `static BoardCell ScoringEngine.Score(CategoryRank? rank, RankMode mode, int cap)`

- [ ] **Step 1: Write the failing tests**

`tests/WorldRankGuesser.Api.Tests/Scoring/ScoringEngineTests.cs`:

```csharp
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;
using WorldRankGuesser.Api.Scoring;

namespace WorldRankGuesser.Api.Tests.Scoring;

public class ScoringEngineTests
{
    // Denmark: best player is world #3 in singles (2nd-best nation there),
    // and its doubles pair is world #5 but the best nation in doubles.
    private static readonly FeedRank Singles = new(EntryRank: 3, CountryRank: 2, "Badminton", "Singles", "Men", "Viktor Axelsen");
    private static readonly FeedRank Doubles = new(EntryRank: 5, CountryRank: 1, "Badminton", "Doubles", "Men", "Kim Astrup");
    private static readonly CategoryRank Denmark = new(BestByEntry: Singles, BestByCountry: Doubles);

    [Fact]
    public void Unranked_scores_the_cap()
    {
        var cell = ScoringEngine.Score(null, RankMode.Country, cap: 150);

        Assert.Equal(150, cell.Score);
        Assert.True(cell.Unranked);
        Assert.Null(cell.CountryRank);
        Assert.Null(cell.EntryRank);
        Assert.Null(cell.Sport);
    }

    [Fact]
    public void Entry_mode_scores_the_entry_rank_of_the_best_entry_feed()
    {
        var cell = ScoringEngine.Score(Denmark, RankMode.Entry, cap: 150);

        Assert.Equal(3, cell.Score);
        Assert.Equal(3, cell.EntryRank);
        Assert.Equal(2, cell.CountryRank);
        Assert.Equal("Singles", cell.Event);
        Assert.Equal("Viktor Axelsen", cell.Competitor);
        Assert.False(cell.Unranked);
    }

    [Fact]
    public void Country_mode_scores_the_country_rank_of_the_best_country_feed()
    {
        var cell = ScoringEngine.Score(Denmark, RankMode.Country, cap: 150);

        Assert.Equal(1, cell.Score);
        Assert.Equal(1, cell.CountryRank);
        Assert.Equal(5, cell.EntryRank);
        Assert.Equal("Doubles", cell.Event);
    }

    [Fact]
    public void A_rank_below_the_cap_scores_the_cap_but_stays_ranked()
    {
        var feed = new FeedRank(EntryRank: 180, CountryRank: 180, "Soccer", null, "Men", null);
        var cell = ScoringEngine.Score(new CategoryRank(feed, feed), RankMode.Country, cap: 150);

        Assert.Equal(150, cell.Score);
        Assert.False(cell.Unranked);
        Assert.Equal(180, cell.CountryRank);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: FAIL to compile — `WorldRankGuesser.Api.Configuration`, `.Rankings`, `.Scoring` do not exist.

- [ ] **Step 3: Write the options**

`src/WorldRankGuesser.Api/Configuration/ScoringOptions.cs`:

```csharp
namespace WorldRankGuesser.Api.Configuration;

public enum RankMode
{
    /// <summary>The federation's published position of the country's best entry (an athlete's world rank).</summary>
    Entry,

    /// <summary>The country's rank among countries in the feed.</summary>
    Country,
}

public sealed class ScoringOptions
{
    public const string Section = "Scoring";

    public RankMode RankMode { get; set; } = RankMode.Country;

    /// <summary>The highest score a pick can cost; also the score of an unranked country.</summary>
    public int Cap { get; set; } = 150;
}
```

`src/WorldRankGuesser.Api/Configuration/GameOptions.cs`:

```csharp
namespace WorldRankGuesser.Api.Configuration;

public sealed class GameOptions
{
    public const string Section = "Game";

    /// <summary>A country is drawable only if it is ranked in at least this many categories.</summary>
    public int MinCategoriesRanked { get; set; } = 1;

    public List<CategoryDefinition> Categories { get; set; } = [];

    public List<AliasRule> Aliases { get; set; } = [];

    /// <summary>Codes in the data that are never drawn (home nations, West Indies).</summary>
    public List<string> NotDrawable { get; set; } = [];

    public static bool IsValid(GameOptions options) =>
        options.MinCategoriesRanked >= 1
        && options.Categories.Count > 0
        && options.Categories.All(c => c.Id.Length > 0 && c.Name.Length > 0 && c.Sports.Count > 0)
        && options.Categories.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == options.Categories.Count;
}

public sealed class CategoryDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The scraper's Sport values this category covers.</summary>
    public List<string> Sports { get; set; } = [];
}

/// <summary>Every target inherits the best result of the sources, in the listed categories (empty = all).</summary>
public sealed class AliasRule
{
    public List<string> Sources { get; set; } = [];

    public List<string> Targets { get; set; } = [];

    public List<string> Categories { get; set; } = [];
}
```

- [ ] **Step 4: Write the records and the engine**

`src/WorldRankGuesser.Api/Rankings/FeedRank.cs`:

```csharp
namespace WorldRankGuesser.Api.Rankings;

/// <summary>A country's standing in one feed (one sport, event and gender).</summary>
public sealed record FeedRank(int EntryRank, int CountryRank, string Sport, string? Event, string Gender, string? Competitor);

/// <summary>A country's best feed in a category under each rank mode. The two may be different feeds.</summary>
public sealed record CategoryRank(FeedRank BestByEntry, FeedRank BestByCountry);
```

`src/WorldRankGuesser.Api/Boards/BoardContent.cs`:

```csharp
namespace WorldRankGuesser.Api.Boards;

public sealed record BoardCategory(string Id, string Name);

public sealed record BoardCountry(string Iso3, string Iso2, string Name);

/// <summary>One country in one category. Ranks and feed details are null when Unranked.</summary>
public sealed record BoardCell(
    int Score,
    int? CountryRank,
    int? EntryRank,
    bool Unranked,
    string? Sport,
    string? Event,
    string? Gender,
    string? Competitor);

/// <summary>Immutable. Cells[countryIndex][categoryIndex]; countries are in draw order.</summary>
public sealed record BoardContent(
    IReadOnlyList<BoardCategory> Categories,
    IReadOnlyList<BoardCountry> Countries,
    IReadOnlyList<IReadOnlyList<BoardCell>> Cells);
```

`src/WorldRankGuesser.Api/Scoring/ScoringEngine.cs`:

```csharp
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Scoring;

/// <summary>The scoring rule, and the only place it lives: score = min(rank in the mode, cap); unranked = cap.</summary>
public static class ScoringEngine
{
    public static BoardCell Score(CategoryRank? rank, RankMode mode, int cap)
    {
        if (rank is null)
        {
            return new BoardCell(cap, null, null, Unranked: true, null, null, null, null);
        }

        var feed = mode == RankMode.Entry ? rank.BestByEntry : rank.BestByCountry;
        var value = mode == RankMode.Entry ? feed.EntryRank : feed.CountryRank;

        return new BoardCell(
            Math.Min(value, cap),
            feed.CountryRank,
            feed.EntryRank,
            Unranked: false,
            feed.Sport,
            feed.Event,
            feed.Gender,
            feed.Competitor);
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: all pass.

- [ ] **Step 6: Add the configuration and bind it**

Replace `src/WorldRankGuesser.Api/appsettings.json` with:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Scoring": {
    "RankMode": "Country",
    "Cap": 150
  },
  "Game": {
    "MinCategoriesRanked": 1,
    "Categories": [
      { "Id": "soccer", "Name": "Soccer", "Sports": [ "Soccer" ] },
      { "Id": "basketball", "Name": "Basketball", "Sports": [ "Basketball" ] },
      { "Id": "cricket", "Name": "Cricket", "Sports": [ "Cricket" ] },
      { "Id": "rugby", "Name": "Rugby", "Sports": [ "Rugby" ] },
      { "Id": "volleyball", "Name": "Volleyball", "Sports": [ "Volleyball" ] },
      { "Id": "tennis", "Name": "Tennis", "Sports": [ "Tennis" ] },
      { "Id": "badminton", "Name": "Badminton", "Sports": [ "Badminton" ] },
      { "Id": "baseball", "Name": "Baseball", "Sports": [ "Baseball", "Softball", "Baseball5" ] },
      { "Id": "hockey", "Name": "Hockey", "Sports": [ "Field Hockey", "Ice Hockey" ] },
      { "Id": "gymnastics", "Name": "Gymnastics", "Sports": [ "Artistic Gymnastics", "Rhythmic Gymnastics" ] }
    ],
    "Aliases": [
      { "Sources": [ "ENG", "SCO", "WAL", "NIR" ], "Targets": [ "GBR" ], "Categories": [] },
      {
        "Sources": [ "WI" ],
        "Targets": [ "ATG", "BRB", "DMA", "GRD", "GUY", "JAM", "KNA", "LCA", "VCT", "TTO", "SXM", "AIA", "VGB", "MSR", "VIR" ],
        "Categories": [ "cricket" ]
      }
    ],
    "NotDrawable": [ "ENG", "SCO", "WAL", "NIR", "WI" ]
  }
}
```

In `src/WorldRankGuesser.Api/Program.cs`, add after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
builder.Services.AddOptions<ScoringOptions>()
    .Bind(builder.Configuration.GetSection(ScoringOptions.Section))
    .Validate(o => o.Cap > 0, "Scoring:Cap must be positive.")
    .ValidateOnStart();

builder.Services.AddOptions<GameOptions>()
    .Bind(builder.Configuration.GetSection(GameOptions.Section))
    .Validate(GameOptions.IsValid, "Game: needs at least one category, unique category IDs, and at least one sport per category.")
    .ValidateOnStart();
```

and at the top of the file:

```csharp
using WorldRankGuesser.Api.Configuration;
```

- [ ] **Step 7: Verify the app still starts with the real configuration**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: all pass, including `Healthz_returns_ok` (it boots the app, so `ValidateOnStart` has run against `appsettings.json`).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Add game and scoring options and the scoring engine"
```

---

### Task 4: Rankings snapshot builder

Turns the view's rows into the immutable read model: country rank per feed, best feed per category per mode, aliases, and the drawable pool.

**Files:**
- Create: `src/WorldRankGuesser.Api/Rankings/CountryRankingRow.cs`, `Rankings/RankingsSnapshot.cs`, `Rankings/RankingsSnapshotBuilder.cs`
- Test: `tests/WorldRankGuesser.Api.Tests/Rankings/TestData.cs`, `Rankings/RankingsSnapshotBuilderTests.cs`

**Interfaces:**
- Consumes: `GameOptions`, `CategoryDefinition`, `AliasRule` (Task 3); `CountryCatalog` (Task 2); `FeedRank`, `CategoryRank` (Task 3); `BoardCountry` (Task 3).
- Produces:
  - `CountryRankingRow` — properties `Sport`, `Event`, `Gender`, `RankingDate` (`DateOnly`), `IsFederationDate`, `Position` (`short`), `ISO3`, `TeamName`, `Competitor`, `Points` (`decimal?`), `RankedEntrants` (`int?`); all `init`.
  - `RankingsSnapshot` — `DateTimeOffset LoadedAt`, `IReadOnlyList<CategoryDefinition> Categories`, `IReadOnlyList<BoardCountry> DrawableCountries` (ordered by ISO3), `CategoryRank? Find(string categoryId, string iso3)`.
  - `static RankingsSnapshot RankingsSnapshotBuilder.Build(IReadOnlyList<CountryRankingRow> rows, GameOptions options, CountryCatalog catalog, DateTimeOffset loadedAt)` — throws `InvalidOperationException` naming every drawable ISO3 that has no ISO2.

- [ ] **Step 1: Write the test data helper**

`tests/WorldRankGuesser.Api.Tests/Rankings/TestData.cs`:

```csharp
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Rankings;

internal static class TestData
{
    public static readonly DateTimeOffset LoadedAt = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    public static readonly CountryCatalog Catalog = new(new Dictionary<string, string>
    {
        ["DNK"] = "DK", ["CHN"] = "CN", ["IDN"] = "ID", ["JPN"] = "JP",
        ["GBR"] = "GB", ["JAM"] = "JM", ["IND"] = "IN", ["AUS"] = "AU",
    });

    public static CountryRankingRow Row(string sport, string? ev, string gender, short position, string iso3, string? competitor = null) =>
        new()
        {
            Sport = sport,
            Event = ev,
            Gender = gender,
            RankingDate = new DateOnly(2026, 9, 14),
            IsFederationDate = true,
            Position = position,
            ISO3 = iso3,
            TeamName = Names.GetValueOrDefault(iso3, iso3),
            Competitor = competitor,
        };

    private static readonly Dictionary<string, string> Names = new()
    {
        ["DNK"] = "Denmark", ["CHN"] = "China", ["IDN"] = "Indonesia", ["JPN"] = "Japan",
        ["GBR"] = "United Kingdom", ["JAM"] = "Jamaica", ["IND"] = "India", ["AUS"] = "Australia",
        ["ENG"] = "England", ["SCO"] = "Scotland", ["WI"] = "West Indies", ["ZZZ"] = "Nowhere",
    };

    public static GameOptions Options(int minCategoriesRanked = 1) => new()
    {
        MinCategoriesRanked = minCategoriesRanked,
        Categories =
        [
            new() { Id = "soccer", Name = "Soccer", Sports = ["Soccer"] },
            new() { Id = "cricket", Name = "Cricket", Sports = ["Cricket"] },
            new() { Id = "badminton", Name = "Badminton", Sports = ["Badminton"] },
            new() { Id = "hockey", Name = "Hockey", Sports = ["Field Hockey", "Ice Hockey"] },
        ],
        Aliases =
        [
            new() { Sources = ["ENG", "SCO"], Targets = ["GBR"], Categories = [] },
            new() { Sources = ["WI"], Targets = ["JAM"], Categories = ["cricket"] },
        ],
        NotDrawable = ["ENG", "SCO", "WI"],
    };
}
```

- [ ] **Step 2: Write the failing tests**

`tests/WorldRankGuesser.Api.Tests/Rankings/RankingsSnapshotBuilderTests.cs`:

```csharp
using WorldRankGuesser.Api.Rankings;
using static WorldRankGuesser.Api.Tests.Rankings.TestData;

namespace WorldRankGuesser.Api.Tests.Rankings;

public class RankingsSnapshotBuilderTests
{
    private static RankingsSnapshot Build(IReadOnlyList<CountryRankingRow> rows, int minCategoriesRanked = 1) =>
        RankingsSnapshotBuilder.Build(rows, Options(minCategoriesRanked), Catalog, LoadedAt);

    [Fact]
    public void Country_rank_is_competition_ranking_of_countries_by_best_entry()
    {
        // The view has one row per country per feed: that country's best entry.
        var snapshot = Build(
        [
            Row("Badminton", "Singles", "Men", 1, "CHN"),
            Row("Badminton", "Singles", "Men", 3, "DNK"),
            Row("Badminton", "Singles", "Men", 3, "IDN"),
            Row("Badminton", "Singles", "Men", 9, "JPN"),
        ]);

        Assert.Equal(1, snapshot.Find("badminton", "CHN")!.BestByCountry.CountryRank);
        Assert.Equal(2, snapshot.Find("badminton", "DNK")!.BestByCountry.CountryRank);
        Assert.Equal(2, snapshot.Find("badminton", "IDN")!.BestByCountry.CountryRank);
        Assert.Equal(4, snapshot.Find("badminton", "JPN")!.BestByCountry.CountryRank);
        Assert.Equal(9, snapshot.Find("badminton", "JPN")!.BestByEntry.EntryRank);
    }

    [Fact]
    public void Best_feed_is_chosen_separately_for_each_mode()
    {
        var snapshot = Build(
        [
            Row("Badminton", "Singles", "Men", 1, "CHN"),
            Row("Badminton", "Singles", "Men", 3, "DNK", "Viktor Axelsen"),
            Row("Badminton", "Doubles", "Men", 5, "DNK", "Kim Astrup"),
            Row("Badminton", "Doubles", "Men", 8, "CHN"),
        ]);

        var denmark = snapshot.Find("badminton", "DNK")!;

        Assert.Equal("Singles", denmark.BestByEntry.Event);
        Assert.Equal(3, denmark.BestByEntry.EntryRank);
        Assert.Equal("Doubles", denmark.BestByCountry.Event);
        Assert.Equal(1, denmark.BestByCountry.CountryRank);
        Assert.Equal("Kim Astrup", denmark.BestByCountry.Competitor);
    }

    [Fact]
    public void A_category_spans_every_sport_it_lists()
    {
        var snapshot = Build(
        [
            Row("Field Hockey", "Outdoor", "Men", 4, "IND"),
            Row("Ice Hockey", null, "Men", 2, "JPN"),
        ]);

        Assert.Equal("Field Hockey", snapshot.Find("hockey", "IND")!.BestByEntry.Sport);
        Assert.Equal("Ice Hockey", snapshot.Find("hockey", "JPN")!.BestByEntry.Sport);
    }

    [Fact]
    public void Sports_outside_every_category_are_ignored()
    {
        var snapshot = Build([Row("Curling", null, "Men", 1, "JPN")]);

        Assert.Empty(snapshot.DrawableCountries);
    }

    [Fact]
    public void An_unranked_pair_is_null()
    {
        var snapshot = Build([Row("Soccer", null, "Men", 1, "JPN")]);

        Assert.Null(snapshot.Find("cricket", "JPN"));
        Assert.Null(snapshot.Find("soccer", "AUS"));
    }

    [Fact]
    public void The_united_kingdom_inherits_the_best_home_nation_in_every_category()
    {
        var snapshot = Build(
        [
            Row("Soccer", null, "Men", 4, "ENG"),
            Row("Soccer", null, "Men", 40, "SCO"),
            Row("Badminton", "Singles", "Men", 20, "GBR"),
        ]);

        Assert.Equal(4, snapshot.Find("soccer", "GBR")!.BestByEntry.EntryRank);
        Assert.Equal(20, snapshot.Find("badminton", "GBR")!.BestByEntry.EntryRank);
    }

    [Fact]
    public void West_indies_members_inherit_only_the_cricket_rank()
    {
        var snapshot = Build(
        [
            Row("Cricket", "ODI", "Men", 9, "WI"),
            Row("Soccer", null, "Men", 2, "WI"),      // not real data; proves the category filter
            Row("Soccer", null, "Men", 60, "JAM"),
        ]);

        Assert.Equal(9, snapshot.Find("cricket", "JAM")!.BestByEntry.EntryRank);
        Assert.Equal(60, snapshot.Find("soccer", "JAM")!.BestByEntry.EntryRank);
    }

    [Fact]
    public void Not_drawable_codes_are_left_out_of_the_pool()
    {
        var snapshot = Build(
        [
            Row("Soccer", null, "Men", 4, "ENG"),
            Row("Badminton", "Singles", "Men", 20, "GBR"),
            Row("Cricket", "ODI", "Men", 9, "WI"),
        ]);

        Assert.Equal(["GBR"], snapshot.DrawableCountries.Select(c => c.Iso3));
        Assert.Equal("GB", snapshot.DrawableCountries[0].Iso2);
        Assert.Equal("United Kingdom", snapshot.DrawableCountries[0].Name);
    }

    [Fact]
    public void The_pool_is_ordered_by_iso3_and_filtered_by_min_categories_ranked()
    {
        IReadOnlyList<CountryRankingRow> rows =
        [
            Row("Soccer", null, "Men", 1, "JPN"),
            Row("Cricket", "ODI", "Men", 1, "IND"),
            Row("Soccer", null, "Men", 2, "IND"),
            Row("Soccer", null, "Men", 3, "AUS"),
        ];

        Assert.Equal(["AUS", "IND", "JPN"], Build(rows).DrawableCountries.Select(c => c.Iso3));
        Assert.Equal(["IND"], Build(rows, minCategoriesRanked: 2).DrawableCountries.Select(c => c.Iso3));
    }

    [Fact]
    public void A_drawable_country_without_an_iso2_code_fails_loudly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build([Row("Soccer", null, "Men", 1, "ZZZ")]));

        Assert.Contains("ZZZ", error.Message);
    }

    [Fact]
    public void The_snapshot_carries_its_load_time_and_categories()
    {
        var snapshot = Build([]);

        Assert.Equal(LoadedAt, snapshot.LoadedAt);
        Assert.Equal(["soccer", "cricket", "badminton", "hockey"], snapshot.Categories.Select(c => c.Id));
    }
}
```

- [ ] **Step 3: Run to see them fail**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: FAIL to compile — `CountryRankingRow`, `RankingsSnapshot`, `RankingsSnapshotBuilder` do not exist.

- [ ] **Step 4: Write the row and the snapshot**

`src/WorldRankGuesser.Api/Rankings/CountryRankingRow.cs`:

```csharp
namespace WorldRankGuesser.Api.Rankings;

/// <summary>
/// One row of dbo.CurrentCountryRankings, the scraper's read model: a country's best-placed entry in one feed.
/// The view is owned by the SportsRankingService repo; this type is read-only.
/// </summary>
public sealed class CountryRankingRow
{
    public string Sport { get; init; } = "";

    public string? Event { get; init; }

    public string Gender { get; init; } = "";

    public DateOnly RankingDate { get; init; }

    public bool IsFederationDate { get; init; }

    public short Position { get; init; }

    public string ISO3 { get; init; } = "";

    public string? TeamName { get; init; }

    public string? Competitor { get; init; }

    public decimal? Points { get; init; }

    public int? RankedEntrants { get; init; }
}
```

`src/WorldRankGuesser.Api/Rankings/RankingsSnapshot.cs`:

```csharp
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>Immutable in-memory read model of the current rankings, after aliases.</summary>
public sealed class RankingsSnapshot(
    DateTimeOffset loadedAt,
    IReadOnlyList<CategoryDefinition> categories,
    IReadOnlyList<BoardCountry> drawableCountries,
    IReadOnlyDictionary<(string CategoryId, string Iso3), CategoryRank> ranks)
{
    public DateTimeOffset LoadedAt { get; } = loadedAt;

    public IReadOnlyList<CategoryDefinition> Categories { get; } = categories;

    /// <summary>Ordered by ISO3 so a seeded draw is repeatable.</summary>
    public IReadOnlyList<BoardCountry> DrawableCountries { get; } = drawableCountries;

    /// <summary>Null when the country is unranked in the category.</summary>
    public CategoryRank? Find(string categoryId, string iso3) => ranks.GetValueOrDefault((categoryId, iso3));
}
```

- [ ] **Step 5: Write the builder**

`src/WorldRankGuesser.Api/Rankings/RankingsSnapshotBuilder.cs`:

```csharp
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;

namespace WorldRankGuesser.Api.Rankings;

public static class RankingsSnapshotBuilder
{
    public static RankingsSnapshot Build(
        IReadOnlyList<CountryRankingRow> rows,
        GameOptions options,
        CountryCatalog catalog,
        DateTimeOffset loadedAt)
    {
        var categoryBySport = options.Categories
            .SelectMany(c => c.Sports.Select(sport => (sport, c.Id)))
            .ToDictionary(x => x.sport, x => x.Id, StringComparer.OrdinalIgnoreCase);

        var ranks = new Dictionary<(string CategoryId, string Iso3), CategoryRank>();

        // Feeds in a fixed order, so a tie between feeds always resolves the same way.
        var feeds = rows
            .GroupBy(r => (r.Sport, r.Event, r.Gender))
            .OrderBy(g => g.Key.Sport, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Event ?? "", StringComparer.Ordinal)
            .ThenBy(g => g.Key.Gender, StringComparer.Ordinal);

        foreach (var feed in feeds)
        {
            if (!categoryBySport.TryGetValue(feed.Key.Sport, out var categoryId)) continue;

            var ordered = feed.OrderBy(r => r.Position).ToList();
            var countryRank = 0;
            short? previousPosition = null;

            for (var i = 0; i < ordered.Count; i++)
            {
                var row = ordered[i];
                if (row.Position != previousPosition)
                {
                    countryRank = i + 1;          // competition ranking: 1, 2, 2, 4
                    previousPosition = row.Position;
                }

                var feedRank = new FeedRank(row.Position, countryRank, row.Sport, row.Event, row.Gender, row.Competitor);
                Merge(ranks, (categoryId, row.ISO3), new CategoryRank(feedRank, feedRank));
            }
        }

        ApplyAliases(ranks, options);

        return new RankingsSnapshot(loadedAt, options.Categories, DrawableCountries(rows, ranks, options, catalog), ranks);
    }

    private static void Merge(
        Dictionary<(string, string), CategoryRank> ranks, (string, string) key, CategoryRank candidate)
    {
        ranks[key] = ranks.TryGetValue(key, out var existing)
            ? new CategoryRank(
                candidate.BestByEntry.EntryRank < existing.BestByEntry.EntryRank ? candidate.BestByEntry : existing.BestByEntry,
                candidate.BestByCountry.CountryRank < existing.BestByCountry.CountryRank ? candidate.BestByCountry : existing.BestByCountry)
            : candidate;
    }

    private static void ApplyAliases(Dictionary<(string CategoryId, string Iso3), CategoryRank> ranks, GameOptions options)
    {
        // Read from the ranks as published, so one rule's result never feeds another rule.
        var published = new Dictionary<(string, string), CategoryRank>(ranks);

        foreach (var rule in options.Aliases)
        {
            var categoryIds = rule.Categories.Count == 0 ? options.Categories.Select(c => c.Id) : rule.Categories;

            foreach (var categoryId in categoryIds)
            foreach (var target in rule.Targets)
            foreach (var source in rule.Sources)
            {
                if (published.TryGetValue((categoryId, source), out var sourceRank))
                {
                    Merge(ranks, (categoryId, target), sourceRank);
                }
            }
        }
    }

    private static List<BoardCountry> DrawableCountries(
        IReadOnlyList<CountryRankingRow> rows,
        Dictionary<(string CategoryId, string Iso3), CategoryRank> ranks,
        GameOptions options,
        CountryCatalog catalog)
    {
        var notDrawable = options.NotDrawable.ToHashSet(StringComparer.Ordinal);

        // A country's display name is the scraper's TeamName; a code with no row of its own has no name and is not drawn.
        var names = rows
            .Where(r => r.TeamName is not null)
            .GroupBy(r => r.ISO3, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().TeamName!, StringComparer.Ordinal);

        var categoriesRanked = ranks.Keys
            .GroupBy(k => k.Iso3, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var countries = new List<BoardCountry>();
        var missingIso2 = new List<string>();

        foreach (var (iso3, name) in names.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            if (notDrawable.Contains(iso3)) continue;
            if (categoriesRanked.GetValueOrDefault(iso3) < options.MinCategoriesRanked) continue;

            if (catalog.TryGetIso2(iso3, out var iso2))
            {
                countries.Add(new BoardCountry(iso3, iso2, name));
            }
            else
            {
                missingIso2.Add(iso3);
            }
        }

        if (missingIso2.Count > 0)
        {
            throw new InvalidOperationException(
                $"No ISO2 code for: {string.Join(", ", missingIso2)}. Add them to Countries/countries.json or to Game:NotDrawable.");
        }

        return countries;
    }
}
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Build the rankings snapshot with country ranks, aliases and the drawable pool"
```

---

### Task 5: Optimal assignment and the board generator

**Files:**
- Create: `src/WorldRankGuesser.Api/Boards/OptimalAssignment.cs`, `Boards/BoardGenerator.cs`
- Test: `tests/WorldRankGuesser.Api.Tests/Boards/OptimalAssignmentTests.cs`, `Boards/BoardGeneratorTests.cs`

**Interfaces:**
- Consumes: `RankingsSnapshot`, `RankingsSnapshotBuilder.Build` (Task 4); `ScoringEngine.Score`, `ScoringOptions`, `BoardContent`, `BoardCell`, `BoardCategory`, `BoardCountry` (Task 3); `TestData` (Task 4).
- Produces:
  - `static int OptimalAssignment.MinTotal(IReadOnlyList<IReadOnlyList<int>> cost)` — square matrix, `cost[country][category]`; throws `ArgumentException` if not square or larger than 20.
  - `record GeneratedBoard(BoardContent Content, int OptimalScore)`
  - `static GeneratedBoard BoardGenerator.Generate(RankingsSnapshot snapshot, ScoringOptions scoring, Random random)` — draws as many countries as there are categories; throws `InvalidOperationException` when the pool is smaller than that.

- [ ] **Step 1: Write the failing tests**

`tests/WorldRankGuesser.Api.Tests/Boards/OptimalAssignmentTests.cs`:

```csharp
using WorldRankGuesser.Api.Boards;

namespace WorldRankGuesser.Api.Tests.Boards;

public class OptimalAssignmentTests
{
    [Fact]
    public void Picks_the_cheaper_diagonal()
    {
        int[][] cost = [[1, 100], [100, 1]];

        Assert.Equal(2, OptimalAssignment.MinTotal(cost));
    }

    [Fact]
    public void Greedy_is_not_optimal()
    {
        // Greedy takes 1 for the first country and is then forced into 100.
        int[][] cost = [[1, 2], [2, 100]];

        Assert.Equal(4, OptimalAssignment.MinTotal(cost));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Matches_brute_force_on_random_matrices(int seed)
    {
        var random = new Random(seed);
        var cost = Enumerable.Range(0, 5)
            .Select(_ => Enumerable.Range(0, 5).Select(_ => random.Next(1, 151)).ToArray())
            .ToArray();

        Assert.Equal(BruteForce(cost, 0, new bool[5]), OptimalAssignment.MinTotal(cost));
    }

    [Fact]
    public void Handles_a_ten_by_ten_board()
    {
        var cost = Enumerable.Range(0, 10)
            .Select(i => Enumerable.Range(0, 10).Select(j => i == j ? 1 : 150).ToArray())
            .ToArray();

        Assert.Equal(10, OptimalAssignment.MinTotal(cost));
    }

    [Fact]
    public void Rejects_a_matrix_that_is_not_square()
    {
        int[][] cost = [[1, 2, 3], [4, 5, 6]];

        Assert.Throws<ArgumentException>(() => OptimalAssignment.MinTotal(cost));
    }

    private static int BruteForce(int[][] cost, int country, bool[] used)
    {
        if (country == cost.Length) return 0;

        var best = int.MaxValue;
        for (var category = 0; category < cost.Length; category++)
        {
            if (used[category]) continue;
            used[category] = true;
            best = Math.Min(best, cost[country][category] + BruteForce(cost, country + 1, used));
            used[category] = false;
        }

        return best;
    }
}
```

`tests/WorldRankGuesser.Api.Tests/Boards/BoardGeneratorTests.cs`:

```csharp
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;
using WorldRankGuesser.Api.Scoring;
using static WorldRankGuesser.Api.Tests.Rankings.TestData;

namespace WorldRankGuesser.Api.Tests.Boards;

public class BoardGeneratorTests
{
    private static readonly ScoringOptions Scoring = new() { RankMode = RankMode.Country, Cap = 150 };

    // Four categories (TestData.Options), six ranked countries.
    private static readonly RankingsSnapshot Snapshot = RankingsSnapshotBuilder.Build(
    [
        Row("Soccer", null, "Men", 1, "JPN"),
        Row("Soccer", null, "Men", 2, "AUS"),
        Row("Soccer", null, "Men", 3, "IND"),
        Row("Cricket", "ODI", "Men", 1, "IND"),
        Row("Cricket", "ODI", "Men", 2, "AUS"),
        Row("Badminton", "Singles", "Men", 1, "CHN"),
        Row("Badminton", "Singles", "Men", 2, "DNK"),
        Row("Badminton", "Singles", "Men", 3, "IDN"),
        Row("Field Hockey", "Outdoor", "Men", 1, "IND"),
    ], Options(), Catalog, LoadedAt);

    [Fact]
    public void Draws_one_distinct_country_per_category()
    {
        var board = BoardGenerator.Generate(Snapshot, Scoring, new Random(1)).Content;

        Assert.Equal(["soccer", "cricket", "badminton", "hockey"], board.Categories.Select(c => c.Id));
        Assert.Equal(4, board.Countries.Count);
        Assert.Equal(4, board.Countries.Select(c => c.Iso3).Distinct().Count());
        Assert.Equal(4, board.Cells.Count);
        Assert.All(board.Cells, row => Assert.Equal(4, row.Count));
    }

    [Fact]
    public void The_same_seed_draws_the_same_board()
    {
        var first = BoardGenerator.Generate(Snapshot, Scoring, new Random(7)).Content;
        var second = BoardGenerator.Generate(Snapshot, Scoring, new Random(7)).Content;

        Assert.Equal(first.Countries, second.Countries);
    }

    [Fact]
    public void Different_seeds_draw_different_boards()
    {
        var draws = Enumerable.Range(0, 20)
            .Select(seed => string.Join(",", BoardGenerator.Generate(Snapshot, Scoring, new Random(seed)).Content.Countries.Select(c => c.Iso3)))
            .Distinct()
            .Count();

        Assert.True(draws > 1);
    }

    [Fact]
    public void Every_cell_is_the_scoring_engines_answer()
    {
        var board = BoardGenerator.Generate(Snapshot, Scoring, new Random(3)).Content;

        for (var country = 0; country < board.Countries.Count; country++)
        for (var category = 0; category < board.Categories.Count; category++)
        {
            var expected = ScoringEngine.Score(
                Snapshot.Find(board.Categories[category].Id, board.Countries[country].Iso3), Scoring.RankMode, Scoring.Cap);

            Assert.Equal(expected, board.Cells[country][category]);
        }
    }

    [Fact]
    public void The_optimal_score_is_the_minimum_assignment_of_the_grid()
    {
        var generated = BoardGenerator.Generate(Snapshot, Scoring, new Random(3));
        var cost = generated.Content.Cells.Select(row => row.Select(cell => cell.Score).ToArray()).ToArray();

        Assert.Equal(OptimalAssignment.MinTotal(cost), generated.OptimalScore);
    }

    [Fact]
    public void A_pool_smaller_than_the_category_count_is_an_error()
    {
        var tiny = RankingsSnapshotBuilder.Build([Row("Soccer", null, "Men", 1, "JPN")], Options(), Catalog, LoadedAt);

        var error = Assert.Throws<InvalidOperationException>(() => BoardGenerator.Generate(tiny, Scoring, new Random(1)));

        Assert.Contains("1", error.Message);
        Assert.Contains("4", error.Message);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: FAIL to compile — `OptimalAssignment` and `BoardGenerator` do not exist.

- [ ] **Step 3: Write the optimal assignment**

`src/WorldRankGuesser.Api/Boards/OptimalAssignment.cs`:

```csharp
using System.Numerics;

namespace WorldRankGuesser.Api.Boards;

/// <summary>The lowest total of assigning each country to a different category. Bitmask DP, O(n * 2^n).</summary>
public static class OptimalAssignment
{
    private const int MaxSize = 20;

    public static int MinTotal(IReadOnlyList<IReadOnlyList<int>> cost)
    {
        var n = cost.Count;
        if (n > MaxSize || cost.Any(row => row.Count != n))
        {
            throw new ArgumentException($"The cost matrix must be square and at most {MaxSize}x{MaxSize}.", nameof(cost));
        }

        // best[mask] = lowest total for the first popcount(mask) countries using exactly the categories in mask.
        var best = new int[1 << n];
        Array.Fill(best, int.MaxValue);
        best[0] = 0;

        for (var mask = 0; mask < best.Length; mask++)
        {
            if (best[mask] == int.MaxValue) continue;

            var country = BitOperations.PopCount((uint)mask);
            if (country == n) continue;

            for (var category = 0; category < n; category++)
            {
                if ((mask & (1 << category)) != 0) continue;

                var next = mask | (1 << category);
                best[next] = Math.Min(best[next], best[mask] + cost[country][category]);
            }
        }

        return best[^1];
    }
}
```

- [ ] **Step 4: Write the board generator**

`src/WorldRankGuesser.Api/Boards/BoardGenerator.cs`:

```csharp
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;
using WorldRankGuesser.Api.Scoring;

namespace WorldRankGuesser.Api.Boards;

public sealed record GeneratedBoard(BoardContent Content, int OptimalScore);

public static class BoardGenerator
{
    public static GeneratedBoard Generate(RankingsSnapshot snapshot, ScoringOptions scoring, Random random)
    {
        var categories = snapshot.Categories.Select(c => new BoardCategory(c.Id, c.Name)).ToList();
        var countries = Draw(snapshot.DrawableCountries, categories.Count, random);

        var cells = countries
            .Select(country => (IReadOnlyList<BoardCell>)categories
                .Select(category => ScoringEngine.Score(snapshot.Find(category.Id, country.Iso3), scoring.RankMode, scoring.Cap))
                .ToList())
            .ToList();

        var optimal = OptimalAssignment.MinTotal(cells.Select(row => row.Select(cell => cell.Score).ToList()).ToList());

        return new GeneratedBoard(new BoardContent(categories, countries, cells), optimal);
    }

    /// <summary>A uniform draw without replacement: the first <paramref name="count"/> steps of a Fisher-Yates shuffle.</summary>
    private static List<BoardCountry> Draw(IReadOnlyList<BoardCountry> pool, int count, Random random)
    {
        if (pool.Count < count)
        {
            throw new InvalidOperationException(
                $"Only {pool.Count} drawable countries for a board of {count}. Check the rankings data and Game:MinCategoriesRanked.");
        }

        var shuffled = pool.ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = random.Next(i, shuffled.Length);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled.Take(count).ToList();
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Generate boards with a uniform draw, scored grid and optimal total"
```

---

### Task 6: Persistence (the `game` schema)

The whole data model from spec section 7 is created now, including columns later phases use, so later phases add behaviour and not migrations.

**Prerequisite:** the SportsRankingService SQL container is running (`docker compose up -d --wait` in that repo) and Docker is available for Testcontainers.

**Files:**
- Create: `src/WorldRankGuesser.Api/Persistence/GameMode.cs`, `Player.cs`, `Board.cs`, `Game.cs`, `Pick.cs`, `GameDbContext.cs`, `Persistence/Migrations/*` (generated)
- Create: `.config/dotnet-tools.json` (generated)
- Modify: `src/WorldRankGuesser.Api/Program.cs`, `appsettings.Development.json`, `WorldRankGuesser.Api.csproj` (packages)
- Test: `tests/WorldRankGuesser.Api.Tests/Integration/SqlServerFixture.cs`, `RankingsSeed.cs`, `PersistenceTests.cs`

**Interfaces:**
- Consumes: `BoardContent` and friends, `RankMode` (Task 3); `CountryRankingRow` (Task 4).
- Produces:
  - Entities `Player`, `Board`, `Game`, `Pick` with the properties shown below; `enum GameMode { Practice, Daily }`.
  - `GameDbContext` with `Players`, `Boards`, `Games`, `Picks`, `DataProtectionKeys`, `CountryRankings`; `const string ConnectionStringName = "WorldRankGuesserConnection"`; `static void GameDbContext.Configure(DbContextOptionsBuilder options, string connectionString)`.
  - Test infrastructure: `SqlServerFixture` (`ConnectionString`, `EmptyConnectionString`, `CreateContext()`), xUnit collection `"sql"`, `RankingsSeed.Countries` (12 ISO3 codes), `RankingsSeed.PositionOf(int countryIndex, int feedIndex)`.

- [ ] **Step 1: Add packages and the EF tool**

```bash
dotnet add src/WorldRankGuesser.Api package Microsoft.EntityFrameworkCore.SqlServer --version 11.0.0-rc.1.26425.128
dotnet add src/WorldRankGuesser.Api package Microsoft.EntityFrameworkCore.Design --version 11.0.0-rc.1.26425.128
dotnet add src/WorldRankGuesser.Api package Microsoft.AspNetCore.DataProtection.EntityFrameworkCore --version 11.0.0-rc.1.26425.128
dotnet add tests/WorldRankGuesser.Api.Tests package Testcontainers.MsSql --version 4.15.0
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 11.0.0-rc.1.26425.128
```

- [ ] **Step 2: Write the entities**

`src/WorldRankGuesser.Api/Persistence/GameMode.cs`:

```csharp
namespace WorldRankGuesser.Api.Persistence;

public enum GameMode
{
    Practice,
    Daily,
}
```

`src/WorldRankGuesser.Api/Persistence/Player.cs`:

```csharp
namespace WorldRankGuesser.Api.Persistence;

public sealed class Player
{
    public Guid Id { get; set; }

    public string? Nickname { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Set by sign-in (phase 4). A player is verified when ExternalSubject is set.</summary>
    public string? ExternalProvider { get; set; }

    public string? ExternalSubject { get; set; }

    public int CurrentStreak { get; set; }

    public int BestStreak { get; set; }

    public DateOnly? LastDailyDate { get; set; }
}
```

`src/WorldRankGuesser.Api/Persistence/Board.cs`:

```csharp
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;

namespace WorldRankGuesser.Api.Persistence;

/// <summary>Immutable once saved. A daily board has a DailyDate; a practice board does not.</summary>
public sealed class Board
{
    public long Id { get; set; }

    public DateOnly? DailyDate { get; set; }

    public RankMode RankMode { get; set; }

    public int Cap { get; set; }

    public DateTimeOffset RankingsLoadedAt { get; set; }

    public int OptimalScore { get; set; }

    public BoardContent Content { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
```

`src/WorldRankGuesser.Api/Persistence/Game.cs`:

```csharp
namespace WorldRankGuesser.Api.Persistence;

public sealed class Game
{
    public Guid Id { get; set; }

    public Guid PlayerId { get; set; }

    public Player Player { get; set; } = null!;

    public long BoardId { get; set; }

    public Board Board { get; set; } = null!;

    public GameMode Mode { get; set; }

    /// <summary>Copied from the board so the one-attempt-per-day index and the leaderboard need no join.</summary>
    public DateOnly? DailyDate { get; set; }

    /// <summary>Index of the country the player is deciding on; equals the number of picks made.</summary>
    public int TurnIndex { get; set; }

    public DateTimeOffset? TurnDeadline { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int? TotalScore { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public List<Pick> Picks { get; set; } = [];
}
```

`src/WorldRankGuesser.Api/Persistence/Pick.cs`:

```csharp
namespace WorldRankGuesser.Api.Persistence;

public sealed class Pick
{
    public Guid GameId { get; set; }

    public int TurnIndex { get; set; }

    public string CategoryId { get; set; } = "";

    public string ISO3 { get; set; } = "";

    public int Score { get; set; }

    public bool WasLate { get; set; }

    public DateTimeOffset PickedAt { get; set; }
}
```

- [ ] **Step 3: Write the context**

`src/WorldRankGuesser.Api/Persistence/GameDbContext.cs`:

```csharp
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
        options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Schema));

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
```

- [ ] **Step 4: Register the context**

Replace `src/WorldRankGuesser.Api/appsettings.Development.json` with:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "WorldRankGuesserConnection": "Server=127.0.0.1,1433;Database=WorldRankGuesser;User Id=sa;Password=Rankings_Dev1!;TrustServerCertificate=True"
  }
}
```

(That is the dev-only credential of the SportsRankingService compose container; production overrides it with `ConnectionStrings__WorldRankGuesserConnection`.)

In `Program.cs`, add after the options registrations:

```csharp
// The connection string is read when the context is first resolved, so test hosts can override it.
builder.Services.AddDbContext<GameDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString(GameDbContext.ConnectionStringName)
        ?? throw new InvalidOperationException($"Connection string '{GameDbContext.ConnectionStringName}' is not configured.");

    GameDbContext.Configure(options, connectionString);
});
```

and the usings `using WorldRankGuesser.Api.Persistence;`.

- [ ] **Step 5: Create the migration and check it**

```bash
dotnet ef migrations add InitialGameSchema --project src/WorldRankGuesser.Api --output-dir Persistence/Migrations
```

Open the generated `*_InitialGameSchema.cs` and confirm:
- it creates `Players`, `Boards`, `Games`, `Picks`, `DataProtectionKeys`, all with `schema: "game"`;
- it contains **no** mention of `CurrentCountryRankings` or `dbo`;
- the three filtered unique indexes carry their `filter:` arguments.

If `CurrentCountryRankings` appears, the `ToView` mapping is wrong; fix it and regenerate (`dotnet ef migrations remove --project src/WorldRankGuesser.Api`, then add again).

- [ ] **Step 6: Apply it to the local database**

```bash
dotnet ef database update --project src/WorldRankGuesser.Api
```

Expected: `Done.` The scraper's `dbo` tables and views are untouched; a `game` schema now exists beside them.

- [ ] **Step 7: Write the test fixture and seed**

`tests/WorldRankGuesser.Api.Tests/Integration/RankingsSeed.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// A stand-in for the scraper's view: a table with the view's exact columns, filled with 12 countries ranked 1-12
/// in one feed of each of the 10 categories. Deterministic, so tests can predict every score.
/// </summary>
internal static class RankingsSeed
{
    public const string CreateTableSql = """
        CREATE TABLE dbo.CurrentCountryRankings (
            Sport nvarchar(100) NOT NULL,
            Event nvarchar(100) NULL,
            Gender nvarchar(20) NOT NULL,
            RankingDate date NOT NULL,
            IsFederationDate bit NOT NULL,
            Position smallint NOT NULL,
            ISO3 varchar(3) NOT NULL,
            TeamName nvarchar(200) NULL,
            Competitor nvarchar(200) NULL,
            Points decimal(12,3) NULL,
            RankedEntrants int NULL);
        """;

    public static readonly string[] Countries =
        ["AUS", "BRA", "CAN", "DEU", "ESP", "FRA", "IND", "ITA", "JPN", "NZL", "USA", "ZAF"];

    private static readonly string[] Names =
        ["Australia", "Brazil", "Canada", "Germany", "Spain", "France", "India", "Italy", "Japan", "New Zealand", "United States of America", "South Africa"];

    public static readonly (string Sport, string? Event, string Gender)[] Feeds =
    [
        ("Soccer", null, "Men"), ("Basketball", null, "Men"), ("Cricket", "ODI", "Men"), ("Rugby", "Union", "Men"),
        ("Volleyball", null, "Men"), ("Tennis", "Singles", "Men"), ("Badminton", "Singles", "Men"), ("Baseball", null, "Men"),
        ("Field Hockey", "Outdoor", "Men"), ("Artistic Gymnastics", "Vault", "Men"),
    ];

    /// <summary>Each feed is a rotation of the country list, so every country holds every position 1-12 somewhere.</summary>
    public static short PositionOf(int countryIndex, int feedIndex) => (short)((countryIndex + feedIndex * 5) % Countries.Length + 1);

    public static async Task InsertAsync(GameDbContext db)
    {
        for (var feed = 0; feed < Feeds.Length; feed++)
        for (var country = 0; country < Countries.Length; country++)
        {
            var (sport, ev, gender) = Feeds[feed];
            var position = PositionOf(country, feed);
            var iso3 = Countries[country];
            var name = Names[country];

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.CurrentCountryRankings
                    (Sport, Event, Gender, RankingDate, IsFederationDate, Position, ISO3, TeamName, Competitor, Points, RankedEntrants)
                VALUES ({sport}, {ev}, {gender}, '2026-09-14', 1, {position}, {iso3}, {name}, NULL, NULL, 1)
                """);
        }
    }
}
```

`tests/WorldRankGuesser.Api.Tests/Integration/SqlServerFixture.cs`:

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// One SQL Server container for the whole test run. Two databases: one migrated and seeded with rankings,
/// one migrated but with no rankings table (to test readiness failure). Tests share the seeded database,
/// so they must never assert on global row counts.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = "";

    public string EmptyConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = WithDatabase("WorldRankGuesserTests");
        EmptyConnectionString = WithDatabase("WorldRankGuesserEmpty");

        await using (var db = CreateContext(ConnectionString))
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync(RankingsSeed.CreateTableSql);
            await RankingsSeed.InsertAsync(db);
        }

        await using (var empty = CreateContext(EmptyConnectionString))
        {
            await empty.Database.MigrateAsync();
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public GameDbContext CreateContext() => CreateContext(ConnectionString);

    private static GameDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameDbContext>();
        GameDbContext.Configure(options, connectionString);
        return new GameDbContext(options.Options);
    }

    private string WithDatabase(string name) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name }.ConnectionString;
}

[CollectionDefinition("sql")]
public sealed class SqlCollection : ICollectionFixture<SqlServerFixture>;
```

If `new MsSqlBuilder()` produces an obsolete warning in this Testcontainers version, use the constructor the warning names (`new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")`) and drop `.WithImage(...)`.

- [ ] **Step 8: Write the persistence tests**

`tests/WorldRankGuesser.Api.Tests/Integration/PersistenceTests.cs`:

```csharp
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
```

- [ ] **Step 9: Run the tests**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: all pass. The first run pulls the SQL Server image and takes a few minutes; later runs take about 30 seconds for container start.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "Add the game schema, its first migration and SQL Server test infrastructure"
```

---

### Task 7: Loading the rankings, and readiness

**Files:**
- Create: `src/WorldRankGuesser.Api/Configuration/RankingsOptions.cs`
- Create: `src/WorldRankGuesser.Api/Rankings/IRankingsReader.cs`, `RankingsReader.cs`, `RankingsStore.cs`, `RankingsRefreshService.cs`
- Create: `src/WorldRankGuesser.Api/Endpoints/HealthEndpoints.cs`
- Modify: `src/WorldRankGuesser.Api/Program.cs`, `appsettings.json`
- Create: `tests/WorldRankGuesser.Api.Tests/Integration/ApiFactory.cs`, `ReadinessTests.cs`
- Delete: `tests/WorldRankGuesser.Api.Tests/HealthzTests.cs` (replaced by `ReadinessTests`, which does not need the developer's database)

**Interfaces:**
- Consumes: `GameDbContext.CountryRankings` (Task 6); `RankingsSnapshotBuilder.Build` (Task 4); `CountryCatalog` (Task 2); `GameOptions` (Task 3); `SqlServerFixture` (Task 6).
- Produces:
  - `interface IRankingsReader { Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct); }`
  - `interface IRankingsStore { RankingsSnapshot? Current { get; } void Set(RankingsSnapshot snapshot); }`
  - `RankingsRefreshService : BackgroundService` — loads once in `StartAsync` (so a started host has a snapshot or has logged why not), then every `Rankings:RefreshMinutes`. A failed refresh keeps the previous snapshot.
  - `GET /healthz` → 200 always. `GET /readyz` → 200 `{"status":"ready","rankingsLoadedAt":...}` when the database answers and a snapshot is loaded; otherwise 503.
  - `ApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)` — a `WebApplicationFactory<Program>` on the given database, with game-start rate limits effectively off unless `settings` overrides them.

- [ ] **Step 1: Write the test host and the failing tests**

`tests/WorldRankGuesser.Api.Tests/Integration/ApiFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Tests.Integration;

public sealed class ApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
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

`tests/WorldRankGuesser.Api.Tests/Integration/ReadinessTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Integration;

[Collection("sql")]
public class ReadinessTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Ready_once_the_rankings_are_loaded()
    {
        await using var factory = new ApiFactory(sql.ConnectionString);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/readyz")).StatusCode);

        var snapshot = factory.Services.GetRequiredService<IRankingsStore>().Current;
        Assert.NotNull(snapshot);
        Assert.Equal(RankingsSeed.Countries, snapshot.DrawableCountries.Select(c => c.Iso3));
        Assert.Equal(RankingsSeed.PositionOf(0, 0), snapshot.Find("soccer", "AUS")!.BestByEntry.EntryRank);
    }

    [Fact]
    public async Task Alive_but_not_ready_when_the_rankings_view_is_missing()
    {
        await using var factory = new ApiFactory(sql.EmptyConnectionString);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/readyz")).StatusCode);
    }
}
```

Delete `tests/WorldRankGuesser.Api.Tests/HealthzTests.cs`.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: FAIL to compile — `IRankingsStore` does not exist.

- [ ] **Step 3: Write the reader, the store and the refresh service**

`src/WorldRankGuesser.Api/Configuration/RankingsOptions.cs`:

```csharp
namespace WorldRankGuesser.Api.Configuration;

public sealed class RankingsOptions
{
    public const string Section = "Rankings";

    public int RefreshMinutes { get; set; } = 60;
}
```

`src/WorldRankGuesser.Api/Rankings/IRankingsReader.cs`:

```csharp
namespace WorldRankGuesser.Api.Rankings;

public interface IRankingsReader
{
    Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct);
}
```

`src/WorldRankGuesser.Api/Rankings/RankingsReader.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>The only code that knows the rankings come from a view. Selecting every mapped column also proves the view still has them.</summary>
public sealed class RankingsReader(GameDbContext db) : IRankingsReader
{
    public async Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct) =>
        await db.CountryRankings.AsNoTracking().ToListAsync(ct);
}
```

`src/WorldRankGuesser.Api/Rankings/RankingsStore.cs`:

```csharp
namespace WorldRankGuesser.Api.Rankings;

public interface IRankingsStore
{
    /// <summary>Null until the first successful load.</summary>
    RankingsSnapshot? Current { get; }

    void Set(RankingsSnapshot snapshot);
}

public sealed class RankingsStore : IRankingsStore
{
    private volatile RankingsSnapshot? _current;

    public RankingsSnapshot? Current => _current;

    public void Set(RankingsSnapshot snapshot) => _current = snapshot;
}
```

`src/WorldRankGuesser.Api/Rankings/RankingsRefreshService.cs`:

```csharp
using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;

namespace WorldRankGuesser.Api.Rankings;

public sealed class RankingsRefreshService(
    IServiceScopeFactory scopes,
    IRankingsStore store,
    IOptions<GameOptions> gameOptions,
    IOptions<RankingsOptions> rankingsOptions,
    CountryCatalog catalog,
    TimeProvider time,
    ILogger<RankingsRefreshService> logger) : BackgroundService
{
    /// <summary>Loads before the host finishes starting, so a started host either has rankings or has logged why not.</summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(rankingsOptions.Value.RefreshMinutes), time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IRankingsReader>().ReadAsync(ct);
            var snapshot = RankingsSnapshotBuilder.Build(rows, gameOptions.Value, catalog, time.GetUtcNow());

            store.Set(snapshot);
            logger.LogInformation(
                "Rankings loaded: {Rows} rows, {Countries} drawable countries.", rows.Count, snapshot.DrawableCountries.Count);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Keep serving the previous snapshot; /readyz reports not-ready only if there has never been one.
            logger.LogError(error, "Loading the rankings failed; keeping the previous snapshot.");
        }
    }
}
```

- [ ] **Step 4: Write the health endpoints**

`src/WorldRankGuesser.Api/Endpoints/HealthEndpoints.cs`:

```csharp
using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

        app.MapGet("/readyz", async (IRankingsStore store, GameDbContext db, CancellationToken ct) =>
        {
            var snapshot = store.Current;
            if (snapshot is null || !await db.Database.CanConnectAsync(ct))
            {
                return Results.Json(new { status = "not ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new { status = "ready", rankingsLoadedAt = snapshot.LoadedAt });
        }).ExcludeFromDescription();

        return app;
    }
}
```

- [ ] **Step 5: Wire it up**

Add to `appsettings.json`, after the `"Game"` section:

```json
  "Rankings": {
    "RefreshMinutes": 60
  }
```

In `Program.cs`: remove the inline `app.MapGet("/healthz", ...)` line, and add.

After the `AddDbContext` registration:

```csharp
builder.Services.AddOptions<RankingsOptions>()
    .Bind(builder.Configuration.GetSection(RankingsOptions.Section))
    .Validate(o => o.RefreshMinutes >= 1, "Rankings:RefreshMinutes must be at least 1.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(CountryCatalog.LoadEmbedded());
builder.Services.AddSingleton<IRankingsStore, RankingsStore>();
builder.Services.AddScoped<IRankingsReader, RankingsReader>();
builder.Services.AddHostedService<RankingsRefreshService>();
```

After `var app = builder.Build();`:

```csharp
app.MapHealthEndpoints();
```

Usings to add: `WorldRankGuesser.Api.Countries`, `WorldRankGuesser.Api.Endpoints`, `WorldRankGuesser.Api.Rankings`.

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: all pass. The not-ready test logs one error ("Loading the rankings failed") — that is the behaviour under test.

- [ ] **Step 7: Check against the real data**

```bash
dotnet run --project src/WorldRankGuesser.Api
```

In another terminal: `curl http://localhost:5170/readyz`
Expected: `{"status":"ready",...}` and a log line `Rankings loaded: <rows> rows, <countries> drawable countries.` with about 225 drawable countries (the 230 codes in the data on 2026-09-19 minus the five not-drawable ones; the numbers move with each scrape). If startup logs `No ISO2 code for: ...`, a federation added a code: add it to `countries.json` (or to `Game:NotDrawable`) and rerun. Stop the app with Ctrl+C.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Load the rankings into memory at startup and hourly, and report readiness"
```

---

### Task 8: Players and the game service

All game rules that touch state live here. HTTP comes in Task 9.

**Files:**
- Create: `src/WorldRankGuesser.Api/Players/PlayerService.cs`
- Create: `src/WorldRankGuesser.Api/Games/GameDtos.cs`, `GameStateMapper.cs`, `GameService.cs`
- Modify: `src/WorldRankGuesser.Api/Program.cs`
- Test: `tests/WorldRankGuesser.Api.Tests/Integration/GameServiceTests.cs`

**Interfaces:**
- Consumes: `GameDbContext`, entities (Task 6); `IRankingsStore` (Task 7); `BoardGenerator.Generate` (Task 5); `ScoringOptions` (Task 3); `ApiFactory`, `SqlServerFixture`, `RankingsSeed` (Tasks 6–7).
- Produces:
  - `PlayerService`: `Task<Player> CreateAsync(CancellationToken ct)`; `Task<bool> TouchAsync(Guid playerId, CancellationToken ct)` — updates `LastSeenAt`, returns false when the player does not exist.
  - DTO records (exact shapes below): `StartGameRequest`, `PickRequest`, `CategoryDto`, `CountryDto`, `CellDto`, `PickDto`, `GridDto`, `GameStateDto`.
  - `static GameStateDto GameStateMapper.ToDto(Game game, DateTimeOffset now)` — requires `game.Board` and `game.Picks` loaded. The only code that decides what a response reveals.
  - `GameService`: `Task<GameStateDto> StartPracticeAsync(Guid playerId, CancellationToken ct)` (throws `RankingsUnavailableException` when no snapshot is loaded); `Task<GameStateDto?> GetAsync(Guid playerId, Guid gameId, CancellationToken ct)`; `Task<PickResult> PickAsync(Guid playerId, Guid gameId, string categoryId, CancellationToken ct)`.
  - `PickResult` — closed hierarchy: `PickResult.Ok(GameStateDto State)`, `PickResult.Conflict(GameStateDto State)`, `PickResult.NotFound`, `PickResult.UnknownCategory`.

- [ ] **Step 1: Write the DTOs**

`src/WorldRankGuesser.Api/Games/GameDtos.cs`:

```csharp
namespace WorldRankGuesser.Api.Games;

public sealed record StartGameRequest(string? Mode);

public sealed record PickRequest(string? CategoryId);

public sealed record CategoryDto(string Id, string Name);

public sealed record CountryDto(string Iso3, string Iso2, string Name);

public sealed record CellDto(
    int Score,
    int? CountryRank,
    int? EntryRank,
    bool Unranked,
    string? Sport,
    string? Event,
    string? Gender,
    string? Competitor);

/// <summary>Score is what the pick cost (the cap when late); Result is what the board says for that country and category.</summary>
public sealed record PickDto(int TurnIndex, string CategoryId, CountryDto Country, int Score, bool WasLate, CellDto Result);

public sealed record GridDto(IReadOnlyList<CountryDto> Countries, IReadOnlyList<IReadOnlyList<CellDto>> Cells);

/// <summary>
/// CurrentCountry is the only country not yet picked that a response ever contains.
/// TotalScore, OptimalScore and Grid are null until IsComplete.
/// </summary>
public sealed record GameStateDto(
    Guid Id,
    string Mode,
    DateOnly? DailyDate,
    string RankMode,
    int Cap,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<PickDto> Picks,
    CountryDto? CurrentCountry,
    DateTimeOffset? Deadline,
    DateTimeOffset ServerNow,
    bool IsComplete,
    int? TotalScore,
    int? OptimalScore,
    GridDto? Grid);
```

- [ ] **Step 2: Write the failing tests**

`tests/WorldRankGuesser.Api.Tests/Integration/GameServiceTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run to see them fail**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: FAIL to compile — `PlayerService`, `GameService`, `PickResult` do not exist.

- [ ] **Step 4: Write the player service**

`src/WorldRankGuesser.Api/Players/PlayerService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Players;

public sealed class PlayerService(GameDbContext db, TimeProvider time)
{
    public async Task<Player> CreateAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var player = new Player { Id = Guid.NewGuid(), CreatedAt = now, LastSeenAt = now };

        db.Players.Add(player);
        await db.SaveChangesAsync(ct);

        return player;
    }

    /// <summary>False when the player no longer exists (for example a cookie that outlived a database reset).</summary>
    public async Task<bool> TouchAsync(Guid playerId, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        return await db.Players
            .Where(p => p.Id == playerId)
            .ExecuteUpdateAsync(update => update.SetProperty(p => p.LastSeenAt, now), ct) == 1;
    }
}
```

- [ ] **Step 5: Write the mapper**

`src/WorldRankGuesser.Api/Games/GameStateMapper.cs`:

```csharp
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Games;

/// <summary>
/// The only code that decides what a response reveals: past picks, the current country, and nothing else
/// until the game is complete.
/// </summary>
public static class GameStateMapper
{
    public static GameStateDto ToDto(Game game, DateTimeOffset now)
    {
        var content = game.Board.Content;
        var complete = game.CompletedAt is not null;

        var picks = game.Picks
            .OrderBy(p => p.TurnIndex)
            .Select(p =>
            {
                var categoryIndex = IndexOfCategory(content, p.CategoryId);
                return new PickDto(
                    p.TurnIndex,
                    p.CategoryId,
                    ToDto(content.Countries[p.TurnIndex]),
                    p.Score,
                    p.WasLate,
                    ToDto(content.Cells[p.TurnIndex][categoryIndex]));
            })
            .ToList();

        return new GameStateDto(
            game.Id,
            game.Mode.ToString(),
            game.DailyDate,
            game.Board.RankMode.ToString(),
            game.Board.Cap,
            content.Categories.Select(c => new CategoryDto(c.Id, c.Name)).ToList(),
            picks,
            complete ? null : ToDto(content.Countries[game.TurnIndex]),
            complete ? null : game.TurnDeadline,
            now,
            complete,
            game.TotalScore,
            complete ? game.Board.OptimalScore : null,
            complete
                ? new GridDto(
                    content.Countries.Select(ToDto).ToList(),
                    content.Cells.Select(row => (IReadOnlyList<CellDto>)row.Select(ToDto).ToList()).ToList())
                : null);
    }

    public static int IndexOfCategory(BoardContent content, string categoryId)
    {
        for (var i = 0; i < content.Categories.Count; i++)
        {
            if (content.Categories[i].Id == categoryId) return i;
        }

        return -1;
    }

    private static CountryDto ToDto(BoardCountry c) => new(c.Iso3, c.Iso2, c.Name);

    private static CellDto ToDto(BoardCell c) =>
        new(c.Score, c.CountryRank, c.EntryRank, c.Unranked, c.Sport, c.Event, c.Gender, c.Competitor);
}
```

- [ ] **Step 6: Write the game service**

`src/WorldRankGuesser.Api/Games/GameService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Games;

public sealed class RankingsUnavailableException() : Exception("The rankings have not been loaded yet.");

public abstract record PickResult
{
    private PickResult() { }

    public sealed record Ok(GameStateDto State) : PickResult;

    /// <summary>The category is used, the game is complete, or a simultaneous pick won. State is the truth to resync to.</summary>
    public sealed record Conflict(GameStateDto State) : PickResult;

    /// <summary>No such game for this player. Never distinguishes "not yours" from "does not exist".</summary>
    public sealed record NotFound : PickResult;

    public sealed record UnknownCategory : PickResult;
}

public sealed class GameService(
    GameDbContext db,
    IRankingsStore rankings,
    IOptions<ScoringOptions> scoring,
    Random random,
    TimeProvider time)
{
    public async Task<GameStateDto> StartPracticeAsync(Guid playerId, CancellationToken ct)
    {
        var snapshot = rankings.Current ?? throw new RankingsUnavailableException();
        var generated = BoardGenerator.Generate(snapshot, scoring.Value, random);
        var now = time.GetUtcNow();

        var game = new Game
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Mode = GameMode.Practice,
            StartedAt = now,
            Board = new Board
            {
                RankMode = scoring.Value.RankMode,
                Cap = scoring.Value.Cap,
                RankingsLoadedAt = snapshot.LoadedAt,
                OptimalScore = generated.OptimalScore,
                Content = generated.Content,
                CreatedAt = now,
            },
        };

        db.Games.Add(game);
        await db.SaveChangesAsync(ct);

        return GameStateMapper.ToDto(game, now);
    }

    public async Task<GameStateDto?> GetAsync(Guid playerId, Guid gameId, CancellationToken ct)
    {
        var game = await LoadAsync(playerId, gameId, tracking: false, ct);

        return game is null ? null : GameStateMapper.ToDto(game, time.GetUtcNow());
    }

    public async Task<PickResult> PickAsync(Guid playerId, Guid gameId, string categoryId, CancellationToken ct)
    {
        var game = await LoadAsync(playerId, gameId, tracking: true, ct);
        if (game is null) return new PickResult.NotFound();

        var content = game.Board.Content;
        var categoryIndex = GameStateMapper.IndexOfCategory(content, categoryId);
        if (categoryIndex < 0) return new PickResult.UnknownCategory();

        var now = time.GetUtcNow();
        if (game.CompletedAt is not null || game.Picks.Any(p => p.CategoryId == categoryId))
        {
            return new PickResult.Conflict(GameStateMapper.ToDto(game, now));
        }

        // The server, not the request, decides which country this pick is for.
        var turn = game.TurnIndex;
        game.Picks.Add(new Pick
        {
            TurnIndex = turn,
            CategoryId = categoryId,
            ISO3 = content.Countries[turn].Iso3,
            Score = content.Cells[turn][categoryIndex].Score,
            WasLate = false,
            PickedAt = now,
        });

        game.TurnIndex = turn + 1;
        if (game.TurnIndex == content.Countries.Count)
        {
            game.CompletedAt = now;
            game.TotalScore = game.Picks.Sum(p => p.Score);
        }

        try
        {
            // One SaveChanges is one transaction: the pick insert and the row-versioned game update succeed or fail together.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A simultaneous pick won: the row version moved, or a unique index (turn, category) fired.
            db.ChangeTracker.Clear();
            var current = await LoadAsync(playerId, gameId, tracking: false, ct);

            return current is null ? new PickResult.NotFound() : new PickResult.Conflict(GameStateMapper.ToDto(current, now));
        }

        return new PickResult.Ok(GameStateMapper.ToDto(game, now));
    }

    private Task<Game?> LoadAsync(Guid playerId, Guid gameId, bool tracking, CancellationToken ct)
    {
        var games = db.Games.Include(g => g.Board).Include(g => g.Picks).AsQueryable();
        if (!tracking) games = games.AsNoTracking();

        return games.SingleOrDefaultAsync(g => g.Id == gameId && g.PlayerId == playerId, ct);
    }
}
```

- [ ] **Step 7: Register the services**

In `Program.cs`, after `AddHostedService<RankingsRefreshService>()`:

```csharp
builder.Services.AddSingleton(Random.Shared);
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<GameService>();
```

Usings to add: `WorldRankGuesser.Api.Games`, `WorldRankGuesser.Api.Players`.

- [ ] **Step 8: Run the tests to see them pass**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: all pass. Run the two "simultaneous" tests five times to be sure they are not flaky:

```bash
for i in 1 2 3 4 5; do dotnet test WorldRankGuesser.slnx --no-build --filter "FullyQualifiedName~imultaneous" || break; done
```

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "Add players and the server-authoritative practice game service"
```

---

### Task 9: Identity, HTTP endpoints and rate limiting

**Files:**
- Create: `src/WorldRankGuesser.Api/Configuration/RateLimitOptions.cs`
- Create: `src/WorldRankGuesser.Api/Players/PlayerIdentity.cs`
- Create: `src/WorldRankGuesser.Api/Endpoints/GameEndpoints.cs`
- Modify: `src/WorldRankGuesser.Api/Program.cs` (full file given), `appsettings.json`
- Test: `tests/WorldRankGuesser.Api.Tests/Integration/GameApiTests.cs`, `AntiCheatTests.cs`

**Interfaces:**
- Consumes: `GameService`, `PickResult`, `PlayerService`, DTOs (Task 8); `IRankingsStore` (Task 7); `ApiFactory` (Task 7).
- Produces:
  - `POST /api/games` body `{"mode":"practice"}` → 200 `GameStateDto`; sets cookie `wrg_player` when the caller has none. 400 for any other mode. 503 when rankings are not loaded. 429 over the limit.
  - `GET /api/games/{id}` → 200 `GameStateDto` | 404.
  - `POST /api/games/{id}/picks` body `{"categoryId":"soccer"}` → 200 `GameStateDto` | 400 | 404 | 409 with the current `GameStateDto` as the body.
  - `GET|POST /api/<anything else>` → 404 (never the front end's `index.html`).
  - `RateLimitOptions { int GameStartsPerPlayerPerHour = 30; int GameStartsPerIpPerHour = 120; }`, section `"RateLimits"`.
  - `PlayerIdentity.GetPlayerId(this ClaimsPrincipal)` → `Guid?`; `PlayerIdentity.EnsurePlayerAsync(HttpContext, PlayerService, CancellationToken)` → `Task<Guid>`.

- [ ] **Step 1: Write the failing API tests**

`tests/WorldRankGuesser.Api.Tests/Integration/GameApiTests.cs`:

```csharp
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
    public async Task Starting_a_game_before_the_rankings_load_returns_503_and_creates_no_player()
    {
        await using var notReady = new ApiFactory(sql.EmptyConnectionString);

        var response = await notReady.CreateClient().PostAsJsonAsync("/api/games", new StartGameRequest("practice"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }
}
```

Note on the rate-limit test: the first request has no cookie, so it is counted in the caller's anonymous partition; requests two to four carry the cookie and are counted in the player's partition, whose limit of 2 blocks the fourth request overall (the third with a cookie).

`tests/WorldRankGuesser.Api.Tests/Integration/AntiCheatTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Games;

namespace WorldRankGuesser.Api.Tests.Integration;

[Collection("sql")]
public class AntiCheatTests(SqlServerFixture sql) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(sql.ConnectionString);
        return Task.CompletedTask;
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
        Assert.Equal(pick.Result.Score, pick.Score);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: the new tests FAIL with 404 (no `/api/games` route yet).

- [ ] **Step 3: Write the options and the identity helper**

`src/WorldRankGuesser.Api/Configuration/RateLimitOptions.cs`:

```csharp
namespace WorldRankGuesser.Api.Configuration;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    public int GameStartsPerPlayerPerHour { get; set; } = 30;

    public int GameStartsPerIpPerHour { get; set; } = 120;
}
```

`src/WorldRankGuesser.Api/Players/PlayerIdentity.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace WorldRankGuesser.Api.Players;

public static class PlayerIdentity
{
    public const string CookieName = "wrg_player";

    private const string PlayerIdClaim = "player_id";

    public static Guid? GetPlayerId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(PlayerIdClaim), out var id) ? id : null;

    /// <summary>The caller's player, created and signed in on the spot when the cookie is missing or stale.</summary>
    public static async Task<Guid> EnsurePlayerAsync(HttpContext http, PlayerService players, CancellationToken ct)
    {
        if (http.User.GetPlayerId() is { } existing && await players.TouchAsync(existing, ct))
        {
            return existing;
        }

        var player = await players.CreateAsync(ct);
        var identity = new ClaimsIdentity(
            [new Claim(PlayerIdClaim, player.Id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme);

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return player.Id;
    }
}
```

- [ ] **Step 4: Write the endpoints**

`src/WorldRankGuesser.Api/Endpoints/GameEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using WorldRankGuesser.Api.Games;
using WorldRankGuesser.Api.Players;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Endpoints;

public static class GameEndpoints
{
    public const string StartGameRoute = "/api/games";

    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(StartGameRoute, StartGame).WithName("StartGame");
        app.MapGet("/api/games/{id:guid}", GetGame).WithName("GetGame");
        app.MapPost("/api/games/{id:guid}/picks", Pick).WithName("Pick");

        // Anything else under /api is a 404, never the front end's index.html.
        app.Map("/api/{**rest}", () => Results.NotFound()).ExcludeFromDescription();

        return app;
    }

    private static async Task<Results<Ok<GameStateDto>, ValidationProblem, ProblemHttpResult>> StartGame(
        StartGameRequest request,
        HttpContext http,
        IRankingsStore rankings,
        PlayerService players,
        GameService games,
        CancellationToken ct)
    {
        if (request.Mode != "practice")
        {
            var message = request.Mode == "daily" ? "The daily challenge is not available yet." : "mode must be \"practice\".";
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = [message] });
        }

        // Checked before a player is created, so a not-ready API leaves no orphan players behind.
        if (rankings.Current is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The rankings are not loaded yet.");
        }

        var playerId = await PlayerIdentity.EnsurePlayerAsync(http, players, ct);

        return TypedResults.Ok(await games.StartPracticeAsync(playerId, ct));
    }

    private static async Task<Results<Ok<GameStateDto>, NotFound>> GetGame(
        Guid id, HttpContext http, GameService games, CancellationToken ct)
    {
        if (http.User.GetPlayerId() is not { } playerId) return TypedResults.NotFound();

        var state = await games.GetAsync(playerId, id, ct);

        return state is null ? TypedResults.NotFound() : TypedResults.Ok(state);
    }

    private static async Task<Results<Ok<GameStateDto>, NotFound, Conflict<GameStateDto>, ValidationProblem>> Pick(
        Guid id, PickRequest request, HttpContext http, GameService games, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CategoryId)) return UnknownCategory();
        if (http.User.GetPlayerId() is not { } playerId) return TypedResults.NotFound();

        return await games.PickAsync(playerId, id, request.CategoryId, ct) switch
        {
            PickResult.Ok ok => TypedResults.Ok(ok.State),
            PickResult.Conflict conflict => TypedResults.Conflict(conflict.State),
            PickResult.UnknownCategory => UnknownCategory(),
            _ => TypedResults.NotFound(),
        };

        static ValidationProblem UnknownCategory() =>
            TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["categoryId"] = ["Unknown category."] });
    }
}
```

- [ ] **Step 5: Replace `Program.cs` with the full composition root**

`src/WorldRankGuesser.Api/Program.cs`:

```csharp
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;
using WorldRankGuesser.Api.Endpoints;
using WorldRankGuesser.Api.Games;
using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Players;
using WorldRankGuesser.Api.Rankings;

var builder = WebApplication.CreateBuilder(args);

// ---- Options -------------------------------------------------------------------------------------------------------
builder.Services.AddOptions<ScoringOptions>()
    .Bind(builder.Configuration.GetSection(ScoringOptions.Section))
    .Validate(o => o.Cap > 0, "Scoring:Cap must be positive.")
    .ValidateOnStart();

builder.Services.AddOptions<GameOptions>()
    .Bind(builder.Configuration.GetSection(GameOptions.Section))
    .Validate(GameOptions.IsValid, "Game: needs at least one category, unique category IDs, and at least one sport per category.")
    .ValidateOnStart();

builder.Services.AddOptions<RankingsOptions>()
    .Bind(builder.Configuration.GetSection(RankingsOptions.Section))
    .Validate(o => o.RefreshMinutes >= 1, "Rankings:RefreshMinutes must be at least 1.")
    .ValidateOnStart();

builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.Section))
    .Validate(o => o.GameStartsPerPlayerPerHour > 0 && o.GameStartsPerIpPerHour > 0, "RateLimits: limits must be positive.")
    .ValidateOnStart();

// ---- Persistence ---------------------------------------------------------------------------------------------------
// The connection string is read when the context is first resolved, so test hosts can override it.
builder.Services.AddDbContext<GameDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString(GameDbContext.ConnectionStringName)
        ?? throw new InvalidOperationException($"Connection string '{GameDbContext.ConnectionStringName}' is not configured.");

    GameDbContext.Configure(options, connectionString);
});

// ---- Rankings and game ---------------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Random.Shared);
builder.Services.AddSingleton(CountryCatalog.LoadEmbedded());
builder.Services.AddSingleton<IRankingsStore, RankingsStore>();
builder.Services.AddScoped<IRankingsReader, RankingsReader>();
builder.Services.AddHostedService<RankingsRefreshService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<GameService>();

// ---- Identity: an anonymous player in an HttpOnly cookie -----------------------------------------------------------
// Keys live in the database so cookies survive restarts and scale-to-zero.
builder.Services.AddDataProtection()
    .SetApplicationName("WorldRankGuesser")
    .PersistKeysToDbContext<GameDbContext>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = PlayerIdentity.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(365);
        options.SlidingExpiration = true;

        // An API never redirects to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });

// ---- Rate limiting: game starts only, per player and per IP --------------------------------------------------------
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static bool IsGameStart(HttpContext http) =>
        HttpMethods.IsPost(http.Request.Method) && http.Request.Path.Equals(GameEndpoints.StartGameRoute, StringComparison.OrdinalIgnoreCase);

    static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    static RateLimitPartition<string> HourlyLimit(HttpContext http, string key, Func<RateLimitOptions, int> limit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            // Read at first use, so configuration overrides (tests, environment) are honoured.
            PermitLimit = limit(http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value),
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
        });

    limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(http => IsGameStart(http)
            ? HourlyLimit(http, $"player:{http.User.GetPlayerId()?.ToString() ?? "anonymous:" + Ip(http)}", o => o.GameStartsPerPlayerPerHour)
            : RateLimitPartition.GetNoLimiter("none")),
        PartitionedRateLimiter.Create<HttpContext, string>(http => IsGameStart(http)
            ? HourlyLimit(http, $"ip:{Ip(http)}", o => o.GameStartsPerIpPerHour)
            : RateLimitPartition.GetNoLimiter("none")));
});

// Unhandled errors and bare status codes become RFC 9457 problem details, never HTML or stack traces.
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();      // before the limiter, so the player partition can see the cookie
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapGameEndpoints();

app.Run();

public partial class Program;
```

Add to `appsettings.json`, after the `"Rankings"` section:

```json
  "RateLimits": {
    "GameStartsPerPlayerPerHour": 30,
    "GameStartsPerIpPerHour": 120
  }
```

Cross-site request forgery note for the reviewer: the cookie is `SameSite=Lax` and both state-changing routes only bind JSON bodies, which a cross-site form cannot send without a CORS preflight that this API never grants. No anti-forgery token is needed.

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test WorldRankGuesser.slnx`
Expected: all pass.

- [ ] **Step 7: Play a game by hand against the real data**

```bash
dotnet run --project src/WorldRankGuesser.Api
```

In another terminal:

```bash
curl -s -c cookies.txt -X POST http://localhost:5170/api/games -H "Content-Type: application/json" -d '{"mode":"practice"}'
```

Expected: JSON with ten `categories`, `"picks":[]`, one `currentCountry`, `"grid":null`. Copy the `id`, then:

```bash
curl -s -b cookies.txt -X POST http://localhost:5170/api/games/<id>/picks -H "Content-Type: application/json" -d '{"categoryId":"soccer"}'
```

Expected: one pick with a `score`, a `result` naming the sport and (for individual sports) a competitor, and a new `currentCountry`. Delete `cookies.txt` afterwards and stop the app.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Add the anonymous player cookie, game endpoints and game-start rate limits"
```

---

### Task 10: OpenAPI document, SvelteKit scaffold and the typed client

**Files:**
- Modify: `src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj`, `Program.cs`
- Create: `src/WorldRankGuesser.Api/wwwroot/.gitkeep`
- Create: `src/WorldRankGuesser.Web/` (scaffold), then overwrite `svelte.config.js`, `vite.config.ts`, `package.json` scripts
- Create: `src/WorldRankGuesser.Web/src/routes/+layout.ts`, `src/lib/api/client.ts`
- Generated and committed: `src/WorldRankGuesser.Web/openapi/WorldRankGuesser.Api.json`, `src/lib/api/schema.d.ts`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: the three game routes and `GameStateDto` (Tasks 8–9).
- Produces:
  - The API build writes `src/WorldRankGuesser.Web/openapi/WorldRankGuesser.Api.json`. `npm run gen:api` turns it into `src/lib/api/schema.d.ts`.
  - The API serves `wwwroot` and falls back to `index.html` for non-API routes (empty until phase 2's Dockerfile copies the build in).
  - `src/lib/api/client.ts`: types `GameState`, `Pick`, `Category`, `Country`, `Cell`; `class ApiError { status: number }`; `startGame(): Promise<GameState>`; `getGame(id: string): Promise<GameState | null>` (null on 404); `pick(id: string, categoryId: string): Promise<{ state: GameState; conflict: boolean }>`.
  - npm scripts: `dev`, `build`, `check`, `test` (Vitest, single run), `test:e2e`, `gen:api`.

- [ ] **Step 1: Generate the OpenAPI document at build time**

```bash
dotnet add src/WorldRankGuesser.Api package Microsoft.AspNetCore.OpenApi --version 11.0.0-rc.1.26425.128
dotnet add src/WorldRankGuesser.Api package Microsoft.Extensions.ApiDescription.Server --version 11.0.0-rc.1.26425.128
mkdir -p src/WorldRankGuesser.Api/wwwroot && touch src/WorldRankGuesser.Api/wwwroot/.gitkeep
```

Add to `src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj`, inside `<Project>`:

```xml
  <PropertyGroup>
    <OpenApiGenerateDocuments>true</OpenApiGenerateDocuments>
    <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/../WorldRankGuesser.Web/openapi</OpenApiDocumentsDirectory>
  </PropertyGroup>
```

In `Program.cs`:

1. Add usings `using System.Reflection;` and `using System.Text.Json.Serialization;`.

2. Directly after `var builder = WebApplication.CreateBuilder(args);` add:

```csharp
// The build-time OpenAPI generator runs this entry point; it must not try to reach a database.
var isOpenApiBuild = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.Services.AddOpenApi();

// Strict numbers: the web default also accepts "12" for 12, which makes every integer `number | string` in the generated types.
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
```

3. Replace `builder.Services.AddHostedService<RankingsRefreshService>();` with:

```csharp
if (!isOpenApiBuild)
{
    builder.Services.AddHostedService<RankingsRefreshService>();
}
```

4. Replace the block from `app.UseAuthentication();` to `app.MapGameEndpoints();` with:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();      // before the limiter, so the player partition can see the cookie
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapGameEndpoints();

// The single-page app's client-side routes (/play/..., /results/...) all load index.html.
app.MapFallbackToFile("index.html");
```

Run: `dotnet build WorldRankGuesser.slnx && dotnet test WorldRankGuesser.slnx --no-build`
Expected: build succeeds, all tests pass, and `src/WorldRankGuesser.Web/openapi/WorldRankGuesser.Api.json` exists. Open it and confirm: paths `/api/games`, `/api/games/{id}` and `/api/games/{id}/picks`; a `GameStateDto` schema; `score` has `"type": "integer"` (not a `["integer","string"]` pair); no `/healthz`.

- [ ] **Step 2: Scaffold SvelteKit**

```bash
npx sv create src/WorldRankGuesser.Web --template minimal --types ts --add vitest="usages:unit" playwright sveltekit-adapter="adapter:static" --install npm --no-download-check --no-dir-check
cd src/WorldRankGuesser.Web
npm install flag-icons
npm install -D openapi-typescript
npx playwright install chromium
```

(`--no-dir-check` because `openapi/` already exists in the folder.) If this version of `sv` rejects the `--add` option syntax, run the same command without `--add ...` and install the three pieces directly: `npm install -D vitest @playwright/test @sveltejs/adapter-static`. Step 3 overwrites every configuration file the add-ons would have written, so the result is the same. Delete the scaffold's demo tests if present: `rm -f src/demo.spec.ts src/routes/page.svelte.spec.ts e2e/demo.test.ts`.

- [ ] **Step 3: Overwrite the configuration**

`src/WorldRankGuesser.Web/svelte.config.js`:

```js
import adapter from '@sveltejs/adapter-static';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	preprocess: vitePreprocess(),
	kit: {
		// A single-page app: every route falls back to index.html, which the API serves.
		adapter: adapter({ fallback: 'index.html' })
	}
};

export default config;
```

`src/WorldRankGuesser.Web/vite.config.ts`:

```ts
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
	plugins: [sveltekit()],
	server: {
		// Same origin in development too, so the player cookie behaves as it does in production.
		proxy: { '/api': 'http://localhost:5170' }
	},
	test: {
		include: ['src/**/*.test.ts'],
		environment: 'node'
	}
});
```

`src/WorldRankGuesser.Web/src/routes/+layout.ts`:

```ts
// A static single-page app: nothing is rendered on a server, everything comes from /api.
export const ssr = false;
export const prerender = false;
```

In `src/WorldRankGuesser.Web/package.json`, set the `scripts` block to exactly:

```json
	"scripts": {
		"dev": "vite dev",
		"build": "vite build",
		"preview": "vite preview",
		"prepare": "svelte-kit sync || echo ''",
		"check": "svelte-kit sync && svelte-check --tsconfig ./tsconfig.json",
		"test": "vitest run",
		"test:e2e": "playwright test",
		"gen:api": "openapi-typescript openapi/WorldRankGuesser.Api.json -o src/lib/api/schema.d.ts"
	},
```

- [ ] **Step 4: Generate the types and write the client**

```bash
npm run gen:api
```

Expected: `src/lib/api/schema.d.ts` exists and contains `GameStateDto` with `totalScore?: number | null` style members.

`src/WorldRankGuesser.Web/src/lib/api/client.ts`:

```ts
import type { components } from './schema';

export type GameState = components['schemas']['GameStateDto'];
export type Pick = components['schemas']['PickDto'];
export type Category = components['schemas']['CategoryDto'];
export type Country = components['schemas']['CountryDto'];
export type Cell = components['schemas']['CellDto'];

export class ApiError extends Error {
	readonly status: number;

	constructor(status: number, message: string) {
		super(message);
		this.status = status;
	}
}

function send(path: string, init?: RequestInit): Promise<Response> {
	return fetch(path, {
		credentials: 'same-origin',
		headers: { 'Content-Type': 'application/json' },
		...init
	});
}

export async function startGame(): Promise<GameState> {
	const response = await send('/api/games', { method: 'POST', body: JSON.stringify({ mode: 'practice' }) });
	if (!response.ok) throw new ApiError(response.status, 'Could not start a game.');

	return response.json();
}

/** Null when the game does not exist or is not this player's. */
export async function getGame(id: string): Promise<GameState | null> {
	const response = await send(`/api/games/${id}`);
	if (response.status === 404) return null;
	if (!response.ok) throw new ApiError(response.status, 'Could not load the game.');

	return response.json();
}

/** A 409 is not an error: its body is the server's current state, which the caller adopts. */
export async function pick(id: string, categoryId: string): Promise<{ state: GameState; conflict: boolean }> {
	const response = await send(`/api/games/${id}/picks`, { method: 'POST', body: JSON.stringify({ categoryId }) });
	if (response.ok || response.status === 409) {
		return { state: await response.json(), conflict: response.status === 409 };
	}

	throw new ApiError(response.status, 'The pick was rejected.');
}
```

- [ ] **Step 5: Verify**

```bash
npm run check
npm run build
```

Expected: `svelte-check found 0 errors`; the build writes `build/index.html`.

- [ ] **Step 6: Extend CI**

Replace `.github/workflows/ci.yml` with:

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
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
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
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
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
```

- [ ] **Step 7: Commit**

```bash
cd ../..
git add -A
git commit -m "Generate the OpenAPI document, scaffold the SvelteKit app and add the typed client"
```

---

### Task 11: Front-end game logic (store, spin sequence, pick description)

Pure logic with Vitest tests. No screens yet.

**Files:**
- Generated: `src/WorldRankGuesser.Web/src/lib/countries/iso2.json`
- Create: `src/WorldRankGuesser.Web/src/lib/game/spin.ts`, `describe.ts`, `gameStore.svelte.ts`
- Test: `src/lib/game/spin.test.ts`, `describe.test.ts`, `gameStore.test.ts`, `testState.ts`

**Interfaces:**
- Consumes: `client.ts` (Task 10); the generator (Task 2).
- Produces:
  - `buildSpinSequence(decoys: readonly string[], finalIso2: string, length: number, random?: () => number): string[]`
  - `describePick(pick: Pick, rankMode: string): string`
  - `type GameClient = { startGame; getGame; pick }` (the three client functions) and `class GameStore` with reactive `state: GameState | null`, `busy: boolean`, `error: string | null`; getters `usedCategoryIds: Set<string>`, `pickFor(categoryId): Pick | undefined`; methods `start(): Promise<string | null>` (the new game's ID), `load(id): Promise<boolean>`, `pick(categoryId): Promise<void>`.

- [ ] **Step 1: Generate the decoy list**

From the repo root:

```bash
mkdir -p src/WorldRankGuesser.Web/src/lib/countries
dotnet run tools/GenerateCountryCatalog/generate.cs -- src/WorldRankGuesser.Api/Countries/countries.json src/WorldRankGuesser.Web/src/lib/countries/iso2.json
git diff --stat -- src/WorldRankGuesser.Api/Countries/countries.json
```

Expected: `iso2.json` is a JSON array of about 245 two-letter codes; `countries.json` is unchanged (no diff). The list has no ranks in it: it only feeds the spin animation.

- [ ] **Step 2: Write the failing tests**

`src/WorldRankGuesser.Web/src/lib/game/testState.ts`:

```ts
import type { GameState, Pick } from '$lib/api/client';

export function gameState(overrides: Partial<GameState> = {}): GameState {
	return {
		id: '11111111-1111-1111-1111-111111111111',
		mode: 'Practice',
		dailyDate: null,
		rankMode: 'Country',
		cap: 150,
		categories: [
			{ id: 'soccer', name: 'Soccer' },
			{ id: 'cricket', name: 'Cricket' }
		],
		picks: [],
		currentCountry: { iso3: 'JPN', iso2: 'JP', name: 'Japan' },
		deadline: null,
		serverNow: '2026-09-19T12:00:00Z',
		isComplete: false,
		totalScore: null,
		optimalScore: null,
		grid: null,
		...overrides
	};
}

export function pickOf(categoryId: string, overrides: Partial<Pick['result']> = {}, score = 3): Pick {
	return {
		turnIndex: 0,
		categoryId,
		country: { iso3: 'JPN', iso2: 'JP', name: 'Japan' },
		score,
		wasLate: false,
		result: {
			score,
			countryRank: 3,
			entryRank: 3,
			unranked: false,
			sport: 'Soccer',
			event: null,
			gender: 'Men',
			competitor: null,
			...overrides
		}
	};
}
```

`src/WorldRankGuesser.Web/src/lib/game/spin.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { buildSpinSequence } from './spin';

const decoys = ['AU', 'BR', 'CA', 'DE', 'JP'];

function seeded(seed: number): () => number {
	return () => {
		seed = (seed * 16807) % 2147483647;
		return seed / 2147483647;
	};
}

describe('buildSpinSequence', () => {
	it('has the requested length and lands on the final country', () => {
		const sequence = buildSpinSequence(decoys, 'JP', 12, seeded(1));

		expect(sequence).toHaveLength(12);
		expect(sequence.at(-1)).toBe('JP');
	});

	it('never shows the final country early or the same flag twice in a row', () => {
		for (let seed = 1; seed <= 20; seed++) {
			const sequence = buildSpinSequence(decoys, 'JP', 30, seeded(seed));

			expect(sequence.slice(0, -1)).not.toContain('JP');
			sequence.forEach((code, i) => expect(code).not.toBe(sequence[i - 1]));
		}
	});

	it('is just the final country when there are too few decoys to spin', () => {
		expect(buildSpinSequence(['JP', 'AU'], 'JP', 12)).toEqual(['JP']);
	});
});
```

`src/WorldRankGuesser.Web/src/lib/game/describe.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { describePick } from './describe';
import { pickOf } from './testState';

describe('describePick', () => {
	it('describes a team ranking', () => {
		expect(describePick(pickOf('soccer'), 'Country')).toBe('#3 · Soccer Men');
	});

	it('names the athlete and their world rank in country mode', () => {
		const pick = pickOf(
			'badminton',
			{ countryRank: 2, entryRank: 3, sport: 'Badminton', event: 'Singles', competitor: 'Viktor Axelsen' },
			2
		);

		expect(describePick(pick, 'Country')).toBe('#2 — Viktor Axelsen, world #3 · Badminton Singles Men');
	});

	it('leads with the world rank in entry mode', () => {
		const pick = pickOf(
			'badminton',
			{ countryRank: 2, entryRank: 3, sport: 'Badminton', event: 'Singles', competitor: 'Viktor Axelsen' },
			3
		);

		expect(describePick(pick, 'Entry')).toBe('#3 — Viktor Axelsen, nation #2 · Badminton Singles Men');
	});

	it('says when the score was capped', () => {
		const pick = pickOf('soccer', { countryRank: 180, entryRank: 180 }, 150);

		expect(describePick(pick, 'Country')).toBe('#180 (scores 150) · Soccer Men');
	});

	it('says unranked', () => {
		const pick = pickOf('cricket', { unranked: true, countryRank: null, entryRank: null, sport: null, gender: null }, 150);

		expect(describePick(pick, 'Country')).toBe('Unranked');
	});
});
```

`src/WorldRankGuesser.Web/src/lib/game/gameStore.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest';
import { GameStore, type GameClient } from './gameStore.svelte';
import { gameState, pickOf } from './testState';

function client(overrides: Partial<GameClient> = {}): GameClient {
	return {
		startGame: vi.fn(async () => gameState()),
		getGame: vi.fn(async () => gameState()),
		pick: vi.fn(async () => ({ state: gameState({ picks: [pickOf('soccer')] }), conflict: false })),
		...overrides
	};
}

describe('GameStore', () => {
	it('start adopts the new game and returns its id', async () => {
		const store = new GameStore(client());

		expect(await store.start()).toBe(gameState().id);
		expect(store.state?.currentCountry?.iso3).toBe('JPN');
		expect(store.busy).toBe(false);
	});

	it('start reports a failure instead of throwing', async () => {
		const store = new GameStore(client({ startGame: vi.fn(async () => Promise.reject(new Error('503'))) }));

		expect(await store.start()).toBeNull();
		expect(store.error).not.toBeNull();
	});

	it('load says whether the game exists', async () => {
		expect(await new GameStore(client()).load('x')).toBe(true);
		expect(await new GameStore(client({ getGame: vi.fn(async () => null) })).load('x')).toBe(false);
	});

	it('pick replaces the state with the server state', async () => {
		const api = client();
		const store = new GameStore(api);
		await store.start();

		await store.pick('soccer');

		expect(api.pick).toHaveBeenCalledWith(gameState().id, 'soccer');
		expect(store.usedCategoryIds.has('soccer')).toBe(true);
		expect(store.pickFor('soccer')?.score).toBe(3);
		expect(store.pickFor('cricket')).toBeUndefined();
	});

	it('does not send a pick for a used category', async () => {
		const api = client({ getGame: vi.fn(async () => gameState({ picks: [pickOf('soccer')] })) });
		const store = new GameStore(api);
		await store.load('x');

		await store.pick('soccer');

		expect(api.pick).not.toHaveBeenCalled();
	});

	it('adopts the server state on a conflict', async () => {
		const serverState = gameState({ picks: [pickOf('cricket')] });
		const store = new GameStore(client({ pick: vi.fn(async () => ({ state: serverState, conflict: true })) }));
		await store.start();

		await store.pick('soccer');

		expect(store.usedCategoryIds.has('cricket')).toBe(true);
		expect(store.error).toBeNull();
	});

	it('reloads from the server when a pick fails', async () => {
		const reloaded = gameState({ picks: [pickOf('soccer')] });
		const api = client({
			pick: vi.fn(async () => Promise.reject(new Error('network'))),
			getGame: vi.fn(async () => reloaded)
		});
		const store = new GameStore(api);
		await store.start();

		await store.pick('soccer');

		expect(api.getGame).toHaveBeenCalledWith(gameState().id);
		expect(store.state).toEqual(reloaded);
		expect(store.error).not.toBeNull();
		expect(store.busy).toBe(false);
	});
});
```

- [ ] **Step 3: Run to see them fail**

Run (in `src/WorldRankGuesser.Web`): `npm test`
Expected: FAIL — cannot resolve `./spin`, `./describe`, `./gameStore.svelte`.

- [ ] **Step 4: Write the three modules**

`src/WorldRankGuesser.Web/src/lib/game/spin.ts`:

```ts
/**
 * The flags the spinner shows before landing: `length - 1` decoys, never the final country,
 * never the same flag twice in a row, then the final country.
 */
export function buildSpinSequence(
	decoys: readonly string[],
	finalIso2: string,
	length: number,
	random: () => number = Math.random
): string[] {
	const pool = decoys.filter((code) => code !== finalIso2);
	if (pool.length < 2) return [finalIso2];

	const sequence: string[] = [];
	while (sequence.length < length - 1) {
		const candidate = pool[Math.floor(random() * pool.length)];
		if (candidate !== sequence.at(-1)) sequence.push(candidate);
	}

	sequence.push(finalIso2);
	return sequence;
}
```

`src/WorldRankGuesser.Web/src/lib/game/describe.ts`:

```ts
import type { Pick } from '$lib/api/client';

/** One line under a filled category card, for example "#2 — Viktor Axelsen, world #3 · Badminton Singles Men". */
export function describePick(pick: Pick, rankMode: string): string {
	const result = pick.result;
	if (result.unranked) return 'Unranked';

	const entryMode = rankMode === 'Entry';
	const lead = entryMode ? result.entryRank : result.countryRank;
	const other = entryMode ? `nation #${result.countryRank}` : `world #${result.entryRank}`;

	const who = result.competitor ? ` — ${result.competitor}, ${other}` : '';
	const capped = lead != null && lead > pick.score ? ` (scores ${pick.score})` : '';
	const feed = [result.sport, result.event, result.gender].filter(Boolean).join(' ');

	return `#${lead}${who}${capped} · ${feed}`;
}
```

`src/WorldRankGuesser.Web/src/lib/game/gameStore.svelte.ts`:

```ts
import * as api from '$lib/api/client';
import type { GameState, Pick } from '$lib/api/client';

export type GameClient = {
	startGame: typeof api.startGame;
	getGame: typeof api.getGame;
	pick: typeof api.pick;
};

/**
 * Holds the latest state the server sent, and nothing else. There are no game rules here:
 * every change is a server response replacing `state`.
 */
export class GameStore {
	state = $state<GameState | null>(null);
	busy = $state(false);
	error = $state<string | null>(null);

	readonly #client: GameClient;

	constructor(client: GameClient = api) {
		this.#client = client;
	}

	get usedCategoryIds(): Set<string> {
		return new Set(this.state?.picks.map((p) => p.categoryId) ?? []);
	}

	pickFor(categoryId: string): Pick | undefined {
		return this.state?.picks.find((p) => p.categoryId === categoryId);
	}

	/** The new game's id, or null (with `error` set) when it could not be started. */
	async start(): Promise<string | null> {
		this.busy = true;
		this.error = null;
		try {
			this.state = await this.#client.startGame();
			return this.state.id;
		} catch {
			this.error = 'Could not start a game. Please try again in a moment.';
			return null;
		} finally {
			this.busy = false;
		}
	}

	/** False when the game does not exist for this player. */
	async load(id: string): Promise<boolean> {
		this.busy = true;
		this.error = null;
		try {
			const loaded = await this.#client.getGame(id);
			if (loaded) this.state = loaded;
			return loaded !== null;
		} catch {
			this.error = 'Could not load the game.';
			return false;
		} finally {
			this.busy = false;
		}
	}

	async pick(categoryId: string): Promise<void> {
		const current = this.state;
		if (!current || this.busy || current.isComplete || this.usedCategoryIds.has(categoryId)) return;

		this.busy = true;
		this.error = null;
		try {
			// A conflict is not an error: the body is the server's truth, and we adopt it.
			this.state = (await this.#client.pick(current.id, categoryId)).state;
		} catch {
			this.error = 'That pick did not go through, so the game was reloaded.';
			try {
				this.state = (await this.#client.getGame(current.id)) ?? current;
			} catch {
				// Still offline: keep what we have; the next action retries.
			}
		} finally {
			this.busy = false;
		}
	}
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `npm test && npm run check`
Expected: all Vitest tests pass; `svelte-check found 0 errors`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add the front-end game store, spin sequence and pick description"
```

---

### Task 12: Screens (home, play, results) and the end-to-end test

Functional, phone-first, deliberately plain: visual design is phase 5.

**Prerequisite for the end-to-end test:** the SportsRankingService SQL container is running with data, and the local database is migrated (Task 6, Step 6). This test is local-only until phase 2 gives CI a seeded container.

**Files:**
- Create: `src/WorldRankGuesser.Web/src/app.css`
- Create: `src/lib/components/Flag.svelte`, `CountrySpinner.svelte`, `CategoryCard.svelte`
- Create/replace: `src/routes/+layout.svelte`, `src/routes/+page.svelte`, `src/routes/play/[gameId]/+page.svelte`, `src/routes/results/[gameId]/+page.svelte`
- Replace: `playwright.config.ts`
- Test: `e2e/practice-game.test.ts`

**Interfaces:**
- Consumes: `GameStore` (Task 11), `describePick`, `buildSpinSequence`, `iso2.json`, client types.
- Produces (relied on by the end-to-end test): a button named `Practice game` on `/`; category buttons with class `category`; filled cards with class `filled`; elements with `data-testid="total"` and `data-testid="optimal"` on the results page.

- [ ] **Step 1: Write the failing end-to-end test**

Replace `src/WorldRankGuesser.Web/playwright.config.ts` with:

```ts
import { defineConfig } from '@playwright/test';

export default defineConfig({
	testDir: 'e2e',
	use: { baseURL: 'http://localhost:5173' },
	webServer: [
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

`src/WorldRankGuesser.Web/e2e/practice-game.test.ts`:

```ts
import { expect, test } from '@playwright/test';

test('a full practice game ends on the results page', async ({ page }) => {
	await page.emulateMedia({ reducedMotion: 'reduce' });     // no spin, so the test does not wait on animation
	await page.goto('/');

	await page.getByRole('button', { name: 'Practice game' }).click();
	await expect(page).toHaveURL(/\/play\/[0-9a-f-]{36}$/);

	const categories = 10;
	await expect(page.locator('.category-card')).toHaveCount(categories);     // waits for the game to load

	for (let turn = 0; turn < categories; turn++) {
		await page.locator('button.category:enabled').first().click();
		await expect(page.locator('.category-card.filled')).toHaveCount(turn + 1);
	}

	await expect(page).toHaveURL(/\/results\//);
	await expect(page.getByTestId('total')).toHaveText(/^\d+$/);
	await expect(page.getByTestId('optimal')).toHaveText(/^\d+$/);
});

test('a refresh in the middle of a game resumes it', async ({ page }) => {
	await page.emulateMedia({ reducedMotion: 'reduce' });
	await page.goto('/');
	await page.getByRole('button', { name: 'Practice game' }).click();
	await page.locator('button.category:enabled').first().click();
	await expect(page.locator('.category-card.filled')).toHaveCount(1);

	await page.reload();

	await expect(page.locator('.category-card.filled')).toHaveCount(1);
	await expect(page.locator('button.category:enabled')).toHaveCount(9);
});
```

- [ ] **Step 2: Run to see it fail**

Run (in `src/WorldRankGuesser.Web`): `npm run test:e2e`
Expected: FAIL — no `Practice game` button on the scaffold's home page.

- [ ] **Step 3: Write the styles and the layout**

`src/WorldRankGuesser.Web/src/app.css`:

```css
:root {
	--bg: #ffffff;
	--fg: #1b1f24;
	--muted: #5c6670;
	--line: #d5dae0;
	--accent: #1f6feb;
	--error: #b42318;
	font-family: system-ui, sans-serif;
	color: var(--fg);
	background: var(--bg);
}

@media (prefers-color-scheme: dark) {
	:root {
		--bg: #0f1317;
		--fg: #e7ebef;
		--muted: #9aa5b1;
		--line: #2b333b;
		--accent: #6ea8fe;
		--error: #ff8a80;
	}
}

body {
	margin: 0;
}

main {
	max-width: 40rem;
	margin: 0 auto;
	padding: 1rem;
}

button {
	font: inherit;
	color: inherit;
	background: transparent;
	border: 1px solid var(--line);
	border-radius: 0.5rem;
	padding: 0.75rem 1rem;
	cursor: pointer;
}

button:disabled {
	opacity: 0.5;
	cursor: default;
}

button.primary {
	background: var(--accent);
	border-color: var(--accent);
	color: #ffffff;
}

.error {
	color: var(--error);
}

.muted {
	color: var(--muted);
}
```

`src/WorldRankGuesser.Web/src/routes/+layout.svelte`:

```svelte
<script lang="ts">
	import 'flag-icons/css/flag-icons.min.css';
	import '../app.css';

	let { children } = $props();
</script>

<main>
	<header><a href="/">World Rank Guesser</a></header>
	{@render children()}
</main>

<style>
	header {
		padding-bottom: 1rem;
		font-weight: 600;
	}

	header a {
		color: inherit;
		text-decoration: none;
	}
</style>
```

- [ ] **Step 4: Write the components**

`src/WorldRankGuesser.Web/src/lib/components/Flag.svelte`:

```svelte
<script lang="ts">
	// SVG flags, because Windows browsers do not render flag emoji.
	let { iso2, size = '1em', label }: { iso2: string; size?: string; label?: string } = $props();
</script>

<span class="fi fi-{iso2.toLowerCase()}" style:font-size={size} role="img" aria-label={label ?? iso2}></span>
```

`src/WorldRankGuesser.Web/src/lib/components/CountrySpinner.svelte`:

```svelte
<script lang="ts">
	import { untrack } from 'svelte';
	import type { Country } from '$lib/api/client';
	import iso2Codes from '$lib/countries/iso2.json';
	import { buildSpinSequence } from '$lib/game/spin';
	import Flag from './Flag.svelte';

	// While `pending` (a pick is in flight) the flags cycle; when the next country arrives they land on it.
	// The spin therefore hides the network round trip.
	let { country, pending, onsettled }: { country: Country; pending: boolean; onsettled: () => void } = $props();

	let shown = $state(untrack(() => country.iso2));
	let settled = $state(false);

	$effect(() => {
		const target = country.iso2;
		const waiting = pending;
		settled = false;

		if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
			if (!waiting) {
				shown = target;
				settled = true;
				untrack(onsettled);
			}
			return;
		}

		const landing = waiting ? null : buildSpinSequence(iso2Codes, target, 14);
		let index = 0;

		const timer = setInterval(() => {
			if (landing === null) {
				shown = iso2Codes[Math.floor(Math.random() * iso2Codes.length)];
				return;
			}

			shown = landing[index++];
			if (index === landing.length) {
				clearInterval(timer);
				settled = true;
				onsettled();
			}
		}, 60);

		return () => clearInterval(timer);
	});
</script>

<div class="spinner">
	<Flag iso2={shown} size="6rem" label={settled ? country.name : 'Drawing a country'} />
	<p class="name" class:hidden={!settled}>{country.name}</p>
</div>

<style>
	.spinner {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.5rem;
		padding: 1rem 0;
	}

	.name {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.hidden {
		visibility: hidden;
	}
</style>
```

`src/WorldRankGuesser.Web/src/lib/components/CategoryCard.svelte`:

```svelte
<script lang="ts">
	import type { Category, Pick } from '$lib/api/client';
	import { describePick } from '$lib/game/describe';
	import Flag from './Flag.svelte';

	let {
		category,
		pick,
		rankMode,
		disabled,
		onpick
	}: { category: Category; pick: Pick | undefined; rankMode: string; disabled: boolean; onpick: () => void } = $props();
</script>

<div class="category-card" class:filled={pick !== undefined}>
	{#if pick}
		<div class="result">
			<Flag iso2={pick.country.iso2} size="2rem" label={pick.country.name} />
			<div>
				<div class="title">{category.name}: <strong>{pick.score}</strong></div>
				<div class="muted detail">{pick.country.name} · {describePick(pick, rankMode)}</div>
			</div>
		</div>
	{:else}
		<button class="category" {disabled} onclick={onpick}>{category.name}</button>
	{/if}
</div>

<style>
	.category-card {
		min-height: 4rem;
		display: flex;
		align-items: stretch;
	}

	button {
		width: 100%;
	}

	.result {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		width: 100%;
		padding: 0.5rem 0.75rem;
		border: 1px solid var(--line);
		border-radius: 0.5rem;
	}

	.detail {
		font-size: 0.85rem;
	}
</style>
```

- [ ] **Step 5: Write the pages**

`src/WorldRankGuesser.Web/src/routes/+page.svelte`:

```svelte
<script lang="ts">
	import { goto } from '$app/navigation';
	import { GameStore } from '$lib/game/gameStore.svelte';

	const store = new GameStore();

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

<button class="primary" disabled={store.busy} onclick={startPractice}>Practice game</button>

{#if store.error}
	<p class="error" role="alert">{store.error}</p>
{/if}
```

`src/WorldRankGuesser.Web/src/routes/play/[gameId]/+page.svelte`:

```svelte
<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import CategoryCard from '$lib/components/CategoryCard.svelte';
	import CountrySpinner from '$lib/components/CountrySpinner.svelte';
	import { GameStore } from '$lib/game/gameStore.svelte';

	const store = new GameStore();

	let missing = $state(false);
	let spinning = $state(true);      // cards stay disabled until the spinner has landed on a country

	$effect(() => {
		const id = page.params.gameId;
		if (id) store.load(id).then((found) => (missing = !found));
	});

	$effect(() => {
		if (!store.state?.isComplete) return;

		const id = store.state.id;
		const timer = setTimeout(() => goto(`/results/${id}`), 1200);     // long enough to see the last card fill
		return () => clearTimeout(timer);
	});

	async function choose(categoryId: string) {
		spinning = true;
		await store.pick(categoryId);
	}
</script>

{#if missing}
	<p>This game does not exist. <a href="/">Start a new one</a>.</p>
{:else if store.state}
	{#if store.state.currentCountry}
		<CountrySpinner country={store.state.currentCountry} pending={store.busy} onsettled={() => (spinning = false)} />
		<p class="muted turn">Country {store.state.picks.length + 1} of {store.state.categories.length}</p>
	{/if}

	{#if store.error}
		<p class="error" role="alert">{store.error}</p>
	{/if}

	<div class="cards">
		{#each store.state.categories as category (category.id)}
			<CategoryCard
				{category}
				pick={store.pickFor(category.id)}
				rankMode={store.state.rankMode}
				disabled={spinning || store.busy}
				onpick={() => choose(category.id)}
			/>
		{/each}
	</div>
{:else}
	<p class="muted">Loading…</p>
{/if}

<style>
	.turn {
		text-align: center;
		margin: 0 0 1rem;
	}

	.cards {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 0.5rem;
	}

	@media (max-width: 30rem) {
		.cards {
			grid-template-columns: 1fr;
		}
	}
</style>
```

`src/WorldRankGuesser.Web/src/routes/results/[gameId]/+page.svelte`:

```svelte
<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import Flag from '$lib/components/Flag.svelte';
	import { describePick } from '$lib/game/describe';
	import { GameStore } from '$lib/game/gameStore.svelte';

	const store = new GameStore();
	let missing = $state(false);

	$effect(() => {
		const id = page.params.gameId;
		if (!id) return;

		store.load(id).then((found) => {
			missing = !found;
			if (found && !store.state?.isComplete) goto(`/play/${id}`);     // not finished: back to the game
		});
	});

	async function playAgain() {
		const id = await store.start();
		if (id) await goto(`/play/${id}`);
	}
</script>

{#if missing}
	<p>This game does not exist. <a href="/">Start a new one</a>.</p>
{:else if store.state?.isComplete}
	<h1>Your score: <span data-testid="total">{store.state.totalScore}</span></h1>
	<p class="muted">
		The best possible score for these ten countries was
		<strong data-testid="optimal">{store.state.optimalScore}</strong>.
	</p>

	<ol>
		{#each store.state.picks as pick (pick.turnIndex)}
			<li>
				<Flag iso2={pick.country.iso2} size="1.5rem" label={pick.country.name} />
				<span>
					<strong>{pick.country.name}</strong> in {store.state.categories.find((c) => c.id === pick.categoryId)?.name}:
					<strong>{pick.score}</strong>
					<span class="muted">({describePick(pick, store.state.rankMode)})</span>
				</span>
			</li>
		{/each}
	</ol>

	<button class="primary" disabled={store.busy} onclick={playAgain}>Play again</button>

	{#if store.error}
		<p class="error" role="alert">{store.error}</p>
	{/if}
{:else}
	<p class="muted">Loading…</p>
{/if}

<style>
	ol {
		list-style: none;
		padding: 0;
		display: grid;
		gap: 0.5rem;
	}

	li {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}
</style>
```

- [ ] **Step 6: Run everything**

```bash
npm run check
npm test
npm run test:e2e
```

Expected: `svelte-check found 0 errors`; Vitest passes; both Playwright tests pass (the first run starts the API and Vite itself).

- [ ] **Step 7: Play it by hand, with motion**

Run the API (`dotnet run --project src/WorldRankGuesser.Api`) and the front end (`npm run dev` in `src/WorldRankGuesser.Web`), open `http://localhost:5173`, and play a game on a phone-sized viewport. Check: the flag spins and lands, the country name appears only after landing, cards are disabled while it spins, a filled card shows the flag, score and description, a browser refresh mid-game resumes it, the results page shows the total and the best possible score, and in the browser's network tab no response before the last pick contains `grid` data or a country you have not reached.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Add the home, play and results screens with an end-to-end practice game test"
```

---

### Task 13: Documentation

**Files:**
- Replace: `CLAUDE.md`, `README.md`

- [ ] **Step 1: Rewrite `CLAUDE.md`**

````markdown
# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A server-authoritative guessing game. The player is dealt ten countries one at a time and assigns each to a different sport category; each pick scores the country's world rank in that category (lower total wins; unranked or below 150th scores 150). `src/WorldRankGuesser.Api` (ASP.NET Core minimal API, .NET 11, EF Core, SQL Server) owns every rule and all state. `src/WorldRankGuesser.Web` (SvelteKit, Svelte 5, TypeScript, static single-page app) only renders what the API returns. Rankings come from the `dbo.CurrentCountryRankings` view that the separate **SportsRankingService** repo fills weekly; that view is the only link between the repos.

Design: `docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md`. Built so far: phases 0–1 (practice mode). Not built yet: deployment, the daily challenge, the timer, streaks, leaderboards, sign-in.

## Commands

```powershell
# Database: the SQL Server container lives in the SportsRankingService repo (docker compose up -d --wait there).
dotnet tool restore
dotnet ef database update --project src/WorldRankGuesser.Api            # apply the game schema; the API never migrates itself
dotnet ef migrations add <Name> --project src/WorldRankGuesser.Api --output-dir Persistence/Migrations

dotnet build WorldRankGuesser.slnx                                      # also regenerates src/WorldRankGuesser.Web/openapi/*.json
dotnet test WorldRankGuesser.slnx                                       # needs Docker (Testcontainers SQL Server)
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~ScoringEngineTests"   # one class; add .Method_name for one test
dotnet run --project src/WorldRankGuesser.Api                           # http://localhost:5170, /readyz says whether rankings loaded

cd src/WorldRankGuesser.Web
npm run dev                # http://localhost:5173, proxies /api to the API
npm run check              # svelte-check
npm test                   # Vitest; one file: npm test -- src/lib/game/spin.test.ts
npm run test:e2e           # Playwright; starts the API and Vite itself; needs the local database with real rankings
npm run gen:api            # after any API contract change: rebuild the API first, then regenerate src/lib/api/schema.d.ts
```

CI fails if the committed OpenAPI document or `schema.d.ts` is out of date.

## Architecture

**The board.** Starting a game draws one country per category and computes the whole country × category grid once (`BoardGenerator`), storing it as immutable JSON on `game.Boards`. Every pick is a lookup on that stored grid, so a rankings refresh never changes a game in progress. A practice game has its own board; a daily challenge (later phase) is one dated board shared by all players.

**Anti-cheat invariants — do not weaken these.** A pick request names only a category; the server applies it to the country at its own `TurnIndex`. `GameStateMapper` is the only code that decides what a response reveals: past picks and the current country, and the grid/optimal score only once the game is complete. A game that is not the caller's is a `404`. `AntiCheatTests` pins all of this. There is deliberately no endpoint that returns rankings.

**Rankings pipeline.** `RankingsReader` (the only code that knows about the view) → `RankingsSnapshotBuilder` → `RankingsStore`, refreshed at startup and hourly by `RankingsRefreshService`; a failed refresh keeps the previous snapshot. The builder computes, per feed, each country's *entry rank* (the published position of its best entry) and *country rank* (competition ranking among countries: 1, 2, 2, 4), keeps the best feed per category separately for each mode, then applies aliases.

**Scoring** lives only in `ScoringEngine`: `min(rank in Scoring:RankMode, Scoring:Cap)`, unranked = cap. Both ranks are stored in every cell, and the mode and cap are stamped on every board, so the two modes can be compared and are never mixed.

**Configuration over code** (`appsettings.json`): the ten categories and the scraper `Sport` values each covers; alias rules (`GBR` inherits the best of `ENG`/`SCO`/`WAL`/`NIR`; the 15 West Indies members inherit `WI` in cricket only); `NotDrawable`; `MinCategoriesRanked`. The category count is never hardcoded: a game has as many turns as its board has categories.

**Countries.** ISO3 identifies a country; display names are the scraper's `TeamName`. Flags need ISO2, which the view lacks, so `Countries/countries.json` is a committed ISO3→ISO2 table generated by `tools/GenerateCountryCatalog/generate.cs`. A drawable code missing from it fails the rankings load with a message naming the code: add it to the table or to `NotDrawable`.

**Persistence.** The API owns SQL schema `game` (history table `game.__EFMigrationsHistory`) and never touches `dbo`. The view is mapped keyless with `ToView`, so it never appears in migrations. Filtered unique indexes enforce one board per daily date, one daily game per player per date, and one pick per category per game; `Games.RowVersion` plus those indexes make a pick atomic — a losing simultaneous pick becomes a `409` carrying the current state. Some columns (streaks, deadlines, external login) exist for later phases and are unused today.

**Identity.** An anonymous player is created on the first `POST /api/games` (not on page load) and carried in the HttpOnly `wrg_player` cookie via ASP.NET Core cookie authentication; data-protection keys are stored in the database so cookies survive restarts. Same-origin hosting is a design requirement: in development Vite proxies `/api`; in production the API serves the built front end.

**Front end.** `GameStore` (`src/lib/game/gameStore.svelte.ts`) holds the last server state and has no game rules; a `409` or a failed pick means "adopt or reload the server state". The spinner cycles decoy flags from `src/lib/countries/iso2.json` (no rank data) while a pick is in flight and lands when the next country arrives. Types in `src/lib/api/schema.d.ts` are generated — never edit them by hand.

**Tests.** Pure logic (scoring, snapshot builder, board generator, optimal assignment) is unit-tested without a database. Everything touching SQL runs against a real SQL Server in Testcontainers (xUnit collection `"sql"`), because the model depends on filtered indexes, row versions and a view; `RankingsSeed` stands in for the scraper's view with 12 countries ranked 1–12 in one feed per category. Tests share one database, so never assert on global row counts.
````

- [ ] **Step 2: Rewrite `README.md`**

````markdown
# WorldRankGuesser

A guessing game: you are dealt ten countries, one at a time, and put each into a different sport. You score the
country's world rank in that sport, and the lowest total wins.

- `src/WorldRankGuesser.Api` — ASP.NET Core API (.NET 11) that owns the rules and the state.
- `src/WorldRankGuesser.Web` — SvelteKit front end.
- Rankings are scraped weekly by [SportsRankingService](../SportsRankingService) into SQL Server.

## Running locally

1. In the SportsRankingService repo: `docker compose up -d --wait`, apply its migrations and run it once so the database has rankings.
2. Here: `dotnet tool restore`, then `dotnet ef database update --project src/WorldRankGuesser.Api`.
3. `dotnet run --project src/WorldRankGuesser.Api`
4. In `src/WorldRankGuesser.Web`: `npm install`, then `npm run dev`, and open http://localhost:5173.

Design and plans are in `docs/superpowers/`.
````

- [ ] **Step 3: Verify the documented commands**

Run each command in the CLAUDE.md `Commands` block once and confirm it works as described (the single-test filter should run 4 tests).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Rewrite CLAUDE.md and the README for the rebuilt app"
```

---

## Done when

- `dotnet test WorldRankGuesser.slnx` and, in `src/WorldRankGuesser.Web`, `npm run check`, `npm test` and `npm run test:e2e` all pass.
- A practice game can be played start to finish at `http://localhost:5173` against the real rankings, survives a refresh, and ends on a results page showing the total and the best possible score.
- No response before a game's last pick contains a future country, the grid, or the optimal score.
- Changing `Scoring:RankMode` to `Entry` in `appsettings.json` and restarting changes how new games score, with no code change.
- CI is green on the `rebuild` branch.
