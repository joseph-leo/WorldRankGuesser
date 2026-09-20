# Phase 2, go live: one repo, two images, Azure Container Apps

Date: 2026-09-19
Status: **draft, brainstorm in progress.** Sections 1–6 are approved in discussion. Sections 7–9 are proposals that have not been reviewed yet. No implementation plan exists.

**To resume:** start a session in this repo and say "continue the phase 2 brainstorm from `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md`, starting at section 7". Review sections 7, 8 and 9 one at a time, resolve section 10, then finalize this document (delete this paragraph, set the status) and write the implementation plan.

Parent design: `2026-09-19-server-authoritative-rebuild-design.md`, section 12 (deployment) and the phase table in section 13. This document replaces that section's "Required outside this repo" paragraph: the scraper moves into this repo.

## 1. Summary

Phase 2 puts practice mode on the public internet at a guaranteed cost of $0 a month, with a one-parameter switch to about $10 a month fixed when the game has players.

- The **SportsRankingService** scraper is imported into this repo with its history and moved to .NET 11. It stays a separate project, image and SQL schema.
- Two container images are published to GitHub Container Registry: the game (API plus built front end) and the scraper.
- Bicep creates an Azure Container Apps environment, the game app (scale to zero), the scraper as a weekly Job, an Azure SQL free-offer database, Log Analytics and three managed identities.
- Three path-filtered GitHub workflows deploy the game, the scraper and the shared infrastructure. Migrations run from the runner as EF bundles.
- The repo becomes public. The old scraper repo is archived and stays private.
- Three defects that only appear behind a proxy with a pausing database are fixed (section 3).

## 2. Decisions

| Decision | Choice | Why |
|---|---|---|
| Order of phases | Go live before the daily challenge, as the parent design says | Azure surprises surface while the app is small; later phases ship through a working pipeline. |
| Repos | One repo. The scraper is imported here. | Phase 2 is exactly the cross-repo work (one database, one Bicep, ordered migrations, .NET 11). The view contract becomes testable. |
| Coupling rule | `WorldRankGuesser.Api` never references `SportsRankingService`. The `dbo.CurrentCountryRankings` view is the only runtime link. Only the SQL test fixture may use the scraper's migrations. | Keeps the decoupling the two-repo split used to enforce. |
| Scraper hosting | A Container Apps Job on a weekly schedule. Never a background service inside the API. | Scale to zero would kill it mid-run; it needs `curl`; it already runs once and exits. |
| Azure account | A new Microsoft account (the old one is inaccessible). Pay-as-you-go; take the free trial if it is granted. | Nothing in the design needs trial credit. |
| Image registry | GHCR, public images | Container Apps cannot pull from GHCR with a managed identity; public images need no credential. The server-authoritative design means the code holds no secrets. |
| Repo visibility | Public, after the checklist in section 8.5 | Unlimited Actions minutes, portfolio value, public images follow naturally. |
| Cost | Stage 1: free serverless SQL set to auto-pause when the monthly allowance is spent, app scales to zero: $0 guaranteed. Stage 2: Basic DTU database (about $5 a month flat) and one always-on replica: about $10 a month fixed. | A fixed-price tier is a real cap. Azure budgets only alert. |
| Never | Overage billing on the serverless tier | An always-awake serverless database costs about $190 a month at 0.5 vCore. It is the only configuration here with an open-ended bill. |
| Custom domain | An optional Bicep parameter, empty at launch. Decide before phase 3 ships. | The player cookie is bound to the hostname, so changing it after streaks exist resets them. Ad networks also require an owned domain. If the game joins a bigger site, use a subdomain, not a path. |
| Monetization | Out of scope. Its only hook on this phase is the domain. | Ads later need a privacy policy and EU consent: a phase of their own. |
| Benchmarks | `WebScrapingBenchmarks` is imported under `tools/`. The .NET 8 vs .NET 11 runtime-async experiment is a separate spike after go live (section 6). | The existing benchmark crawls live URLs and cannot measure it. |

## 3. Defects found in the current code

Found by reading `Program.cs`, `GameDbContext` and `RankingsRefreshService` against the target hosting. All three are in scope; section 9 has the fixes.

