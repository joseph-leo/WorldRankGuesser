# WorldRankGuesser rebuild: server-authoritative game on ASP.NET Core + Svelte

Date: 2026-09-19
Status: design approved in discussion; awaiting review of this document

## 1. Summary

WorldRankGuesser is rebuilt from a .NET 7 Blazor Server app that scrapes rankings on demand into:

- an **ASP.NET Core minimal API on .NET 11** that owns all game rules and state,
- a **SvelteKit + TypeScript** single-page front end that only renders what the API tells it,
- reading rankings from the SQL Server database that the separate **SportsRankingService** repo fills weekly.

The API and the built front end ship as **one container on one domain**, hosted on Azure Container Apps with Azure SQL. The same image runs under Docker Compose as a VPS fallback.

The game becomes a **daily challenge** (everyone gets the same 10 countries each UTC day, one ranked attempt, two leaderboards) plus unlimited unranked **practice** games. Players are anonymous by default and can optionally sign in to become verified.

## 2. Goals and non-goals

**Goals**

- A real, published game, played mostly on phones.
- A player cannot cheat by opening browser dev tools: the browser never holds rankings, upcoming countries, or scores it could alter.
- Streaks, past scores and history that survive across sessions and cannot be edited by the player.
- Two scoring modes that can be switched by configuration, to compare them in play.
- A codebase that demonstrates full-stack .NET with a modern TypeScript front end and Azure Container Apps.

**Non-goals**

- Preventing a player from looking rankings up in another tab. A per-pick timer raises the cost; nothing removes it.
- Preventing a determined player from pre-playing the daily challenge in a private window. Optional sign-in and a verified leaderboard raise the bar.
- Keeping the rankings secret. They are public data; the aim is only to stop peeking during a game.
- Changes to SportsRankingService. Those are listed in section 12 and get their own spec in that repo.
- Visual design. Look, layout and animation polish is a later pass (phase 5).

## 3. Decisions

| Topic | Decision |
|---|---|
| Runtime | `net11.0` for the API (RC until general availability in November 2026). .NET 10 and 11 both end support in November 2028, and 11 brings runtime async. |
| Front end | SvelteKit, Svelte 5, TypeScript, built to static files (`adapter-static`, SPA fallback, no server rendering). |
| API location | In this repo. The game is the API's only consumer and the rules it implements are game rules. No third solution. |
| Boundary with the scraper | The `dbo.CurrentCountryRankings` view is the only link between the repos. |
| Topology | One container serves `/api/*` and the static front end from one origin. No cross-origin requests, first-party cookies. |
| Hosting | Azure Container Apps (API), Container Apps Job (weekly scraper), Azure SQL free tier, GitHub Container Registry. Docker Compose is the local environment and the VPS fallback. |
| Identity | Anonymous server-side player in an HttpOnly cookie; optional OAuth sign-in later claims that player. |
| Game modes | Daily challenge (ranked, one attempt per UTC day, timed) and practice (unranked, unlimited, untimed). |
| Categories | The 10 from the old `Game.razor`, defined in configuration (section 5.1). |
| Country pool | Only countries ranked in at least one category, after aliases (about 230). |
| Scoring | `min(rank, 150)`; unranked scores 150. Rank is either the entry rank or the country rank, chosen by configuration (section 5.3). |

## 4. Architecture

```
Browser ──https──► Azure Container App (one image, one domain)
                    ├─ /api/*  → ASP.NET Core minimal API (.NET 11)
                    └─ /*      → built SvelteKit files (static, fallback to index.html)
                            │
                            ▼
                    Azure SQL, one database
                     ├─ dbo.*   owned by SportsRankingService (its migrations, its views)
                     └─ game.*  owned by this API (its migrations)
                            ▲
Container Apps Job, weekly ─┘   SportsRankingService, run-once, exit code 0/1
```

### 4.1 Repository layout

The Blazor project is deleted; git history keeps it.

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

### 4.2 Units inside the API

Each unit has one purpose and is testable alone.

