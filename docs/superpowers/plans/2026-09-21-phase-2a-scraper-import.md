# Phase 2a: Scraper Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the SportsRankingService scraper, its tests and its benchmarks into this repo with their history, build them on .NET 11, and make the game's SQL tests run against the scraper's real `dbo.CurrentCountryRankings` view instead of a hand-written stand-in.

**Architecture:** The scraper repo is restructured on a branch in its own repo (`git mv` into the paths this repo uses), tagged, then merged here with `--allow-unrelated-histories`; nothing is copied, so every parser keeps its history. A second commit moves the imported projects to .NET 11 by letting them inherit `Directory.Build.props`. The game's Testcontainers fixture then applies the scraper's migrations before the game's, and the seed writes rankings through the scraper's own `RankingRepository`, so the view is fed exactly as production feeds it. `WorldRankGuesser.Api` still never references `SportsRankingService`; only the test project does.

**Tech Stack:** .NET 11 SDK `11.0.100-rc.1.26425.128`, EF Core `11.0.0-rc.1.26425.128` (SqlServer, Sqlite, Design), xUnit 2.9.3, Testcontainers.MsSql, BenchmarkDotNet 0.15.8, git.

**Spec:** `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md`, sections 5 (repo layout and the import) and 6 (the benchmark paragraph only). Plan 2b covers the images, compose and app changes; plan 2c covers going public, Azure and the pipelines.

## Global Constraints

- The coupling rule (spec section 2): `WorldRankGuesser.Api` never references `SportsRankingService`. The `dbo.CurrentCountryRankings` view is the only runtime link. Only the SQL test fixture may use the scraper's migrations.
- Two independent migration sets: the scraper owns `dbo` (history table `dbo.__EFMigrationsHistory`), the game owns `game` (`game.__EFMigrationsHistory`). On a new database `dbo` goes first.
- Every project inherits `TargetFramework` `net11.0`, `Nullable` `enable` and `ImplicitUsings` `enable` from the root `Directory.Build.props`; no imported project may set them.
- Package versions: EF Core and `Microsoft.Extensions.*` at `11.0.0-rc.1.26425.128`; `Microsoft.NET.Test.Sdk` `17.14.1`; `xunit` `2.9.3`; `xunit.runner.visualstudio` `3.1.5`; `Microsoft.Extensions.TimeProvider.Testing` `10.10.0` (no 11.0 build exists on 2026-09-21); `BenchmarkDotNet` `0.15.8`. `HtmlAgilityPack` and `Newtonsoft.Json` stay as they are.
- New nullable warnings are fixed at the source, never suppressed. A throwaway build on 2026-09-21 found exactly three, all in `SportsRankingService/Models/SoccerRankDate.cs` (CS8618 on `id`, `iso`, `dateText`), plus one pre-existing xUnit2029 in `FigParserTests.cs`; all 354 scraper tests passed on .NET 11 and the benchmark project built with BenchmarkDotNet 0.15.8.
- Every commit on the branch passes `dotnet build WorldRankGuesser.slnx` and `dotnet test WorldRankGuesser.slnx` with only the .NET 11 SDK installed (CI has no .NET 8 runtime), so the scraper projects join the solution in the same commit that moves them to .NET 11.
- Tests share one SQL Server database; never assert on global row counts.
- Windows: run git and dotnet commands in PowerShell from the repo root unless a step says otherwise. Paths in this plan use forward slashes; git accepts them on Windows.

**Precondition:** the working tree of this repo is clean. On 2026-09-21 `main` had 29 modified files (the rank-mode work) and an untracked `tools/SimulateBoards/`; the owner commits or stashes those before Task 1. The scraper repo (`../SportsRankingService`) was clean, `main` equal to `origin/main`, with `bwf-curl-fetcher-iihf-wikipedia` and `persistence-redesign` already merged into `main`; Task 1 verifies rather than assumes this.

---

## File structure

**Created or moved into this repo by the merge (Task 3), with history:**

| Path | From (scraper repo) | Responsibility |
|---|---|---|
| `src/SportsRankingService/` | `SportsRankingService/` | The scraper console app: parsers, fetchers, resolvers, `Persistence/` with the `dbo` migrations and the two views. |
| `src/SportsRankingService/CLAUDE.md` | `CLAUDE.md` | The scraper's guidance; loads only when working in that folder. |
| `src/SportsRankingService/README.md` | `README.md` | The scraper's own README (scheduling, reading the data, configuration). Moved rather than deleted because this repo's root already has a `README.md`. |
| `tests/SportsRankingService.Tests/` | `SportsRankingService.Tests/` | Fixture-based parser tests, SQLite repository tests, curl loopback tests. |
| `tools/WebScrapingBenchmarks/` | `WebScrapingBenchmarks/` | BenchmarkDotNet project, outside the solution like `tools/GenerateCountryCatalog`. |
| `docs/superpowers/specs/2026-09-1{5,6,7}-*.md`, `docs/superpowers/plans/2026-09-1{5,7}-*.md` | same paths | The scraper's design history. Dated names, no collisions. |
| `docker-compose.yml` | `docker-compose.yml` | The development SQL Server, unchanged. Plan 2b adds the `stack` profile. |

**Deleted in the scraper repo before the merge (this repo already provides them):** `SportsRankingService.sln`, `global.json`, `.gitignore`, `.gitattributes`, `.config/dotnet-tools.json`.

**Modified here:**