1. **No forwarded headers.** The per-IP rate limit keys on `Connection.RemoteIpAddress`. Behind the ingress every player has the proxy's address, so `GameStartsPerIpPerHour` would be one limit shared by the whole world.
2. **No SQL connection retries.** A paused free-tier database rejects the first connection while it resumes, so the first visitor after idle gets a 500. The code opens no explicit transactions, so a retrying execution strategy is safe.
3. **One startup load, then an hour's wait.** `RankingsRefreshService.StartAsync` loads once and the next attempt is `RefreshMinutes` later. The app and the database go idle together, so a cold start usually meets a paused database, the load fails, and `/readyz` reports not-ready for an hour. `StartAsync` also blocks host startup on that load, which can trip a startup probe.

The free allowance also shapes the design: 100,000 vCore-seconds a month at the 0.5 vCore minimum is about 55 database-awake hours. An always-on replica refreshing hourly would wake the database every hour and spend the month's allowance in under two weeks. So in stage 1 the app must scale to zero and the refresh interval becomes 12 hours.

## 4. Approaches chosen

- **Import:** restructure on a branch in the scraper repo, then `git merge --allow-unrelated-histories`. Rejected: `git subtree add` (file history is awkward across the prefix) and a plain copy (loses the history that explains each parser).
- **Migrations:** EF migrations bundles run from the GitHub runner with Entra authentication, through a temporary SQL firewall rule removed in an `always()` step. Rejected: a migration Job inside Azure (a third image plus start, poll and log plumbing) and self-migration at startup (breaks "the API never migrates itself"; replicas race). Database users are created once by hand from `infra/bootstrap.sql`.
- **Pipeline and Bicep layout:** three Bicep entry points and three path-filtered workflows, so a parser fix never redeploys the game and no workflow needs the other's image tag. Rejected: one `main.bicep` plus `az containerapp update` (a later infra deploy resets both images) and one workflow gated by a paths-filter action (same result, more YAML).

## 5. Repo layout and the import (approved)

```
src/WorldRankGuesser.Api/                 unchanged
src/WorldRankGuesser.Web/                 unchanged
src/SportsRankingService/                 the scraper console app, net11.0
    CLAUDE.md                             the scraper's existing CLAUDE.md (loads only when working in this folder)
    Dockerfile                            section 6
tests/WorldRankGuesser.Api.Tests/         unchanged
tests/SportsRankingService.Tests/         parser fixtures and SQLite repository tests
tools/GenerateCountryCatalog/             unchanged
tools/WebScrapingBenchmarks/              outside the solution, like GenerateCountryCatalog
docs/superpowers/specs|plans/             both repos' documents (dated names, no collisions)
Dockerfile                                the game image
docker-compose.yml                        SQL Server for development plus the production-shaped stack
infra/                                    section 7
.github/workflows/                        ci.yml plus three deploy workflows
```

Steps:

1. **Tidy the scraper repo.** Confirm its local branches (`bwf-curl-fetcher-iihf-wikipedia`, `persistence-redesign`) are merged into `main` (they were on 2026-09-19) and push `main`, which was 45 commits ahead of `origin`.
2. **Restructure there** on an `import-layout` branch: `git mv` everything to the paths above; delete what this repo already provides (`.sln`, `global.json`, `.gitignore`, `.gitattributes`, `.config/dotnet-tools.json`, keeping the newer `dotnet-ef` here). `docker-compose.yml` moves as it is.
3. **Tag** the tip of that branch `scraper-net8-baseline` (section 6, benchmarks).
4. **Merge here** with `--allow-unrelated-histories`; add both scraper projects to `WorldRankGuesser.slnx`.
5. **Move to .NET 11 in its own commit.** Both projects drop `<TargetFramework>` and inherit `Directory.Build.props`. EF Core, `Microsoft.Extensions.*` and test packages move to the versions the game uses. New nullable warnings are fixed, not suppressed wholesale; if the count is very large, a per-project `<Nullable>` override with a note. Accepted when all scraper tests pass and one live run saves every enabled feed to the local database.
6. **Archive the old repo** with a README pointer. It stays private.

Rules and documents that change:

- Root `CLAUDE.md`: the coupling rule from section 2 replaces "the only link between the repos"; the Commands section loses "the SQL Server container lives in the SportsRankingService repo" and gains the scraper's run-once and add-migration commands.
- **Two independent migration sets.** The scraper owns `dbo` (`dbo.__EFMigrationsHistory`), the game owns `game` (`game.__EFMigrationsHistory`). On a new database `dbo` goes first.
- **View-contract test.** `tests/WorldRankGuesser.Api.Tests` references the scraper project. Its Testcontainers fixture applies the scraper's real migrations in place of the hand-written stand-in view, and `RankingsSeed` inserts `RankingReleases` and `RankingRows` so the real view yields the 12 seeded countries. Renaming a column the view projects then fails the game's tests. This is the only reference between the two, and only tests have it.
- Parent design: section 4.1 gets this layout; section 12's "Required outside this repo" paragraph is replaced by a pointer to this document.

