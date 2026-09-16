# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 8 Worker Service that scrapes world sports rankings (FIFA, ICC, World Rugby, BWF, etc.) from public web pages and JSON APIs, normalizes them to `SportsRanking` rows (sport / event / gender / position / ISO3 country code), and inserts them into a SQL Server database (`WorldRankGuesser`). `WebScrapingBenchmarks` holds BenchmarkDotNet benchmarks and `SportsRankingService.Tests` holds xUnit tests.

## Commands

All commands run from the repo root unless noted.

```powershell
dotnet build SportsRankingService.sln          # build both projects (warnings are expected; 0 errors)
dotnet run --project SportsRankingService       # run the worker (needs SQL Server; see below)
dotnet run --project WebScrapingBenchmarks --configuration Release   # run benchmarks (or WebScrapingBenchmarks\RunBenchmark.bat)
dotnet test SportsRankingService.Tests          # parser and FIFA date-id tests against saved fixtures; no network
```

**Tests are fixture-based.** `SportsRankingService.Tests/Fixtures/` holds one real response per feed with its source URL and capture date in `FIXTURES.md`. Parser tests are pure: `new XParser().Parse(Fixture.Read("..."))`, then assert count and known positions. `RankingSourceRunnerTests` covers the whole pipeline for one item with a fake `IHttpFetcher` keyed by URL, including resolver round-trips. Expect some tests to be **red or skipped on purpose**: a red test with a fixture is the spec for a feed whose site changed markup (write the new parser to make it pass); a skipped test names a feed whose source is dead or blocks scripted clients (find a new source first, then capture a fixture). Never make a red test pass by weakening its assertions, and re-capture a fixture only when the federation changes its format. `InternalsVisibleTo` exposes internals to the test and benchmark projects.

`serviceconfig.json` is copied next to the binaries and loaded from `AppContext.BaseDirectory` (required, not optional), so the worker runs from any working directory. `Worker:Interval` in `appsettings.json` sets the run cadence (default one day; a run also happens at startup).

`global.json` pins SDK 8.0.0 with `rollForward: latestMajor`, so newer SDKs work. `Microsoft.EntityFrameworkCore.SqlServer` is pinned to 7.x on a net8.0 project; leave it unless upgrading deliberately.

Which benchmark runs is hardcoded in `WebScrapingBenchmarks/Program.cs` (`BenchmarkRunner.Run<ScrapeServiceBenchmark>()`); swap the type to run `CountryUtilBenchmark`. `ScrapeServiceBenchmark` builds the real pipeline from `serviceconfig.json` and hits every enabled live URL, no database.

## Database

Connection string `WorldRankGuesserConnection` in `appsettings.json` points at a local SQL Express instance; `Program.cs` appends `;Encrypt=False`. The `DbContext` was reverse-engineered with EF Core Power Tools (`efpt.config.json`, `CodeTemplates/EFCore/*.t4`) from tables `dbo.SportsRankings` and `dbo.SportsRankings_History`. Regenerate with the tool rather than hand-editing `WorldRankGuesserContext`; the `SportsRanking.Builder.cs` partial holds hand-written members (constructor, `IRanking` impl) and survives regeneration.

## Architecture

**The unit of work is a feed, not a sport.** `serviceconfig.json` holds a flat `Rankings` array of `RankingItem`s (bound to `RankingSourcesOptions`). Each item says what to stamp on the rows (`Sport`, `Event`, `Gender`), where to fetch (`Url`), which parser understands the response (`Source`), and optionally which `UrlResolver` turns the configured URL into the real one. `Enabled: false` plus a `Note` keeps a dead feed documented without fetching it.

**Pipeline per run** (`Worker` → `RankingUpdater` → `RankingSourceRunner` → EF insert):

1. `Worker` runs `IRankingUpdater.UpdateAllAsync` at startup and then every `Worker:Interval` via `PeriodicTimer`; a failed run is logged, never fatal.
2. `RankingUpdater` runs every enabled item in parallel, catches per item, and inserts each item's rows through its own scoped `WorldRankGuesserContext`.
3. `RankingSourceRunner` resolves the URL (`IUrlResolver`), fetches it (`IHttpFetcher` over a named `IHttpClientFactory` client with a browser User-Agent), parses it (`IRankingParser`), and stamps the rows (`RankingMapper`). A failed fetch yields no rows; a malformed response throws `ParseException`; an unknown `Source` or `UrlResolver` name throws `InvalidOperationException` because that is a config error.
4. Parsers and resolvers are registered as plain `IRankingParser` / `IUrlResolver` singletons in `AddRankingPipeline`; the runner indexes them by `SourceName` / `Name`. No factories, no enums, no name matching.

