# Persistence redesign: Docker SQL Server, code-first schema, releases with history

Date: 2026-09-15. Status: approved design.

## Goal

Replace the current persistence (a reverse-engineered `SportsRankings` table that gets a full
copy of every ranking on every run, stamped with the scrape date) with:

1. A local SQL Server that anyone can start with Docker Compose.
2. A code-first schema owned by EF Core migrations.
3. Immutable ranking *releases* that are only inserted when a feed's content changes, which
   gives current rankings and history from the same two tables.
4. The federation's own ranking date on each release where the feed publishes one.
5. A run-once console process driven by an external scheduler instead of a resident worker.
6. A pipeline that never touches EF types; only a repository in `Persistence/` does.

Out of scope: a consumer application (planned, not built), migrating data from the old
database (it no longer exists), fetch workarounds for BWF / IIHF / ATP.

## Decisions taken with the user

| Question | Decision |
|---|---|
| Other database consumers | Planned, not built. `CurrentRankings` view is its read model; nothing depends on it yet. |
| Entity vs model | Fetch/parse/stamp produce plain records; only `Persistence/` maps them to entities. |
| Applying migrations | Manual `dotnet ef database update`; the app never migrates. |
| Date when the feed has none | Scrape date, with `IsFederationDate = false`. |
| Run cadence | Run once and exit; Task Scheduler / cron runs it weekly. No timer loop. |
| Hosting | Plain console app on `Host.CreateApplicationBuilder`; no `BackgroundService`. |
| SQL Server image | `mcr.microsoft.com/mssql/server:2022-latest`. |
| Table design | Release header + rows, deduplicated by content hash (option A). |

## 1. Environment

`docker-compose.yml` at the repo root:

- service `sql`, image `mcr.microsoft.com/mssql/server:2022-latest`, `ACCEPT_EULA=Y`,
  `MSSQL_SA_PASSWORD=Rankings_Dev1!`, port `1433:1433`, named volume `sqldata` mounted at
  `/var/opt/mssql`, healthcheck running `sqlcmd -S localhost -U sa -P ... -C -Q "SELECT 1"`.
- The dev password is deliberately committed: it protects a local container only. Any other
  environment overrides the connection string through the
  `ConnectionStrings__WorldRankGuesserConnection` environment variable, which the default
  host configuration already reads.

`appsettings.json` connection string:
`Server=localhost,1433;Database=WorldRankGuesser;User Id=sa;Password=Rankings_Dev1!;TrustServerCertificate=True`.
`Program.cs` no longer appends `;Encrypt=False`. The `Worker` section is removed.

Setup from a clean checkout: `docker compose up -d`, `dotnet tool restore`,
`dotnet ef database update --project SportsRankingService`, `dotnet run --project SportsRankingService`.
The migration creates the database when it does not exist.

## 2. EF tooling

- Packages: `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.EntityFrameworkCore.Design`
  8.0.x (deliberate upgrade from 7.x; brings native `DateOnly` mapping),
  `Microsoft.Extensions.Hosting` 8.0.x.
- `.config/dotnet-tools.json` pins `dotnet-ef` 8.0.x as a repo-local tool.
- Migrations in `SportsRankingService/Persistence/Migrations/`. The initial migration
  `InitialSchema` creates `RankingReleases`, `RankingRows` and the `CurrentRankings` view
  (raw SQL in `Up`, `DROP VIEW` in `Down`).
- Design time: EF tools obtain the context through the app's host builder (`Program`), so no
  separate design-time factory unless that proves not to work, in which case an
  `IDesignTimeDbContextFactory` reading `appsettings.json` is added.
- Removed: `efpt.config.json`, `CodeTemplates/`, `DbContext/WorldRankGuesserContext.cs`,
  `Models/SportsRanking.Model.cs`, `Models/SportsRanking.Builder.cs`,
  `Models/SportsRankings_History.cs`, `Models/IRanking.cs`, `Repository/`,
  `Utilities/LogHelper.cs`, `serviceconfig_test.json`, `Worker.cs`,
  `Configuration/WorkerOptions.cs`, and the superseded files already listed in CLAUDE.md.

## 3. Pipeline model

The pipeline gains the ranking date and loses every EF reference.

