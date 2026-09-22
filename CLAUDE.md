# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A server-authoritative guessing game. The player is dealt ten countries one at a time and assigns each to a different sport category; each pick scores the country's world rank in that category (lower total wins; unranked or below 150th scores 150). `src/WorldRankGuesser.Api` (ASP.NET Core minimal API, .NET 11, EF Core, SQL Server) owns every rule and all state. `src/WorldRankGuesser.Web` (SvelteKit, Svelte 5, TypeScript, static single-page app) only renders what the API returns. Rankings come from the `dbo.CurrentCountryRankings` view, filled weekly by `src/SportsRankingService`, the scraper imported from its own repo in phase 2 (it has its own `CLAUDE.md`). Coupling rule: `WorldRankGuesser.Api` never references `SportsRankingService`; the view is the only runtime link, and only the SQL test fixture may use the scraper's migrations.

Design: `docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md`. Built so far: phases 0–1 (practice mode). Not built yet: deployment, the daily challenge, the timer, streaks, leaderboards, sign-in.

## Commands

```powershell
dotnet tool restore                                                      # installs dotnet-ef from .config/dotnet-tools.json
docker compose up -d --wait                                              # local SQL Server 2022 (sa / Rankings_Dev1!, loopback-only)
dotnet ef database update --project src/SportsRankingService             # the scraper's dbo schema and views; on a new database, before the game's
dotnet run --project src/SportsRankingService                            # scrape every enabled feed once into the local database (exit 1 if a feed failed)
dotnet run --project src/SportsRankingService -- --only Soccer           # a subset; see src/SportsRankingService/CLAUDE.md
dotnet ef migrations add <Name> --project src/SportsRankingService --output-dir Persistence/Migrations   # scraper schema change
dotnet ef database update --project src/WorldRankGuesser.Api            # apply the game schema; the API never migrates itself
dotnet ef migrations add <Name> --project src/WorldRankGuesser.Api --output-dir Persistence/Migrations

dotnet build WorldRankGuesser.slnx                                      # also regenerates src/WorldRankGuesser.Web/openapi/*.json
dotnet test WorldRankGuesser.slnx                                       # needs Docker (Testcontainers SQL Server)
dotnet test tests/SportsRankingService.Tests                            # the scraper alone: fixtures and SQLite, no Docker
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~ScoringEngineTests"   # one class; add .Method_name for one test
dotnet run --project src/WorldRankGuesser.Api                           # http://localhost:5170, /readyz says whether rankings loaded
dotnet run tools/SimulateBoards/simulate.cs -- 20000                    # read-only board statistics on the live rankings, both modes; optional 2nd arg overrides the cap
docker compose --profile stack up --build -d                             # the production-shaped stack: migrate (dbo, then game), then the game image on http://localhost:8080 as Production
docker compose run --rm scraper                                          # the scraper image once against the stack's database, after up; append --only ... for a subset
docker compose --profile stack down                                      # stop the stack, the development database container included (its volume stays); docker compose up -d --wait brings the database back
docker build -t worldrankguesser-game . ; docker build -f src/SportsRankingService/Dockerfile -t worldrankguesser-scraper .   # the two images (CI also builds `--target migrate`)

cd src/WorldRankGuesser.Web
npm run dev                # http://localhost:5173, proxies /api and /readyz to the API
npm run check              # svelte-check
npm test                   # Vitest; one file: npm test -- src/lib/game/spin.test.ts
npm run test:e2e           # Playwright; starts the API and Vite itself; writes real games into the local database's game schema
npm run gen:api            # after any API contract change: rebuild the API first, then regenerate src/lib/api/schema.d.ts
$env:E2E_BASE_URL='http://localhost:8080'; npm run test:e2e   # the same Playwright game against a deployed app (the stack, staging); starts no local servers
```

CI fails if the committed OpenAPI document or `schema.d.ts` is out of date. `dotnet build` boots the app without a database to generate the OpenAPI document; data-protection's `PersistKeysToDbContext` is skipped for that build so it never touches a database. Front-end TypeScript is pinned to `^5.9.3` because `openapi-typescript` 7.13 rejects TypeScript 6; `npm ci`/`npm install` need no extra flags.

