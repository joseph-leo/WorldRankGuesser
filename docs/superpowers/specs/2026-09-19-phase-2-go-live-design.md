# Phase 2, go live: one repo, two images, two Azure environments

Date: 2026-09-19, completed 2026-09-21
Status: **approved design.** Every section was approved in discussion and the owner's decisions are made (section 2). No implementation plan exists yet.

Parent design: `2026-09-19-server-authoritative-rebuild-design.md`, section 12 (deployment) and the phase table in section 13. This document replaces that section's "Required outside this repo" paragraph: the scraper moves into this repo.

## 1. Summary

Phase 2 puts practice mode on the public internet at a guaranteed cost of $0 a month, with a private staging copy beside it and a two-parameter switch to about $10 a month fixed when the game has players.

- The **SportsRankingService** scraper is imported into this repo with its history and moved to .NET 11. It stays a separate project, image and SQL schema.
- Two container images are published to GitHub Container Registry: the game (API plus built front end) and the scraper.
- Bicep creates two environments from the same templates, **staging** and **production**, each with a Container Apps environment, the game app (scale to zero), the scraper as a weekly Job, an Azure SQL free-offer database, Log Analytics and its own managed identities. Staging is private behind an IP allow-list and is built first; both cost $0.
- Three path-filtered GitHub workflows deploy the game, the scraper and the shared infrastructure. A merge to `main` deploys to staging; fast-forwarding `prod` to a commit of `main` promotes the images that passed staging to production, which never builds. Migrations run from the runner as EF bundles.
- The repo becomes public. The old scraper repo is archived and stays private.
- Three defects that only appear behind a proxy with a pausing database are fixed (section 3).

## 2. Decisions