| Path | Change |
|---|---|
| `WorldRankGuesser.slnx` | Adds the scraper project and its tests (Task 4). |
| `src/SportsRankingService/SportsRankingService.csproj`, `tests/SportsRankingService.Tests/SportsRankingService.Tests.csproj`, `tools/WebScrapingBenchmarks/SportsRankingBenchmarks.csproj` | Drop the three properties `Directory.Build.props` provides; package versions; the benchmark's project reference path (Task 4). |
| `src/SportsRankingService/Models/SoccerRankDate.cs` | The three nullable warnings (Task 4). |
| `tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj` | Adds the project reference to the scraper (Task 5). |
| `tests/WorldRankGuesser.Api.Tests/Integration/SqlServerFixture.cs` | Applies the scraper's migrations, then the game's (Task 5). |
| `tests/WorldRankGuesser.Api.Tests/Integration/RankingsSeed.cs` | Saves snapshots through `RankingRepository` instead of inserting into a stand-in table (Task 5). |
| `tests/WorldRankGuesser.Api.Tests/Integration/ViewContractTests.cs` | New: pins the view's columns and the seeded positions (Task 5). |
| `CLAUDE.md`, `README.md`, `docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md` | The coupling rule, the commands, the layout (Task 6). |

---

### Task 1: Verify the scraper repo and restructure it on a branch

This task runs **in the scraper repo**, `C:\Users\Josep\OneDrive\Documents\GitHub\SportsRankingService`. It changes nothing in this repo.

**Files (scraper repo):**
- Move: everything, per the table above
- Delete: `SportsRankingService.sln`, `global.json`, `.gitignore`, `.gitattributes`, `.config/dotnet-tools.json`
- Modify: `WebScrapingBenchmarks/SportsRankingBenchmarks.csproj` project reference path (moved with the folder, so the edit lands in `tools/WebScrapingBenchmarks/SportsRankingBenchmarks.csproj`)

**Interfaces:**
- Produces: branch `import-layout` and tag `scraper-net8-baseline` in the scraper repo, at the same commit. Task 3 fetches both.

- [ ] **Step 1: Confirm the scraper repo is clean, pushed, and its branches merged**

```powershell
cd ..\SportsRankingService
git status --short
git fetch origin
git log --oneline origin/main..main
git branch --merged main
```

Expected: `git status --short` prints nothing; `git log origin/main..main` prints nothing (nothing unpushed); `git branch --merged main` lists `bwf-curl-fetcher-iihf-wikipedia`, `main` and `persistence-redesign`. If `main` is ahead of `origin`, `git push origin main` first. If a branch is not merged, stop and ask the owner; do not merge it yourself.

- [ ] **Step 2: Create the branch and move the folders**

```powershell
git switch -c import-layout
New-Item -ItemType Directory -Force src, tests, tools | Out-Null
git mv SportsRankingService src/SportsRankingService
git mv SportsRankingService.Tests tests/SportsRankingService.Tests
git mv WebScrapingBenchmarks tools/WebScrapingBenchmarks
git mv CLAUDE.md src/SportsRankingService/CLAUDE.md
git mv README.md src/SportsRankingService/README.md
```

`docs/superpowers/**` and `docker-compose.yml` stay where they are: this repo uses the same paths and has no `docker-compose.yml`.

- [ ] **Step 3: Delete the files this repo already provides**

```powershell
git rm SportsRankingService.sln global.json .gitignore .gitattributes .config/dotnet-tools.json
git ls-files | Select-String -NotMatch '^(src|tests|tools|docs)/' 
```

Expected: the second command prints only `docker-compose.yml`.

- [ ] **Step 4: Fix the benchmark project's reference path**

In `tools/WebScrapingBenchmarks/SportsRankingBenchmarks.csproj`, change

```xml
    <ProjectReference Include="..\SportsRankingService\SportsRankingService.csproj" />
```

to

```xml
    <ProjectReference Include="..\..\src\SportsRankingService\SportsRankingService.csproj" />
```

The test project's reference must change too: from `tests/SportsRankingService.Tests`, `..\SportsRankingService\` would be `tests/SportsRankingService/`, which does not exist. In `tests/SportsRankingService.Tests/SportsRankingService.Tests.csproj` change it to

```xml
    <ProjectReference Include="..\..\src\SportsRankingService\SportsRankingService.csproj" />
```

- [ ] **Step 5: Prove the moved tree still builds and tests on .NET 8**

The `global.json` is gone, so the newest installed SDK (11.0.100-rc.1) builds the still-`net8.0` projects; the .NET 8 runtime on this machine runs the tests.

```powershell
dotnet test tests/SportsRankingService.Tests
dotnet build tools/WebScrapingBenchmarks
```

Expected: `Passed! - Failed: 0, Passed: 354, Skipped: 1`; the benchmark build reports 0 errors.

- [ ] **Step 6: Commit and tag**

```powershell
git add -A
git commit -m @'
Restructure for import into WorldRankGuesser

Move the three projects under src/, tests/ and tools/, and drop the
solution, SDK pin, git attributes and tool manifest that the target
repo provides.
'@
git tag scraper-net8-baseline
git log --oneline -1
```

Expected: the commit lists renames only (plus the two csproj edits and five deletions), no adds, so history follows every file.

---

### Task 2: Start the import branch in this repo

**Files:** none changed; a branch is created.

**Interfaces:**
- Produces: branch `phase-2a-scraper-import` off `main`, the branch every later task commits to.

- [ ] **Step 1: Confirm the precondition and branch**

```powershell
cd ..\WorldRankGuesser
git status --short
git switch -c phase-2a-scraper-import main
```

Expected: `git status --short` prints nothing. If it prints anything, stop: the owner must commit or stash first (see Precondition).

---

### Task 3: Merge the scraper's history into this repo

**Files:**
- Create (by merge): everything in the File structure table

**Interfaces:**
- Consumes: branch `import-layout` and tag `scraper-net8-baseline` from Task 1.
- Produces: the scraper's files at their final paths; the tag `scraper-net8-baseline` in this repo's history (the benchmark spike in spec section 6 checks it out).

- [ ] **Step 1: Fetch the scraper repo as a temporary remote**

```powershell
git remote add scraper ..\SportsRankingService
git fetch scraper import-layout --tags
git ls-tree --name-only scraper/import-layout
```

Expected: `docker-compose.yml`, `docs`, `src`, `tests`, `tools`.