`dotnet test WorldRankGuesser.slnx` now also runs the scraper's tests (fixtures and in-memory SQLite, no network).

## 6. Images, compose and benchmarks (approved)

Both images build with the repo root as context (both need `global.json` and `Directory.Build.props`). One `.dockerignore`: `node_modules`, `bin`, `obj`, `.git`, `build`, `.svelte-kit`.

**Game image, `Dockerfile`, three stages**

| Stage | Base | Does |
|---|---|---|
| `web` | `node:24` | `npm ci`, `npm run build` (`adapter-static` writes `build/`). Uses the committed `schema.d.ts`; never regenerates API types. |
| `api` | .NET 11 SDK pinned to the RC in `global.json` | `dotnet publish -c Release` with build-time OpenAPI generation switched off (it writes into the Web folder and boots the app). Copies the `web` output into `wwwroot`. |
| runtime | ASP.NET chiseled "extra" | Non-root, no shell, port 8080. "extra" because `Microsoft.Data.SqlClient` refuses globalization-invariant mode and only "extra" ships ICU. No `HEALTHCHECK`; Container Apps probes the app. |

**Scraper image, `src/SportsRankingService/Dockerfile`, two stages.** SDK publish, then the plain .NET `runtime` image, not chiseled, because `CurlFetcher` starts `curl` as a process: `apt-get install curl ca-certificates`. Non-root `app` user. Runs once and exits; `Program.cs` already returns 1 on failure, which marks a Job run failed.

Scraper risks, each with a check in the plan:

1. **curl's TLS fingerprint differs on Linux.** The BWF feeds pass Cloudflare with Windows curl (Schannel); Debian's curl uses OpenSSL. Check: run the container locally and confirm all 7 curl-fetched feeds (5 BWF, 2 ice hockey) parse. If not, try a curl build with another TLS backend first.
2. **Datacenter addresses are challenged more than home addresses.** Check: compare per-feed results of the first Azure run with a local run. A failed feed keeps its previous release, so the game runs on slightly stale data. If feeds stay blocked, run the scraper image on a schedule from a home machine against Azure SQL; nothing else changes.

**`docker-compose.yml`, one file with profiles**

- `sql`, no profile: the scraper repo's service unchanged (container name, volume, `127.0.0.1:1433`, healthcheck), so existing local data survives and `docker compose up -d --wait` stays the development command.
- `game` and `scraper`, `profiles: ["stack"]`: built from the two Dockerfiles, SQL-auth connection strings to `sql`. `game` on `127.0.0.1:8080` with `ASPNETCORE_ENVIRONMENT=Production`. `scraper` runs on demand: `docker compose run --rm scraper`.
- `migrate`, also `stack`: a one-shot SDK-image service that applies `dbo` then `game` before `game` starts. On a VPS it is an explicit `docker compose run migrate`.
- The Production cookie is `Secure`. Browsers exempt `http://localhost`, so the local stack works. A VPS needs TLS in front: a commented Caddy service shows how.

Accepted when, from a clean clone, `docker compose --profile stack up --build` brings up the database, applies both migration sets and serves a playable practice game on `localhost:8080`; `docker compose run --rm scraper` fills the view; and CI builds both images on every pull request without pushing.

**Benchmarks.** Phase 2 imports the project under `tools/`, creates the `scraper-net8-baseline` tag, moves BenchmarkDotNet off 0.13.11 to a version that knows .NET 11, and confirms it builds. The experiment is a later spike: an offline benchmark (the real `RankingSourceRunner` over the recorded test fixtures with a stub fetcher that yields asynchronously), measured on .NET 8 (a worktree at the tag), .NET 11, and .NET 11 with runtime async on (a compile-time switch, so a separate build). The existing `ScrapeServiceBenchmark` fetches 54 live URLs per iteration: network latency swamps the effect, and repeated full crawls invite blocking. Expect a modest result, mostly fewer allocations per await.

## 7. Azure infrastructure, identity and cost stages (proposed, not reviewed)

One resource group, one region (section 10), everything created by Bicep so deleting the group removes the deployment.

**`infra/main.bicep`, shared**