| Unit | Responsibility | Depends on |
|---|---|---|
| `RankingsReader` | Reads `dbo.CurrentCountryRankings` into plain rows. | EF Core |
| `RankingsSnapshot` + `RankingsRefreshService` | Immutable in-memory table: for each category and country, the best entry rank and best country rank with the details of the feed that produced them. Rebuilt at startup and hourly. | `RankingsReader`, category and alias configuration |
| `CountryCatalog` | Committed ISO3 → ISO2 table (including `XKX` → `XK`) for flags. The view has no ISO2, and a committed table avoids depending on the host's ICU data, the same choice the scraper made for names. Display names come from the view's `TeamName`. A drawable country missing from the table fails snapshot build loudly. | nothing |
| `ScoringEngine` | Pure function: snapshot cell + rank mode + cap → score. | nothing |
| `BoardGenerator` | Draws 10 countries and computes the 10×10 grid and the optimal score. | `RankingsSnapshot`, `ScoringEngine`, a random source |
| `OptimalAssignment` | Minimum-total assignment of 10 countries to 10 categories (bitmask dynamic programming). | nothing |
| `GameService` | Start, resume, pick; owns turn order, the deadline and completion. | `BoardGenerator`, `GameDbContext`, `TimeProvider` |
| `DailyChallengeService` | Gets or creates the board for a UTC date. | `BoardGenerator`, `GameDbContext`, `TimeProvider` |
| `PlayerService` | Creates players, nickname, streak updates, history; later, claim and merge. | `GameDbContext` |
| `LeaderboardService` | Top 100 and the caller's rank, all or verified. | `GameDbContext` |
| Endpoints | Thin minimal-API handlers that map HTTP to the services. | the services |

Nothing outside `RankingsReader` knows the view exists. Nothing outside `ScoringEngine` knows the scoring rule.

## 5. Game rules

### 5.1 Categories

Configuration maps each category to the scraper's `Sport` values. Category IDs are fixed lowercase names.

| Category ID | Display name | Scraper `Sport` values |
|---|---|---|
| `soccer` | Soccer | Soccer |
| `basketball` | Basketball | Basketball |
| `cricket` | Cricket | Cricket |
| `rugby` | Rugby | Rugby |
| `volleyball` | Volleyball | Volleyball |
| `tennis` | Tennis | Tennis |
| `badminton` | Badminton | Badminton |
| `baseball` | Baseball | Baseball, Softball, Baseball5 |
| `hockey` | Hockey | Field Hockey, Ice Hockey |
| `gymnastics` | Gymnastics | Artistic Gymnastics, Rhythmic Gymnastics |

The number of categories is never hardcoded. A game has as many turns as its board has categories.

### 5.2 Country aliases

Some codes in the data are not countries a player would expect to draw. Configuration holds an alias table:

- `GBR` takes the best result among `GBR`, `ENG`, `SCO`, `WAL` and `NIR` in every category.
- The 15 West Indies cricket members (Antigua and Barbuda, Barbados, Dominica, Grenada, Guyana, Jamaica, St Kitts and Nevis, St Lucia, St Vincent and the Grenadines, Trinidad and Tobago, Sint Maarten, Anguilla, British Virgin Islands, Montserrat, US Virgin Islands) each inherit the `WI` result in `cricket`, in addition to any entry of their own.
- `ENG`, `SCO`, `WAL`, `NIR` and `WI` are never drawn.

Aliases are applied when the snapshot is built, after ranks are computed on the feeds as published.

### 5.3 Scoring

For one feed (one sport, event and gender), two ranks exist for each country:

- **Entry rank**: the `Position` of the country's best-placed entry, as the federation published it. Denmark's best badminton player is world number 3, so Denmark's entry rank is 3.
- **Country rank**: the country's rank among countries in that feed, ordered by best entry. Denmark is the second-best nation, so its country rank is 2. Countries tied on entry rank share a country rank, and the next rank skips accordingly (standard competition ranking). For team sports the two ranks are equal.

A category's rank for a country is the **lowest value across all feeds of that category**, computed separately for each mode.

```
score = min(rank in the configured mode, Cap)      unranked → Cap
```

Configuration:

