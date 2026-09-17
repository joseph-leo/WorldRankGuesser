# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 8 run-once console app that scrapes world sports rankings (FIFA, ICC, World Rugby, BWF, etc.) from public web pages and JSON APIs, normalizes each feed to a `RankingSnapshot` (sport / event / gender / ranking date / entries of position + ISO3 country code), and stores it in SQL Server (`WorldRankGuesser`) as an immutable release only when the feed's content or federation date changed. `WebScrapingBenchmarks` holds BenchmarkDotNet benchmarks and `SportsRankingService.Tests` holds xUnit tests.

## Commands

All commands run from the repo root unless noted.

```powershell
docker compose up -d --wait                              # local SQL Server 2022 (sa / Rankings_Dev1!, port 1433, loopback-only)
dotnet tool restore                                      # repo-local dotnet-ef 8.0.31
dotnet ef database update --project SportsRankingService --startup-project SportsRankingService # apply migrations (the app never migrates itself)
dotnet build SportsRankingService.sln                    # build all projects (warnings are expected; 0 errors)
dotnet run --project SportsRankingService                # fetch every enabled feed once, save, exit (0 = all saved, 1 = a feed failed)
dotnet run --project WebScrapingBenchmarks --configuration Release   # run benchmarks (or WebScrapingBenchmarks\RunBenchmark.bat)
dotnet test SportsRankingService.Tests                   # parser, resolver, snapshot and repository tests; no network, SQLite in-memory for the repository
dotnet ef migrations add <Name> --project SportsRankingService --startup-project SportsRankingService --output-dir Persistence/Migrations   # after changing the entities
```

**Tests are fixture-based.** `SportsRankingService.Tests/Fixtures/` holds one real response per feed with its source URL and capture date in `FIXTURES.md`. Parser tests are pure: `new XParser().Parse(Fixture.Read("..."))`, then assert count and known positions. `RankingSourceRunnerTests` covers the whole pipeline for one item with a fake `IHttpFetcher` keyed by URL, including resolver round-trips. Expect some tests to be **red or skipped on purpose**: a red test with a fixture is the spec for a feed whose site changed markup (write the new parser to make it pass); a skipped test names a feed whose source is dead or blocks scripted clients (find a new source first, then capture a fixture). Never make a red test pass by weakening its assertions, and re-capture a fixture only when the federation changes its format. `InternalsVisibleTo` exposes internals to the test and benchmark projects.

`appsettings.json` and `serviceconfig.json` are copied next to the binaries and the host's content root is `AppContext.BaseDirectory`, so the process runs from any working directory. There is no timer: the process runs every enabled feed once and exits; schedule it weekly with Task Scheduler or cron (README).

`global.json` pins SDK 8.0.0 with `rollForward: latestMajor`, so newer SDKs work. EF Core packages are 8.0.31 across the solution (SqlServer, Design, Sqlite in tests) and `dotnet-ef` is pinned to the same version in `.config/dotnet-tools.json`.

Which benchmark runs is hardcoded in `WebScrapingBenchmarks/Program.cs` (`BenchmarkRunner.Run<ScrapeServiceBenchmark>()`); swap the type to run `CountryUtilBenchmark`. `ScrapeServiceBenchmark` builds the real pipeline from `serviceconfig.json` and hits every enabled live URL, no database.

## Persistence

Code-first EF Core in `SportsRankingService/Persistence/`; migrations live in `Persistence/Migrations/`. Two tables: `RankingReleases` (one immutable row per published ranking table per feed: sport, event, gender, `RankingDate`, `IsFederationDate`, SHA-256 `ContentHash`, `FirstSeenAt`, `LastSeenAt`) and `RankingRows` (`ReleaseId`, `Ordinal`, `Position`, `ISO3` varchar(3), `TeamName`). `RankingRepository.SaveAsync` hashes the snapshot (rows plus the federation date; a scrape date is excluded) and compares it with the feed's newest release (greatest `Id`): equal means only `LastSeenAt` moves (`Unchanged`); different means a new release with its rows (`Inserted`). Nothing is ever updated except `LastSeenAt`, so all releases are the history and the newest per feed is current. The `CurrentRankings` view (raw SQL in the initial migration, not mapped in EF) exposes the newest release per feed for the planned consumer app.

The connection string `WorldRankGuesserConnection` in `appsettings.json` points at the Docker Compose SQL Server with a committed dev password; override it elsewhere via the `ConnectionStrings__WorldRankGuesserConnection` environment variable. Repository tests run on SQLite in-memory through `EnsureCreated`, which is why "newest" is by `Id` and never by ordering on `FirstSeenAt` (SQLite cannot order by `DateTimeOffset`); the view is only verified against the real server. Because the view is raw SQL outside the EF model, a later migration that renames or retypes a column it projects must drop and re-create the view itself, in that same migration, since EF will not notice the mismatch.

## Architecture

**The unit of work is a feed, not a sport.** `serviceconfig.json` holds a flat `Rankings` array of `RankingItem`s (bound to `RankingSourcesOptions`). Each item says what to stamp on the rows (`Sport`, `Event`, `Gender`), where to fetch (`Url`), which parser understands the response (`Source`), and optionally which `UrlResolver` turns the configured URL into the real one. `Enabled: false` plus a `Note` keeps a dead feed documented without fetching it.

**Pipeline per run** (`Program` → `RankingUpdater` → `RankingSourceRunner` → `IRankingRepository`):