- [ ] **Step 2: Check that no path collides**

```powershell
$ours = git ls-tree -r --name-only main
git ls-tree -r --name-only scraper/import-layout | Where-Object { $ours -contains $_ }
```

Expected: no output: no path exists in both trees. If anything prints, stop and report it; the spec expects none (the scraper's design documents have dates 2026-09-15 to 17, the game's 2026-09-19, and this repo has no `docker-compose.yml`).

- [ ] **Step 3: Merge**

```powershell
git merge --allow-unrelated-histories --no-edit scraper/import-layout
git remote remove scraper
git tag --list scraper-net8-baseline
git log --oneline -3
git log --oneline --follow -- src/SportsRankingService/Parsers/BwfParser.cs | Measure-Object -Line
```

Expected: the merge succeeds with no conflicts; the tag is listed; the last command counts more than one commit, proving the parser's history came across. If `git merge` reports a conflict, abort with `git merge --abort` and report it.

- [ ] **Step 4: Build what the merge added, still on .NET 8**

The imported projects still say `net8.0`; the csproj's own property overrides `Directory.Build.props`, so nothing changes yet. This step only proves the merge left them intact at their new paths.

```powershell
dotnet build src/SportsRankingService
dotnet build tools/WebScrapingBenchmarks
dotnet build WorldRankGuesser.slnx
```

Expected: 0 errors from each. The solution build is unaffected because the solution does not yet include the scraper.

No commit: the merge commit is the commit for this task. Push the tag so it exists on GitHub too:

```powershell
git push origin scraper-net8-baseline
```

---

### Task 4: Move the scraper to .NET 11 and add it to the solution

**Files:**
- Modify: `src/SportsRankingService/SportsRankingService.csproj`
- Modify: `tests/SportsRankingService.Tests/SportsRankingService.Tests.csproj`
- Modify: `tools/WebScrapingBenchmarks/SportsRankingBenchmarks.csproj`
- Modify: `src/SportsRankingService/Models/SoccerRankDate.cs`
- Modify: `WorldRankGuesser.slnx`

**Interfaces:**
- Produces: `SportsRankingService` builds as `net11.0`, referenced by Task 5's test project as `..\..\src\SportsRankingService\SportsRankingService.csproj`; `dotnet test WorldRankGuesser.slnx` runs the scraper's tests.

- [ ] **Step 1: Let the three projects inherit `Directory.Build.props`**

In each of the three `.csproj` files, delete these three lines from the first `<PropertyGroup>`:

```xml
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
```

`SportsRankingService.csproj` keeps `<OutputType>Exe</OutputType>` and `<UserSecretsId>`; the test project keeps `<IsPackable>` and `<IsTestProject>`; the benchmark project keeps `<OutputType>Exe</OutputType>`.

- [ ] **Step 2: Package versions**

`src/SportsRankingService/SportsRankingService.csproj`:

```xml
    <PackageReference Include="HtmlAgilityPack" Version="1.11.54" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="11.0.0-rc.1.26425.128" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="11.0.0-rc.1.26425.128">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="11.0.0-rc.1.26425.128" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="11.0.0-rc.1.26425.128" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

`tests/SportsRankingService.Tests/SportsRankingService.Tests.csproj`:

```xml
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="11.0.0-rc.1.26425.128" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
```

`tools/WebScrapingBenchmarks/SportsRankingBenchmarks.csproj`:

```xml
    <PackageReference Include="BenchmarkDotNet" Version="0.15.8" />
```

- [ ] **Step 3: Build and list the warnings**

```powershell
dotnet build tests/SportsRankingService.Tests 2>&1 | Select-String "warning|error" | Sort-Object -Unique
```

Expected: 0 errors; the warnings are exactly `CS8618` three times in `Models/SoccerRankDate.cs` (lines 13, 15, 18: `id`, `iso`, `dateText`) and `xUnit2029` once in `Parsers/FigParserTests.cs` line 52. The xUnit one predates the move and stays. Any other warning is new to this SDK and is fixed at its source in this task before Step 5; report it in the commit message.

- [ ] **Step 4: Fix the three nullable warnings**

`FifaDateIdResolver` deserializes this DTO with Newtonsoft.Json from FIFA's `__NEXT_DATA__` and then reads `.id` and `.iso` unconditionally (`ExtractLatestDate`, `ToRankingDate`). `required` states that invariant and clears the warning; it changes no behaviour, because Newtonsoft sets properties by reflection and a property missing from the JSON still arrives as null and fails at `DateTimeOffset.Parse`, as it does today. No test constructs the type with an initializer, so nothing else changes. Replace the whole file with

```csharp
namespace SportsRankingService.Models;

/// <summary>One entry of FIFA's ranking-schedule list inside the page's __NEXT_DATA__; Newtonsoft binds it by property name.</summary>
public class SoccerRankDate
{
    public required string id { get; set; }

    public required string iso { get; set; }