```json
"Scoring": { "RankMode": "Country", "Cap": 150 }
```

`RankMode` is `Entry` or `Country`. Both ranks are always computed and stored on the board, and the API returns both, so a result can be displayed as "#2 — Viktor Axelsen, world #3" in either mode. The mode and cap are recorded on every board, so games played under different settings are never mixed on one leaderboard and can be compared afterwards.

### 5.4 The board

A board is created once and never changes. It holds the categories, the 10 countries in draw order, the rank mode, the cap, and the 10×10 grid. Each cell holds: score, country rank, entry rank, unranked flag, and the sport, event, gender and competitor of the feed that produced the best rank.

- A **daily** board has a UTC date. All players are scored against the same grid, even if the scraper runs during the day.
- A **practice** game gets a board of its own.

Countries are drawn uniformly without replacement from the pool of countries ranked in at least `MinCategoriesRanked` categories (default 1). The optimal score is computed when the board is created.

### 5.5 Turns and the timer

- The server holds the turn index. The browser learns one country at a time.
- A pick names only a category. The server applies it to the current country.
- Daily games are timed: each turn's deadline is `reveal time + SpinAllowanceSeconds + TurnSeconds`. A pick arriving after the deadline plus `LatencyGraceSeconds` is accepted, marked late, and scores `Cap`.
- Defaults: `TurnSeconds` 20, `SpinAllowanceSeconds` 3, `LatencyGraceSeconds` 2. `TurnSeconds` 0 disables the timer. Practice games are untimed.
- An abandoned daily game can be resumed; the turn that was open when the player left is late.
- A game completes on its last pick. `TotalScore` is the sum of pick scores; lower is better.

### 5.6 Daily challenge and streaks

- A day is a UTC calendar day. The day's board is created by the first request for it; a unique index on the date resolves simultaneous requests.
- One ranked attempt per player per day, enforced by a unique index. Starting the daily again returns the existing game.
- Completing a daily game updates the player's streak: if `LastDailyDate` is yesterday the streak grows by one, if it is today nothing changes, otherwise it restarts at 1. `BestStreak` tracks the maximum.

## 6. API

All routes are under `/api`, all are scoped to the player cookie, and all return JSON. Errors use RFC 9457 problem details.

| Route | Behaviour |
|---|---|
| `POST /games` `{ mode }` | `mode` is `daily` or `practice`. Creates the player if the request has no cookie. Returns the game state. For `daily`, returns the existing attempt if there is one. Rate limited: 30 starts per hour per player and 120 per hour per IP by default, both configurable; exceeding either returns `429`. |
| `GET /games/{id}` | The game state. `404` if the game is not the caller's. |
| `POST /games/{id}/picks` `{ categoryId }` | Applies the pick to the current country. Returns the pick result and either the next country or the final summary. `409` if the category is used, the game is complete, or a concurrent pick won. |
| `GET /daily/{date}/leaderboard?verified=` | Top 100 and the caller's rank. |
| `GET /me` | Nickname, verified flag, streaks, summary statistics. |
| `PUT /me/nickname` | 3 to 24 characters, not unique. |
| `GET /me/history` | The caller's completed games, newest first, paged. |
| `GET /healthz`, `GET /readyz` | Liveness; readiness (database reachable, rankings loaded, view columns present). |

**Game state** contains: game ID, mode, daily date, categories, the picks made so far with their results, the current country (ISO2, ISO3, name) or none if complete, `deadline`, `serverNow`, and on completion the total, the optimal score and the full grid.

**What a response never contains:** a country beyond the current turn, or any grid cell the player has not earned, until the game is complete. For daily boards the grid is only returned to a player whose own attempt is complete.

**OpenAPI:** the API publishes its description with the built-in `Microsoft.AspNetCore.OpenApi`. The front end's types are generated from it.

## 7. Data model

Schema `game`, EF Core migrations with the history table in the same schema. The API's database user has `SELECT` on `dbo.CurrentCountryRankings` and full rights on `game`, nothing else.