**Parsers are pure functions of the response.** `Parsing/IRankingParser` returns `RankEntry(Position, ISO3, TeamName?)` and knows nothing about sport, event or gender. Two base classes do the boilerplate: `JsonRankingParser<TRoot>` deserializes into a small DTO record declared by the subclass (unknown properties are ignored, so declare only what you need; use `[JsonPropertyName]` for snake_case or hyphenated keys) and `HtmlRankingParser` selects rows by XPath and maps each `HtmlNode`. One parser per federation feed shape: `Fih`, `WorldRugby`, `Svns`, `EspnTennis`, `Wta`, `VolleyballWorld`, `Icc`, `FifaV3`, `Fiba`, `Bwf`, `Wbsc`. Country codes are normalized inside the parser because only it knows what the feed emits: `IOCToISO3()` for IOC-style codes, `CountryUtil.TryGetISO3FromCountry` for names or slugs (throw `ParseException` naming the value when it does not resolve, so a new country shows up in the logs and in the fixture test rather than silently vanishing).

**URL resolvers** handle feeds whose ranking URL depends on a preliminary request; the configured `Url` carries a `{0}` placeholder. `FifaDateId` reads the newest ranking-schedule id from the FIFA page's `__NEXT_DATA__` (`ranking.dates`, newest by ISO timestamp, works for the men's and women's pages alike). `WbscReleaseDate` reads the release-date list embedded in rankings.wbsc.org and takes the newest for the `sportId` in the item's URL, because the WBSC API returns an empty list for any other date. `SvnsSeries` reads the season's series ids from svns.com and picks the one whose World Rugby series metadata `sport` is `mrs` (men) or `wrs` (women). `Identity` is the default.

**Adding a feed.** Capture a real response into `SportsRankingService.Tests/Fixtures/` and list it in `FIXTURES.md`; write the parser test against it (red); add a `<Feed>Parser` subclassing one of the two base classes with a unique `SourceName`; register it in `AddRankingPipeline`; add the item(s) to `serviceconfig.json`. If the URL needs a discovered id or date, add an `IUrlResolver` the same way.

**Country handling decisions.** England, Scotland, Wales and Northern Ireland map to `GBR` when they arrive as names (BWF); FIFA's own `ENG`/`SCO`/`WAL`/`NIR` codes are kept as emitted. West Indies stays `WI` (no ISO3 code). Kosovo is `XKX`. BWF doubles pairs produce one row per partner with the pair's position; "Athlete Independent Neutral" players are dropped.

**Null handling idiom.** `NotNullOrEmpty()` (in `Utilities/NullOrEmptyExtension.cs`) throws `ArgumentNullException` with the caller expression if the value is null or an empty collection; the resolvers use it on page-scraping steps that must not silently succeed.

## Current state

Live on 2026-09-15: 29 feeds enabled and parsing (2,525 rows per run), 8 disabled. Disabled with notes in `serviceconfig.json`: the five BWF badminton feeds (Cloudflare returns 403 to .NET's TLS handshake while curl passes; parser and fixtures are ready), IIHF ice hockey (403), ATP doubles (Cloudflare challenge). Gymnastics was dropped with the old parser set. Remaining design debt: every run inserts a full copy of every ranking (no upsert, `SportsRankings_History` unused, `RankDate` is scrape time not the federation's ranking date), and `IWorldRankRepository` exists but is unused. The connection string in `appsettings.json` names a specific machine.

The September 2026 refactor left the superseded classes in place because file deletion was unavailable to the tool: the old `Parsers/*Parser.cs` for Badminton, Baseball, Basketball, Cricket, Gymnastics, Hockey, Rugby, Soccer, Tennis and Volleyball plus `IParser`, everything under `Factories/`, `Services/WorldRankService.cs`, `Services/IScrapeService.cs`, `Utilities/ConfigHelper.cs`, `Utilities/LogHelper.cs`, `Utilities/ServiceConfigJsonConverter.cs`, `Models/WorldRankInfo.cs`, `Models/RankInfo.cs`, `Utilities/JsonKeys.cs`, `jsonkeys.json`, `serviceconfig_test.json`, `Enums/RankingType.cs`, and the one-line "superseded" test files. None are referenced; delete them.