- Log Analytics workspace, 30-day retention, a daily ingestion cap that keeps it inside the free 5 GB a month.
- Container Apps environment, Consumption only, no virtual network.
- SQL logical server with **Entra-only authentication**; the admin is the owner's account (object ID parameter). No SQL password exists anywhere in Azure.
- The database, by parameter `sqlSku`: `free` (General Purpose serverless, 0.5 to 1 vCore, free limit on, exhaustion behaviour **auto-pause**, shortest auto-pause delay) or `basic` (Basic DTU).
- Firewall: the "allow Azure services" rule. A Consumption environment without a virtual network has no fixed outbound address. This admits connections from any Azure tenant to the login endpoint, which is acceptable only because authentication is Entra-only.
- Three user-assigned managed identities: `id-game`, `id-scraper`, `id-deploy`. `id-deploy` has Contributor on the resource group and a **federated credential** for this repo's `production` GitHub environment, so GitHub logs in with OIDC and no app registration or secret exists.
- A budget on the resource group with an alert at about $5.

**`infra/game.bicep`**: the Container App. Parameters: `image`, `minReplicas` (0 in stage 1, 1 in stage 2), `customDomain` (empty by default). External ingress to 8080, HTTPS only, 0 to 2 replicas on an HTTP scale rule, 0.25 vCPU and 0.5 GiB, identity `id-game`. Settings: the connection string with `Authentication=Active Directory Managed Identity` and the identity's client ID, a 60-second connect timeout, forwarded headers on, `Rankings:RefreshMinutes=720`. **All three probes use `/healthz`, never `/readyz`**: if readiness followed `/readyz`, a visitor arriving while the database resumes would get an ingress error instead of the front end's waking-up state. With `customDomain` set, the app gets the hostname and a free managed certificate; the DNS records are manual and documented, and must exist before that deploy.

**`infra/scraper.bicep`**: the Job. Parameters: `image`, `cron` (default weekly, Monday 06:00 UTC). 0.5 vCPU and 1 GiB, 30-minute timeout, one retry, identity `id-scraper`. `az containerapp job start` runs it on demand.

**`infra/bootstrap.sql`**, run once by the owner in the portal query editor after the first `main.bicep` deploy: `CREATE USER ... FROM EXTERNAL PROVIDER` for the three identities. `id-game`: `SELECT` on `dbo.CurrentCountryRankings` only (ownership chaining covers the tables under the view) and read-write on schema `game`. `id-scraper`: read-write on schema `dbo`. `id-deploy`: `db_owner`, for migrations.

**First deploy is by hand** (`az deployment group create` as the owner), because `id-deploy` does not exist until `main.bicep` has run. `infra/README.md` is the runbook: create the account and subscription, create the resource group, deploy `main.bicep`, run `bootstrap.sql`, set the GitHub `production` environment and the three non-secret variables (client, tenant and subscription IDs).

Data-protection keys stay in the `game` schema, unencrypted at rest beyond the database's own encryption. The framework logs a warning about this; accepted, since only `id-game` and `id-deploy` can read them.

| Stage | Database | App | Cost | Behaviour |
|---|---|---|---|---|
| 1, launch | `sqlSku=free` | `minReplicas=0` | $0, guaranteed | 30 to 60 second first load after idle. If the allowance runs out, the front end's waking-up state times out into a "resting until the 1st" message. |
| 2, when it has players | `sqlSku=basic` | `minReplicas=1` | about $10 a month, fixed | No pauses, no cold starts, no allowance. S0 (about $15) is the next fixed step if 5 DTUs become the bottleneck. |

## 8. Pipelines, migrations and going public (proposed, not reviewed)

### 8.1 CI