| Decision | Choice | Why |
|---|---|---|
| Order of phases | Go live before the daily challenge, as the parent design says | Azure surprises surface while the app is small; later phases ship through a working pipeline. |
| Repos | One repo. The scraper is imported here. | Phase 2 is exactly the cross-repo work (one database, one Bicep, ordered migrations, .NET 11). The view contract becomes testable. |
| Coupling rule | `WorldRankGuesser.Api` never references `SportsRankingService`. The `dbo.CurrentCountryRankings` view is the only runtime link. Only the SQL test fixture may use the scraper's migrations. | Keeps the decoupling the two-repo split used to enforce. |
| Scraper hosting | A Container Apps Job on a weekly schedule. Never a background service inside the API. | Scale to zero would kill it mid-run; it needs `curl`; it already runs once and exits. |
| Environments | Local, staging, production. Staging and production are separate resource groups from the same Bicep; staging is built first. | The pipeline and the releases are worked out before production exists, and production's first deploy is a rehearsed one. "Staging" because every release passes through it; unfinished work stays local. |
| Staging privacy | An IP allow-list on the ingress | Free, no code, and staging behaves exactly like production. Rejected: a sign-in wall from Container Apps' built-in authentication (a sidecar in front of every request, an app registration and a secret, and in the way of phase 4's sign-in) and an unlisted URL (the repo's public workflow logs show it). |
| Release flow | A merge to `main` deploys to staging. Fast-forwarding `prod` to a commit of `main` promotes to production the image that passed staging. | Section 4. |
| Statistics | Distance from optimal, best and optimal pick counts, and those marks in the share grid wait for the first slice of phase 3. | They derive from `Games.TotalScore`, `Boards.OptimalScore`, `Picks` and the immutable board, so games played before then can be backfilled, and they make a good first release through the new pipeline: a migration, a contract change and a front-end change. That spec must move the counting from `results.ts` to the server and define "optimal pick" for a board with more than one optimal assignment. |
| Azure account | A new Microsoft account (the old one is inaccessible). Pay-as-you-go; take the free trial if it is granted. | Nothing in the design needs trial credit. |
| Image registry | GHCR, public images | Container Apps cannot pull from GHCR with a managed identity; public images need no credential. The server-authoritative design means the code holds no secrets. |
| Repo visibility | Public, after the checklist in section 8.8 | Unlimited Actions minutes, portfolio value, public images follow naturally. |
| Cost | Stage 1: free serverless SQL set to auto-pause when the monthly allowance is spent, app scales to zero: $0 guaranteed. Stage 2: Basic DTU database (about $5 a month flat) and one always-on replica: about $10 a month fixed. | A fixed-price tier is a real cap. Azure budgets only alert. |
| Never | Overage billing on the serverless tier | An always-awake serverless database costs about $190 a month at 0.5 vCore. It is the only configuration here with an open-ended bill. |
| Region | East US 2 for everything | The first free database fixes the region of every later one in the subscription. The game is turn-based and cold starts dominate latency, so nearest the owner wins. Plain East US has often refused new SQL servers on new subscriptions. |
| Custom domain | A subdomain of the owner's existing domain, on production from its first deploy. Staging keeps its default hostname. The hostname is a value in `infra/production/game.bicepparam`. | The player cookie is bound to the hostname, so changing it after streaks exist resets them; launching on the final name means it never changes. Ad networks also require an owned domain. A subdomain, not a path, if the game ever joins a bigger site. |
| Licence | None at launch: all rights reserved, and the README says so. | The code stays readable, which is the portfolio value, but nobody may rehost the game, which matters if it carries ads later. A licence can be added later and never taken back. |
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
- **Migrations:** EF migrations bundles run from the GitHub runner with Entra authentication, through a temporary SQL firewall rule removed in an `always()` step. Rejected: a migration Job inside Azure (a third image plus start, poll and log plumbing) and self-migration at startup (breaks "the API never migrates itself"; replicas race). Database users are created by hand, once per environment, from `infra/bootstrap.sql`.
- **Pipeline and Bicep layout:** three Bicep entry points and three path-filtered workflows, so a parser fix never redeploys the game and no workflow needs the other's image tag. Rejected: one `main.bicep` plus `az containerapp update` (a later infra deploy resets both images) and one workflow gated by a paths-filter action (same result, more YAML).
- **Release flow:** `main` deploys to staging and a fast-forward of `prod` deploys to production, promoting the image tagged `staged-<hash>` after staging's smoke test (section 8.1). Staging's database therefore receives exactly `main`'s migrations in `main`'s order, the sequence production will get; tests gate staging through the pull request with no second run; and one deliberate act releases the whole repo. Rejected: `stage/*` branches deploy to staging and `main` to production (staging's database drifts when a branch with a migration is abandoned, and the merged inputs only equal the staged ones under an up-to-date rule), though `stage/*` survives as the optional way to try unmerged work; an approval gate on the run that deployed staging (with three workflows, pending approvals pile up and an old one can be approved after a new one); and promoting by commit SHA (path filters mean the tip of `prod` often has no image of its own, and a SHA tag says "built", not "passed staging").

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
- Root `CLAUDE.md` also gains the release flow of section 8.1: `main` deploys to staging, `prod` is only ever fast-forwarded and deploys to production, `stage/*` is for trying unmerged work in Azure, and production never builds. This lands with the workflows in plan 2c, when those branches exist; plan 2a deliberately left it out.

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

## 7. Azure infrastructure, environments, identity and cost stages (approved)

Two environments, **staging** and **production**, are built from the same Bicep with one folder of parameter files each; local development is the third tier and needs nothing here. Each environment is one resource group (`rg-wrg-<env>`) in the same subscription, in East US 2, so deleting the group removes the environment. Staging is a private, production-shaped copy that every release passes through (section 8). It is built first and accepted against section 9; production is then the same templates with the other folder's parameters.

Both environments cost $0 (checked against Microsoft's documentation on 2026-09-21). The SQL free offer allows 10 databases per subscription, each with its own monthly allowance. The Container Apps free grant (180,000 vCPU-seconds, 360,000 GiB-seconds and 2 million requests a month) is per subscription, so the two share it: about 200 replica-hours at 0.25 vCPU. **The first free database fixes the region of every later free database in the subscription**, so the region was chosen before staging exists (section 2) and staging's first deploy is what commits to it.

```
infra/bootstrap.bicep          subscription scope; run by the owner once per environment
infra/main.bicep               the shared resources of one environment
infra/game.bicep               the Container App
infra/scraper.bicep            the Job
infra/staging/                 main.bicepparam, game.bicepparam, scraper.bicepparam
infra/production/              the same three (a .bicepparam file binds to one template)
infra/bootstrap.sql            database users; run by the owner once per environment
infra/README.md                the runbook
```

**`infra/bootstrap.bicep`**: the resource group; the user-assigned identity `id-deploy`; its **federated credential** for this repo's GitHub environment of the same name, so GitHub logs in with OIDC and no app registration or secret exists; and Contributor for `id-deploy` on that group only, so a staging deploy has no rights in production. It is separate from `main.bicep` because a Contributor cannot write role assignments: a template that contains one can only be deployed by the owner, and everything else must stay deployable by `id-deploy`. It also creates `id-monitor`: Reader on the group and a federated credential for the `main` branch, for the scheduled scraper check.

**`infra/main.bicep`**, every name carrying the environment

- Log Analytics workspace, 30-day retention, a daily ingestion cap by parameter. The two caps together stay inside the free 5 GB a month.
- Container Apps environment, Consumption only, no virtual network.
- SQL logical server with **Entra-only authentication**; the admin is the owner's account (object ID parameter). No SQL password exists anywhere in Azure.
- The database, by parameter `sqlSku`: `free` (General Purpose serverless, 0.5 to 1 vCore, free limit on, exhaustion behaviour **auto-pause**, shortest auto-pause delay) or `basic` (Basic DTU).
- Firewall: the "allow Azure services" rule. A Consumption environment without a virtual network has no fixed outbound address. This admits connections from any Azure tenant to the login endpoint, which is acceptable only because authentication is Entra-only.
- Two user-assigned managed identities: `id-game` and `id-scraper`.
- A budget on the resource group with an alert by parameter.

**`infra/game.bicep`**: the Container App. Parameters: `image`, `minReplicas` (0 in stage 1, 1 in stage 2), `customDomain` (empty by default), `allowedIps` (empty by default). External ingress to 8080, HTTPS only, 0 to 2 replicas on an HTTP scale rule, 0.25 vCPU and 0.5 GiB, identity `id-game`. Settings: the connection string with `Authentication=Active Directory Managed Identity` and the identity's client ID, a 60-second connect timeout, forwarded headers on, `Rankings:RefreshMinutes=720`. **All three probes use `/healthz`, never `/readyz`**: if readiness followed `/readyz`, a visitor arriving while the database resumes would get an ingress error instead of the front end's waking-up state. With `customDomain` set, the app gets the hostname and a free managed certificate; the DNS records are manual and documented, and must exist before that deploy.

`allowedIps` becomes the ingress's IP restrictions: with any address listed, the ingress refuses everyone else; empty means public. This is how staging is private, with no code and no sign-in wall in front of the app. The owner's address comes from a secret on the GitHub `staging` environment, not from the committed parameter file, because the repo and its workflow logs are public. When the address changes, update the secret and redeploy; `az containerapp ingress access-restriction set` admits another address (a phone on mobile data) until the next deploy. `ASPNETCORE_ENVIRONMENT` is `Production` in both environments: staging differs from production by parameter values only, never by code path.

**`infra/scraper.bicep`**: the Job. Parameters: `image`, `cron`. 0.5 vCPU and 1 GiB, 30-minute timeout, one retry, identity `id-scraper`. `az containerapp job start` runs it on demand. Production runs Monday 06:00 UTC. Staging runs **Friday 06:00 UTC**, so a feed that broke during the week fails in staging first and the weekend is there to fix it before production's run. A feed that breaks between Friday and Monday is caught by production's own run, which keeps the previous release.

**`infra/bootstrap.sql`**, run once per environment by the owner in the portal query editor after that environment's first `main.bicep` deploy: `CREATE USER ... FROM EXTERNAL PROVIDER` for the three identities. `id-game`: `SELECT` on `dbo.CurrentCountryRankings` only (ownership chaining covers the tables under the view) and read-write on schema `game`. `id-scraper`: read-write on schema `dbo`. `id-deploy`: `db_owner`, for migrations.

**An environment's first deploy is by hand.** `infra/README.md` is the runbook, followed once per environment, staging first: create the account and subscription (first time only); as the owner, deploy `bootstrap.bicep` (`az deployment sub create`) and then `main.bicep` (`az deployment group create`), because production's pipeline cannot run before its `prod` branch exists; run `bootstrap.sql`; create the GitHub environment of the same name with its branch rule, its non-secret variables (client, tenant and subscription IDs, resource group) and, for staging, the `allowedIps` secret; then the first deploys (section 8.7). After that, `main.bicep` changes go through `infra.yml`. The runbook also holds the steps to reset staging, which is disposable.

| Parameter | staging | production |
|---|---|---|
| `sqlSku`, `minReplicas` | `free` and 0, always | by stage, below |
| `allowedIps` | the owner's address | empty |
| `customDomain` | never | the owner's subdomain, from the first deploy |
| scraper `cron` | Friday 06:00 UTC | Monday 06:00 UTC |
| Log Analytics daily cap | 0.03 GB | 0.12 GB |
| budget alert | $1 | $5 |

Data-protection keys stay in the `game` schema, unencrypted at rest beyond the database's own encryption. The framework logs a warning about this; accepted, since only `id-game` and `id-deploy` can read them. Each environment has its own database, so its own keys, players and games; nothing is shared between staging and production.

The cost stages apply to production only. Staging stays on the free database and scale to zero.

| Stage | Database | App | Cost | Behaviour |
|---|---|---|---|---|
| 1, launch | `sqlSku=free` | `minReplicas=0` | $0, guaranteed | 30 to 60 second first load after idle. If the allowance runs out, the front end's waking-up state times out into a "resting until the 1st" message. |
| 2, when it has players | `sqlSku=basic` | `minReplicas=1` | about $10 a month, fixed | No pauses, no cold starts, no allowance. S0 (about $15) is the next fixed step if 5 DTUs become the bottleneck. |

## 8. Pipelines, promotion and going public (approved)

### 8.1 Branches and promotion

| Branch | Rule | A push |
|---|---|---|
| `main` | The default branch. Changes arrive by pull request with green CI; no force-pushes. | deploys to **staging** |
| `stage/*` | Optional and unprotected: a way to try unmerged work in Azure, which most of this phase's first-run fixes will need. | deploys to **staging**, with no CI first |
| `prod` | Only ever fast-forwarded to a commit of `main`. No force-pushes, no deletion. Created when production exists (8.7). | deploys to **production** |

Promotion is one command: `git push origin main:prod`, or `<sha>:prod` to release an earlier commit. Git refuses it unless it is a fast-forward, so the two histories stay identical, and the GitHub `production` environment's page records what was deployed when. A pull request from `main` into `prod` also works with the mechanism below, but each merge commit it makes exists only on `prod`.

`main` must stay releasable: promoting a commit ships everything before it, so a hotfix cannot overtake unfinished work that is already merged.

`stage/*` has one cost. Staging's database normally receives exactly `main`'s migrations in `main`'s order, which is what rehearses production's. A `stage/*` branch that adds a migration and is then abandoned leaves that migration behind; reset staging (section 7's runbook) when that happens.

**Production never builds.** Each deploy workflow hashes its own inputs, the tree entries of exactly the paths in its path filter, into `<hash>`. The staging job tags the image it built `staged-<hash>` only after its smoke test passes. The production job computes the same hash at the tip of `prod` and resolves `staged-<hash>` to a digest, or fails with "these sources never passed staging". So a commit whose staging deploy failed, or one made directly on `prod`, cannot ship. The key is the inputs and not the commit SHA for two reasons. The workflows are path-filtered, so the tip of `prod` is often a docs or scraper-only commit for which no game image was ever built. And a SHA tag exists as soon as the image is built, so it says "built", where this tag says "built and passed staging".

GitHub environments: `staging` admits `main` and `stage/*`; `production` admits only `prod`. Each holds its own non-secret variables (client, tenant and subscription IDs, resource group), and `staging` holds the `allowedIps` secret. Neither has required reviewers: the promotion is the release decision. Each workflow takes its environment from the ref, so there are three deploy workflows, not six. One `concurrency` group per workflow and environment runs deploys one at a time, in order.

### 8.2 CI

`ci.yml` keeps running everything on every pull request (the scraper's tests arrive through the solution) and gains a job that builds both images without pushing. The deploy workflows do not call it. A pull request needs green CI to merge and the merge is what deploys to staging, so tests gate staging without running twice; pushes to `stage/*` are deliberately ungated. `ci.yml` still runs on pushes to `main`, beside the staging deploy, which catches the rare merge whose combination was never tested.

### 8.3 `deploy-game.yml`

On pushes to `main`, `stage/**` and `prod` that touch `src/WorldRankGuesser.*/**`, `Dockerfile`, `.dockerignore`, `Directory.Build.props`, `global.json`, `infra/game.bicep` or `infra/*/game.bicepparam`, and on manual dispatch. Both parameter files are inputs, so the rule has no exceptions: a change to production's parameters also passes through staging, at the cost of one redundant staging deploy.

**Staging job** (`main` or `stage/*`, environment `staging`):

1. Compute `<hash>`.
2. Build and push `ghcr.io/joseph-leo/worldrankguesser-game:sha-<sha>`.
3. Build the `game` migrations bundle (self-contained, `linux-x64`).
4. Log in to Azure with OIDC; add a SQL firewall rule for the runner's address; run the bundle with `Authentication=Active Directory Default`; remove the rule (`if: always()`).
5. `az deployment group create -f infra/game.bicep -p infra/staging/game.bicepparam`, with the image by digest and `allowedIps` from the secret.
6. Smoke test: admit the runner's address on the ingress, poll `/readyz` for up to three minutes, run the Playwright tests against the staging URL (section 9), remove the address (`if: always()`). This follows step 5 because a Bicep deploy resets the restrictions to the declared list.
7. Tag the digest `staged-<hash>`.

**Production job** (`prod`, environment `production`):

1. Compute `<hash>`; resolve `staged-<hash>` to a digest, or fail.
2. Build the bundle from the same sources, which hold the same migrations; firewall rule, run, remove, as above.
3. `az deployment group create -f infra/game.bicep -p infra/production/game.bicepparam` with that digest.
4. Smoke test: poll `/readyz`. The ingress step must never run here: one Allow rule on an open ingress shuts everyone else out.

Migrations run before the new revision starts, so every migration must work with the revision already running (add first, remove in a later deploy). The same rule makes **rollback** safe: a manual dispatch on `prod` with an older `staged-` tag deploys that image and skips the bundle.

### 8.4 `deploy-scraper.yml`

**Promotion moves the scraper's image, never its data.** Each environment's Job scrapes the public sites into its own database on its own schedule, and no rankings row ever travels between environments. In a week with no scraper change nobody does anything: staging scrapes on Friday, production scrapes on Monday, and the checks (8.6) stay silent. Staging's Friday run is a rehearsal by the same image in the same kind of network, not the source of production's data. Promotion happens only when the scraper's code changed, exactly as for the game.

The same two jobs for `src/SportsRankingService/**`, the shared build files, `infra/scraper.bicep` and `infra/*/scraper.bicepparam`: image `worldrankguesser-scraper`, the `dbo` bundle, `scraper.bicep`. The staging smoke test is the scrape itself: start the Job once, wait for it to succeed, then tag. That is the only meaningful test of a scraper in Azure, and it is what a weekend parser fix needs: merge the fix (or push `stage/fix-…`), watch staging run the real scrape, promote before Monday 06:00 UTC. The production job deploys without running the Job; a manual-dispatch input starts it once.

### 8.5 `infra.yml`

On pushes that touch `infra/main.bicep` or `infra/*/main.bicepparam`, and on manual dispatch: deploys `main.bicep` with the ref's parameter file. There is no image to tag, so nothing checks that an infrastructure change succeeded in staging; it has at least been deployed there, because every change reaches `prod` through `main`. A change that needs new shared infrastructure is two merges, `main.bicep` first, because the workflows of one push run side by side. No what-if on pull requests.

### 8.6 `scraper-check.yml`

Staging's Friday run (section 7) is only useful if its failure is heard. Scheduled for Friday and Monday at 09:00 UTC, three hours after each Job: reads the latest execution of the matching environment's Job and fails if it did not succeed or is more than a day old. GitHub's failed-workflow email is the alert: $0, and nothing new to run in Azure. Scheduled workflows run from `main`, which `production` does not admit, so the check logs in as `id-monitor`: a per-environment identity from `bootstrap.bicep` with Reader on its resource group and a federated credential for the `main` branch. It can read and nothing else.

### 8.7 First deploys

An environment's first deploys are manual dispatches, because its files were merged before it existed: `deploy-scraper.yml` with one Job run, then `deploy-game.yml`, whose smoke test needs rankings in the view. For staging, dispatch on `main`. For production, create the custom hostname's DNS records first (a CNAME to the app's default hostname and the `asuid` TXT record, both documented in the runbook), then create `prod` at the accepted commit (`git push origin <sha>:prod`), protect it, and dispatch on `prod`; the `staged-` tags already exist. Until `prod` is created nothing targets production, so merges before then are quiet.

### 8.8 Going public, in this order

1. Revoke both Sportradar trial keys at Sportradar. They are in this repo's history and, after the import, in the scraper's too. Revocation is the fix; history is not rewritten.
2. Run `gitleaks` over the full history after the import. The 2026-09-19 regex pass over both repos found only those keys and the dev-only `Rankings_Dev1!` password (a container bound to `127.0.0.1`; never reused). All 82 commits use the GitHub `noreply` address.
3. Turn on secret scanning and push protection.
4. Require approval before workflows from outside contributors run. Forked pull requests get no OIDC token and cannot reach either GitHub environment.
5. Refresh the README. It says that no licence is granted: all rights reserved (section 2).
6. Change the visibility.
7. Protect `main`: pull request, green CI, no force-pushes.
8. After the first image push, set both GHCR packages to public (a one-time manual step per package).

### 8.9 Order of work in the phase

Import and .NET 11 (section 5) → images and compose (section 6) → app changes (section 9) → CI green on a pull request → go public (8.8) → staging by hand (section 7's runbook) → the workflows → staging's first deploys (8.7) → section 9 accepted on staging → production by hand, its DNS records, `prod` created, its first deploys.

`main`, `rebuild` and `origin/main` were the same commit on 2026-09-21. Whether `ci.yml` has run green on GitHub was not checked; expect first-run fixes.

## 9. App changes and testing (approved)

**Forwarded headers.** `UseForwardedHeaders` first in the pipeline, for `X-Forwarded-For` and `X-Forwarded-Proto`, forward limit 1, behind the setting `Hosting:TrustForwardedHeaders`: off by default, on in Azure. With it on, known proxies and networks are cleared, because the container is reachable only through the environment's ingress. With it off, a directly exposed container (compose, VPS without the setting) never trusts a spoofed header. Test: with the setting on, two `X-Forwarded-For` addresses get separate per-IP limits; with it off, the header is ignored.

**SQL retries.** `EnableRetryOnFailure` in `GameDbContext.Configure` (six attempts, 30 seconds maximum delay). `DbUpdateConcurrencyException` is not transient and is never retried, so the `409` path is unchanged. A retry after a commit whose acknowledgement was lost lands on the unique indexes and becomes the existing conflict path.

**Rankings refresh.** `StartAsync` no longer loads. `ExecuteAsync` attempts at once; while no snapshot exists it retries on a backoff of 5, 10, 20, 40, then 60 seconds; after the first success it waits `RefreshMinutes`. It already takes `TimeProvider`, so the test drives a fake clock: a reader that fails twice then succeeds yields a snapshot in about 15 fake seconds. Integration tests that assumed "a started host has rankings" get a wait-for-ready helper in the fixture.

**Front end: waking up.** Before starting a game the start screen checks `/readyz`. While it is not ready: "Waking up the server…", polling every 2 seconds for up to 90, then a plain message with a retry button that also covers a spent allowance. This is server state, not a game rule, so `GameStore` stays rule-free. Vitest covers the polling state; the Playwright tests are unchanged.

**The Playwright game also runs against staging.** `playwright.config.ts` reads `E2E_BASE_URL`: when it is set, that is the `baseURL` and no local servers are started. The staging smoke test (8.3, step 6) polls `/readyz` and then runs the existing three tests against the staging URL while the runner's address is admitted. So `staged-<hash>` means a full game was played through the real ingress, over HTTPS with the `Secure` cookie, against Azure SQL with the managed identity; not only that the app reported ready. The games it writes stay in staging's database. Production's smoke test stays `/readyz` alone: no test games among real players' data.

**The promotion guard is one script with a test.** Computing `<hash>` and resolving `staged-<hash>` live in one composite action that both deploy workflows use, so the staging and production jobs cannot disagree about the hash. CI runs it with a hash that has no tag and expects the refusal.

**Phase 2 is accepted in two steps.**

*Staging:* a staging deploy has played its Playwright game from a cold start, the app going ready within 90 seconds; a request from an address that is not allowed is refused by the ingress; the app logs the caller's public address and not the ingress's, which confirms the forwarded-headers assumption (forward limit 1, the ingress appends the caller last); the Job has succeeded once in Azure and its per-feed results match a local run (or the differences are recorded); a parser-only merge deploys only the scraper and a docs-only merge deploys nothing; `scraper-check.yml`, dispatched before the first Job run, has failed and its email arrived; the budget alert exists.

*Production:* `prod` was created at the commit staging accepted; both production deploys resolved their `staged-` tags and built nothing; a full practice game plays at the custom hostname, over its managed certificate, from a cold start; Monday's Job and its check have each succeeded once; the budget alert exists; the repo and both packages are public; the old repo is archived.

## 10. To verify

The owner's decisions (region, custom domain, licence) are made and recorded in section 2. What remains is to verify while planning or building; all of it was stated from memory in discussion:

- That East US 2 accepts a new SQL server and the free offer on this subscription. `main.bicep` creates the server and the database together, so a refusal locks nothing; the fallback is another US region, chosen before any free database exists.
- That a custom hostname with a managed certificate deploys from Bicep in one pass. It is known to need two (add the hostname unbound, issue the certificate, then bind); the plan scripts whichever works.
- Azure prices, and that a free-offer database converts to Basic in place. There is no fallback by copy: Microsoft's free-offer page says a free database cannot be copied, only converted to a paid tier.
- That a request refused by the ingress IP restrictions does not wake a scaled-to-zero app, so strangers cannot spend staging's allowances.
- Whether the scraper exits non-zero when only some feeds fail. If it does not, `scraper-check.yml` reads the per-feed results from Log Analytics instead of the execution's status.
- Whether creating `prod` triggers the path-filtered workflows by itself. The runbook dispatches them in order either way; a run that started early and failed its smoke test is rerun after the scraper's Job.
- That a new subscription's quota allows two Container Apps environments in the region. Fallback: both apps and both Jobs share one environment, and everything else stays separate.
- Image tags for the .NET 11 RC on Microsoft's registry.
- `Authentication=Active Directory Default` picks up the runner's OIDC `az login`.
- The default visibility of a GHCR package pushed from Actions.
- A BenchmarkDotNet version that supports .NET 11, and the runtime-async switches for the installed RC (spike).
- What `POST /api/games` returns today when no snapshot exists; the waking-up state relies on it or on `/readyz`.