## Architecture

**The board.** Starting a game draws one country per category and computes the whole country × category grid once (`BoardGenerator`), storing it as immutable JSON on `game.Boards`. A board whose optimal assignment has more than `Game:MaxCapPicksInOptimal` (1) picks scoring the cap is drawn again, up to 50 draws, then used as it is with a logged warning — so a game can always start. Every pick is a lookup on that stored grid, so a rankings refresh never changes a game in progress. A practice game has its own board; a daily challenge (later phase) is one dated board shared by all players.

**Anti-cheat invariants — do not weaken these.** A pick request names only a category; the server applies it to the country at its own `TurnIndex`. `GameStateMapper` is the only code that decides what a response reveals: during a game, which category each past country went to and the current country — never what a pick scored, so a bad draw cannot be seen and abandoned; once the game is complete, the pick scores, the total, the grid, each country's best category, and the optimal score with its assignment (`OptimalAssignment.Solve`, recomputed from the immutable board on read — a true minimum total, not a greedy one). A game that is not the caller's is a `404`. `AntiCheatTests` pins all of this. There is deliberately no endpoint that returns rankings.

**Rankings pipeline.** `RankingsReader` (the only code that knows about the view) → `RankingsSnapshotBuilder` → `RankingsStore`, refreshed in the background by `RankingsRefreshService`: at once after the host starts, then on a 5/10/20/40/60 s backoff until the first snapshot exists (a cold start usually meets a paused database), then every `Rankings:RefreshMinutes`; a failed refresh keeps the previous snapshot, and a view with fewer drawable countries than categories does not count as loaded, because no board can be drawn from it. Host startup never waits for a load: `/readyz` is 503 until the first snapshot, `/healthz` is 200 at once. The builder computes, per feed, each country's *entry rank* (the published position of its best entry) and *country rank* (competition ranking among countries: 1, 2, 2, 4), keeps the best feed per category separately for each mode, then applies aliases.

**Scoring** lives only in `ScoringEngine`: `min(rank in Scoring:RankMode, Scoring:Cap)`, unranked = cap. Both ranks are stored in every cell, and the mode and cap are stamped on every board, so the two modes can be compared and are never mixed.

**Configuration over code** (`appsettings.json`): the ten categories and the scraper `Sport` values each covers; alias rules (`GBR` inherits the best of `ENG`/`SCO`/`WAL`/`NIR`; the 15 West Indies members inherit `WI` in cricket only); `NotDrawable`; `MinCategoriesUnderCap` (a country is drawable only if it scores under `Scoring:Cap` in at least that many categories in *both* rank modes, so the pool never depends on the mode and no drawn country is a guaranteed cap for every player); `MaxCapPicksInOptimal`. The category count is never hardcoded: a game has as many turns as its board has categories.

**Countries.** ISO3 identifies a country; display names are the scraper's `TeamName`. Flags need ISO2, which the view lacks, so `Countries/countries.json` is a committed ISO3→ISO2 table generated by `tools/GenerateCountryCatalog/generate.cs`, a file-based `dotnet run <file>.cs` app — this SDK disables reflection-based `JsonSerializer` by default for those, so the script sets `#:property JsonSerializerIsReflectionEnabledByDefault=true`. A drawable code missing from the table fails the rankings load with a message naming the code: add it to the table or to `NotDrawable`.

**Persistence.** The API owns SQL schema `game` (history table `game.__EFMigrationsHistory`) and never touches `dbo`. The view is mapped keyless with `ToView`, so it never appears in migrations. Filtered unique indexes enforce one board per daily date and one daily game per player per date; a plain unique index enforces one pick per category per game. `Games.RowVersion` plus those indexes make a pick atomic — a losing simultaneous pick becomes a `409` carrying the current state. Some columns (streaks, deadlines, external login) exist for later phases and are unused today.