| Table | Columns | Constraints and notes |
|---|---|---|
| `Players` | `Id` GUID, `Nickname`, `CreatedAt`, `LastSeenAt`, `ExternalProvider`, `ExternalSubject`, `CurrentStreak`, `BestStreak`, `LastDailyDate` | Unique on (`ExternalProvider`, `ExternalSubject`) where not null. Verified means `ExternalSubject` is set. Streak columns are rebuildable from `Games`. |
| `Boards` | `Id`, `DailyDate`, `RankMode`, `Cap`, `RankingsLoadedAt`, `OptimalScore`, `Content` (JSON), `CreatedAt` | Unique on `DailyDate` where not null. `Content` holds categories, countries in order, and the grid. Immutable. |
| `Games` | `Id` GUID, `PlayerId`, `BoardId`, `Mode`, `DailyDate`, `TurnIndex`, `TurnDeadline`, `StartedAt`, `CompletedAt`, `TotalScore`, `RowVersion` | Unique on (`PlayerId`, `DailyDate`) where `DailyDate` is not null. Index on (`DailyDate`, `TotalScore`) including `StartedAt`, `CompletedAt` for leaderboards. |
| `Picks` | `GameId`, `TurnIndex`, `CategoryId`, `ISO3`, `Score`, `WasLate`, `PickedAt` | Primary key (`GameId`, `TurnIndex`). Unique on (`GameId`, `CategoryId`). |
| `DataProtectionKeys` | EF Core data-protection key store | Keeps cookies valid across container restarts and scale to zero. |

**Pick transaction:** load the game, check ownership, completion, turn and deadline, insert the pick, advance `TurnIndex`, set the next deadline or complete the game and update the streak, save with the row version. A losing concurrent request fails on the row version or a unique index and returns `409`.

**Rankings read model:** a keyless entity mapped to the view with `ToView` and excluded from migrations.

**Leaderboard order:** `TotalScore` ascending, then elapsed time (`CompletedAt - StartedAt`) ascending. The verified leaderboard is the same query joined to verified players, evaluated at query time, so earlier games appear once a player signs in.

## 8. Identity

- ASP.NET Core cookie authentication. The cookie is HttpOnly, Secure, `SameSite=Lax`, with a one-year sliding lifetime, and carries the player ID.
- The player row is created on the first `POST /games`, not on page load.
- Data-protection keys are persisted to the database.

**Sign-in (phase 4):** Google, GitHub and Microsoft through ASP.NET Core's OAuth handlers, using the same cookie.

- If the external identity is new, it is attached to the current anonymous player.
- If it already belongs to a player, the anonymous player's games move to that player. Where both have a daily game on the same date, the verified player's game is kept and the anonymous one is deleted. Streaks are recomputed from `Games`, and the anonymous player row is deleted. All of this runs in one transaction.

## 9. Front end

- SvelteKit, Svelte 5, TypeScript, `adapter-static` with `fallback: 'index.html'`, server rendering off. The API serves the build output and maps unknown routes to `index.html`. In development Vite proxies `/api` to the local API.
- **Routes:** `/` (daily status, streak, practice), `/play/[gameId]`, `/results/[gameId]`, `/leaderboard/[date]`, `/me`.
- **No rules in the browser.** One game-state module holds the latest server state and exposes `start`, `load` and `pick`. `/play/[gameId]` loads from `GET /games/{id}`, so a refresh resumes correctly.
- **Types** are generated from the API's OpenAPI document with `openapi-typescript`; CI fails if the generated file is out of date.
- **Spin and timer:** a click starts the flag spin and sends the pick together. When the response arrives the card reveals the result and the spin lands on the next country. The countdown uses `deadline` and `serverNow` from the server. The spin is skipped under `prefers-reduced-motion`. The decoy countries in the spin come from a static list shipped with the front end that contains no ranks.
- **Flags** are SVGs from `flag-icons`, keyed by ISO2, because Windows browsers do not render flag emoji.
- **Share:** a spoiler-free emoji grid (one square per pick in turn order, coloured by score: ⭐ 1, 🟩 2–10, 🟨 11–50, 🟧 51 to one below the cap, 🟥 the cap; plus the total, the date and the streak) through the Web Share API with a clipboard fallback.
- **Styling:** scoped Svelte CSS and CSS custom properties, mobile first, no UI framework.

