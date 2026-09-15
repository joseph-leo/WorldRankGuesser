# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 8 Worker Service that scrapes world sports rankings (FIFA, ICC, World Rugby, BWF, etc.) from public web pages and JSON APIs, normalizes them to `SportsRanking` rows (sport / event / gender / position / ISO3 country code), and inserts them into a SQL Server database (`WorldRankGuesser`). A second project holds BenchmarkDotNet benchmarks. There are no unit tests.

## Commands

All commands run from the repo root unless noted.

```powershell
dotnet build SportsRankingService.sln          # build both projects (warnings are expected; 0 errors)
dotnet run --project SportsRankingService       # run the worker (needs SQL Server; see below)
dotnet run --project WebScrapingBenchmarks --configuration Release   # run benchmarks (or WebScrapingBenchmarks\RunBenchmark.bat)
```

Run the worker from the `SportsRankingService` directory (or via `--project`) — `ConfigHelper` loads `serviceconfig.json` from `Directory.GetCurrentDirectory()`, and it is **not** copied to the output folder. The file is loaded with `optional: true`, so running from the wrong directory silently yields zero ranking items.

`global.json` pins SDK 8.0.0 with `rollForward: latestMajor`, so newer SDKs work. `Microsoft.EntityFrameworkCore.SqlServer` is pinned to 7.x on a net8.0 project; leave it unless upgrading deliberately.

Which benchmark runs is hardcoded in `WebScrapingBenchmarks/Program.cs` (`BenchmarkRunner.Run<ScrapeServiceBenchmark>()`); swap the type to run `CountryUtilBenchmark`. Benchmarks hit live URLs.

## Database

Connection string `WorldRankGuesserConnection` in `appsettings.json` points at a local SQL Express instance; `Program.cs` appends `;Encrypt=False`. The `DbContext` was reverse-engineered with EF Core Power Tools (`efpt.config.json`, `CodeTemplates/EFCore/*.t4`) from tables `dbo.SportsRankings` and `dbo.SportsRankings_History`. Regenerate with the tool rather than hand-editing `WorldRankGuesserContext`; the `SportsRanking.Builder.cs` partial holds hand-written members (constructor, `IRanking` impl) and survives regeneration.

## Architecture

**Pipeline per tick** (`Worker` → `RankingUpdater` → `WorldRankService` → `IParser` → EF insert):

1. `Worker.ExecuteAsync` loops every ~100s and calls `IRankingUpdater.UpdateRankingsAsync(RankingType.World, ...)`.
2. `RankingUpdater` fans out one task per `WorldSports` enum value, resolves an `IScrapeService` via `IScrapeServiceFactory`, and inserts the results through a scoped `WorldRankGuesserContext` (it uses `IServiceScopeFactory` because the updater is transient and the DbContext is scoped).
3. `WorldRankService` (the only `IScrapeService` today) looks up the sport's `RankingItem` list (URLs + Gender/Event/Sport metadata) from `serviceconfig.json`, fetches each URL in parallel, and hands the raw response to the sport's `IParser`.
4. Each `Parsers/*Parser.cs` turns HTML (HtmlAgilityPack) or JSON (Newtonsoft `JObject`) into `SportsRanking` objects, converting federation-specific IOC codes to ISO3 via `CountryUtil.IOCToISO3`. Parsers usually `switch` on `rankingItem.Sport` / `Event` / `Gender` because one federation site serves several variants with different markup.

**Factory-by-name convention.** `ParserFactory` and `ScrapeServiceFactory` both receive a `Func<IEnumerable<T>>` of every registered implementation and pick the one whose **type name contains the enum name** (`WorldSports.Hockey` → `HockeyParser`, `RankingType.World` → `WorldRankService`). So:
- A new sport needs: an enum member in `WorldSports`, a `<Name>Parser : IParser` class whose name contains that member, a `services.AddTransient<IParser, <Name>Parser>()` line in `ScrapeServiceFactoryExtensions.AddParserFactory`, and a `"<Name>": [ ...RankingItems ]` section in `serviceconfig.json`.
- The enum name, the config section key, and the parser class name must all agree. Commenting out an enum member (as with `Gymnastics`) disables the sport without deleting its parser.

**Config files.** `serviceconfig.json` is keyed by `WorldSports` name → array of `RankingItem`. `serviceconfig_test.json` is a scratch copy for experimenting. An earlier experiment with a generic key-driven JSON converter (`jsonkeys.json`, `JsonKeys`, `RankInfo`, `RankListConverter`) was removed in September 2026 because a recursive first-match key search cannot handle object-wrapped feeds; if a generic JSON path is wanted, use typed DTOs per feed or explicit JSON paths, not key search.

**Soccer is special.** FIFA's ranking API needs a date id; `WorldRankService.GetLatestId` scrapes the FIFA ranking page's embedded page-data `<script>` (the one containing `"dates"`, walking `props.pageProps.pageData.ranking.dates`) for the latest id and `string.Format`s it into the configured URL. `SoccerRankDate` mirrors that JSON.

**Null handling idiom.** `NotNullOrEmpty()` (in `Utilities/NullOrEmptyExtension.cs`) is used everywhere in place of null checks: it throws `ArgumentNullException` with the caller expression if the value is null or an empty collection. Parsers wrap their body in try/catch, log with the sport name, and return an empty list on failure so one bad source does not abort the other sports.

## Current state

All ten parsers are hand-written and active (Gymnastics is disabled via the enum). `IWorldRankRepository` / `WorldRankRepository` exist but are not registered in DI (commented out in `Program.cs`); inserts go directly through the DbContext. Known design debt from the September 2026 review: every tick inserts a full copy of every ranking (no upsert, history table unused, `RankDate` is scrape time not ranking date), `HttpClient` is created per request, `ConfigHelper` rebuilds configuration per sport per tick, and there are no tests.