1. `Program.cs` builds the generic host (configuration, logging, DI only; no hosted service), runs `IRankingUpdater.UpdateAllAsync` once and returns exit code 1 if any feed failed.
2. `RankingUpdater` runs every enabled item in parallel, catches per item, saves each snapshot through `IRankingRepository` from its own scope, and returns an `UpdateSummary` (feeds, inserted, unchanged, failed).
3. `RankingSourceRunner` resolves the URL (`IUrlResolver` → `ResolvedUrl(Url, RankingDate?)`), fetches it (`IHttpFetcher`), parses it (`IRankingParser` → `ParsedRanking(Entries, RankingDate?)`) and builds a `RankingSnapshot` via `RankingSnapshotBuilder`: entries ordered by position, `Take` applied by position, and the ranking date chosen as parser's date, else resolver's date, else today from `TimeProvider` with `IsFederationDate = false`. A failed fetch yields null; a malformed response throws `ParseException`; an unknown `Source` or `UrlResolver` name throws `InvalidOperationException` because that is a config error.
4. Parsers and resolvers are registered as plain `IRankingParser` / `IUrlResolver` singletons in `AddRankingPipeline`; the runner indexes them by `SourceName` / `Name`. Nothing outside `Persistence/` and `Program.cs` references EF Core.

**Parsers are pure functions of the response.** `Parsing/IRankingParser` returns `ParsedRanking(Entries, RankingDate?)` of `RankEntry(Position, ISO3, TeamName?)` and knows nothing about sport, event or gender. Two base classes do the boilerplate: `JsonRankingParser<TRoot>` deserializes into a small DTO record declared by the subclass (unknown properties are ignored, so declare only what you need; use `[JsonPropertyName]` for snake_case or hyphenated keys) and `HtmlRankingParser` selects rows by XPath and maps each `HtmlNode`. One parser per federation feed shape: `Fih`, `WorldRugby`, `Svns`, `EspnTennis`, `Wta`, `VolleyballWorld`, `Icc`, `FifaV3`, `Fiba`, `Bwf`, `Wbsc`. Country codes are normalized inside the parser because only it knows what the feed emits: `IOCToISO3()` for IOC-style codes, `CountryUtil.TryGetISO3FromCountry` for names or slugs (throw `ParseException` naming the value when it does not resolve, so a new country shows up in the logs and in the fixture test rather than silently vanishing). Feeds that publish a ranking date override `GetRankingDate` on the base class and parse it with `IsoDate.Parse` (throws `ParseException` for a non-ISO value): ICC `rank_date`, WTA `rankedAt`, ESPN `update`, WBSC row `date`, FIBA's `rankingDatesselect` dropdown, World Rugby's root-level `effective.label`. FIH and Volleyball World only stamp file-generation time and return null. FIFA's date comes from the resolver (the `iso` of the chosen schedule entry; the digits in the id are not the ranking date) and WBSC's also from its resolver.

**URL resolvers** handle feeds whose ranking URL depends on a preliminary request; the configured `Url` carries a `{0}` placeholder. `FifaDateId` reads the newest ranking-schedule id from the FIFA page's `__NEXT_DATA__` (`ranking.dates`, newest by ISO timestamp, works for the men's and women's pages alike). `WbscReleaseDate` reads the release-date list embedded in rankings.wbsc.org and takes the newest for the `sportId` in the item's URL, because the WBSC API returns an empty list for any other date. `SvnsSeries` reads the season's series ids from svns.com and picks the one whose World Rugby series metadata `sport` is `mrs` (men) or `wrs` (women). `Identity` is the default.

**Adding a feed.** Capture a real response into `SportsRankingService.Tests/Fixtures/` and list it in `FIXTURES.md`; write the parser test against it (red); add a `<Feed>Parser` subclassing one of the two base classes with a unique `SourceName`; register it in `AddRankingPipeline`; add the item(s) to `serviceconfig.json`. If the URL needs a discovered id or date, add an `IUrlResolver` the same way.

**Country handling decisions.** England, Scotland, Wales and Northern Ireland map to `GBR` when they arrive as names (BWF); FIFA's own `ENG`/`SCO`/`WAL`/`NIR` codes are kept as emitted. West Indies stays `WI` (no ISO3 code). Kosovo is `XKX`. BWF doubles pairs produce one row per partner with the pair's position; "Athlete Independent Neutral" players are dropped.

**Null handling idiom.** `NotNullOrEmpty()` (in `Utilities/NullOrEmptyExtension.cs`) throws `ArgumentNullException` with the caller expression if the value is null or an empty collection; the resolvers use it on page-scraping steps that must not silently succeed.

## Current state

Live on 2026-09-15: 29 feeds enabled and parsing (about 2,500 rows per run), 8 disabled. Disabled with notes in `serviceconfig.json`: the five BWF badminton feeds (Cloudflare returns 403 to .NET's TLS handshake while curl passes; parser and fixtures are ready), IIHF ice hockey (403), ATP doubles (Cloudflare challenge). Persistence was redesigned on 2026-09-15 (spec: `docs/superpowers/specs/2026-09-15-persistence-redesign-design.md`): releases with insert-on-change, federation ranking dates where feeds publish them, Docker Compose SQL Server, code-first migrations, run-once console. No consumer app exists yet; `CurrentRankings` is its intended read model.