## 10. Error handling

- Validation failures return `400` problem details naming the field.
- A game that is not the caller's returns `404`, never `403`, so game IDs cannot be probed.
- Conflicts (used category, completed game, lost race) return `409` with the current game state, so the front end resynchronises instead of showing an error.
- If the rankings snapshot has not loaded, `/readyz` fails and `POST /games` returns `503`. Games in progress are unaffected because their boards are stored.
- A refresh that fails keeps the previous snapshot and logs the error.
- The front end treats any `409` or network failure during a pick as "reload the game state".

## 11. Testing

- **Unit (xUnit):** scoring engine (both modes, cap, aliases, best across feeds, ties), board generator with a seeded random source, optimal assignment against brute force on small boards, streak transitions, claim and merge. Time comes from `TimeProvider`.
- **Integration:** `WebApplicationFactory` against SQL Server in Testcontainers, because the model relies on filtered unique indexes, row versions, JSON columns and a view. The fixture creates a stand-in `dbo.CurrentCountryRankings` with the real column list and seeds it.
- **Anti-cheat, as API tests:** a category cannot be used twice; a pick on another player's game returns `404`; a late pick scores the cap; two simultaneous picks yield one success and one `409`; a second daily start returns the same game; no response contains a future country or an unearned cell; a daily grid is withheld until the caller's attempt is complete.
- **Front end:** Vitest for the game-state module and the share grid; one Playwright test plays a full practice game against the running container.

## 12. Deployment

- **Image:** multi-stage Dockerfile (Node build, `dotnet publish` with the web output in `wwwroot`, slim ASP.NET runtime). Pushed to GitHub Container Registry.
- **Infrastructure (Bicep, `infra/`):** Container Apps environment, the Container App (external ingress, 0 to 2 replicas), the weekly scraper Job, Azure SQL logical server with a free-tier serverless database, Log Analytics. The API authenticates to SQL with a managed identity.
- **Pipelines:** pull requests build and test. Merges to main build and push the image, apply migrations from an EF migrations bundle, and update the Container App. GitHub authenticates to Azure with OIDC federation. The API never migrates itself.
- **Cold starts:** scale to zero and the SQL free tier's auto-pause both delay the first request after idle. Setting minimum replicas to 1 is the documented remedy if it matters.
- **Fallback:** `docker-compose.yml` runs the same image with a SQL Server container.

**The scraper** lives in this repo since phase 2; its image, Job, migrations and .NET 11 move are in `2026-09-19-phase-2-go-live-design.md`.

**Housekeeping:** the Sportradar trial keys in the old `wwwroot/urls.json` remain in git history after the file is deleted. Revoke them at Sportradar.

## 13. Phases

Each phase gets its own implementation plan.

| Phase | Delivers |
|---|---|
| 0. Reset | Blazor project removed; solution layout in section 4.1; `net11.0`; CI that builds and tests; CLAUDE.md rewritten. Local development uses the SportsRankingService SQL Server container, so no compose file is needed yet. |
| 1. Practice game | Rankings snapshot, aliases, both scoring modes, boards, games and picks, anonymous player cookie, Svelte game and results screens, tests in section 11 for what exists. |
| 2. Go live | Dockerfile, `docker-compose.yml` (the image plus SQL Server: the VPS fallback), Bicep, deploy pipeline, Azure SQL, custom domain. The scraper Job runs weekly. |
| 3. Daily challenge | First slice: per-game statistics computed by the server (distance from optimal, the counts of best-sport and optimal picks), kept in history and shown in the share grid as best, optimal or both; see the Statistics decision in the phase 2 design. Then daily boards, timer, one attempt per day, streaks, history, nickname, both leaderboards (the verified one is empty until phase 4), share grid, optimal score on results. |
| 4. Sign-in | OAuth providers, claim and merge, verified flag. |
| 5. Visual polish | Design pass; may overlap phases 3 and 4. |