```csharp
// Parsing/
public sealed record RankEntry(short Position, string ISO3, string? TeamName = null);   // unchanged
public sealed record ParsedRanking(IReadOnlyList<RankEntry> Entries, DateOnly? RankingDate = null);

public interface IRankingParser
{
    string SourceName { get; }
    ParsedRanking Parse(string response);
}
```

- `JsonRankingParser<TRoot>` gets `protected virtual DateOnly? GetRankingDate(TRoot root) => null;`
  `HtmlRankingParser` gets `protected virtual DateOnly? GetRankingDate(HtmlDocument document) => null;`
  Overrides: `Icc` (`rank_date`), `Wta` (`rankedAt` of the first entry), `EspnTennis`
  (`update` on the ranking group), `Wbsc` (row `date`), `Fiba` (first option of the
  `rankingDatesselect` dropdown), `Bwf` (publication date if the payload has one; otherwise
  none). FIH and Volleyball World expose only file-generation time, so they return null.
- A date that fails to parse throws `ParseException` naming the value, same policy as country codes.

```csharp
// Services/UrlResolvers/
public sealed record ResolvedUrl(string Url, DateOnly? RankingDate = null);

public interface IUrlResolver
{
    string Name { get; }
    Task<ResolvedUrl> ResolveAsync(RankingItem item, CancellationToken cancellationToken);
}
```

- `FifaDateIdResolver` returns the `iso` date of the schedule entry it picked.
  `WbscReleaseDateResolver` returns the release date it picked. `Identity` and `SvnsSeries` return null.

```csharp
// Services/
public sealed record RankingSnapshot(
    string Sport, string? Event, string Gender,
    DateOnly RankingDate, bool IsFederationDate,
    IReadOnlyList<RankEntry> Entries);
```

- `RankingSourceRunner.RunAsync` returns `RankingSnapshot?` (null when the fetch failed).
  It picks `parsed.RankingDate ?? resolved.RankingDate`; when both are null it uses
  `DateOnly.FromDateTime(timeProvider.GetLocalNow().Date)` and sets `IsFederationDate = false`.
  `TimeProvider` is injected (`TimeProvider.System` registered in DI).
- `RankingMapper` becomes `RankingSnapshotBuilder` (static): orders by position, applies
  `RankingItem.Take`, stamps sport/event/gender and the date decision.

## 4. Persistence layer (`SportsRankingService/Persistence/`)

Entities (plain classes, configured with `IEntityTypeConfiguration<T>`):

| `RankingRelease` | SQL | Notes |
|---|---|---|
| `Id` int | identity PK | |
| `Sport` | nvarchar(50) not null | |
| `Event` | nvarchar(50) null | |
| `Gender` | nvarchar(10) not null | |
| `RankingDate` | date not null | federation date or scrape date |
| `IsFederationDate` | bit not null | |
| `ContentHash` | binary(32) not null | SHA-256 of the ordered rows |
| `FirstSeenAt` | datetimeoffset not null | when this content was first stored |
| `LastSeenAt` | datetimeoffset not null | bumped on every unchanged re-scrape |
| `Rows` | | navigation to `RankingRow` |

Index `IX_RankingReleases_Feed` on (`Sport`, `Event`, `Gender`, `FirstSeenAt` DESC).

| `RankingRow` | SQL | Notes |
|---|---|---|
| `ReleaseId` int | PK part, FK cascade | |
| `Ordinal` int | PK part | 0-based order in the feed; keeps doubles partners at one position distinct |
| `Position` smallint | not null | |
| `ISO3` | varchar(3) not null | not fixed length: West Indies is `WI` |
| `TeamName` | nvarchar(100) null | |

View `CurrentRankings`: for each (`Sport`, `Event`, `Gender`) the release with the greatest
`FirstSeenAt`, joined to its rows; columns `Sport`, `Event`, `Gender`, `RankingDate`,
`IsFederationDate`, `Position`, `ISO3`, `TeamName`. Not mapped in EF; it is the planned
consumer's read model.

Content hash: `RankingContentHash.Compute(IReadOnlyList<RankEntry>)` returns SHA-256 over the
UTF-8 text `"{Position}\t{ISO3}\t{TeamName}\n"` per entry in order (`TeamName` empty when null).
Only the rows participate, not the date, so a feed that only re-stamps its date does not create
a release. (If a federation republishes identical standings under a new date, the stored
`RankingDate` stays the older one; accepted.)