`ci.yml` keeps running everything on every pull request (the scraper's tests arrive through the solution). It gains a job that builds both images without pushing, and a `workflow_call` trigger so the deploy workflows reuse it.

### 8.2 `deploy-game.yml`

On pushes to `main` that touch `src/WorldRankGuesser.*/**`, `Dockerfile`, `infra/game.bicep`, `Directory.Build.props` or `global.json`, and on manual dispatch. Jobs: call `ci.yml`; then, in the `production` environment:

1. Build and push `ghcr.io/joseph-leo/worldrankguesser-game:<sha>`.
2. Build the `game` migrations bundle (self-contained, `linux-x64`).
3. Log in to Azure with OIDC; add a SQL firewall rule for the runner's address.
4. Run the bundle with `Authentication=Active Directory Default`.
5. Remove the firewall rule (`if: always()`).
6. `az deployment group create -f infra/game.bicep -p image=<sha>`.
7. Smoke test: poll `/readyz` for up to three minutes; fail the run if it never goes ready.

Migrations run before the new revision starts, so every migration must work with the revision already running (add first, remove in a later deploy).

### 8.3 `deploy-scraper.yml`

The same shape for `src/SportsRankingService/**`, its tests and `infra/scraper.bicep`: image, the `dbo` bundle, `scraper.bicep`. A manual-dispatch input starts the Job once after deploying.

### 8.4 `infra.yml`

On pushes to `main` that touch `infra/main.bicep`, and on manual dispatch: deploys `main.bicep`. No what-if on pull requests.

### 8.5 Going public, in this order

1. Revoke both Sportradar trial keys at Sportradar. They are in this repo's history and, after the import, in the scraper's too. Revocation is the fix; history is not rewritten.
2. Run `gitleaks` over the full history after the import. The 2026-09-19 regex pass over both repos found only those keys and the dev-only `Rankings_Dev1!` password (a container bound to `127.0.0.1`; never reused). All 82 commits use the GitHub `noreply` address.
3. Turn on secret scanning and push protection.
4. Require approval before workflows from outside contributors run. Forked pull requests get no OIDC token and cannot reach the `production` environment.
5. Decide the licence (section 10) and refresh the README.
6. Change the visibility.
7. After the first image push, set both GHCR packages to public (a one-time manual step per package).

### 8.6 Order of work in the phase

Import and .NET 11 (section 5) → images and compose (section 6) → app changes (section 9) → push `rebuild`, CI green on a pull request, fast-forward `main` → go public (8.5) → Azure bootstrap (section 7) → workflows → first deploys: scraper, one Job run, then the game → custom domain when chosen.

`rebuild` had never been pushed on 2026-09-19 and `ci.yml` had never run on GitHub; expect first-run fixes.

## 9. App changes and testing (proposed, not reviewed)

**Forwarded headers.** `UseForwardedHeaders` first in the pipeline, for `X-Forwarded-For` and `X-Forwarded-Proto`, forward limit 1, behind the setting `Hosting:TrustForwardedHeaders`: off by default, on in Azure. With it on, known proxies and networks are cleared, because the container is reachable only through the environment's ingress. With it off, a directly exposed container (compose, VPS without the setting) never trusts a spoofed header. Test: with the setting on, two `X-Forwarded-For` addresses get separate per-IP limits; with it off, the header is ignored.

**SQL retries.** `EnableRetryOnFailure` in `GameDbContext.Configure` (six attempts, 30 seconds maximum delay). `DbUpdateConcurrencyException` is not transient and is never retried, so the `409` path is unchanged. A retry after a commit whose acknowledgement was lost lands on the unique indexes and becomes the existing conflict path.

**Rankings refresh.** `StartAsync` no longer loads. `ExecuteAsync` attempts at once; while no snapshot exists it retries on a backoff of 5, 10, 20, 40, then 60 seconds; after the first success it waits `RefreshMinutes`. It already takes `TimeProvider`, so the test drives a fake clock: a reader that fails twice then succeeds yields a snapshot in about 15 fake seconds. Integration tests that assumed "a started host has rankings" get a wait-for-ready helper in the fixture.

**Front end: waking up.** Before starting a game the start screen checks `/readyz`. While it is not ready: "Waking up the server…", polling every 2 seconds for up to 90, then a plain message with a retry button that also covers a spent allowance. This is server state, not a game rule, so `GameStore` stays rule-free. Vitest covers the polling state; the Playwright game is unchanged.

**Phase 2 is accepted when:** a full practice game plays at the public URL from a cold start, going ready within 90 seconds; the weekly Job has succeeded once in Azure and its per-feed results match a local run (or the differences are recorded); a parser-only commit deploys only the scraper; the budget alert exists; the repo and both packages are public; the old repo is archived.

## 10. Open items

Decisions for the owner:

- Azure region.
- Custom domain: a subdomain of the existing gamer-tag domain, or a new domain. Needed before phase 3 ships.
- Licence for the public repo (none means all rights reserved).

To verify while planning or building, all stated from memory in discussion:

- Azure prices, and that a free-offer database scales to Basic in place (fallback: a database copy).
- Image tags for the .NET 11 RC on Microsoft's registry.
- `Authentication=Active Directory Default` picks up the runner's OIDC `az login`.
- The default visibility of a GHCR package pushed from Actions.
- A BenchmarkDotNet version that supports .NET 11, and the runtime-async switches for the installed RC (spike).
- What `POST /api/games` returns today when no snapshot exists; the waking-up state relies on it or on `/readyz`.
