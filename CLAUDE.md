# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A server-authoritative guessing game. The player is dealt ten countries one at a time and assigns each to a different sport category; each pick scores the country's world rank in that category (lower total wins; unranked or below 150th scores 150). `src/WorldRankGuesser.Api` (ASP.NET Core minimal API, .NET 11, EF Core, SQL Server) owns every rule and all state. `src/WorldRankGuesser.Web` (SvelteKit, Svelte 5, TypeScript, static single-page app) only renders what the API returns. Rankings come from the `dbo.CurrentCountryRankings` view that the separate **SportsRankingService** repo fills weekly; that view is the only link between the repos.

Design: `docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md`. Built so far: phases 0–1 (practice mode). Not built yet: deployment, the daily challenge, the timer, streaks, leaderboards, sign-in.

## Commands

```powershell
# Database: the SQL Server container lives in the SportsRankingService repo (docker compose up -d --wait there).
dotnet tool restore                                                      # installs dotnet-ef from .config/dotnet-tools.json
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
npm run test:e2e           # Playwright; starts the API and Vite itself; writes real games into the local database's game schema
npm run gen:api            # after any API contract change: rebuild the API first, then regenerate src/lib/api/schema.d.ts
```

CI fails if the committed OpenAPI document or `schema.d.ts` is out of date. `dotnet build` boots the app without a database to generate the OpenAPI document, so it logs a non-fatal DataProtection key-ring error — expected, not a build failure. Front-end TypeScript is pinned to `^5.9.3` because `openapi-typescript` 7.13 rejects TypeScript 6; `npm ci`/`npm install` need no extra flags.

## Architecture

**The board.** Starting a game draws one country per category and computes the whole country × category grid once (`BoardGenerator`), storing it as immutable JSON on `game.Boards`. Every pick is a lookup on that stored grid, so a rankings refresh never changes a game in progress. A practice game has its own board; a daily challenge (later phase) is one dated board shared by all players.

**Anti-cheat invariants — do not weaken these.** A pick request names only a category; the server applies it to the country at its own `TurnIndex`. `GameStateMapper` is the only code that decides what a response reveals: past picks and the current country, and the grid/optimal score only once the game is complete. A game that is not the caller's is a `404`. `AntiCheatTests` pins all of this. There is deliberately no endpoint that returns rankings.

**Rankings pipeline.** `RankingsReader` (the only code that knows about the view) → `RankingsSnapshotBuilder` → `RankingsStore`, refreshed at startup and hourly by `RankingsRefreshService`; a failed refresh keeps the previous snapshot. The builder computes, per feed, each country's *entry rank* (the published position of its best entry) and *country rank* (competition ranking among countries: 1, 2, 2, 4), keeps the best feed per category separately for each mode, then applies aliases.

**Scoring** lives only in `ScoringEngine`: `min(rank in Scoring:RankMode, Scoring:Cap)`, unranked = cap. Both ranks are stored in every cell, and the mode and cap are stamped on every board, so the two modes can be compared and are never mixed.

**Configuration over code** (`appsettings.json`): the ten categories and the scraper `Sport` values each covers; alias rules (`GBR` inherits the best of `ENG`/`SCO`/`WAL`/`NIR`; the 15 West Indies members inherit `WI` in cricket only); `NotDrawable`; `MinCategoriesRanked`. The category count is never hardcoded: a game has as many turns as its board has categories.

**Countries.** ISO3 identifies a country; display names are the scraper's `TeamName`. Flags need ISO2, which the view lacks, so `Countries/countries.json` is a committed ISO3→ISO2 table generated by `tools/GenerateCountryCatalog/generate.cs`, a file-based `dotnet run <file>.cs` app — this SDK disables reflection-based `JsonSerializer` by default for those, so the script sets `#:property JsonSerializerIsReflectionEnabledByDefault=true`. A drawable code missing from the table fails the rankings load with a message naming the code: add it to the table or to `NotDrawable`.

**Persistence.** The API owns SQL schema `game` (history table `game.__EFMigrationsHistory`) and never touches `dbo`. The view is mapped keyless with `ToView`, so it never appears in migrations. Filtered unique indexes enforce one board per daily date, one daily game per player per date, and one pick per category per game; `Games.RowVersion` plus those indexes make a pick atomic — a losing simultaneous pick becomes a `409` carrying the current state. Some columns (streaks, deadlines, external login) exist for later phases and are unused today.

**Identity.** An anonymous player is created on the first `POST /api/games` (not on page load) and carried in the HttpOnly `wrg_player` cookie via ASP.NET Core cookie authentication; data-protection keys are stored in the database so cookies survive restarts. Same-origin hosting is a design requirement: in development Vite proxies `/api`; in production the API serves the built front end.

**Front end.** `GameStore` (`src/lib/game/gameStore.svelte.ts`) holds the last server state and has no game rules; a `409` or a failed pick means "adopt or reload the server state". The spinner cycles decoy flags from `src/lib/countries/iso2.json` (no rank data) while a pick is in flight and lands when the next country arrives. Types in `src/lib/api/schema.d.ts` are generated — never edit them by hand. `svelte.config.js` is hand-maintained: the current `sv` CLI scaffold no longer emits one, so don't expect it to reappear from a `sv` regeneration.

**Tests.** Pure logic (scoring, snapshot builder, board generator, optimal assignment) is unit-tested without a database. Everything touching SQL runs against a real SQL Server in Testcontainers (xUnit collection `"sql"`), because the model depends on filtered indexes, row versions and a view; `RankingsSeed` stands in for the scraper's view with 12 countries ranked 1–12 in one feed per category. Tests share one database, so never assert on global row counts.