**Hosting.** `GameDbContext.Configure` retries transient SQL failures (six attempts, 30 s maximum delay): a paused Azure SQL database refuses connections while it resumes. Concurrency conflicts are not transient, so the `409` path never retries. `Hosting:TrustForwardedHeaders` (off by default) puts the forwarded-headers middleware first in the pipeline for `X-Forwarded-For` and `X-Forwarded-Proto`, forward limit 1, every proxy trusted; on only behind an ingress that rewrites those headers, so a directly exposed container cannot be spoofed. Images: the root `Dockerfile` (SvelteKit build → `dotnet publish` with build-time OpenAPI generation off and the web output in `wwwroot` → chiseled "extra" ASP.NET runtime: ICU for SqlClient, non-root, port 8080, no `HEALTHCHECK`; plus a `migrate` target for compose that applies `dbo` then `game`) and `src/SportsRankingService/Dockerfile` (plain runtime with `curl`). Both build from the repo root (`.dockerignore`), and **their .NET tags follow `global.json`: when it moves, they move.** `docker-compose.yml` is the development SQL Server plus the `stack` profile; in the stack `ASPNETCORE_ENVIRONMENT` is `Production`, so the player cookie is `Secure`, which browsers exempt on `localhost`; a VPS needs TLS in front (the commented Caddy service). Both csproj files exclude `appsettings.Development.json` from `dotnet publish`; the scraper's `appsettings.json` still carries its dev-only default connection string into its image, and the `migrate` target is a source build that carries both projects' settings, so neither is a secret-free image — acceptable because the credential is the loopback-only dev password the spec treats as public, and the migrate image is never pushed.

**Two migration sets.** The scraper owns schema `dbo` (history table `dbo.__EFMigrationsHistory`, `src/SportsRankingService/Persistence/Migrations`); the game owns `game`. They never share a migration. On a new database apply `dbo` first. `git tag scraper-net8-baseline` marks the scraper as imported, before its move to .NET 11.

**Identity.** An anonymous player is created on the first `POST /api/games` (not on page load) and carried in the HttpOnly `wrg_player` cookie via ASP.NET Core cookie authentication; data-protection keys are stored in the database so cookies survive restarts. Same-origin hosting is a design requirement: in development Vite proxies `/api` and `/readyz`; in production the API serves the built front end.

**Front end.** `GameStore` (`src/lib/game/gameStore.svelte.ts`) holds the last server state and has no game rules; a `409` or a failed pick means "adopt or reload the server state". The one exception to "server state only" is `pendingPick`: the pick in flight, shown on its card at once (country only, never a score) and dropped when the server answers, so a pick that did not apply reopens its card. The spinner cycles decoy flags from `src/lib/countries/iso2.json` (no rank data) while a pick is in flight and lands when the next country arrives. Before a game can start, the start screen polls `/readyz` through `ServerReadiness` (`src/lib/api/readiness.svelte.ts`): one call at a time, 2 s after each answer, for 90 s, then a retry message; server state, not a game rule. Types in `src/lib/api/schema.d.ts` are generated — never edit them by hand. `svelte.config.js` is hand-maintained: the current `sv` CLI scaffold no longer emits one, so don't expect it to reappear from a `sv` regeneration.

**Tests.** Pure logic (scoring, snapshot builder, board generator, optimal assignment) is unit-tested without a database. `RankingsRefreshServiceTests` drives the backoff on a `FakeTimeProvider`. Everything touching SQL runs against a real SQL Server in Testcontainers (xUnit collection `"sql"`), because the model depends on filtered indexes, row versions and a view. The fixture applies the scraper's migrations and then the game's, so `dbo.CurrentCountryRankings` is the real view, and `RankingsSeed` writes 12 countries ranked 1–12 in one feed per category through the scraper's `RankingRepository`; `ViewContractTests` pins the view's columns. Tests share one database, so never assert on global row counts. Integration tests that start games call `ApiFactory.WaitUntilReadyAsync()`, because the rankings load after the host starts.