    public required string dateText { get; set; }
}
```

Newtonsoft.Json 13.0.1 and later honour `required` members (it fails deserialization when one is missing, which is the behaviour we want). Rebuild:

```powershell
dotnet build tests/SportsRankingService.Tests 2>&1 | Select-String "warning|error" | Sort-Object -Unique
```

Expected: only the `xUnit2029` line remains.

- [ ] **Step 5: Run the scraper's tests and build the benchmarks**

```powershell
dotnet test tests/SportsRankingService.Tests
dotnet build tools/WebScrapingBenchmarks
```

Expected: `Passed! - Failed: 0, Passed: 354, Skipped: 1 ... (net11.0)`; the benchmark build reports 0 errors and 0 warnings. `FifaDateIdResolverTests` are among the 354 and cover the DTO change.

- [ ] **Step 6: Add both projects to the solution**

Replace the whole of `WorldRankGuesser.slnx` with

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/SportsRankingService/SportsRankingService.csproj" />
    <Project Path="src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/SportsRankingService.Tests/SportsRankingService.Tests.csproj" />
    <Project Path="tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 7: Run the whole solution**

Docker must be running (the game's SQL tests use Testcontainers).

```powershell
dotnet build WorldRankGuesser.slnx
dotnet test WorldRankGuesser.slnx --no-build
git status --short
```

Expected: both test assemblies pass; `git status` shows only the five files this task modifies. In particular `src/WorldRankGuesser.Web/openapi/WorldRankGuesser.Api.json` is unchanged (the build regenerates it; a diff there would mean the API changed, which this task must not do).

- [ ] **Step 8: One live run saves every enabled feed (the spec's acceptance for this step)**

The merge put `docker-compose.yml` at this repo's root; it is the scraper repo's file unchanged, same container name and volume, so the local database and its data survive. This run uses the live federation sites and takes a few minutes.

```powershell
docker compose up -d --wait
dotnet tool restore
dotnet ef database update --project src/SportsRankingService
dotnet run --project src/SportsRankingService
$LASTEXITCODE
```

Expected: `database update` reports no pending migrations (the local database already has `InitialSchema`); the run ends with a line like `54 feeds: N new releases, M unchanged, 0 failed` and `$LASTEXITCODE` is `0`. New versus unchanged depends on what the federations published since the last local run; only `0 failed` matters.

If a feed fails, tell a site change from a regression before touching anything: in the scraper repo, `git switch --detach scraper-net8-baseline` and `dotnet run --project src/SportsRankingService -- --only "<the feed's name from the log>"` (that checkout is .NET 8 and this machine has the runtime). The same failure there means the site changed, which is not this task's problem: note the feed in the commit message and carry on. A failure only on .NET 11 is a regression of this task: fix it before committing, and add the case to the scraper's tests if a fixture can capture it. Afterwards `git switch main` in the scraper repo.

- [ ] **Step 9: Commit**

```powershell
git add WorldRankGuesser.slnx src/SportsRankingService/SportsRankingService.csproj tests/SportsRankingService.Tests/SportsRankingService.Tests.csproj tools/WebScrapingBenchmarks/SportsRankingBenchmarks.csproj src/SportsRankingService/Models/SoccerRankDate.cs
git commit -m @'
Move the scraper to .NET 11 and add it to the solution

The three imported projects inherit the target framework from
Directory.Build.props. EF Core and Microsoft.Extensions move to the
game's 11.0 RC build, the test packages to the game's versions,
BenchmarkDotNet to 0.15.8 (the first that knows .NET 11). The only new
nullable warnings were the three properties of SoccerRankDate, which
FifaDateIdResolver reads unconditionally, so they become required.
'@
```

---

### Task 4b: Restore `dotnet ef` for the scraper and keep the local database

Added during execution (2026-09-21) after Task 4's live run found two defects the plan had assumed away.

1. `dotnet ef database update --project src/SportsRankingService` fails with `Unknown argument '--applicationName'`. At design time `dotnet ef` runs the program's `Main` with its own arguments (`--applicationName <assembly>`) to find the host and intercepts `Build()`; the scraper's `FeedFilter.Parse(args)` rejects that argument and exits before the host exists. It broke in the old repo when `--only` was added after the only migration, so nobody noticed. Every future scraper migration and plan 2c's migration bundle depend on it.
2. `docker compose up -d --wait` from this repo fails with a container-name conflict. The running container was created by the old repo's Compose project, `sportsrankingservice` (Compose names a project after its folder), on volume `sportsrankingservice_sqldata`. From this repo Compose uses another project name, so it fights over `container_name: worldrankguesser-sql` and would create a new, empty volume: the spec's "existing local data survives" (section 6) needs the project name pinned. Task 4's failed attempt also left an empty volume `phase-2a-scraper-import_sqldata` behind.

**Files:**
- Modify: `src/SportsRankingService/Program.cs:6-18`
- Modify: `docker-compose.yml:1-5`

**Interfaces:**
- Consumes: `FeedFilter.All` (`src/SportsRankingService/Configuration/FeedFilter.cs`), `Microsoft.EntityFrameworkCore.EF.IsDesignTime`.
- Produces: `dotnet ef ... --project src/SportsRankingService` works, which Task 6 documents and plan 2c's bundles need; `docker compose up -d --wait` from this repo reuses the existing container and data, which Task 6 documents.

- [ ] **Step 1: Reproduce both failures (red)**

Docker Desktop must be running.

```powershell
dotnet ef migrations list --project src/SportsRankingService
docker compose up -d --wait
docker volume ls --format '{{.Name}}' | Select-String sqldata
```

Expected: the first prints `Unknown argument '--applicationName'. usage: SportsRankingService [--only "<Sport [Event [Gender]]>"]...` and fails; the second fails with a message that the container name `worldrankguesser-sql` is already in use; the third lists `sportsrankingservice_sqldata` (the real data) and `phase-2a-scraper-import_sqldata` (the stray, empty one).

- [ ] **Step 2: Skip the command line at design time**

In `src/SportsRankingService/Program.cs`, replace the block from the first comment through the closing brace of the `catch` (currently lines 6 to 18):

```csharp
// A run-once console app: an external scheduler (Task Scheduler, cron) runs it weekly.
// `--only <feed>` (repeatable) reruns a subset, e.g. the feeds a previous run reported as failed.
FeedFilter filter;