```csharp
public enum SaveOutcome { Inserted, Unchanged }

public interface IRankingRepository
{
    Task<SaveOutcome> SaveAsync(RankingSnapshot snapshot, CancellationToken cancellationToken);
}
```

`RankingRepository(RankingsDbContext db, TimeProvider clock)`:

1. Load the newest release for the snapshot's feed (`OrderByDescending(FirstSeenAt).FirstOrDefault()`), header only.
2. Compute the hash. If it equals the newest release's hash: set `LastSeenAt = now`, save, return `Unchanged`.
3. Otherwise add a `RankingRelease` with `FirstSeenAt = LastSeenAt = now` and one `RankingRow`
   per entry (`Ordinal` = index), save, return `Inserted`.

An empty snapshot (zero entries) is rejected by the updater before it reaches the repository,
as today.

`RankingUpdater`:

- Per feed: run, then `SaveAsync` through `IRankingRepository` from a fresh scope (context is
  scoped; the updater is transient and runs feeds in parallel).
- Returns `UpdateSummary(int Feeds, int Inserted, int Unchanged, int Failed)`; a feed that
  yielded no rows or threw counts as failed. Log line:
  `"{Feeds} feeds: {Inserted} new releases, {Unchanged} unchanged, {Failed} failed"`.

## 5. Entry point

- `SportsRankingService.csproj` switches from `Microsoft.NET.Sdk.Worker` to `Microsoft.NET.Sdk`
  with `OutputType Exe`. `Microsoft.Extensions.Hosting` stays for configuration, logging,
  options, `IHttpClientFactory` and EF registration.
- `Program.cs`: `Host.CreateApplicationBuilder(args)`, add `serviceconfig.json` from
  `AppContext.BaseDirectory` (required), register `RankingSourcesOptions`, `AddDbContext<RankingsDbContext>`,
  `AddScoped<IRankingRepository, RankingRepository>`, `AddSingleton(TimeProvider.System)`,
  `AddRankingPipeline()`. Build, create a scope, call `UpdateAllAsync` once with a token
  cancelled by Ctrl+C, log the summary, return `summary.Failed > 0 ? 1 : 0`. An unhandled
  exception is logged and returns 1.
- README documents the weekly schedule:
  `schtasks /Create /SC WEEKLY /D MON /ST 06:00 /TN SportsRankings /TR "<path>\SportsRankingService.exe"`
  and `0 6 * * 1 /path/to/SportsRankingService`.

## 6. Testing

- Parser tests: `Parse(...).Entries` replaces the bare list; feeds with a date assert
  `RankingDate` (ICC `2026-09-12`, WTA `2026-09-14`, ESPN `2026-09-10`, WBSC `2026-03-26`,
  FIBA `2026-09-01`); FIH and Volleyball World assert null.
- Resolver tests: FIFA asserts `2026-06-11` from the men's page; WBSC asserts the picked date.
- Runner tests: a fixed `TimeProvider` (`FakeTimeProvider` from
  `Microsoft.Extensions.TimeProvider.Testing`) checks the fallback date and
  `IsFederationDate = false` for FIH, and `true` with the FIFA resolver's date.
- Repository tests on SQLite in-memory (`Microsoft.EntityFrameworkCore.Sqlite`, `EnsureCreated`):
  first save inserts a release with N rows; identical save returns `Unchanged`, bumps
  `LastSeenAt`, adds nothing; changed content returns `Inserted` and the feed now has two
  releases; two entries at the same position are both stored; different feeds do not interfere.
  The SQL view is not covered by these tests.
- One-time verification in the implementing session against the compose container: apply the
  migration, run the app, `SELECT COUNT(*) FROM CurrentRankings`, run again and confirm no new
  releases.
- `ScrapeServiceBenchmark` adapts to `RankingSnapshot?`.

## Documentation

CLAUDE.md: commands (compose, tool restore, ef update), the persistence section (replaces
"Database"), pipeline description (`ParsedRanking`, `ResolvedUrl`, `RankingSnapshot`), run mode,
and the "Current state" design-debt list. README: setup and scheduling.