try
{
    filter = FeedFilter.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
```

with

```csharp
// A run-once console app: an external scheduler (Task Scheduler, cron) runs it weekly.
// `--only <feed>` (repeatable) reruns a subset, e.g. the feeds a previous run reported as failed.
// At design time `dotnet ef` runs this program with its own arguments (--applicationName) only to find the host,
// so they are not a feed filter.
FeedFilter filter = FeedFilter.All;

if (!EF.IsDesignTime)
{
    try
    {
        filter = FeedFilter.Parse(args);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}
```

`EF` is `Microsoft.EntityFrameworkCore.EF`; the file already has `using Microsoft.EntityFrameworkCore;`. Nothing else in the file changes: the host is still built outside the `try`, which is what lets `dotnet ef` intercept it.

- [ ] **Step 3: Verify `dotnet ef` (green)**

```powershell
dotnet build src/SportsRankingService
dotnet ef migrations list --project src/SportsRankingService
dotnet ef database update --project src/SportsRankingService
dotnet run --project src/SportsRankingService -- --bogus
```

Expected: the build has 0 errors and no new warnings; `migrations list` prints `20260917221403_InitialSchema` (the local database, if reachable, marks it applied; if the database is not reachable the list still prints and the tool warns); `database update` prints `No migrations were applied. The database is already up to date.`; the last command still prints `Unknown argument '--bogus'. usage: ...` and exits 1 (`$LASTEXITCODE`), which shows the usage error is intact outside design time.

- [ ] **Step 4: Pin the Compose project name**

In `docker-compose.yml`, replace the four comment lines at the top (lines 1 to 4) with

```yaml
# Local SQL Server for development. Start with `docker compose up -d --wait`.
# The SA password is a dev-only credential for this container; it is also in
# src/SportsRankingService/appsettings.json. Override the connection string elsewhere with the
# ConnectionStrings__WorldRankGuesserConnection environment variable.
# The project name is the old SportsRankingService repo's folder name, which Compose used as its project name,
# so this file keeps addressing that repo's container and its volume (sportsrankingservice_sqldata): local data
# survives the scraper's move into this repo.
name: sportsrankingservice
```

The `services:` line and everything below it stay as they are.

- [ ] **Step 5: Verify Compose reuses the container, and remove the stray volume**

```powershell
docker compose up -d --wait
docker compose ps --format '{{.Name}} {{.Status}}'
docker inspect worldrankguesser-sql --format '{{index .Config.Labels "com.docker.compose.project"}} {{range .Mounts}}{{.Name}}{{end}}'
docker volume rm phase-2a-scraper-import_sqldata
docker volume ls --format '{{.Name}}' | Select-String sqldata
```

Expected: `up` exits 0 and reports the service running/healthy (it adopts the existing container; if it says the container was recreated, that is also fine, because the volume is the same); `ps` shows `worldrankguesser-sql` healthy; `inspect` prints `sportsrankingservice sportsrankingservice_sqldata`; the `volume rm` succeeds (the stray volume is empty and attached to nothing; if Docker refuses because it is in use, stop and report instead of forcing); the last line lists only `sportsrankingservice_sqldata`.

- [ ] **Step 6: Scraper tests and commit**

```powershell
dotnet test tests/SportsRankingService.Tests
git status --short
```

Expected: 354 passed, 1 skipped; status shows only the two modified files (plus the openapi line-ending churn if you built the solution, which is not committed).

```powershell
git add src/SportsRankingService/Program.cs docker-compose.yml
git commit -m @'
Let dotnet ef run the scraper at design time; pin the Compose project

dotnet ef starts the program with --applicationName to find the host,
and FeedFilter rejected it before the host existed, so no scraper
migration could be listed, applied or bundled. The command line is now
skipped at design time. The Compose project name is pinned to the old
repo's, so this file keeps using the existing container and volume.
'@
```

---

### Task 5: The view-contract test

The game's SQL fixture stops creating a stand-in table and applies the scraper's real migrations, which create `dbo.RankingReleases`, `dbo.RankingRows` and the two views. The seed then saves one `RankingSnapshot` per feed through the scraper's `RankingRepository`, so `dbo.CurrentCountryRankings` yields the same 12 countries × 10 feeds the existing tests expect (`RankedEntrants` is 1 for every row because each country has one entry per feed). Renaming a column the view projects now fails these tests, because `RankingsReader` selects every mapped column.

**Files:**
- Modify: `tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj`
- Create: `tests/WorldRankGuesser.Api.Tests/Integration/ViewContractTests.cs`
- Modify: `tests/WorldRankGuesser.Api.Tests/Integration/SqlServerFixture.cs`
- Modify: `tests/WorldRankGuesser.Api.Tests/Integration/RankingsSeed.cs`

**Interfaces:**
- Consumes (scraper, all public): `SportsRankingService.Persistence.RankingsDbContext(DbContextOptions<RankingsDbContext>)`; `SportsRankingService.Persistence.RankingRepository(RankingsDbContext db, TimeProvider clock)` with `Task<SaveOutcome> SaveAsync(RankingSnapshot snapshot, CancellationToken cancellationToken)`; `SportsRankingService.Services.RankingSnapshot(string Sport, string? Event, string Gender, DateOnly RankingDate, bool IsFederationDate, IReadOnlyList<RankingSnapshotEntry> Entries)`; `SportsRankingService.Services.RankingSnapshotEntry(short Position, string ISO3, string? TeamName = null, string? Competitor = null, decimal? Points = null)`.
- Consumes (game): `GameDbContext.Configure(DbContextOptionsBuilder, string)`, `RankingsReader(GameDbContext)` with `Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken)`.
- Produces: `RankingsSeed.Countries`, `RankingsSeed.Feeds`, `RankingsSeed.PositionOf(int countryIndex, int feedIndex)` keep their names and values; `ReadinessTests`, `PersistenceTests` and `GameApiTests` use them unchanged. `RankingsSeed.CreateTableSql` is removed. `RankingsSeed.InsertAsync(GameDbContext)` becomes `RankingsSeed.SaveAsync(string connectionString)`.

- [ ] **Step 1: Reference the scraper from the test project**

In `tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj`, replace the project-reference group with

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\WorldRankGuesser.Api\WorldRankGuesser.Api.csproj" />
    <!-- Only for the SQL fixture: the scraper's migrations create the real dbo.CurrentCountryRankings view. The API never references the scraper. -->
    <ProjectReference Include="..\..\src\SportsRankingService\SportsRankingService.csproj" />
  </ItemGroup>
```

Both referenced projects copy an `appsettings.json` into the test output directory; the last copy wins and it does not matter, because `WebApplicationFactory` reads the API's settings from the API project's own folder (its content root), and the scraper's `Program` never runs in tests.

```powershell
dotnet build tests/WorldRankGuesser.Api.Tests
```

Expected: 0 errors.

- [ ] **Step 2: Write the contract test**

Create `tests/WorldRankGuesser.Api.Tests/Integration/ViewContractTests.cs`:

```csharp
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// The view contract between the scraper and the game. The fixture applies the scraper's real migrations, so a
/// change to a column dbo.CurrentCountryRankings projects fails here, in the game's tests, before it reaches a database.
/// </summary>
[Collection("sql")]
public class ViewContractTests(SqlServerFixture sql)
{
    [Fact]
    public async Task The_scrapers_view_yields_the_seeded_rankings_with_every_mapped_column()
    {
        await using var db = sql.CreateContext();

        var rows = await new RankingsReader(db).ReadAsync(CancellationToken.None);

        var seeded = rows.Where(r => RankingsSeed.Feeds.Contains((r.Sport, r.Event, r.Gender))).ToList();
        Assert.Equal(RankingsSeed.Feeds.Length * RankingsSeed.Countries.Length, seeded.Count);

        for (var feed = 0; feed < RankingsSeed.Feeds.Length; feed++)
        for (var country = 0; country < RankingsSeed.Countries.Length; country++)
        {
            var (sport, ev, gender) = RankingsSeed.Feeds[feed];
            var row = Assert.Single(seeded, r => r.Sport == sport && r.Event == ev && r.Gender == gender && r.ISO3 == RankingsSeed.Countries[country]);

            Assert.Equal(RankingsSeed.PositionOf(country, feed), row.Position);
            Assert.Equal(new DateOnly(2026, 9, 14), row.RankingDate);
            Assert.True(row.IsFederationDate);
            Assert.False(string.IsNullOrEmpty(row.TeamName));
            Assert.Null(row.Competitor);
            Assert.Null(row.Points);
            Assert.Equal((int?)1, row.RankedEntrants); // one entry per country per feed in the seed
        }
    }
}
```

The test filters to the seeded feeds rather than asserting the total, because the database is shared.

- [ ] **Step 3: Run it against the stand-in and see it pass**

```powershell
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~ViewContractTests"
```

Expected: PASS. This is the characterization: the stand-in already satisfies the contract. The next step makes it red.

- [ ] **Step 4: Apply the scraper's migrations in the fixture (red)**

Replace `InitializeAsync` and the helpers in `tests/WorldRankGuesser.Api.Tests/Integration/SqlServerFixture.cs` so the file reads

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SportsRankingService.Persistence;
using Testcontainers.MsSql;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// One SQL Server container for the whole test run. Two databases: one with both migration sets (the scraper's dbo,
/// then the game's game schema) and seeded rankings; one with only the game's migrations and so no rankings view
/// (to test readiness failure). Tests share the seeded database, so they must never assert on global row counts.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = "";

    public string EmptyConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = WithDatabase("WorldRankGuesserTests");
        EmptyConnectionString = WithDatabase("WorldRankGuesserEmpty");

        // dbo first, as on a new database in production: the scraper's tables and views, then the game schema.
        await using (var rankings = CreateRankingsContext(ConnectionString))
        {
            await rankings.Database.MigrateAsync();
        }

        await using (var db = CreateContext(ConnectionString))
        {
            await db.Database.MigrateAsync();
        }

        await RankingsSeed.SaveAsync(ConnectionString);

        await using (var empty = CreateContext(EmptyConnectionString))
        {
            await empty.Database.MigrateAsync();
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public GameDbContext CreateContext() => CreateContext(ConnectionString);

    /// <summary>The scraper's context, for the fixture and the seed only; the API never sees it.</summary>
    public static RankingsDbContext CreateRankingsContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RankingsDbContext>().UseSqlServer(connectionString).Options);

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

`RankingsSeed.SaveAsync` does not exist yet, so:

```powershell
dotnet build tests/WorldRankGuesser.Api.Tests
```

Expected: a compile error naming `SaveAsync` on `RankingsSeed`. That is the red state for this task.

- [ ] **Step 5: Seed through the scraper's repository (green)**

Replace `tests/WorldRankGuesser.Api.Tests/Integration/RankingsSeed.cs` with

```csharp
using SportsRankingService.Persistence;
using SportsRankingService.Services;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// Rankings for the tests, written the way the scraper writes them: one release per feed through
/// <see cref="RankingRepository"/>, so the real dbo.CurrentCountryRankings view serves them. 12 countries ranked 1-12
/// in one feed of each of the 10 categories, deterministic, so tests can predict every score.
/// </summary>
internal static class RankingsSeed
{
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

    public static async Task SaveAsync(string connectionString)
    {
        await using var db = SqlServerFixture.CreateRankingsContext(connectionString);
        var repository = new RankingRepository(db, TimeProvider.System);

        for (var feed = 0; feed < Feeds.Length; feed++)
        {
            var (sport, ev, gender) = Feeds[feed];
            var entries = Enumerable.Range(0, Countries.Length)
                .Select(country => new RankingSnapshotEntry(PositionOf(country, feed), Countries[country], Names[country]))
                .OrderBy(entry => entry.Position)
                .ToList();

            var outcome = await repository.SaveAsync(
                new RankingSnapshot(sport, ev, gender, new DateOnly(2026, 9, 14), IsFederationDate: true, entries),
                CancellationToken.None);

            if (outcome != SaveOutcome.Inserted)
                throw new InvalidOperationException($"Seeding {sport} {ev} {gender}: expected Inserted, got {outcome}.");
        }
    }
}
```

The entries are sorted by position because the repository assigns `Ordinal` in entry order and the view picks a country's best entry by `MIN(Ordinal)`; with one entry per country it makes no difference to the result, but it matches what `RankingSnapshotBuilder` produces.

- [ ] **Step 6: Run the SQL collection**

```powershell
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~Integration"
```

Expected: every test passes, including `ViewContractTests`, `ReadinessTests.Ready_once_the_rankings_are_loaded`, `ReadinessTests.Alive_but_not_ready_when_the_rankings_view_is_missing` (the empty database still has no view) and `PersistenceTests.The_rankings_view_is_readable_through_the_context`.

- [ ] **Step 7: Prove the contract bites**

Temporarily rename the column in the view. In `src/SportsRankingService/Persistence/Migrations/20260917221403_InitialSchema.cs`, in the `CREATE VIEW dbo.CurrentCountryRankings` statement, change `c.RankedEntrants` to `c.RankedEntrants AS Entrants`. Run

```powershell
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~ViewContractTests"
```

Expected: FAIL with a SQL error naming `RankedEntrants` (invalid column name), raised from `RankingsReader.ReadAsync`. Then revert the migration:

```powershell
git checkout -- src/SportsRankingService/Persistence/Migrations/20260917221403_InitialSchema.cs
git status --short
```

Expected: only the four files of this task are modified or new.

- [ ] **Step 8: Full solution, then commit**

```powershell
dotnet build WorldRankGuesser.slnx
dotnet test WorldRankGuesser.slnx --no-build
git add tests/WorldRankGuesser.Api.Tests/WorldRankGuesser.Api.Tests.csproj tests/WorldRankGuesser.Api.Tests/Integration/SqlServerFixture.cs tests/WorldRankGuesser.Api.Tests/Integration/RankingsSeed.cs tests/WorldRankGuesser.Api.Tests/Integration/ViewContractTests.cs
git commit -m @'
Test the game against the scraper's real rankings view

The SQL fixture applies the scraper's migrations before the game's,
and the seed saves one release per feed through RankingRepository, so
dbo.CurrentCountryRankings is the real view and a change to a column
it projects fails the game's tests. Only the test project references
the scraper.
'@
```

---

### Task 6: Documents and the old repo

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md` (sections 4.1 and 12)
- Modify (scraper repo): `README.md` on `main` there, then archive

**Interfaces:** none; documentation only.

- [ ] **Step 1: CLAUDE.md**

In `CLAUDE.md`:

1. In the "What this is" paragraph, replace the last sentence, `Rankings come from the `dbo.CurrentCountryRankings` view that the separate **SportsRankingService** repo fills weekly; that view is the only link between the repos.`, with:

   ```
   Rankings come from the `dbo.CurrentCountryRankings` view, filled weekly by `src/SportsRankingService`, the scraper imported from its own repo in phase 2 (it has its own `CLAUDE.md`). Coupling rule: `WorldRankGuesser.Api` never references `SportsRankingService`; the view is the only runtime link, and only the SQL test fixture may use the scraper's migrations.
   ```

2. In "Commands", replace the line `# Database: the SQL Server container lives in the SportsRankingService repo (docker compose up -d --wait there).` with:

   ```
   docker compose up -d --wait                                              # local SQL Server 2022 (sa / Rankings_Dev1!, loopback-only)
   dotnet ef database update --project src/SportsRankingService             # the scraper's dbo schema and views; on a new database, before the game's
   dotnet run --project src/SportsRankingService                            # scrape every enabled feed once into the local database (exit 1 if a feed failed)
   dotnet run --project src/SportsRankingService -- --only Soccer           # a subset; see src/SportsRankingService/CLAUDE.md
   dotnet ef migrations add <Name> --project src/SportsRankingService --output-dir Persistence/Migrations   # scraper schema change
   ```

   and keep the existing `dotnet tool restore` and game lines. After the `dotnet test WorldRankGuesser.slnx` line add:

   ```
   dotnet test tests/SportsRankingService.Tests                            # the scraper alone: fixtures and SQLite, no Docker
   ```

3. In "Tests", replace the fragment from `; `RankingsSeed` stands in for the scraper's view` up to and including `in one feed per category.` with:

   ```
   . The fixture applies the scraper's migrations and then the game's, so `dbo.CurrentCountryRankings` is the real view, and `RankingsSeed` writes 12 countries ranked 1–12 in one feed per category through the scraper's `RankingRepository`; `ViewContractTests` pins the view's columns.
   ```

   so the sentence before it now ends at `and a view.`

4. Add a paragraph after "Persistence":

   ```
   **Two migration sets.** The scraper owns schema `dbo` (history table `dbo.__EFMigrationsHistory`, `src/SportsRankingService/Persistence/Migrations`); the game owns `game`. They never share a migration. On a new database apply `dbo` first. `git tag scraper-net8-baseline` marks the scraper as imported, before its move to .NET 11.
   ```

- [ ] **Step 2: README.md**

Replace the bullet `- Rankings are scraped weekly by [SportsRankingService](../SportsRankingService) into SQL Server.` with

```
- `src/SportsRankingService` — the scraper that fills the rankings view weekly ([its README](src/SportsRankingService/README.md)).
```

and step 1 of "Running locally" with

```
1. `docker compose up -d --wait`, then `dotnet tool restore`, `dotnet ef database update --project src/SportsRankingService` and `dotnet run --project src/SportsRankingService` so the database has rankings.
2. `dotnet ef database update --project src/WorldRankGuesser.Api`.
```

renumbering the steps that follow.

- [ ] **Step 3: Parent design**

In `docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md`:

1. Section 4.1: replace the layout block with

   ```
   src/WorldRankGuesser.Api/          minimal API, EF Core, net11.0
   src/WorldRankGuesser.Web/          SvelteKit + TypeScript
   src/SportsRankingService/          the scraper (imported in phase 2; its own CLAUDE.md and README)
   tests/WorldRankGuesser.Api.Tests/  xUnit unit and integration tests; the SQL fixture applies the scraper's migrations too
   tests/SportsRankingService.Tests/  the scraper's fixture-based tests
   tools/                             file-based and standalone tools, outside the solution (country catalog, board simulator, benchmarks)
   Dockerfile                         build web → publish API with web output in wwwroot → runtime image
   docker-compose.yml                 SQL Server for development; the production-shaped stack behind a profile
   infra/                             Bicep
   .github/workflows/                 CI and deploy
   ```

2. Section 12: replace the paragraph starting `**Required outside this repo (SportsRankingService):**` with

   ```
   **The scraper** lives in this repo since phase 2; its image, Job, migrations and .NET 11 move are in `2026-09-19-phase-2-go-live-design.md`.
   ```

- [ ] **Step 3b: The scraper's own CLAUDE.md (added during execution)**

`src/SportsRankingService/CLAUDE.md` still describes the old repo. Change only what the import made false; leave the Architecture, Persistence and Current state sections alone.

1. First paragraph: `A .NET 8 run-once console app` becomes `A .NET 11 run-once console app, imported into the WorldRankGuesser repo on 2026-09-21 (`src/SportsRankingService`),`. Replace the last sentence, from `` `WebScrapingBenchmarks` holds `` to the end of the paragraph, with: `` `tools/WebScrapingBenchmarks` holds BenchmarkDotNet benchmarks (outside the solution) and `tests/SportsRankingService.Tests` holds xUnit tests. ``
2. Replace the whole `## Commands` code block with

   ```powershell
   docker compose up -d --wait                              # local SQL Server 2022 (sa / Rankings_Dev1!, port 1433, loopback-only); the repo root's compose file
   dotnet tool restore                                      # repo-local dotnet-ef (the version in .config/dotnet-tools.json at the repo root)
   dotnet ef database update --project src/SportsRankingService   # apply the dbo migrations (the app never migrates itself); on a new database, before the game's
   dotnet build WorldRankGuesser.slnx                       # the whole solution, this project included
   dotnet run --project src/SportsRankingService            # fetch every enabled feed once, save, exit (0 = all saved, 1 = a feed failed)
   dotnet run --project src/SportsRankingService -- --only "Cricket Women" --only Soccer   # only the feeds whose "Sport Event Gender" name contains a pattern's words in order
   dotnet run --project tools/WebScrapingBenchmarks --configuration Release   # run benchmarks
   dotnet test tests/SportsRankingService.Tests             # parser, resolver, fetcher, snapshot and repository tests; no external network (the curl tests use loopback and need curl on the PATH), SQLite in-memory for the repository
   dotnet ef migrations add <Name> --project src/SportsRankingService --output-dir Persistence/Migrations   # after changing the entities
   ```

   and, in the sentence after it, `` `SportsRankingService.Tests/Fixtures/` `` becomes `` `tests/SportsRankingService.Tests/Fixtures/` ``.
3. Replace the paragraph starting `` `global.json` pins SDK 8.0.0 `` with: `` The project inherits `net11.0` from the repo root's `Directory.Build.props` and `global.json`; EF Core is the same 11.0 build as the game's, and `dotnet-ef` comes from the root tool manifest. `dotnet ef` runs `Program` at design time with `--applicationName` to find the host; `Program.cs` skips the command line when `EF.IsDesignTime` is set, so that argument is not a usage error. ``
4. In the paragraph starting `Which benchmark runs is hardcoded in`, `` `WebScrapingBenchmarks/Program.cs` `` becomes `` `tools/WebScrapingBenchmarks/Program.cs` ``.
5. In `## Persistence`, `` Code-first EF Core in `SportsRankingService/Persistence/` `` becomes `` Code-first EF Core in `Persistence/` ``.
6. In "Adding a feed", `` `SportsRankingService.Tests/Fixtures/` `` becomes `` `tests/SportsRankingService.Tests/Fixtures/` ``.

- [ ] **Step 4: Commit here**

```powershell
git add CLAUDE.md README.md src/SportsRankingService/CLAUDE.md docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md
git commit -m @'
Document the imported scraper and the two migration sets
'@
```

- [ ] **Step 5: Point the old repo at this one, then archive it**

In the scraper repo, on `main` (not on `import-layout`, which keeps the pre-import layout for the tag):

```powershell
cd ..\SportsRankingService
git switch main
```

Prepend to its `README.md`:

```
> **Archived.** This scraper lives in the WorldRankGuesser repo since 2026-09-21
> (`src/SportsRankingService`, full history included). Nothing here is maintained.

```

Then:

```powershell
git add README.md
git commit -m "Point at the WorldRankGuesser repo; this one is archived"
git push origin main
git push origin import-layout scraper-net8-baseline
```

Then archive it on GitHub: repository **Settings → General → Danger Zone → Archive this repository**. It stays private. (`gh` is not installed on this machine; if it is later, `gh repo archive joseph-leo/SportsRankingService` does the same.)

- [ ] **Step 6: Open the pull request**

Back in this repo:

```powershell
cd ..\WorldRankGuesser
git push -u origin phase-2a-scraper-import
```

Open a pull request from `phase-2a-scraper-import` to `main` titled "Import the scraper and move it to .NET 11" with this body:

```
Phase 2a of docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md (section 5).

- The SportsRankingService repo is merged in with its history (tag scraper-net8-baseline marks the import).
- The three projects inherit net11.0; 354 scraper tests pass.
- The game's SQL tests run against the scraper's real dbo.CurrentCountryRankings view.
- The old repo is archived.
```

Expected: CI green (`api` job: the solution now runs both test assemblies, so it needs Docker on the runner, which `ubuntu-latest` has; `web` job unchanged). CI has never run on GitHub before this repo's `main` was pushed (spec 8.9), so expect first-run fixes; fix them on this branch.

Plan 2a is complete when the pull request is merged: `dotnet test WorldRankGuesser.slnx` on `main` runs 354 scraper tests and the game's tests against the real view, and `docker compose up -d --wait` from this repo's root starts the development database.
