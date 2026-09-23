# Phase 2c: Azure, Pipelines and Going Public Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put practice mode on the public internet at `https://games.foweeti.com` through a private staging copy and a promotion pipeline, at a guaranteed $0 a month, with the repository public.

**Architecture:** Bicep deploys one environment shape twice (`staging`, `production`): a Container Apps environment, the game as a scale-to-zero app, the scraper as a weekly Job, an Entra-only Azure SQL server with a free serverless database, Log Analytics, a budget, and user-assigned identities that GitHub reaches through OIDC federation. Four GitHub workflows deploy the game, the scraper and the shared infrastructure and check the weekly scrape; a composite action hashes each deploy workflow's inputs, so production only ever pulls an image tagged `staged-<hash>` after staging's smoke test, and never builds. Migrations run from the runner as self-contained EF bundles through a temporary SQL firewall rule. Every step only the owner can do (accounts, DNS, database users, GitHub settings) is a checkpoint task with its exact commands, run from this machine after `az login` and `gh auth login`.

**Tech Stack:** Bicep 0.46 through Azure CLI 2.90 (`az bicep`), Azure Container Apps (Consumption), Azure SQL General Purpose serverless (free offer) and Basic, Log Analytics, user-assigned managed identities with federated credentials, GitHub Actions on `ubuntu-24.04` (`actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/setup-node@v7`, `azure/login@v3`, `docker/login-action@v4`, `docker/setup-buildx-action@v4`, `docker/build-push-action@v7`), GHCR, `docker buildx imagetools`, EF Core 11 RC1 migration bundles (`dotnet-ef 11.0.0-rc.1`), Playwright 1.63, gitleaks 8.30, actionlint 1.7, GitHub CLI 2.101, `yq` 4.

**Spec:** `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md`, sections 7 (infrastructure), 8 (pipelines, promotion, going public) and the promotion-guard paragraph of section 9; section 10 lists what to verify while building. Parent design: `docs/superpowers/specs/2026-09-19-server-authoritative-rebuild-design.md`, section 12. Plans 2a and 2b are merged; the scraper branch `scraper-wta-paging-bwf-cap` (WTA paging, the BWF cap) must be merged first, because the staging scraper smoke test expects the scrape to exit 0.

## Global Constraints

- **Region** `eastus2` for everything. The first free SQL database fixes the region of every later one in the subscription; staging's first `main.bicep` deploy is what commits to it (spec section 7).
- **Names** (`<env>` is `staging` or `production`): resource group `rg-wrg-<env>`; Log Analytics `log-wrg-<env>`; Container Apps environment `cae-wrg-<env>`; SQL server `sql-wrg-<env>-<uniq>` where `<uniq>` is `uniqueString(resourceGroup().id)` (server names are global DNS names), database `WorldRankGuesser`; game app `ca-wrg-<env>-game`; scraper job `caj-wrg-<env>-scraper`; identities `id-wrg-<env>-deploy`, `id-wrg-<env>-monitor`, `id-wrg-<env>-game`, `id-wrg-<env>-scraper`; budget `budget-wrg-<env>`; production's managed certificate `cert-games-foweeti-com`. Bicep derives every name from the `env` parameter; the workflows spell the app and job names out per job (a rename touches both, noted in the runbook).
- **Images** `ghcr.io/joseph-leo/worldrankguesser-game` and `ghcr.io/joseph-leo/worldrankguesser-scraper`, tagged `sha-<commit sha>` on every staging build and `staged-<hash>` after staging's smoke test, deployed to Azure **by digest only**. `provenance: false` on the build step, so the digest names a plain image manifest.
- **Production never builds** (spec 8.1). `<hash>` is the first 12 hex characters of the SHA-256 of `git ls-tree -r --full-tree HEAD -- <inputs>`, where the inputs are exactly the paths of the workflow's push filter, listed once per workflow in its top-level `env.PATHS` (directories without a trailing `/**`); a test pins that the filter and the list agree.
- **Both environments cost $0**: `sqlSku: 'free'` and `minReplicas: 0` in staging always; production the same at launch (stage 1), `basic` and 1 in stage 2 by a parameter change alone. Never an always-on serverless database.
- **Staging is private** by `allowedIps` on the ingress, from the GitHub `staging` environment secret `ALLOWED_IPS` (a JSON array of CIDRs), never from a committed file. **Production's ingress never gets an Allow rule**: one Allow rule on an open ingress shuts everyone else out. Only the staging job admits the runner's address, and removes it in an `if: always()` step.
- **`ASPNETCORE_ENVIRONMENT=Production` in both environments**; staging differs from production by parameter values only. All three container probes use `/healthz`, never `/readyz`. `Hosting__TrustForwardedHeaders=true` and `Rankings__RefreshMinutes=720` in Azure only.
- **Migrations** run from the runner as self-contained `linux-x64` bundles with `Authentication=Active Directory Default`, through a firewall rule named `gha-<run id>-<run attempt>` that an `if: always()` step removes. On a new database the scraper's `dbo` bundle runs before the game's. Migrations run before the new revision starts, so every migration must work with the revision already running (add first, remove in a later deploy).
- **No secret in the repo and no SQL password anywhere.** The SQL server is Entra-only; the owner's UPN and object ID, the budget email and the owner's IP are GitHub environment variables and secrets that `.bicepparam` files read with `readEnvironmentVariable(...)`, with a placeholder default so `az bicep build` works without them.
- **Kill switch:** every deploy workflow's job runs only when the repository variable `DEPLOYS_ENABLED` is `true`, so merging the workflows before an environment exists produces skipped runs, not red ones.
- **Runner** `ubuntu-24.04` (pinned; `ubuntu-latest` is about to move). Its image already has Docker with buildx 0.37, Azure CLI 2.90 with Bicep 0.46, gh, jq, yq, Node 22; it has no .NET 11, so every .NET step uses `actions/setup-dotnet@v6` with `global-json-file: global.json`.
- **Branches** (spec 8.1): `main` deploys to staging; `stage/*` deploys to staging with no CI first; `prod` is only ever fast-forwarded to a commit of `main` (`git push origin main:prod`) and deploys to production. GitHub cannot enforce "fast-forward only"; the `prod` ruleset blocks force pushes, deletion and merge commits, and the guard refuses an image that never passed staging.
- **Every commit passes** `dotnet build WorldRankGuesser.slnx` and `dotnet test WorldRankGuesser.slnx` (Docker Desktop running); `npm run check` and `npm test` in `src/WorldRankGuesser.Web` when the front end changed; `az bicep build` and `az bicep lint` with zero warnings for every `.bicep` and `.bicepparam` when `infra/` changed; `actionlint` when `.github/` changed; `bash .github/actions/promotion-guard/test.sh` when the guard or a deploy workflow changed. `src/WorldRankGuesser.Web/openapi/*.json` and `src/lib/api/schema.d.ts` never change in this plan.
- **Windows:** PowerShell from the repo root unless a step says "in Git Bash" (the `.sh` scripts; `bash script.sh` also works from PowerShell). A tool call keeps no shell state, so each step's commands are self-contained. Work on a plain branch, not a worktree: this machine has a stale worktree entry git cannot delete.
- **Owner-only steps** (`! command` in the session runs it in the owner's terminal) are marked **Owner:**; everything else the implementer runs.
- Commit messages: imperative subject, a wrapped body that says why, and the session's attribution trailers:

  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
  ```

**Precondition:** `main` contains the merged scraper branch (`git log --oneline main | grep "Fetch paged feeds"` prints a line); `git status --short` on `main` prints nothing; Docker Desktop is running with the development database (`docker compose ps` shows `worldrankguesser-sql` healthy); the owner has a Microsoft account able to create an Azure subscription (Part B) and controls the `foweeti.com` zone at Google (Part C).

## Review Focus

1. **A docs-only or scraper-only merge to `main` must not build or deploy the game.** The push filter and the hash inputs are the same list; `test.sh`'s consistency check (Task 12) pins that they agree, and the hash test pins that a change outside the inputs leaves the hash unchanged (Task 4).
2. **The runner's temporary Allow rule on staging's ingress must be removed even when Playwright fails**, or the owner is locked out of staging until the next deploy. Both removal steps run under `if: always() && steps.<id>.outcome != 'skipped'` (Task 11); the reviewer checks the step order, since no local test can.
3. **The hash must describe the committed tree, never the working tree**, or a dirty checkout could tag an image that does not match its sources. `test.sh` edits a file without committing and expects the same hash (Task 4).
4. **A first `/readyz` that hangs while the database resumes must tell the player the server is waking**, not sit silent for a minute behind a disabled button. `readiness.test.ts` pins "waking" after 1.5 s of silence (Task 2).
5. **`prod` created at a commit already on `main` may trigger no path-filtered run at all** (GitHub defines the diff only in terms of commits pushed), so production's first deploys are always explicit dispatches on `prod` (Task 22); `workflow_dispatch` on every deploy workflow makes that possible (Tasks 11 to 13).

---

## File structure

**Created**

| Path | Responsibility |
|---|---|
| `.github/actions/promotion-guard/action.yml` | The composite action: `mode: hash` (outputs `hash`, `tag`), `mode: resolve` (also `digest`, or a refusal), `mode: tag` (writes `staged-<hash>` on a digest). |
| `.github/actions/promotion-guard/hash.sh` | `<hash>` of the tree entries of exactly the given paths at `HEAD`; a path missing from the tree is an error, not a silent omission. |
| `.github/actions/promotion-guard/resolve.sh` | A `staged-*` tag to a digest from the `Digest:` line of `docker buildx imagetools inspect` (no `--format`: buildx 0.30 mishandles the template), or exit 1 with "never passed staging". |
| `.github/actions/promotion-guard/tag.sh` | `docker buildx imagetools create` of a tag on a digest. |
| `.github/actions/promotion-guard/test.sh` | The guard's tests: determinism, sensitivity, working-tree independence, the refusal, the resolve, and the filter/inputs consistency of both deploy workflows. Runs locally (Git Bash) and in CI. |
| `.github/scripts/build-bundle.sh` | A self-contained `linux-x64` EF migrations bundle for one project. |
| `.github/scripts/sql-firewall.sh` | `open` / `close` the runner's firewall rule on the environment's one SQL server; prints the server FQDN. |
| `.github/scripts/wait-ready.sh` | Polls `<url>/readyz` until 200 or a timeout. |
| `.github/scripts/run-job.sh` | Starts a Container Apps Job and waits for its execution to succeed. |
| `.github/scripts/check-job.sh` | Fails unless the Job's latest execution succeeded within a window. |
| `.github/workflows/deploy-game.yml` | Spec 8.3: staging job (build, migrate, deploy, smoke test, tag) and production job (resolve, migrate, deploy, poll). |
| `.github/workflows/deploy-scraper.yml` | Spec 8.4: the same for the scraper; staging's smoke test is one Job run. |
| `.github/workflows/infra.yml` | Spec 8.5: `main.bicep` with the ref's parameter file. |
| `.github/workflows/scraper-check.yml` | Spec 8.6: Friday and Monday 09:00 UTC, the matching environment's latest Job execution, as `id-wrg-<env>-monitor`. |
| `infra/bootstrap.bicep` | Subscription scope, owner only: the resource group and the `bootstrap-group.bicep` module. |
| `infra/bootstrap-group.bicep` | Resource-group scope: `id-wrg-<env>-deploy` (federated to the GitHub environment, Contributor) and `id-wrg-<env>-monitor` (federated to `main`, Reader). |
| `infra/main.bicep` | The shared resources of one environment (spec section 7). |
| `infra/game.bicep` | The Container App; with a custom hostname, its managed certificate too, bound in the same deployment. |
| `infra/scraper.bicep` | The Job. |
| `infra/staging/{bootstrap,main,game,scraper}.bicepparam` | Staging's values (spec section 7's table). |
| `infra/production/{bootstrap,main,game,scraper}.bicepparam` | Production's values, the hostname included. |
| `tests/SportsRankingService.Tests/Persistence/EntraAuthenticationTests.cs` | The Entra provider ships with the scraper (Task 5). |
| `infra/staging/bootstrap.sql`, `infra/production/bootstrap.sql` | The three database users and their grants, run once per environment by the owner. |
| `infra/README.md` | The runbook: first deploys, reset staging, DNS, promotion, rollback, cost stage 2. |

**Modified**

| Path | Change |
|---|---|
| `src/WorldRankGuesser.Web/src/lib/api/readiness.svelte.ts`, `readiness.test.ts` | "waking" after 1.5 s of silence on the first check (Task 2). |
| `src/WorldRankGuesser.Web/playwright.config.ts` | `E2E_BASE_URL=""` counts as unset (Task 2). |
| `src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj`, `src/SportsRankingService/SportsRankingService.csproj`, `tests/WorldRankGuesser.Api.Tests/Integration/PersistenceTests.cs` | The SqlClient Azure extension, with a test that it ships (Task 5). |
| `.github/workflows/ci.yml` | `infra`, `actions` and `guard` jobs; the runner pinned (Task 10). |
| `README.md` | "All rights reserved" and the live URL (Task 14). |
| `CLAUDE.md` | Commands for the CLIs, the release flow, the environments (Task 14). |
| `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md` | The status line; the hostname in section 2 (Task 14). |

---

## Part A: code and going public (no Azure account needed)

### Task 1: Tooling, logins and the branch

**Files:** none in the repo.

**Interfaces:**
- Produces: `az` (with Bicep), `gh` (logged in), `gitleaks`, `yq`, `actionlint` on the PATH of a new shell; branch `phase-2c-azure-pipelines` off `main`.

- [ ] **Step 1: Confirm the precondition**

```powershell
git switch main; git pull --ff-only
git log --oneline -5
git status --short
```

Expected: the log includes "Fetch paged feeds page by page; cap the two broken BWF lists"; `git status --short` prints nothing. If the scraper branch is not merged, stop: the owner merges it first.

- [ ] **Step 2: Install the CLIs**

```powershell
winget install -e --id Microsoft.AzureCLI
winget install -e --id GitHub.cli
winget install -e --id Gitleaks.Gitleaks
winget install -e --id MikeFarah.yq
winget install -e --id rhysd.actionlint
```

Expected: each ends with "Successfully installed" (or "already installed"). A winget prompt for the source agreement is answered by the owner if it appears.

- [ ] **Step 3: Verify from a new shell (PATH changes need one)**

```powershell
az version --output table
az bicep install
az bicep version
gh --version
gitleaks version
yq --version
actionlint --version
```

Expected: Azure CLI 2.80 or later, Bicep 0.40 or later, gh 2.100 or later, gitleaks 8.30 or later, yq 4.x, actionlint 1.7.x. If a tool is missing from the PATH, the session's shell was started before the install: the owner restarts the session or the tool is called by its full path (`C:\Program Files\...`).

- [ ] **Step 4: Owner: log in to GitHub**

**Owner:** in the session, `! gh auth login --web -s workflow -s write:packages -s read:packages` (the `workflow` scope lets the CLI push workflow files; `write:packages` lets Task 16 create the two GHCR packages with the CLI's token; `read:packages` reads their visibility). Then the implementer runs:

```powershell
gh auth setup-git
gh auth status
gh repo view joseph-leo/WorldRankGuesser --json visibility,defaultBranchRef --jq '{visibility: .visibility, default: .defaultBranchRef.name}'
```

Expected: "Logged in to github.com account joseph-leo"; visibility `PRIVATE`, default `main`. (`az login` is Part B's first step; nothing in Part A needs Azure.)

- [ ] **Step 5: Branch**

```powershell
git switch -c phase-2c-azure-pipelines main
```

---

### Task 2: The two front-end carry-overs from 2b

`ServerReadiness` only says "waking" after the first `/readyz` has *answered* not-ready. When only the database is paused, the first call itself hangs (the endpoint awaits `CanConnectAsync`, which retries for up to a minute), and the player sees a disabled button and no message. After 1.5 s of silence the phase becomes `waking`. And `E2E_BASE_URL=""` must count as unset: a workflow that exports an empty variable would otherwise make Playwright target the empty string.

**Files:**
- Modify: `src/WorldRankGuesser.Web/src/lib/api/readiness.svelte.ts`
- Modify: `src/WorldRankGuesser.Web/src/lib/api/readiness.test.ts`
- Modify: `src/WorldRankGuesser.Web/playwright.config.ts:5`

**Interfaces:**
- Produces: `new ServerReadiness(check?, { intervalMs?, timeoutMs?, slowAfterMs? })`; `slowAfterMs` defaults to 1500. The start page needs no change: it already renders the `waking` phase.

- [ ] **Step 1: Write the failing test**

In `src/WorldRankGuesser.Web/src/lib/api/readiness.test.ts`, after the test `waits for each answer before asking again, so a slow /readyz is never stacked`, add:

```ts
	it('says it is waking the server when the first answer takes longer than 1.5 seconds', async () => {
		let answer!: (ready: boolean) => void;
		const check = vi.fn(() => new Promise<boolean>((resolve) => (answer = resolve)));
		const server = new ServerReadiness(check);

		const waiting = server.wait();
		await vi.advanceTimersByTimeAsync(1000);
		expect(server.phase).toBe('checking');

		await vi.advanceTimersByTimeAsync(600);
		expect(server.phase).toBe('waking');

		answer(true);
		await waiting;
		expect(server.phase).toBe('ready');
	});
```

- [ ] **Step 2: Run it and see it fail**

```powershell
cd src/WorldRankGuesser.Web; npm test -- src/lib/api/readiness.test.ts
```

Expected: 1 failed: `expected 'checking' to be 'waking'`.

- [ ] **Step 3: Make it pass**

In `src/WorldRankGuesser.Web/src/lib/api/readiness.svelte.ts`:

1. Replace the class doc comment's last sentence `This is server state, not a game rule.` with `The first call can itself hang while the database resumes, so after \`slowAfterMs\` of silence the phase is \`waking\`. This is server state, not a game rule.`
2. Add a field after `readonly #timeoutMs: number;`:

   ```ts
	readonly #slowAfterMs: number;
   ```

3. Replace the constructor with:

   ```ts
	constructor(
		check: () => Promise<boolean> = api.isReady,
		{ intervalMs = 2000, timeoutMs = 90_000, slowAfterMs = 1500 } = {}
	) {
		this.#check = check;
		this.#intervalMs = intervalMs;
		this.#timeoutMs = timeoutMs;
		this.#slowAfterMs = slowAfterMs;
	}
   ```

4. Replace `#run` with:

   ```ts
	async #run(): Promise<void> {
		this.phase = 'checking';
		const deadline = Date.now() + this.#timeoutMs;

		// A first call that hangs (the database is resuming) is a wake-up too; say so before it answers.
		const slow = setTimeout(() => {
			if (this.phase === 'checking') this.phase = 'waking';
		}, this.#slowAfterMs);

		try {
			// A rejected check (a network error, say) is not ready, same as a false answer — never a crash.
			while (!(await this.#check().catch(() => false))) {
				if (Date.now() >= deadline) {
					this.phase = 'unavailable';
					return;
				}
				this.phase = 'waking';
				await new Promise((resolve) => setTimeout(resolve, this.#intervalMs));
			}

			this.phase = 'ready';
		} finally {
			clearTimeout(slow);
		}
	}
   ```

- [ ] **Step 4: Run the file, then the whole front-end suite**

```powershell
cd src/WorldRankGuesser.Web; npm test -- src/lib/api/readiness.test.ts; npm test; npm run check
```

Expected: the readiness file passes (7 tests); `npm test` passes with 36 tests; `svelte-check` reports 0 errors.

- [ ] **Step 5: `E2E_BASE_URL=""` counts as unset**

In `src/WorldRankGuesser.Web/playwright.config.ts`, replace

```ts
const deployed = process.env.E2E_BASE_URL;
```

with

```ts
// An empty value counts as unset, so a workflow that exports the variable blank still starts the local servers.
const deployed = process.env.E2E_BASE_URL || undefined;
```

This is a configuration file (a TDD exception): check it by hand:

```powershell
cd src/WorldRankGuesser.Web; $env:E2E_BASE_URL=''; npx playwright test --list
```

Expected: the two tests are listed (the config loads; with the variable empty, `webServer` is defined and no server is started by `--list`). Then `Remove-Item Env:E2E_BASE_URL`.

- [ ] **Step 6: Commit**

```powershell
git add src/WorldRankGuesser.Web/src/lib/api/readiness.svelte.ts src/WorldRankGuesser.Web/src/lib/api/readiness.test.ts src/WorldRankGuesser.Web/playwright.config.ts
git commit -F - @'
Say the server is waking when the first readiness check hangs

When only the database is paused the first /readyz call itself waits
on the connection, and the start screen showed nothing for up to a
minute. After 1.5 seconds of silence the phase is waking. An empty
E2E_BASE_URL now counts as unset, for workflows that export it blank.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 3: The migration bundles, built and run before any Azure exists

The deploy workflows apply migrations with EF bundles (spec section 4). Both the build (design time: `dotnet ef` runs each program's `Main`) and the run (a Linux executable against a real server) are the riskiest steps of the first deploy, and both can be proven here: the bundle is built on Windows for `linux-x64` and executed in a Linux container against the development SQL Server, on a database that does not exist yet.

**Files:**
- Create: `.github/scripts/build-bundle.sh`
- Modify: `src/SportsRankingService/Program.cs:34`

**Interfaces:**
- Produces: `.github/scripts/build-bundle.sh <project-dir> <output-path>`; the bundle runs with the connection string in the app's own setting, `ConnectionStrings__WorldRankGuesserConnection=<connection string> ASPNETCORE_ENVIRONMENT=Production <output-path>`, and applies only pending migrations. Not `--connection`: a bundle creates the DbContext through the app's own configuration and startup, where the API's context factory demands a configured string and the scraper's `Program` loads its files, before `--connection` could apply.
- Modifies: `src/SportsRankingService/Program.cs` so `serviceconfig.json` is optional at design time (a bundle carries no configuration files; `dotnet ef` and a bundle both set `EF.IsDesignTime`).

- [ ] **Step 1: The script**

Create `.github/scripts/build-bundle.sh`:

```bash
#!/usr/bin/env bash
# Builds a self-contained linux-x64 EF Core migrations bundle for one project:
#   .github/scripts/build-bundle.sh src/WorldRankGuesser.Api artifacts/efbundle-game
#   .github/scripts/build-bundle.sh src/SportsRankingService artifacts/efbundle-scraper
# `dotnet ef` runs the project's Main at design time to find the DbContext. The API's context factory needs a
# connection string to be *configured* (nothing is connected to), and Production keeps appsettings.Development.json
# out; the scraper's appsettings.json carries its own dev string. The bundle's own run gets the real string through
# the same variable (never --connection: the app's startup demands a configured string before it could apply).
# `--target-runtime` is the bundle's option; the common `--runtime` would only set the restore RID.
set -euo pipefail

project="${1:?project directory}"
output="${2:?output path}"

export ConnectionStrings__WorldRankGuesserConnection="${ConnectionStrings__WorldRankGuesserConnection:-Server=design-time;Database=WorldRankGuesser;Encrypt=True}"
export ASPNETCORE_ENVIRONMENT=Production

dotnet tool restore
dotnet ef migrations bundle \
  --project "$project" --startup-project "$project" --configuration Release \
  --self-contained --target-runtime linux-x64 \
  --output "$output" --force
```

- [ ] **Step 1b: The scraper's configuration file is optional at design time**

A bundle carries no configuration files, and the scraper's `Program` requires `serviceconfig.json` before the host is built, so a bundle run fails with "configuration file 'serviceconfig.json' was not found and is not optional". At design time nothing reads the feed list: `dotnet ef` and a bundle both set `EF.IsDesignTime` and only need the DbContext. In `src/SportsRankingService/Program.cs`, replace

```csharp
builder.Configuration.AddJsonFile("serviceconfig.json", optional: false);
```

with

```csharp
// Required at run time, where it is the feed list; optional at design time (dotnet ef, a migrations bundle), where
// only the DbContext matters and no configuration file travels with a self-contained bundle.
builder.Configuration.AddJsonFile("serviceconfig.json", optional: EF.IsDesignTime);
```

`Microsoft.EntityFrameworkCore` is already imported at the top of the file. Then `dotnet test tests/SportsRankingService.Tests` still passes (367 tests, 1 skipped by design) and `dotnet run --project src/SportsRankingService -- --only Soccer` still loads the feed list at run time (it prints "Fetching ..." lines and exits 0).

- [ ] **Step 2: Build both bundles (in Git Bash, from the repo root)**

```bash
bash .github/scripts/build-bundle.sh src/SportsRankingService artifacts/efbundle-scraper
bash .github/scripts/build-bundle.sh src/WorldRankGuesser.Api artifacts/efbundle-game
ls -la artifacts/
git status --short
```

Expected: each ends with "Building bundle..." then "Done."; `artifacts/` holds two files, about 110 MB (scraper) and 140 MB (game), self-contained; `git status` shows only the new script and the `Program.cs` change of Step 1b (`artifacts/` is already in `.gitignore`). The game's build also regenerates the OpenAPI document into the Web folder; `git status` must not show `src/WorldRankGuesser.Web/openapi` as modified. If it does, the contract changed elsewhere: stop.

- [ ] **Step 3: Run them in a Linux container against a database that does not exist yet**

```powershell
docker compose up -d --wait
$conn = "Server=host.docker.internal,1433;Database=WorldRankGuesserBundleTest;User Id=sa;Password=Rankings_Dev1!;TrustServerCertificate=True"
docker run --rm -v "${PWD}\artifacts:/b" -e ASPNETCORE_ENVIRONMENT=Production -e "ConnectionStrings__WorldRankGuesserConnection=$conn" mcr.microsoft.com/dotnet/runtime:11.0.0-rc.1-resolute /b/efbundle-scraper
docker run --rm -v "${PWD}\artifacts:/b" -e ASPNETCORE_ENVIRONMENT=Production -e "ConnectionStrings__WorldRankGuesserConnection=$conn" mcr.microsoft.com/dotnet/runtime:11.0.0-rc.1-resolute /b/efbundle-game
docker exec worldrankguesser-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -d WorldRankGuesserBundleTest -Q "SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id ORDER BY 1"
docker run --rm -v "${PWD}\artifacts:/b" -e ASPNETCORE_ENVIRONMENT=Production -e "ConnectionStrings__WorldRankGuesserConnection=$conn" mcr.microsoft.com/dotnet/runtime:11.0.0-rc.1-resolute /b/efbundle-game
docker exec worldrankguesser-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -Q "DROP DATABASE WorldRankGuesserBundleTest"
```

Expected: the first run creates the database and prints the `dbo` migrations being applied; the second prints the `game` migrations; the table list includes `dbo.RankingReleases`, `dbo.RankingRows`, `dbo.__EFMigrationsHistory`, `game.Boards`, `game.Games`, `game.Picks`, `game.Players`, `game.DataProtectionKeys`, `game.__EFMigrationsHistory`; the third run prints "No migrations were applied. The database is already up to date."; the drop succeeds. If the container cannot reach `host.docker.internal`, Docker Desktop's host networking is off: the runbook's Azure path does not need it, but this proof does, so turn it on in Docker Desktop settings and rerun.

If a bundle fails to *build* with a message about the connection string or the host, the design-time path of that project broke since 2b (the `migrate` Docker stage exercises the same path; compare with `docker build --target migrate .`) and must be fixed in that project before continuing.

- [ ] **Step 4: Commit**

```powershell
git add .github/scripts/build-bundle.sh src/SportsRankingService/Program.cs
git update-index --chmod=+x .github/scripts/build-bundle.sh
git commit -F - @'
Add the script that builds a migrations bundle for the runner

Both deploy workflows apply migrations with self-contained linux-x64
EF bundles built on the runner. The script sets what design time needs
(a configured connection string, the Production environment) and uses
the bundle's own --target-runtime option. A bundle runs with the
connection string in the app's own setting rather than --connection,
because the app's startup demands one before that option could apply,
and the scraper's feed list is optional at design time, since no
configuration file travels with a bundle. Proven from Windows against
the development SQL Server in a Linux container, on a new database.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 4: The promotion guard

One composite action that both deploy workflows use (spec section 9), so the staging and production jobs cannot disagree about the hash. Three tiny scripts do the work and a fourth tests them, locally and in CI.

**Files:**
- Create: `.github/actions/promotion-guard/action.yml`
- Create: `.github/actions/promotion-guard/hash.sh`
- Create: `.github/actions/promotion-guard/resolve.sh`
- Create: `.github/actions/promotion-guard/tag.sh`
- Create: `.github/actions/promotion-guard/test.sh`

**Interfaces:**
- Produces: `hash.sh <path>...` prints 12 hex characters (exit 2 naming a path that is not in `HEAD`'s tree); `resolve.sh <image> <tag>` prints `sha256:...` or exits 1 with `never passed staging`; `tag.sh <image> <digest> <tag>`; the action `./.github/actions/promotion-guard` with inputs `image`, `paths` (newline-separated), `mode` (`hash` default, `resolve`, `tag`), `digest` (for `tag`), `tag` (for `resolve`: a `staged-*` tag to use instead of the computed one), and outputs `hash`, `tag`, `digest`.
- Task 12 extends `test.sh` with the filter/inputs consistency check once the deploy workflows exist.

- [ ] **Step 1: Write the failing tests**

Create `.github/actions/promotion-guard/test.sh`:

```bash
#!/usr/bin/env bash
# Tests for the promotion guard. Run from anywhere: bash .github/actions/promotion-guard/test.sh
# Needs git, docker (for the registry checks; network) and, for the consistency check, yq.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$here/../../.." && pwd)"
failures=0

pass() { echo "ok   - $1"; }
fail() { echo "FAIL - $1"; failures=$((failures + 1)); }

# ---- hash.sh in a throwaway repository ---------------------------------------------------------------------------
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
git -C "$tmp" init -q
git -C "$tmp" config user.email test@example.com
git -C "$tmp" config user.name test
mkdir -p "$tmp/a" "$tmp/b"
echo one > "$tmp/a/x"
echo two > "$tmp/b/y"
echo three > "$tmp/root.txt"
git -C "$tmp" add -A && git -C "$tmp" commit -qm first

h1="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
h2="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
[[ "$h1" =~ ^[0-9a-f]{12}$ ]] && pass "hash is 12 hex characters" || fail "hash is 12 hex characters: got '$h1'"
[[ "$h1" == "$h2" ]] && pass "hash is deterministic" || fail "hash is deterministic"

echo four >> "$tmp/b/y" && git -C "$tmp" commit -qam "outside the inputs"
h3="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
[[ "$h3" == "$h1" ]] && pass "a change outside the inputs leaves the hash alone" || fail "a change outside the inputs leaves the hash alone"

echo five >> "$tmp/a/x"
h4="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
[[ "$h4" == "$h1" ]] && pass "the working tree is ignored: only HEAD counts" || fail "the working tree is ignored: only HEAD counts"

git -C "$tmp" commit -qam "inside the inputs"
h5="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
[[ "$h5" != "$h1" ]] && pass "a committed change inside the inputs changes the hash" || fail "a committed change inside the inputs changes the hash"

h6="$(cd "$tmp" && bash "$here/hash.sh" a b root.txt)"
[[ "$h6" != "$h5" ]] && pass "the input list is part of the hash" || fail "the input list is part of the hash"

if (cd "$tmp" && bash "$here/hash.sh" a nope 2>/dev/null); then
  fail "a path missing from the tree is an error"
else
  pass "a path missing from the tree is an error"
fi

# ---- resolve.sh against real registries ----------------------------------------------------------------------------
if out="$(bash "$here/resolve.sh" ghcr.io/joseph-leo/worldrankguesser-game staged-000000000000 2>&1)"; then
  fail "an unknown staged tag is refused"
else
  [[ "$out" == *"never passed staging"* ]] && pass "an unknown staged tag is refused, naming the reason" || fail "an unknown staged tag is refused, naming the reason: got '$out'"
fi

# Microsoft's registry: no anonymous pull limit, and an image this repository pulls anyway.
digest="$(bash "$here/resolve.sh" mcr.microsoft.com/dotnet/runtime 11.0.0-rc.1-resolute 2>/dev/null || true)"
[[ "$digest" == sha256:* ]] && pass "an existing tag resolves to its digest" || fail "an existing tag resolves to its digest: got '$digest'"

# ---- consistency of each deploy workflow's push filter with its hash inputs (added in Task 12) --------------------

echo
if [[ "$failures" -eq 0 ]]; then echo "all guard tests passed"; else echo "$failures guard test(s) failed"; exit 1; fi
```

- [ ] **Step 2: Run it and see it fail**

In Git Bash: `bash .github/actions/promotion-guard/test.sh`

Expected: the first hash check fails with `No such file or directory` for `hash.sh` (the scripts do not exist), and the summary says tests failed with exit 1.

- [ ] **Step 3: The scripts**

Create `.github/actions/promotion-guard/hash.sh`:

```bash
#!/usr/bin/env bash
# Prints the hash of a deploy workflow's inputs: the first 12 hex characters of the SHA-256 of the tree entries of
# exactly the given paths at HEAD (`git ls-tree -r`: mode, type, blob id and path of every file under them). The
# working tree plays no part. A path that is not in the tree is an error: a typo must not silently drop an input.
#   hash.sh src/WorldRankGuesser.Api Dockerfile ...
set -euo pipefail

if [[ $# -eq 0 ]]; then
  echo "hash.sh: no input paths given" >&2
  exit 2
fi

for path in "$@"; do
  if ! git cat-file -e "HEAD:${path}" 2>/dev/null; then
    echo "hash.sh: '${path}' is not in HEAD's tree" >&2
    exit 2
  fi
done

git ls-tree -r --full-tree HEAD -- "$@" | sha256sum | cut -c1-12
```

Create `.github/actions/promotion-guard/resolve.sh`:

```bash
#!/usr/bin/env bash
# Resolves <image>:<tag> to its digest, or refuses: a staged-<hash> tag exists only after the staging job's smoke
# test passed on exactly these inputs, so a missing tag means these sources never passed staging.
#   resolve.sh ghcr.io/joseph-leo/worldrankguesser-game staged-0123456789ab
set -euo pipefail

image="${1:?image}"
tag="${2:?tag}"

# The plain output's "Digest:" line, not a --format template: buildx 0.30 (Docker Desktop) mishandles
# '{{.Manifest.Digest}}', printing the whole report, while the runner's 0.37 does not; the text line is the same in both.
if ! report="$(docker buildx imagetools inspect "${image}:${tag}" 2>/dev/null)"; then
  echo "::error::${image}:${tag} does not exist: these sources never passed staging" >&2
  exit 1
fi

digest="$(printf '%s\n' "$report" | awk '/^Digest:/ { print $2; exit }')"
if [[ "$digest" != sha256:* ]]; then
  echo "::error::no digest in the inspect output for ${image}:${tag}: ${report}" >&2
  exit 1
fi

echo "$digest"
```

Create `.github/actions/promotion-guard/tag.sh`:

```bash
#!/usr/bin/env bash
# Adds <tag> to the image already pushed at <digest>, without pulling or rebuilding.
#   tag.sh ghcr.io/joseph-leo/worldrankguesser-game sha256:... staged-0123456789ab
set -euo pipefail

image="${1:?image}"
digest="${2:?digest}"
tag="${3:?tag}"

docker buildx imagetools create --tag "${image}:${tag}" "${image}@${digest}"
echo "tagged ${image}:${tag} -> ${digest}"
```

- [ ] **Step 4: Run the tests and see them pass**

In Git Bash: `bash .github/actions/promotion-guard/test.sh`

Expected: nine `ok` lines and "all guard tests passed". The two registry checks need the network; `docker buildx imagetools` needs Docker Desktop running. Neither check touches Docker Hub, whose anonymous pull limit would make the test flaky.

- [ ] **Step 5: The action**

Create `.github/actions/promotion-guard/action.yml`:

```yaml
name: Promotion guard
description: >
  Hashes a deploy workflow's inputs (the tree entries of exactly the paths in its push filter) and, by mode, resolves
  or writes the image tag staged-<hash>. Production never builds: it deploys the digest behind that tag or is refused.
inputs:
  image:
    description: The image, e.g. ghcr.io/joseph-leo/worldrankguesser-game
    required: true
  paths:
    description: Newline-separated repository paths, exactly the workflow's push filter (directories without /**)
    required: true
  mode:
    description: hash (default), resolve (the staged tag to a digest, or a refusal) or tag (write the staged tag)
    required: false
    default: hash
  digest:
    description: For mode tag, the digest to tag
    required: false
    default: ''
  tag:
    description: For mode resolve, a staged-* tag to resolve instead of the computed one (a rollback)
    required: false
    default: ''
outputs:
  hash:
    description: The 12-character hash of the inputs at HEAD
    value: ${{ steps.hash.outputs.hash }}
  tag:
    description: staged-<hash>, or the tag given for a rollback
    value: ${{ steps.hash.outputs.tag }}
  digest:
    description: In mode resolve, the digest behind the tag
    value: ${{ steps.resolve.outputs.digest }}
runs:
  using: composite
  steps:
    - id: hash
      shell: bash
      env:
        PATHS: ${{ inputs.paths }}
        OVERRIDE_TAG: ${{ inputs.tag }}
      run: |
        set -euo pipefail
        mapfile -t paths < <(printf '%s\n' "$PATHS" | sed '/^[[:space:]]*$/d')
        hash="$("$GITHUB_ACTION_PATH/hash.sh" "${paths[@]}")"
        tag="${OVERRIDE_TAG:-staged-$hash}"
        echo "inputs hash $hash, tag $tag"
        echo "hash=$hash" >> "$GITHUB_OUTPUT"
        echo "tag=$tag" >> "$GITHUB_OUTPUT"
    - id: resolve
      if: inputs.mode == 'resolve'
      shell: bash
      env:
        IMAGE: ${{ inputs.image }}
        TAG: ${{ steps.hash.outputs.tag }}
      run: |
        set -euo pipefail
        digest="$("$GITHUB_ACTION_PATH/resolve.sh" "$IMAGE" "$TAG")"
        echo "$IMAGE:$TAG -> $digest"
        echo "digest=$digest" >> "$GITHUB_OUTPUT"
    - if: inputs.mode == 'tag'
      shell: bash
      env:
        IMAGE: ${{ inputs.image }}
        DIGEST: ${{ inputs.digest }}
        TAG: ${{ steps.hash.outputs.tag }}
      run: |
        set -euo pipefail
        [[ -n "$DIGEST" ]] || { echo "::error::mode tag needs a digest" >&2; exit 1; }
        "$GITHUB_ACTION_PATH/tag.sh" "$IMAGE" "$DIGEST" "$TAG"
```

- [ ] **Step 6: Lint the action and check the scripts' mode bits**

```powershell
actionlint
git add .github/actions/promotion-guard
git update-index --chmod=+x .github/actions/promotion-guard/hash.sh .github/actions/promotion-guard/resolve.sh .github/actions/promotion-guard/tag.sh .github/actions/promotion-guard/test.sh .github/scripts/build-bundle.sh
git ls-files -s .github/actions/promotion-guard .github/scripts
```

Expected: `actionlint` prints nothing (it lints `.github/workflows/*.yml`; `action.yml` files are checked when a workflow uses them, in Task 12). `git ls-files -s` shows mode `100755` for every `.sh` file: the runner calls them directly, and Windows checkouts do not carry the bit by themselves.

- [ ] **Step 7: Commit**

```powershell
git commit -F - @'
Add the promotion guard: hash the inputs, resolve or write staged-<hash>

Both deploy workflows compute the same hash from the tree entries of
exactly their push-filter paths at HEAD. The staging job tags the image
it built staged-<hash> only after its smoke test; the production job
resolves that tag to a digest or is refused, so a commit whose staging
deploy failed, or one made directly on prod, cannot ship. The tests
pin determinism, sensitivity, working-tree independence, a path typo,
the refusal and a real resolve.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---
### Task 5: SqlClient 7 needs the Azure extension to authenticate as an identity

Both projects resolve `Microsoft.Data.SqlClient` 7.0.2 through EF Core's SQL Server provider. Since SqlClient 7.0, "Azure Active Directory / Entra ID authentication functionality (`ActiveDirectoryAuthenticationProvider` and related types) has been extracted into a new `Microsoft.Data.SqlClient.Extensions.Azure` package"; applications using `ActiveDirectoryDefault`, `ActiveDirectoryManagedIdentity` and the other Entra modes "must now install the package separately", and "no code changes are required beyond adding the package reference" (SqlClient 7.0.0 release notes). Without it, the first connection in Azure, and the first migration bundle on the runner, fail with an error naming the package. The extension's 7.0.2 pairs with the core 7.0.2.

**Files:**
- Modify: `src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj`
- Modify: `src/SportsRankingService/SportsRankingService.csproj`
- Test: `tests/WorldRankGuesser.Api.Tests/Integration/PersistenceTests.cs`
- Test: `tests/SportsRankingService.Tests/Persistence/EntraAuthenticationTests.cs`

**Interfaces:**
- Produces: both published outputs (and therefore both migration bundles of Task 3) carry `Microsoft.Data.SqlClient.Extensions.Azure.dll`, so `Authentication=Active Directory Managed Identity` (the app and the Job) and `Active Directory Default` (the bundles on the runner) work.

- [ ] **Step 1: Write the failing tests**

The provider type lives in the extension assembly; loading it by name proves the package is referenced and shipped, with no network involved. Add to `tests/WorldRankGuesser.Api.Tests/Integration/PersistenceTests.cs`, after `The_context_retries_transient_failures`:

```csharp
    [Fact]
    public void The_entra_authentication_provider_ships_with_the_api()
    {
        // Since SqlClient 7 the Entra modes (Active Directory Managed Identity in Azure) live in a separate package,
        // without which the first connection in Azure fails; referencing it is the only wiring it needs.
        var provider = Type.GetType("Microsoft.Data.SqlClient.ActiveDirectoryAuthenticationProvider, Microsoft.Data.SqlClient.Extensions.Azure");

        Assert.NotNull(provider);
    }
```

Create `tests/SportsRankingService.Tests/Persistence/EntraAuthenticationTests.cs`:

```csharp
namespace SportsRankingService.Tests.Persistence;

public class EntraAuthenticationTests
{
    /// <summary>
    /// The scraper runs in Azure as a managed identity (Authentication=Active Directory Managed Identity). Since
    /// SqlClient 7 that provider lives in Microsoft.Data.SqlClient.Extensions.Azure; referencing it is the only wiring.
    /// </summary>
    [Fact]
    public void The_entra_authentication_provider_ships_with_the_scraper()
    {
        var provider = Type.GetType("Microsoft.Data.SqlClient.ActiveDirectoryAuthenticationProvider, Microsoft.Data.SqlClient.Extensions.Azure");

        Assert.NotNull(provider);
    }
}
```

- [ ] **Step 2: Run them and see them fail**

```powershell
dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~EntraAuthenticationTests"
dotnet test WorldRankGuesser.slnx --filter "FullyQualifiedName~PersistenceTests.The_entra_authentication_provider"
```

Expected: both FAIL with `Assert.NotNull() Failure: Value is null` (the assembly is not there to load).

- [ ] **Step 3: Reference the package in both projects**

```powershell
dotnet add src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj package Microsoft.Data.SqlClient.Extensions.Azure --version 7.0.2
dotnet add src/SportsRankingService/SportsRankingService.csproj package Microsoft.Data.SqlClient.Extensions.Azure --version 7.0.2
dotnet list src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj package --include-transitive | Select-String SqlClient
```

Expected: `Microsoft.Data.SqlClient.Extensions.Azure 7.0.2` as a top-level reference and `Microsoft.Data.SqlClient 7.0.2` transitive, with no version conflict warning (NU1605/NU1608). If the core package's version has moved past 7.0.2 by then, use the extension version equal to the core's.

- [ ] **Step 4: Run the tests and see them pass, then the whole solution**

```powershell
dotnet test WorldRankGuesser.slnx
git status --short
```

Expected: every test passes (the two new ones included); `git status` shows the two `.csproj` files and the two test files, and nothing under `src/WorldRankGuesser.Web/openapi`.

- [ ] **Step 5: Rebuild the bundles of Task 3 and confirm the assembly is inside**

In Git Bash:

```bash
bash .github/scripts/build-bundle.sh src/WorldRankGuesser.Api artifacts/efbundle-game
grep -c "Microsoft.Data.SqlClient.Extensions.Azure" artifacts/efbundle-game
```

Expected: a count of at least 1 (the self-contained bundle embeds the assembly name in its manifest).

- [ ] **Step 6: Commit**

```powershell
git add src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj src/SportsRankingService/SportsRankingService.csproj tests/WorldRankGuesser.Api.Tests/Integration/PersistenceTests.cs tests/SportsRankingService.Tests/Persistence/EntraAuthenticationTests.cs
git commit -F - @'
Reference the SqlClient Azure extension in the API and the scraper

Since SqlClient 7 the Entra authentication providers live in
Microsoft.Data.SqlClient.Extensions.Azure. Both projects run in Azure as
managed identities and their migration bundles authenticate as the
deploy identity, so without it the first connection there fails. No
code changes: the reference is the wiring. A test in each project loads
the provider type from the extension assembly.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---
### Task 6: `bootstrap.bicep`: the resource group and the two identities GitHub logs in as

Spec section 7. Owner-only, once per environment, because a Contributor cannot write role assignments: this template holds every role assignment, and everything else stays deployable by the deploy identity. `id-wrg-<env>-deploy` is federated to the GitHub environment of the same name and is Contributor on the group only, so a staging deploy has no rights in production. `id-wrg-<env>-monitor` is federated to the `main` branch and is Reader, for the scheduled scraper check.

**Files:**
- Create: `infra/bootstrap.bicep`
- Create: `infra/bootstrap-group.bicep`
- Create: `infra/staging/bootstrap.bicepparam`
- Create: `infra/production/bootstrap.bicepparam`

**Interfaces:**
- Produces: `az deployment sub create --location eastus2 --parameters infra/<env>/bootstrap.bicepparam` with outputs `resourceGroupName`, `tenantId`, `subscriptionId`, `deployClientId`, `monitorClientId` (the GitHub variables of Task 18); identities named `id-wrg-<env>-deploy` and `id-wrg-<env>-monitor`.

- [ ] **Step 1: The subscription-scope template**

Create `infra/bootstrap.bicep`:

```bicep
// Owner only, once per environment (infra/README.md): the resource group and the two identities GitHub logs in as.
// Separate from main.bicep because a Contributor cannot write role assignments: this file holds every role
// assignment, so everything else stays deployable by id-wrg-<env>-deploy, which has Contributor on this group only.
//   az deployment sub create --location eastus2 --name bootstrap-staging --parameters infra/staging/bootstrap.bicepparam
targetScope = 'subscription'

@allowed(['staging', 'production'])
param env string

// East US 2 for everything: the first free SQL database fixes the region of every later one in the subscription.
param location string = 'eastus2'

// The federated credentials trust this repository's GitHub environments and its main branch. Renaming or
// transferring the repository breaks that trust silently (GitHub's OIDC subject carries the name): redeploy then.
param githubRepository string = 'joseph-leo/WorldRankGuesser'

resource rg 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: 'rg-wrg-${env}'
  location: location
}

module identities 'bootstrap-group.bicep' = {
  name: 'bootstrap-${env}'
  scope: rg
  params: {
    env: env
    location: location
    githubRepository: githubRepository
  }
}

output resourceGroupName string = rg.name
output tenantId string = subscription().tenantId
output subscriptionId string = subscription().subscriptionId
output deployClientId string = identities.outputs.deployClientId
output monitorClientId string = identities.outputs.monitorClientId
```

- [ ] **Step 2: The resource-group-scope module**

Create `infra/bootstrap-group.bicep`:

```bicep
// The two identities GitHub Actions logs in as, with OIDC and no secret (deployed into the group by bootstrap.bicep).
//   id-wrg-<env>-deploy   trusts the GitHub environment named <env>; Contributor on this group: deploys, migrates,
//                         starts the Job, opens and closes firewall and ingress rules. Not in the other environment.
//   id-wrg-<env>-monitor  trusts the main branch (scheduled workflows run from main, which the production
//                         environment does not admit); Reader on this group: reads the Job's executions, nothing else.
param env string
param location string
param githubRepository string

var contributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
var readerRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7')
var githubIssuer = 'https://token.actions.githubusercontent.com'
var audiences = ['api://AzureADTokenExchange']

resource deploy 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-wrg-${env}-deploy'
  location: location
}

resource deployCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: deploy
  name: 'github-environment-${env}'
  properties: {
    issuer: githubIssuer
    subject: 'repo:${githubRepository}:environment:${env}'
    audiences: audiences
  }
}

resource deployContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deploy.id, contributorRoleId)
  properties: {
    roleDefinitionId: contributorRoleId
    principalId: deploy.properties.principalId
    principalType: 'ServicePrincipal'
    description: 'GitHub Actions deploys the ${env} environment'
  }
}

resource monitor 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-wrg-${env}-monitor'
  location: location
}

resource monitorCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: monitor
  name: 'github-branch-main'
  properties: {
    issuer: githubIssuer
    subject: 'repo:${githubRepository}:ref:refs/heads/main'
    audiences: audiences
  }
}

resource monitorReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, monitor.id, readerRoleId)
  properties: {
    roleDefinitionId: readerRoleId
    principalId: monitor.properties.principalId
    principalType: 'ServicePrincipal'
    description: 'The scheduled scraper check reads the ${env} Job executions'
  }
}

output deployClientId string = deploy.properties.clientId
output monitorClientId string = monitor.properties.clientId
```

- [ ] **Step 3: The two parameter files**

Create `infra/staging/bootstrap.bicepparam`:

```bicep
using '../bootstrap.bicep'

param env = 'staging'
```

Create `infra/production/bootstrap.bicepparam`:

```bicep
using '../bootstrap.bicep'

param env = 'production'
```

- [ ] **Step 4: Build and lint**

```powershell
az bicep build --file infra/bootstrap.bicep --stdout | Out-Null
az bicep lint --file infra/bootstrap.bicep
az bicep build-params --file infra/staging/bootstrap.bicepparam --stdout | Out-Null
az bicep build-params --file infra/production/bootstrap.bicepparam --stdout | Out-Null
```

Expected: no output from any command (a warning or error prints a line naming the file and position; fix its cause). If a linter rule fires that cannot apply here, turn that one rule off in a new `infra/bicepconfig.json` with a comment saying why, and commit it with this task:

```json
{
  "analyzers": {
    "core": {
      "enabled": true,
      "rules": {}
    }
  }
}
```

- [ ] **Step 5: Commit**

```powershell
git add infra/bootstrap.bicep infra/bootstrap-group.bicep infra/staging/bootstrap.bicepparam infra/production/bootstrap.bicepparam
git commit -F - @'
Add bootstrap.bicep: the resource group and the identities GitHub uses

Owner only, once per environment, because it holds every role
assignment: the deploy identity trusts the GitHub environment of the
same name and is Contributor on the group alone, so a staging deploy
has no rights in production; the monitor identity trusts the main
branch and is Reader, for the scheduled scraper check. No secret exists.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 7: `main.bicep`: the shared resources of one environment

Spec section 7's `main.bicep`: Log Analytics with a daily cap, a Container Apps environment on the built-in Consumption workload profile (consumption-only environments are now the legacy type), an Entra-only SQL server whose admin is the owner, the database by `sqlSku` (`free`: General Purpose serverless with the free limit and auto-pause on exhaustion; `basic`: Basic DTU), the "allow Azure services" firewall rule, the game and scraper identities, and a budget. Every name carries the environment. Bicep has no decimal literals, so `0.5` and `0.03` are written `json('0.5')`.

**Files:**
- Create: `infra/main.bicep`
- Create: `infra/staging/main.bicepparam`
- Create: `infra/production/main.bicepparam`

**Interfaces:**
- Consumes: environment variables `SQL_ADMIN_LOGIN`, `SQL_ADMIN_OBJECT_ID`, `BUDGET_EMAIL` at compile time of the parameter files (placeholders when unset).
- Produces: resources named as in Global Constraints; outputs `environmentDefaultDomain`, `customDomainVerificationId`, `sqlServerName`, `sqlServerFqdn`, `gameIdentityClientId`, `scraperIdentityClientId`; parameter `budgetEnabled` for a subscription too new for Cost Management (up to 48 h).

- [ ] **Step 1: The template**

Create `infra/main.bicep`:

```bicep
// The shared resources of one environment (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md, section 7).
// First deployed by the owner from the runbook; afterwards by infra.yml on every change to this file or its
// parameter files. Both environments cost $0: the database is the free offer that pauses when its monthly
// allowance is spent, and the app scales to zero. No SQL password exists anywhere: the server is Entra-only.
//   az deployment group create --resource-group rg-wrg-staging --parameters infra/staging/main.bicepparam
@allowed(['staging', 'production'])
param env string

param location string = resourceGroup().location

// The owner's account is the SQL server's Entra admin: the user principal name and its object id.
param sqlAdminLogin string
param sqlAdminObjectId string

// free: General Purpose serverless, 0.5 to 1 vCore, the free monthly limit, auto-pause when it is spent (stage 1).
// basic: Basic DTU, about $5 a month flat (stage 2, production only). A free database converts in place; not back.
@allowed(['free', 'basic'])
param sqlSku string = 'free'

// Log Analytics ingestion cap in GB per day, as a string because Bicep has no decimal literals. The two
// environments together stay inside the free 5 GB a month.
param logDailyCapGb string = '0.03'

// The budget only alerts; a fixed-price tier is the real cap. Cost Management can refuse a budget for up to 48 hours
// on a new subscription: deploy with budgetEnabled=false then, and again with true later.
param budgetAmount int = 1
param budgetEmail string
param budgetEnabled bool = true
param budgetStartDate string = '${substring(utcNow('yyyy-MM-dd'), 0, 7)}-01T00:00:00Z'

var uniq = uniqueString(resourceGroup().id)

resource log 'Microsoft.OperationalInsights/workspaces@2026-03-01' = {
  name: 'log-wrg-${env}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json(logDailyCapGb)
    }
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// A workload-profiles environment with only the built-in Consumption profile has no environment charge; only
// replicas are billed. Every app and job in it says workloadProfileName: 'Consumption'.
resource cae 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: 'cae-wrg-${env}'
  location: location
  properties: {
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: log.properties.customerId
        sharedKey: log.listKeys().primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

resource sql 'Microsoft.Sql/servers@2025-01-01' = {
  name: 'sql-wrg-${env}-${uniq}'
  location: location
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// A Consumption environment without a virtual network has no fixed outbound address, so the app and the Job reach
// the server through the "allow Azure services" rule. It admits any Azure tenant to the login endpoint, which is
// acceptable only because authentication is Entra-only. The runner's own rule is added and removed per deploy.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
  parent: sql
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource db 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: sql
  name: 'WorldRankGuesser'
  location: location
  sku: sqlSku == 'free'
    ? {
        name: 'GP_S_Gen5'
        tier: 'GeneralPurpose'
        family: 'Gen5'
        capacity: 1
      }
    : {
        name: 'Basic'
        tier: 'Basic'
        capacity: 5
      }
  properties: sqlSku == 'free'
    ? {
        useFreeLimit: true
        freeLimitExhaustionBehavior: 'AutoPause'
        autoPauseDelay: 15
        minCapacity: json('0.5')
        maxSizeBytes: 34359738368
        requestedBackupStorageRedundancy: 'Local'
        zoneRedundant: false
        collation: 'SQL_Latin1_General_CP1_CI_AS'
      }
    : {
        maxSizeBytes: 2147483648
        requestedBackupStorageRedundancy: 'Local'
        zoneRedundant: false
        collation: 'SQL_Latin1_General_CP1_CI_AS'
      }
}

resource gameIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-wrg-${env}-game'
  location: location
}

resource scraperIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-wrg-${env}-scraper'
  location: location
}

resource budget 'Microsoft.Consumption/budgets@2026-06-01' = if (budgetEnabled) {
  name: 'budget-wrg-${env}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actual: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [budgetEmail]
      }
      forecast: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [budgetEmail]
      }
    }
  }
}

output environmentDefaultDomain string = cae.properties.defaultDomain
output customDomainVerificationId string = cae.properties.customDomainConfiguration.customDomainVerificationId
output sqlServerName string = sql.name
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName
output gameIdentityClientId string = gameIdentity.properties.clientId
output scraperIdentityClientId string = scraperIdentity.properties.clientId
```

- [ ] **Step 2: The parameter files**

Create `infra/staging/main.bicepparam`:

```bicep
using '../main.bicep'

// The owner's identity and email come from the GitHub environment (or the shell, for the runbook's first deploy),
// never from this public file. The placeholders only let `az bicep build` succeed without them.
param env = 'staging'
param sqlAdminLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN', 'placeholder@example.com')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID', '00000000-0000-0000-0000-000000000000')
param sqlSku = 'free'
param logDailyCapGb = '0.03'
param budgetAmount = 1
param budgetEmail = readEnvironmentVariable('BUDGET_EMAIL', 'placeholder@example.com')
param budgetEnabled = bool(readEnvironmentVariable('BUDGET_ENABLED', 'true'))
```

Create `infra/production/main.bicepparam`:

```bicep
using '../main.bicep'

// Stage 1 (launch): the free database and, in game.bicepparam, no always-on replica: $0. Stage 2, when the game has
// players: sqlSku = 'basic' here and minReplicas = 1 there, about $10 a month fixed (spec section 7).
param env = 'production'
param sqlAdminLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN', 'placeholder@example.com')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID', '00000000-0000-0000-0000-000000000000')
param sqlSku = 'free'
param logDailyCapGb = '0.12'
param budgetAmount = 5
param budgetEmail = readEnvironmentVariable('BUDGET_EMAIL', 'placeholder@example.com')
param budgetEnabled = bool(readEnvironmentVariable('BUDGET_ENABLED', 'true'))
```

- [ ] **Step 3: Build and lint**

```powershell
az bicep build --file infra/main.bicep --stdout | Out-Null
az bicep lint --file infra/main.bicep
az bicep build-params --file infra/staging/main.bicepparam --stdout | Out-Null
az bicep build-params --file infra/production/main.bicepparam --stdout | Out-Null
```

Expected: no output. If the build reports that `json(...)` or `bool(...)` is not allowed in a parameter file, replace `param budgetEnabled = bool(...)` with a plain `param budgetEnabled = true` (the runbook then passes `--parameters budgetEnabled=false` on the command line when needed) and keep `logDailyCapGb` a string, which `main.bicep` already converts.

- [ ] **Step 4: Commit**

```powershell
git add infra/main.bicep infra/staging/main.bicepparam infra/production/main.bicepparam
git commit -F - @'
Add main.bicep: the shared resources of one environment

Log Analytics with a daily cap, a Container Apps environment on the
built-in Consumption profile, an Entra-only SQL server administered by
the owner, the free serverless database that pauses when its allowance
is spent (or Basic, by parameter), the two workload identities and a
budget. Every name carries the environment; the owner's identity and
email are read from the environment at compile time, never committed.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 8: `game.bicep`: the Container App

Spec section 7's `game.bicep`. External HTTPS-only ingress on 8080, 0 to 2 replicas on an HTTP scale rule, 0.25 vCPU and 0.5 GiB, the game identity, the managed-identity connection string, forwarded headers on, a 12-hour refresh, and all three probes on `/healthz`: a visitor arriving while the database resumes must reach the front end's waking-up state, not an ingress error. `allowedIps` is how staging is private. With `customDomain` set, the hostname is bound automatically to a managed certificate declared in the same template (`bindingType: 'Auto'`), which needs the CNAME and the `asuid` TXT record to exist first (Task 21) and the app reachable by the certificate authority, so production never gets an allow-list.

**Files:**
- Create: `infra/game.bicep`
- Create: `infra/staging/game.bicepparam`
- Create: `infra/production/game.bicepparam`

**Interfaces:**
- Consumes: the resources of Task 7 by name (`existing`): `cae-wrg-<env>`, `sql-wrg-<env>-<uniq>`, `id-wrg-<env>-game`; environment variables `GAME_IMAGE` and, for staging, `ALLOWED_IPS` (a JSON array of CIDR strings) at compile time of the parameter files.
- Produces: app `ca-wrg-<env>-game`; output `fqdn`; parameters `image`, `minReplicas`, `maxReplicas`, `customDomain`, `allowedIps`, `refreshMinutes`.

- [ ] **Step 1: The template**

Create `infra/game.bicep`:

```bicep
// The game's Container App (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md, section 7). Deployed by
// deploy-game.yml with the image by digest; the shared resources come from main.bicep by name.
//   GAME_IMAGE=ghcr.io/joseph-leo/worldrankguesser-game@sha256:... az deployment group create \
//     --resource-group rg-wrg-staging --parameters infra/staging/game.bicepparam
@allowed(['staging', 'production'])
param env string

param location string = resourceGroup().location

// The image by digest, so a rankings refresh or a rebuilt tag never changes what runs.
param image string

// 0 in stage 1 (scale to zero, $0), 1 in stage 2 (no cold starts, about $5 a month).
param minReplicas int = 0
param maxReplicas int = 2

// '' = the default hostname only. With a name, the hostname is added and bound to a managed certificate declared
// below, which needs the DNS records first (infra/README.md) and the app reachable from the certificate authority.
param customDomain string = ''

// [] = public. Any entry makes the ingress refuse every other address (an Allow list denies the rest), which is
// how staging is private; never set on production.
param allowedIps array = []

// 12 hours: an hourly refresh would wake the paused database and spend the free allowance in two weeks.
param refreshMinutes int = 720

var uniq = uniqueString(resourceGroup().id)

resource cae 'Microsoft.App/managedEnvironments@2026-07-01' existing = {
  name: 'cae-wrg-${env}'
}

resource sql 'Microsoft.Sql/servers@2025-01-01' existing = {
  name: 'sql-wrg-${env}-${uniq}'
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: 'id-wrg-${env}-game'
}

resource app 'Microsoft.App/containerApps@2026-07-01' = {
  name: 'ca-wrg-${env}-game'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: cae.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        ipSecurityRestrictions: [
          for (cidr, i) in allowedIps: {
            name: 'allow-${i}'
            ipAddressRange: cidr
            action: 'Allow'
          }
        ]
        customDomains: empty(customDomain)
          ? []
          : [
              {
                name: customDomain
                bindingType: 'Auto'
              }
            ]
      }
    }
    template: {
      containers: [
        {
          name: 'game'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              // No password: the app is the identity. Connect Timeout 60 rides out a resuming database, with the
              // context's own retries behind it.
              name: 'ConnectionStrings__WorldRankGuesserConnection'
              value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=WorldRankGuesser;Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;Connect Timeout=60'
            }
            {
              // The environment's ingress rewrites X-Forwarded-For, so the per-IP limit keys on the caller.
              name: 'Hosting__TrustForwardedHeaders'
              value: 'true'
            }
            {
              name: 'Rankings__RefreshMinutes'
              value: string(refreshMinutes)
            }
          ]
          // All three on /healthz, never /readyz: a visitor arriving while the database resumes must get the front
          // end's waking-up state, not an ingress error.
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              initialDelaySeconds: 2
              periodSeconds: 3
              timeoutSeconds: 3
              failureThreshold: 10
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

// A free managed certificate for the custom hostname, validated through the CNAME. The hostname must be on the app
// first (dependsOn) and the DNS records must exist before this deployment (infra/README.md).
resource certificate 'Microsoft.App/managedEnvironments/managedCertificates@2026-07-01' = if (!empty(customDomain)) {
  parent: cae
  name: 'cert-${replace(customDomain, '.', '-')}'
  location: location
  properties: {
    subjectName: customDomain
    domainControlValidation: 'CNAME'
  }
  dependsOn: [app]
}

output fqdn string = app.properties.configuration.ingress.fqdn
```

- [ ] **Step 2: The parameter files**

Create `infra/staging/game.bicepparam`:

```bicep
using '../game.bicep'

// The image is set by the deploy workflow (by digest). The owner's address comes from the staging environment's
// ALLOWED_IPS secret, a JSON array of CIDRs such as ["203.0.113.5/32"]; the placeholder [] would make staging
// public, so the workflow always sets it. Staging stays on stage 1 for good.
param env = 'staging'
param image = readEnvironmentVariable('GAME_IMAGE', 'ghcr.io/joseph-leo/worldrankguesser-game:placeholder')
param minReplicas = 0
param allowedIps = json(readEnvironmentVariable('ALLOWED_IPS', '[]'))
```

Create `infra/production/game.bicepparam`:

```bicep
using '../game.bicep'

// The hostname is bound from the first deploy, because the player cookie is bound to it (spec section 2). No
// allow-list ever: production is public, and the certificate authority must reach the app.
param env = 'production'
param image = readEnvironmentVariable('GAME_IMAGE', 'ghcr.io/joseph-leo/worldrankguesser-game:placeholder')
param minReplicas = 0
param customDomain = 'games.foweeti.com'
param allowedIps = []
```

- [ ] **Step 3: Build and lint**

```powershell
az bicep build --file infra/game.bicep --stdout | Out-Null
az bicep lint --file infra/game.bicep
az bicep build-params --file infra/staging/game.bicepparam --stdout | Out-Null
az bicep build-params --file infra/production/game.bicepparam --stdout | Out-Null
```

Expected: no output. If the build reports `json(...)` is not allowed in a parameter file, staging's `allowedIps` becomes `split(readEnvironmentVariable('ALLOWED_IPS', ''), ',')` with the secret written as `203.0.113.5/32` (comma-separated, no brackets); if `split` is refused too, the template takes `param allowedIps string = ''` and splits it itself with `empty(allowedIps) ? [] : split(allowedIps, ',')`. Record which form was needed in the runbook.

- [ ] **Step 4: Commit**

```powershell
git add infra/game.bicep infra/staging/game.bicepparam infra/production/game.bicepparam
git commit -F - @'
Add game.bicep: the Container App by digest, with the custom hostname

External HTTPS-only ingress, 0 to 2 replicas on an HTTP rule, the game
identity's password-less connection string, forwarded headers on, a
12-hour refresh, and all three probes on /healthz so a visitor meeting
a resuming database sees the waking-up state. An allow-list makes
staging private; production binds games.foweeti.com to a managed
certificate declared in the same template.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 9: `scraper.bicep`, the database users, and the whole `infra/` folder green

Spec section 7's `scraper.bicep` (a scheduled Job: 0.5 vCPU, 1 GiB, a 30-minute timeout, one retry, the scraper identity; staging Friday 06:00 UTC, production Monday 06:00 UTC) and `bootstrap.sql` (one file per environment: the three users and their grants). The SQL file is idempotent and is run twice by the runbook: once after `main.bicep` (users, roles, the `game` schema and the schema grants) and once after the first scraper deploy, when the view the game user reads exists.

**Files:**
- Create: `infra/scraper.bicep`
- Create: `infra/staging/scraper.bicepparam`
- Create: `infra/production/scraper.bicepparam`
- Create: `infra/staging/bootstrap.sql`
- Create: `infra/production/bootstrap.sql`

**Interfaces:**
- Consumes: `cae-wrg-<env>`, `sql-wrg-<env>-<uniq>`, `id-wrg-<env>-scraper` by name; environment variable `SCRAPER_IMAGE` at compile time of the parameter files.
- Produces: Job `caj-wrg-<env>-scraper`; parameters `image`, `cron`; the database users `id-wrg-<env>-deploy` (`db_owner`), `id-wrg-<env>-scraper` (read-write on `dbo`), `id-wrg-<env>-game` (`SELECT` on `dbo.CurrentCountryRankings`, read-write on `game`).

- [ ] **Step 1: The Job template**

Create `infra/scraper.bicep`:

```bicep
// The scraper as a scheduled Container Apps Job (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md,
// section 7): never a background service in the API, because scale to zero would kill it mid-run. It runs every
// enabled feed once and exits 1 if any feed failed, which marks the execution Failed; one retry follows. Staging runs
// on Friday so a feed that broke during the week fails there first, production on Monday.
//   SCRAPER_IMAGE=ghcr.io/joseph-leo/worldrankguesser-scraper@sha256:... az deployment group create \
//     --resource-group rg-wrg-staging --parameters infra/staging/scraper.bicepparam
//   az containerapp job start --name caj-wrg-staging-scraper --resource-group rg-wrg-staging      # on demand
@allowed(['staging', 'production'])
param env string

param location string = resourceGroup().location

// The image by digest.
param image string

// Five fields, UTC.
param cron string

var uniq = uniqueString(resourceGroup().id)

resource cae 'Microsoft.App/managedEnvironments@2026-07-01' existing = {
  name: 'cae-wrg-${env}'
}

resource sql 'Microsoft.Sql/servers@2025-01-01' existing = {
  name: 'sql-wrg-${env}-${uniq}'
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: 'id-wrg-${env}-scraper'
}

resource job 'Microsoft.App/jobs@2026-07-01' = {
  name: 'caj-wrg-${env}-scraper'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: cae.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 1800
      replicaRetryLimit: 1
      scheduleTriggerConfig: {
        cronExpression: cron
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'scraper'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'DOTNET_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__WorldRankGuesserConnection'
              value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=WorldRankGuesser;Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;Connect Timeout=60'
            }
          ]
        }
      ]
    }
  }
}

output jobName string = job.name
```

- [ ] **Step 2: The parameter files**

Create `infra/staging/scraper.bicepparam`:

```bicep
using '../scraper.bicep'

// Friday 06:00 UTC: a feed that broke during the week fails here first, and the weekend is there to fix it before
// production's Monday run.
param env = 'staging'
param image = readEnvironmentVariable('SCRAPER_IMAGE', 'ghcr.io/joseph-leo/worldrankguesser-scraper:placeholder')
param cron = '0 6 * * 5'
```

Create `infra/production/scraper.bicepparam`:

```bicep
using '../scraper.bicep'

// Monday 06:00 UTC. A feed that breaks between Friday and Monday is caught by this run, which keeps the previous
// release for that feed.
param env = 'production'
param image = readEnvironmentVariable('SCRAPER_IMAGE', 'ghcr.io/joseph-leo/worldrankguesser-scraper:placeholder')
param cron = '0 6 * * 1'
```

- [ ] **Step 3: The database users**

Create `infra/staging/bootstrap.sql`:

```sql
-- The staging database's users: run by the owner, connected as the server's Entra admin to the WorldRankGuesser
-- database (portal query editor), twice: after the first main.bicep deploy, and again after the first scraper
-- deploy, when dbo.CurrentCountryRankings exists and the game user's grant on it can be applied. Every statement is
-- idempotent. Production: infra/production/bootstrap.sql. A managed identity's user name is the identity's name.

-- The game schema exists before the game's migrations run, so its grants can be given now; the migration's own
-- CREATE SCHEMA is skipped when the schema exists.
IF SCHEMA_ID(N'game') IS NULL EXEC (N'CREATE SCHEMA [game] AUTHORIZATION dbo;');

-- Migrations run from the GitHub runner as the deploy identity.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'id-wrg-staging-deploy')
    CREATE USER [id-wrg-staging-deploy] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [id-wrg-staging-deploy];

-- The scraper reads and writes its own schema, dbo; its migrations are the deploy identity's job.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'id-wrg-staging-scraper')
    CREATE USER [id-wrg-staging-scraper] FROM EXTERNAL PROVIDER;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [id-wrg-staging-scraper];

-- The game reads one view (ownership chaining covers the tables beneath it) and owns its data in schema game.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'id-wrg-staging-game')
    CREATE USER [id-wrg-staging-game] FROM EXTERNAL PROVIDER;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::game TO [id-wrg-staging-game];
IF OBJECT_ID(N'dbo.CurrentCountryRankings', N'V') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.CurrentCountryRankings TO [id-wrg-staging-game];
ELSE
    PRINT 'dbo.CurrentCountryRankings does not exist yet: run this file again after the first scraper deploy.';

SELECT dp.name AS [user], dp.type_desc, r.name AS [role]
FROM sys.database_principals dp
LEFT JOIN sys.database_role_members rm ON rm.member_principal_id = dp.principal_id
LEFT JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
WHERE dp.name LIKE 'id-wrg-%';
```

Create `infra/production/bootstrap.sql` from the staging file: every `staging` becomes `production` (the header's first sentence and the ten identity names), and the header's `Production: infra/production/bootstrap.sql` becomes `Staging: infra/staging/bootstrap.sql`. Check with:

```powershell
(Get-Content infra/production/bootstrap.sql | Select-String 'staging' -AllMatches).Matches.Count
(Get-Content infra/production/bootstrap.sql | Select-String 'id-wrg-production-' -AllMatches).Matches.Count
```

Expected: `1` (only the sentence pointing at the staging file) and `10`.

- [ ] **Step 4: The whole folder, as CI will check it**

In Git Bash, the same loops as the `infra` job of Task 10:

```bash
set -e
for f in infra/*.bicep; do echo "== $f"; out="$(az bicep build --file "$f" --stdout 2>&1 >/dev/null)"; [[ -z "$out" ]] || { echo "$out"; exit 1; }; done
for f in infra/*/*.bicepparam; do echo "== $f"; out="$(az bicep build-params --file "$f" --stdout 2>&1 >/dev/null)"; [[ -z "$out" ]] || { echo "$out"; exit 1; }; done
echo all green
```

Expected: one `== ` line per file (five templates, ten parameter files) and `all green`.

- [ ] **Step 5: Commit**

```powershell
git add infra/scraper.bicep infra/staging/scraper.bicepparam infra/production/scraper.bicepparam infra/staging/bootstrap.sql infra/production/bootstrap.sql
git commit -F - @'
Add scraper.bicep and the database users of each environment

The scraper is a scheduled Job with the scraper identity, a 30-minute
timeout and one retry: staging on Friday, production on Monday. The
SQL file per environment creates the three users from the external
provider and gives the deploy identity db_owner, the scraper read-write
on dbo, and the game read-write on its schema and SELECT on the one
view; it is idempotent, and run again once the view exists.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---
### Task 10: CI lints the templates, the workflows and the guard

`ci.yml` gains three jobs: `infra` builds and lints every Bicep file and parameter file, `actions` runs actionlint over the workflows, and `guard` runs the promotion guard's tests. The four existing jobs move to the pinned runner.

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `.github/actions/promotion-guard/test.sh` (Task 4); `infra/*.bicep` and `infra/*/*.bicepparam` (Tasks 6 to 9).
- Produces: the check names `api`, `web`, `images`, `infra`, `actions`, `guard`, which the `main` ruleset requires (Task 16).

- [ ] **Step 1: Pin the runner and add the jobs**

In `.github/workflows/ci.yml`, replace every `runs-on: ubuntu-latest` with `runs-on: ubuntu-24.04`, and append after the `images` job:

```yaml
  # Every template and parameter file compiles with no warning, and every parameter file matches its template.
  infra:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v7
      - name: Build and lint the templates
        run: |
          set -euo pipefail
          for f in infra/*.bicep; do
            echo "== $f"
            out="$(az bicep build --file "$f" --stdout 2>&1 >/dev/null)" || { echo "$out"; exit 1; }
            if [[ -n "$out" ]]; then echo "$out"; echo "::error::$f built with warnings"; exit 1; fi
          done
      - name: Build the parameter files against their templates
        run: |
          set -euo pipefail
          for f in infra/*/*.bicepparam; do
            echo "== $f"
            out="$(az bicep build-params --file "$f" --stdout 2>&1 >/dev/null)" || { echo "$out"; exit 1; }
            if [[ -n "$out" ]]; then echo "$out"; echo "::error::$f built with warnings"; exit 1; fi
          done

  # The workflows and the composite action parse and use the actions and contexts correctly.
  actions:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v7
      - id: get_actionlint
        run: bash <(curl -fsSL https://raw.githubusercontent.com/rhysd/actionlint/main/scripts/download-actionlint.bash)
        shell: bash
      - run: ${{ steps.get_actionlint.outputs.executable }} -color
        shell: bash

  # The promotion guard's own tests: the hash, the refusal, a real resolve, and each deploy workflow's push filter
  # against its hash inputs (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md, section 9).
  guard:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v7
      - run: bash .github/actions/promotion-guard/test.sh
```

- [ ] **Step 2: Lint and run what can run locally**

```powershell
actionlint
bash .github/actions/promotion-guard/test.sh
```

Expected: `actionlint` prints nothing; the guard tests pass. The `infra` job's loops already ran in Task 9's Step 4 over every template, so CI is green on the pull request.

- [ ] **Step 3: Commit**

```powershell
git add .github/workflows/ci.yml
git commit -F - @'
Lint the templates, the workflows and the guard in CI; pin the runner

infra builds every Bicep template and parameter file and fails on a
warning, actions runs actionlint, guard runs the promotion guard's
tests. ubuntu-latest is about to move to 26.04, so the jobs pin 24.04.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 11: `deploy-game.yml` and the runner-side scripts

Spec section 8.3. The staging job builds and pushes `sha-<sha>`, applies the game migrations, deploys by digest, admits the runner on the ingress, waits for `/readyz`, plays the Playwright game, removes the runner and only then tags `staged-<hash>`. The production job resolves `staged-<hash>` at the tip of `prod` (or a tag given on dispatch, for a rollback), applies the migrations (skipped for a rollback), deploys by digest and waits for `/readyz`. Three scripts keep the YAML readable and reusable by the scraper workflow.

**Files:**
- Create: `.github/scripts/sql-firewall.sh`
- Create: `.github/scripts/wait-ready.sh`
- Create: `.github/workflows/deploy-game.yml`

**Interfaces:**
- Consumes: `./.github/actions/promotion-guard` (Task 4); `.github/scripts/build-bundle.sh` (Task 3); `infra/game.bicep` with `infra/<env>/game.bicepparam` reading `GAME_IMAGE` and, for staging, `ALLOWED_IPS` from the environment (Task 8); GitHub environment variables `AZURE_CLIENT_ID`, `AZURE_RESOURCE_GROUP` and the staging secret `ALLOWED_IPS`; repository variables `DEPLOYS_ENABLED`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (Task 18).
- Produces: `.github/scripts/sql-firewall.sh open|close` (env `RG`, `RULE`; `open` writes `fqdn=<server fqdn>` to `$GITHUB_OUTPUT`); `.github/scripts/wait-ready.sh <base url> <timeout seconds>`; the workflow `Deploy game`, dispatchable with `staged_tag`.

- [ ] **Step 1: The firewall script**

Create `.github/scripts/sql-firewall.sh`:

```bash
#!/usr/bin/env bash
# Admits the runner to the environment's one SQL server for the migration bundle, then removes it:
#   RG=rg-wrg-staging RULE=gha-123-1 .github/scripts/sql-firewall.sh open    # writes fqdn=... to $GITHUB_OUTPUT
#   RG=rg-wrg-staging RULE=gha-123-1 .github/scripts/sql-firewall.sh close
# The runner's address comes from ipify (IPv4; Azure SQL rules are IPv4 only). One server per resource group.
set -euo pipefail

action="${1:?open|close}"
: "${RG:?RG is the resource group}" "${RULE:?RULE is the firewall rule name}"

server="$(az sql server list --resource-group "$RG" --query '[0].name' -o tsv)"
[[ -n "$server" ]] || { echo "::error::no SQL server in resource group $RG" >&2; exit 1; }

case "$action" in
  open)
    ip="$(curl -fsS https://api.ipify.org)"
    az sql server firewall-rule create --resource-group "$RG" --server "$server" --name "$RULE" \
      --start-ip-address "$ip" --end-ip-address "$ip" --output none
    fqdn="$(az sql server show --resource-group "$RG" --name "$server" --query fullyQualifiedDomainName -o tsv)"
    echo "admitted $ip to $fqdn as $RULE"
    echo "fqdn=$fqdn" >> "${GITHUB_OUTPUT:-/dev/stdout}"
    ;;
  close)
    az sql server firewall-rule delete --resource-group "$RG" --server "$server" --name "$RULE" --output none
    echo "removed $RULE from $server"
    ;;
  *)
    echo "usage: sql-firewall.sh open|close" >&2
    exit 2
    ;;
esac
```

- [ ] **Step 2: The readiness poll**

Create `.github/scripts/wait-ready.sh`:

```bash
#!/usr/bin/env bash
# Polls <base url>/readyz every 5 seconds until it answers 200, or fails after <timeout seconds>:
#   .github/scripts/wait-ready.sh https://ca-wrg-staging-game.example.azurecontainerapps.io 180
# A scaled-to-zero app and a paused database both need time; /readyz is 503 until the rankings are loaded.
set -euo pipefail

base="${1:?base url}"
timeout="${2:-180}"
deadline=$(( $(date +%s) + timeout ))

while :; do
  code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 "$base/readyz" || echo 000)"
  echo "$(date -u +%T) /readyz -> $code"
  [[ "$code" == "200" ]] && exit 0
  if [[ "$(date +%s)" -ge "$deadline" ]]; then
    echo "::error::$base/readyz did not answer 200 within ${timeout}s" >&2
    exit 1
  fi
  sleep 5
done
```

- [ ] **Step 3: The workflow**

Create `.github/workflows/deploy-game.yml`:

```yaml
name: Deploy game

# main and stage/* deploy to staging; prod deploys to production and never builds
# (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md, sections 8.1 and 8.3). env.PATHS must list exactly the
# paths below, directories without /**: the promotion guard's tests check that they agree.
on:
  push:
    branches: [main, 'stage/**', prod]
    paths:
      - 'src/WorldRankGuesser.Api/**'
      - 'src/WorldRankGuesser.Web/**'
      - 'Dockerfile'
      - '.dockerignore'
      - 'Directory.Build.props'
      - 'global.json'
      - '.config/dotnet-tools.json'
      - 'infra/game.bicep'
      - 'infra/staging/game.bicepparam'
      - 'infra/production/game.bicepparam'
      - '.github/workflows/deploy-game.yml'
      - '.github/actions/promotion-guard/**'
      - '.github/scripts/**'
  workflow_dispatch:
    inputs:
      staged_tag:
        description: 'Production only: deploy this staged-<hash> tag instead of the tip of prod (a rollback; migrations are skipped)'
        required: false
        type: string
        default: ''

permissions:
  contents: read
  packages: write
  id-token: write

concurrency:
  group: deploy-game-${{ github.ref == 'refs/heads/prod' && 'production' || 'staging' }}
  cancel-in-progress: false

env:
  IMAGE: ghcr.io/joseph-leo/worldrankguesser-game
  PATHS: |
    src/WorldRankGuesser.Api
    src/WorldRankGuesser.Web
    Dockerfile
    .dockerignore
    Directory.Build.props
    global.json
    .config/dotnet-tools.json
    infra/game.bicep
    infra/staging/game.bicepparam
    infra/production/game.bicepparam
    .github/workflows/deploy-game.yml
    .github/actions/promotion-guard
    .github/scripts

jobs:
  staging:
    if: github.ref != 'refs/heads/prod' && vars.DEPLOYS_ENABLED == 'true'
    runs-on: ubuntu-24.04
    environment:
      name: staging
      url: https://${{ steps.app.outputs.fqdn }}
    env:
      RG: ${{ vars.AZURE_RESOURCE_GROUP }}
      APP: ca-wrg-staging-game
      RULE: gha-${{ github.run_id }}-${{ github.run_attempt }}
    steps:
      - uses: actions/checkout@v7

      - id: guard
        uses: ./.github/actions/promotion-guard
        with:
          image: ${{ env.IMAGE }}
          paths: ${{ env.PATHS }}

      - uses: docker/login-action@v4
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - uses: docker/setup-buildx-action@v4
      - id: build
        name: Build and push sha-${{ github.sha }}
        uses: docker/build-push-action@v7
        with:
          context: .
          push: true
          provenance: false
          tags: ${{ env.IMAGE }}:sha-${{ github.sha }}

      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
      - name: Build the game migrations bundle
        run: .github/scripts/build-bundle.sh src/WorldRankGuesser.Api artifacts/efbundle-game

      - uses: azure/login@v3
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}

      - id: sql
        name: Admit the runner to the SQL server
        run: .github/scripts/sql-firewall.sh open
      - name: Apply the game migrations
        env:
          ConnectionStrings__WorldRankGuesserConnection: Server=tcp:${{ steps.sql.outputs.fqdn }},1433;Database=WorldRankGuesser;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=60
        run: ASPNETCORE_ENVIRONMENT=Production artifacts/efbundle-game
      - name: Remove the runner from the SQL server
        if: always() && steps.sql.outcome != 'skipped'
        run: .github/scripts/sql-firewall.sh close

      - name: Deploy the app by digest
        env:
          GAME_IMAGE: ${{ env.IMAGE }}@${{ steps.build.outputs.digest }}
          ALLOWED_IPS: ${{ secrets.ALLOWED_IPS }}
        run: >
          az deployment group create --resource-group "$RG" --name "game-${{ github.run_id }}"
          --template-file infra/game.bicep --parameters infra/staging/game.bicepparam --output none
      - id: app
        run: echo "fqdn=$(az containerapp show --name "$APP" --resource-group "$RG" --query properties.configuration.ingress.fqdn -o tsv)" >> "$GITHUB_OUTPUT"

      # Staging only: an Allow rule denies everyone else, which is why production's job has no such step.
      - id: ingress
        name: Admit the runner on the ingress
        run: >
          az containerapp ingress access-restriction set --name "$APP" --resource-group "$RG"
          --rule-name "$RULE" --ip-address "$(curl -fsS https://api.ipify.org)/32" --action Allow --output none
      - name: Wait for /readyz
        run: .github/scripts/wait-ready.sh "https://${{ steps.app.outputs.fqdn }}" 180
      - uses: actions/setup-node@v7
        with:
          node-version: 24
          cache: npm
          cache-dependency-path: src/WorldRankGuesser.Web/package-lock.json
      - name: Play the practice game through the ingress
        working-directory: src/WorldRankGuesser.Web
        env:
          E2E_BASE_URL: https://${{ steps.app.outputs.fqdn }}
        run: |
          npm ci
          npx playwright install --with-deps chromium
          npx playwright test
      - uses: actions/upload-artifact@v5
        if: failure()
        with:
          name: playwright-report
          path: src/WorldRankGuesser.Web/playwright-report/
          retention-days: 14
      - name: Remove the runner from the ingress
        if: always() && steps.ingress.outcome != 'skipped'
        run: az containerapp ingress access-restriction remove --name "$APP" --resource-group "$RG" --rule-name "$RULE" --output none

      - name: Tag the image staged-${{ steps.guard.outputs.hash }}
        uses: ./.github/actions/promotion-guard
        with:
          image: ${{ env.IMAGE }}
          paths: ${{ env.PATHS }}
          mode: tag
          digest: ${{ steps.build.outputs.digest }}

  production:
    if: github.ref == 'refs/heads/prod' && vars.DEPLOYS_ENABLED == 'true'
    runs-on: ubuntu-24.04
    environment:
      name: production
      url: https://games.foweeti.com
    env:
      RG: ${{ vars.AZURE_RESOURCE_GROUP }}
      APP: ca-wrg-production-game
      RULE: gha-${{ github.run_id }}-${{ github.run_attempt }}
      ROLLBACK: ${{ inputs.staged_tag != '' }}
    steps:
      - uses: actions/checkout@v7

      - uses: docker/login-action@v4
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - id: guard
        name: Resolve the staged tag, or refuse
        uses: ./.github/actions/promotion-guard
        with:
          image: ${{ env.IMAGE }}
          paths: ${{ env.PATHS }}
          mode: resolve
          tag: ${{ inputs.staged_tag }}

      - uses: actions/setup-dotnet@v6
        if: env.ROLLBACK != 'true'
        with:
          global-json-file: global.json
      - name: Build the game migrations bundle
        if: env.ROLLBACK != 'true'
        run: .github/scripts/build-bundle.sh src/WorldRankGuesser.Api artifacts/efbundle-game

      - uses: azure/login@v3
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}

      - id: sql
        name: Admit the runner to the SQL server
        if: env.ROLLBACK != 'true'
        run: .github/scripts/sql-firewall.sh open
      - name: Apply the game migrations
        if: env.ROLLBACK != 'true'
        env:
          ConnectionStrings__WorldRankGuesserConnection: Server=tcp:${{ steps.sql.outputs.fqdn }},1433;Database=WorldRankGuesser;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=60
        run: ASPNETCORE_ENVIRONMENT=Production artifacts/efbundle-game
      - name: Remove the runner from the SQL server
        if: always() && steps.sql.outcome != 'skipped'
        run: .github/scripts/sql-firewall.sh close

      - name: Deploy the app by digest
        env:
          GAME_IMAGE: ${{ env.IMAGE }}@${{ steps.guard.outputs.digest }}
        run: >
          az deployment group create --resource-group "$RG" --name "game-${{ github.run_id }}"
          --template-file infra/game.bicep --parameters infra/production/game.bicepparam --output none
      - name: Wait for /readyz
        run: .github/scripts/wait-ready.sh "https://$(az containerapp show --name "$APP" --resource-group "$RG" --query properties.configuration.ingress.fqdn -o tsv)" 180
```

- [ ] **Step 4: Lint, mode bits, and the guard tests**

```powershell
actionlint
git add .github/scripts/sql-firewall.sh .github/scripts/wait-ready.sh .github/workflows/deploy-game.yml
git update-index --chmod=+x .github/scripts/sql-firewall.sh .github/scripts/wait-ready.sh
bash .github/actions/promotion-guard/test.sh
```

Expected: `actionlint` prints nothing (it now also checks `action.yml` through the `uses: ./.github/actions/promotion-guard` steps); the guard tests pass. If actionlint objects to `inputs.staged_tag` on a `push` event, it is a false positive by design: the `inputs` context is empty outside a dispatch, so `inputs.staged_tag != ''` is `false` on a push, which is what `ROLLBACK` needs; silence it with `# actionlint-ignore` only if it is reported as an error, not a warning.

- [ ] **Step 5: Commit**

```powershell
git commit -F - @'
Deploy the game: staging on main and stage/*, production on prod

The staging job builds sha-<sha>, applies the game migrations through a
temporary firewall rule, deploys by digest, admits the runner on the
ingress, waits for /readyz, plays the Playwright game, removes the
runner and only then tags staged-<hash>. The production job resolves
that tag at the tip of prod (or a tag given on dispatch, for a
rollback), applies the migrations, deploys by digest and polls /readyz.
It never builds and never touches the ingress. A repository variable,
DEPLOYS_ENABLED, keeps both jobs skipped until an environment exists.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 12: `deploy-scraper.yml` and the filter/inputs consistency test

Spec section 8.4: the same two jobs for the scraper, with the `dbo` bundle and `scraper.bicep`. Staging's smoke test is the scrape itself: start the Job once and wait for the execution to succeed. The production job deploys without running the Job; a dispatch input starts it once. Promotion moves the image, never the data. This task also adds the guard test that keeps each deploy workflow's push filter and its `env.PATHS` equal.

**Files:**
- Create: `.github/scripts/run-job.sh`
- Create: `.github/workflows/deploy-scraper.yml`
- Modify: `.github/actions/promotion-guard/test.sh` (the consistency section)

**Interfaces:**
- Consumes: the guard, `build-bundle.sh`, `sql-firewall.sh`; `infra/scraper.bicep` with `infra/<env>/scraper.bicepparam` reading `SCRAPER_IMAGE` (Task 9); the same GitHub variables as Task 11.
- Produces: `.github/scripts/run-job.sh <job> <resource group> <timeout seconds>` (also used by the runbook); the workflow `Deploy scraper`, dispatchable with `staged_tag` and `run_job`.

- [ ] **Step 1: Write the failing consistency test**

In `.github/actions/promotion-guard/test.sh`, replace the line

```bash
# ---- consistency of each deploy workflow's push filter with its hash inputs (added in Task 12) --------------------
```

with:

```bash
# ---- each deploy workflow's push filter equals its env.PATHS (directories listed there without /**) ---------------
for wf in deploy-game deploy-scraper; do
  file="$repo_root/.github/workflows/$wf.yml"
  if [[ ! -f "$file" ]]; then fail "$wf.yml exists"; continue; fi
  if ! command -v yq >/dev/null 2>&1; then echo "skip - yq is not installed: $wf filter/inputs consistency"; continue; fi
  filter="$(yq -r '.["on"].push.paths[]' "$file" | sed 's|/\*\*$||' | sort)"
  inputs="$(yq -r '.env.PATHS' "$file" | sed '/^[[:space:]]*$/d' | sort)"
  if [[ "$filter" == "$inputs" ]]; then
    pass "$wf: the push filter and the hash inputs agree"
  else
    fail "$wf: the push filter and the hash inputs differ"
    diff <(echo "$filter") <(echo "$inputs") || true
  fi
done
```

- [ ] **Step 2: Run it and see it fail**

In Git Bash: `bash .github/actions/promotion-guard/test.sh`

Expected: `ok` for `deploy-game` and `FAIL - deploy-scraper.yml exists`; exit 1.

- [ ] **Step 3: The Job runner script**

Create `.github/scripts/run-job.sh`:

```bash
#!/usr/bin/env bash
# Starts a Container Apps Job once and waits for that execution to succeed:
#   .github/scripts/run-job.sh caj-wrg-staging-scraper rg-wrg-staging 2100
# The scraper exits 1 when any feed failed, which marks the execution Failed; the Job's one retry then runs it
# again, so the wait allows two runs. Status values: Running, Processing, Succeeded, Failed, Stopped, Degraded, Unknown.
set -euo pipefail

job="${1:?job name}"
rg="${2:?resource group}"
timeout="${3:-2100}"

execution="$(az containerapp job start --name "$job" --resource-group "$rg" --query name -o tsv)"
echo "started $job execution $execution"
deadline=$(( $(date +%s) + timeout ))

while :; do
  status="$(az containerapp job execution show --name "$job" --resource-group "$rg" --job-execution-name "$execution" --query properties.status -o tsv)"
  echo "$(date -u +%T) $status"
  case "$status" in
    Succeeded) exit 0 ;;
    Failed|Stopped|Degraded)
      echo "::error::$job execution $execution ended with $status; read its console logs in Log Analytics (ContainerAppConsoleLogs_CL) or with: az containerapp job logs show --name $job --resource-group $rg --execution $execution --container scraper" >&2
      exit 1 ;;
  esac
  if [[ "$(date +%s)" -ge "$deadline" ]]; then
    echo "::error::timed out after ${timeout}s waiting for $job execution $execution" >&2
    exit 1
  fi
  sleep 20
done
```

- [ ] **Step 4: The workflow**

Create `.github/workflows/deploy-scraper.yml`:

```yaml
name: Deploy scraper

# The scraper's image and Job, promoted like the game (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md,
# section 8.4). Promotion moves the image, never the data: each environment scrapes into its own database on its
# own schedule. env.PATHS must list exactly the paths below, directories without /**.
on:
  push:
    branches: [main, 'stage/**', prod]
    paths:
      - 'src/SportsRankingService/**'
      - '.dockerignore'
      - 'Directory.Build.props'
      - 'global.json'
      - '.config/dotnet-tools.json'
      - 'infra/scraper.bicep'
      - 'infra/staging/scraper.bicepparam'
      - 'infra/production/scraper.bicepparam'
      - '.github/workflows/deploy-scraper.yml'
      - '.github/actions/promotion-guard/**'
      - '.github/scripts/**'
  workflow_dispatch:
    inputs:
      staged_tag:
        description: 'Production only: deploy this staged-<hash> tag instead of the tip of prod (a rollback; migrations are skipped)'
        required: false
        type: string
        default: ''
      run_job:
        description: 'Production only: start the Job once after deploying'
        required: false
        type: boolean
        default: false

permissions:
  contents: read
  packages: write
  id-token: write

concurrency:
  group: deploy-scraper-${{ github.ref == 'refs/heads/prod' && 'production' || 'staging' }}
  cancel-in-progress: false

env:
  IMAGE: ghcr.io/joseph-leo/worldrankguesser-scraper
  PATHS: |
    src/SportsRankingService
    .dockerignore
    Directory.Build.props
    global.json
    .config/dotnet-tools.json
    infra/scraper.bicep
    infra/staging/scraper.bicepparam
    infra/production/scraper.bicepparam
    .github/workflows/deploy-scraper.yml
    .github/actions/promotion-guard
    .github/scripts

jobs:
  staging:
    if: github.ref != 'refs/heads/prod' && vars.DEPLOYS_ENABLED == 'true'
    runs-on: ubuntu-24.04
    environment:
      name: staging
    env:
      RG: ${{ vars.AZURE_RESOURCE_GROUP }}
      JOB: caj-wrg-staging-scraper
      RULE: gha-${{ github.run_id }}-${{ github.run_attempt }}
    steps:
      - uses: actions/checkout@v7

      - id: guard
        uses: ./.github/actions/promotion-guard
        with:
          image: ${{ env.IMAGE }}
          paths: ${{ env.PATHS }}

      - uses: docker/login-action@v4
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - uses: docker/setup-buildx-action@v4
      - id: build
        name: Build and push sha-${{ github.sha }}
        uses: docker/build-push-action@v7
        with:
          context: .
          file: src/SportsRankingService/Dockerfile
          push: true
          provenance: false
          tags: ${{ env.IMAGE }}:sha-${{ github.sha }}

      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
      - name: Build the dbo migrations bundle
        run: .github/scripts/build-bundle.sh src/SportsRankingService artifacts/efbundle-scraper

      - uses: azure/login@v3
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}

      - id: sql
        name: Admit the runner to the SQL server
        run: .github/scripts/sql-firewall.sh open
      - name: Apply the dbo migrations
        env:
          ConnectionStrings__WorldRankGuesserConnection: Server=tcp:${{ steps.sql.outputs.fqdn }},1433;Database=WorldRankGuesser;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=60
        run: ASPNETCORE_ENVIRONMENT=Production artifacts/efbundle-scraper
      - name: Remove the runner from the SQL server
        if: always() && steps.sql.outcome != 'skipped'
        run: .github/scripts/sql-firewall.sh close

      - name: Deploy the Job by digest
        env:
          SCRAPER_IMAGE: ${{ env.IMAGE }}@${{ steps.build.outputs.digest }}
        run: >
          az deployment group create --resource-group "$RG" --name "scraper-${{ github.run_id }}"
          --template-file infra/scraper.bicep --parameters infra/staging/scraper.bicepparam --output none

      - name: Scrape once and wait for the execution to succeed
        run: .github/scripts/run-job.sh "$JOB" "$RG" 2100

      - name: Tag the image staged-${{ steps.guard.outputs.hash }}
        uses: ./.github/actions/promotion-guard
        with:
          image: ${{ env.IMAGE }}
          paths: ${{ env.PATHS }}
          mode: tag
          digest: ${{ steps.build.outputs.digest }}

  production:
    if: github.ref == 'refs/heads/prod' && vars.DEPLOYS_ENABLED == 'true'
    runs-on: ubuntu-24.04
    environment:
      name: production
    env:
      RG: ${{ vars.AZURE_RESOURCE_GROUP }}
      JOB: caj-wrg-production-scraper
      RULE: gha-${{ github.run_id }}-${{ github.run_attempt }}
      ROLLBACK: ${{ inputs.staged_tag != '' }}
    steps:
      - uses: actions/checkout@v7

      - uses: docker/login-action@v4
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - id: guard
        name: Resolve the staged tag, or refuse
        uses: ./.github/actions/promotion-guard
        with:
          image: ${{ env.IMAGE }}
          paths: ${{ env.PATHS }}
          mode: resolve
          tag: ${{ inputs.staged_tag }}

      - uses: actions/setup-dotnet@v6
        if: env.ROLLBACK != 'true'
        with:
          global-json-file: global.json
      - name: Build the dbo migrations bundle
        if: env.ROLLBACK != 'true'
        run: .github/scripts/build-bundle.sh src/SportsRankingService artifacts/efbundle-scraper

      - uses: azure/login@v3
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}

      - id: sql
        name: Admit the runner to the SQL server
        if: env.ROLLBACK != 'true'
        run: .github/scripts/sql-firewall.sh open
      - name: Apply the dbo migrations
        if: env.ROLLBACK != 'true'
        env:
          ConnectionStrings__WorldRankGuesserConnection: Server=tcp:${{ steps.sql.outputs.fqdn }},1433;Database=WorldRankGuesser;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=60
        run: ASPNETCORE_ENVIRONMENT=Production artifacts/efbundle-scraper
      - name: Remove the runner from the SQL server
        if: always() && steps.sql.outcome != 'skipped'
        run: .github/scripts/sql-firewall.sh close

      - name: Deploy the Job by digest
        env:
          SCRAPER_IMAGE: ${{ env.IMAGE }}@${{ steps.guard.outputs.digest }}
        run: >
          az deployment group create --resource-group "$RG" --name "scraper-${{ github.run_id }}"
          --template-file infra/scraper.bicep --parameters infra/production/scraper.bicepparam --output none

      - name: Scrape once (on request)
        if: inputs.run_job == true
        run: .github/scripts/run-job.sh "$JOB" "$RG" 2100
```

- [ ] **Step 5: Lint, mode bits, and the guard tests**

```powershell
actionlint
git add .github/scripts/run-job.sh .github/workflows/deploy-scraper.yml .github/actions/promotion-guard/test.sh
git update-index --chmod=+x .github/scripts/run-job.sh
bash .github/actions/promotion-guard/test.sh
```

Expected: `actionlint` prints nothing; the guard tests print `ok` for both consistency lines and "all guard tests passed".

- [ ] **Step 6: Commit**

```powershell
git commit -F - @'
Deploy the scraper; pin each deploy workflow's filter to its hash inputs

The scraper's staging smoke test is the scrape itself: the Job runs
once and the image is tagged only when the execution succeeded, which
is what a weekend parser fix needs before Monday's production run. The
production job deploys without scraping; a dispatch input starts the
Job once. The guard's tests now read both deploy workflows and fail
when a push filter and its env.PATHS list disagree.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 13: `infra.yml` and `scraper-check.yml`

Spec sections 8.5 and 8.6. `infra.yml` deploys `main.bicep` with the ref's parameter file. `scraper-check.yml` runs on Friday and Monday at 09:00 UTC, three hours after the matching environment's Job, reads its latest execution as the environment's `monitor` identity (Reader only, federated to `main`) and fails unless it succeeded within the last day; GitHub's failed-workflow email is the alert.

**Files:**
- Create: `.github/scripts/check-job.sh`
- Create: `.github/workflows/infra.yml`
- Create: `.github/workflows/scraper-check.yml`

**Interfaces:**
- Consumes: `infra/main.bicep` with `infra/<env>/main.bicepparam` reading `SQL_ADMIN_LOGIN`, `SQL_ADMIN_OBJECT_ID`, `BUDGET_EMAIL` (Task 7); repository variables `AZURE_MONITOR_CLIENT_ID_STAGING`, `AZURE_MONITOR_CLIENT_ID_PRODUCTION`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and repository secrets `SQL_ADMIN_LOGIN`, `SQL_ADMIN_OBJECT_ID`, `BUDGET_EMAIL` (Task 18; secrets, so a public repository's logs mask them).
- Produces: `.github/scripts/check-job.sh <job> <resource group> <max age seconds>`; the workflows `Deploy infrastructure` and `Scraper check` (dispatchable with an `environment` choice).

- [ ] **Step 1: The check script**

Create `.github/scripts/check-job.sh`:

```bash
#!/usr/bin/env bash
# Fails unless the Job's most recent execution succeeded and started within the last <max age seconds>:
#   .github/scripts/check-job.sh caj-wrg-staging-scraper rg-wrg-staging 86400
# No execution at all fails too: a check dispatched before the first run must fail (the spec's acceptance).
set -euo pipefail

job="${1:?job name}"
rg="${2:?resource group}"
max_age="${3:-86400}"

latest="$(az containerapp job execution list --name "$job" --resource-group "$rg" \
  --query 'sort_by(@, &properties.startTime)[-1].{name: name, status: properties.status, start: properties.startTime}' -o json)"

if [[ -z "$latest" || "$latest" == "null" ]]; then
  echo "::error::$job has no execution yet" >&2
  exit 1
fi

name="$(echo "$latest" | jq -r .name)"
status="$(echo "$latest" | jq -r .status)"
start="$(echo "$latest" | jq -r .start)"
age=$(( $(date +%s) - $(date -d "$start" +%s) ))
echo "$job latest execution $name: $status, started $start (${age}s ago)"

if [[ "$status" != "Succeeded" ]]; then
  echo "::error::$job latest execution $name is $status" >&2
  exit 1
fi
if [[ "$age" -gt "$max_age" ]]; then
  echo "::error::$job latest execution is ${age}s old, more than ${max_age}s" >&2
  exit 1
fi
```

- [ ] **Step 2: `infra.yml`**

Create `.github/workflows/infra.yml`:

```yaml
name: Deploy infrastructure

# main.bicep with the ref's parameter file (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md, section
# 8.5). No image, so nothing records that a change passed staging; it has at least been deployed there, because
# every change reaches prod through main. A change the game or the scraper needs is two merges, main.bicep first.
on:
  push:
    branches: [main, 'stage/**', prod]
    paths:
      - 'infra/main.bicep'
      - 'infra/staging/main.bicepparam'
      - 'infra/production/main.bicepparam'
      - '.github/workflows/infra.yml'
  workflow_dispatch:

permissions:
  contents: read
  id-token: write

concurrency:
  group: infra-${{ github.ref == 'refs/heads/prod' && 'production' || 'staging' }}
  cancel-in-progress: false

jobs:
  deploy:
    if: vars.DEPLOYS_ENABLED == 'true'
    runs-on: ubuntu-24.04
    environment:
      name: ${{ github.ref == 'refs/heads/prod' && 'production' || 'staging' }}
    env:
      ENV: ${{ github.ref == 'refs/heads/prod' && 'production' || 'staging' }}
      RG: ${{ vars.AZURE_RESOURCE_GROUP }}
      SQL_ADMIN_LOGIN: ${{ secrets.SQL_ADMIN_LOGIN }}
      SQL_ADMIN_OBJECT_ID: ${{ secrets.SQL_ADMIN_OBJECT_ID }}
      BUDGET_EMAIL: ${{ secrets.BUDGET_EMAIL }}
    steps:
      - uses: actions/checkout@v7
      - uses: azure/login@v3
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - name: Deploy main.bicep
        run: >
          az deployment group create --resource-group "$RG" --name "main-${{ github.run_id }}"
          --template-file infra/main.bicep --parameters "infra/$ENV/main.bicepparam" --output none
```

- [ ] **Step 3: `scraper-check.yml`**

Create `.github/workflows/scraper-check.yml`:

```yaml
name: Scraper check

# Three hours after each environment's Job (staging Friday 06:00 UTC, production Monday 06:00 UTC): fails unless the
# latest execution succeeded within the last day, so GitHub's failed-workflow email is the alert
# (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md, section 8.6). Scheduled runs use main, which the
# production environment does not admit, so the check logs in as the environment's monitor identity: Reader on its
# resource group, federated to refs/heads/main, repository variables rather than environment ones.
on:
  schedule:
    - cron: '0 9 * * 1,5'
  workflow_dispatch:
    inputs:
      environment:
        description: Which environment's Job to check
        type: choice
        options: [staging, production]
        default: staging

permissions:
  contents: read
  id-token: write

jobs:
  check:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v7
      - id: pick
        name: Friday is staging's day, Monday production's
        env:
          INPUT_ENV: ${{ inputs.environment }}
        run: |
          set -euo pipefail
          if [[ -n "$INPUT_ENV" ]]; then env_name="$INPUT_ENV"
          elif [[ "$(date -u +%u)" == "1" ]]; then env_name=production
          else env_name=staging
          fi
          echo "env=$env_name" >> "$GITHUB_OUTPUT"
          if [[ "$env_name" == "production" ]]; then
            echo "client_id=${{ vars.AZURE_MONITOR_CLIENT_ID_PRODUCTION }}" >> "$GITHUB_OUTPUT"
          else
            echo "client_id=${{ vars.AZURE_MONITOR_CLIENT_ID_STAGING }}" >> "$GITHUB_OUTPUT"
          fi
      - uses: azure/login@v3
        with:
          client-id: ${{ steps.pick.outputs.client_id }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - name: The latest execution succeeded within a day
        run: .github/scripts/check-job.sh "caj-wrg-${{ steps.pick.outputs.env }}-scraper" "rg-wrg-${{ steps.pick.outputs.env }}" 86400
```

- [ ] **Step 4: Lint and mode bits**

```powershell
actionlint
git add .github/scripts/check-job.sh .github/workflows/infra.yml .github/workflows/scraper-check.yml
git update-index --chmod=+x .github/scripts/check-job.sh
```

Expected: `actionlint` prints nothing.

- [ ] **Step 5: Commit**

```powershell
git commit -F - @'
Deploy the shared infrastructure; check the weekly scrape

infra.yml deploys main.bicep with the ref's parameter file. The scraper
check runs Friday and Monday at 09:00 UTC as the environment's monitor
identity, which can read and nothing else, and fails unless the Job's
latest execution succeeded within a day; a check dispatched before the
first run fails on purpose.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---
### Task 14: The runbook and the other documents

`infra/README.md` is the runbook the owner follows once per environment and afterwards for every unusual act (spec section 7). The root README says the licence position and the live address; the root CLAUDE.md gains the release flow of spec 8.1 and the deployment paragraph plan 2a deliberately left out; the spec records the hostname and the plan.

**Files:**
- Create: `infra/README.md`
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md:3-4` and the "Custom domain" row of section 2

**Interfaces:** none; documentation only. The runbook's commands are the same ones Parts B and C run.

- [ ] **Step 1: The runbook**

Create `infra/README.md`:

````markdown
# Running WorldRankGuesser in Azure

Two environments from the same templates: **staging** (`rg-wrg-staging`, private behind an IP allow-list, the
default hostname) and **production** (`rg-wrg-production`, public at https://games.foweeti.com). Design:
`docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md`, sections 7 and 8. Both cost $0 a month: the free
serverless database pauses when its monthly allowance is spent and the app scales to zero.

| Resource | Name (`<env>` is `staging` or `production`) |
|---|---|
| Resource group | `rg-wrg-<env>` |
| Log Analytics, Container Apps environment | `log-wrg-<env>`, `cae-wrg-<env>` |
| SQL server, database | `sql-wrg-<env>-<unique>`, `WorldRankGuesser` (Entra-only; the owner is the admin) |
| Game app, scraper Job | `ca-wrg-<env>-game`, `caj-wrg-<env>-scraper` |
| Identities | `id-wrg-<env>-deploy` (GitHub deploys), `id-wrg-<env>-monitor` (GitHub reads), `id-wrg-<env>-game`, `id-wrg-<env>-scraper` |
| Budget | `budget-wrg-<env>` |

The names are derived from `env` in the templates and spelled out in the workflows (`deploy-game.yml`,
`deploy-scraper.yml`, `scraper-check.yml`): a rename touches both.

## How releases flow

- A merge to `main` deploys to **staging**. A push to `stage/<anything>` also deploys to staging, with no CI first:
  the way to try unmerged work in Azure.
- `prod` is only ever fast-forwarded to a commit of `main`, and deploys to **production**:
  `git push origin main:prod` (or `git push origin <sha>:prod` for an earlier commit). Git refuses a non-fast-forward.
- **Production never builds.** Each deploy workflow hashes its inputs (`env.PATHS` in the workflow: exactly its
  push-filter paths) into `<hash>`; staging tags the image it built `staged-<hash>` only after its smoke test; the
  production job resolves `staged-<hash>` at the tip of `prod` or fails with "these sources never passed staging".
- A parser-only merge deploys only the scraper; a docs-only merge deploys nothing; a change to `main.bicep` goes
  through `infra.yml`, and a change that the game or the scraper needs is two merges, `main.bicep` first.
- Migrations run before the new revision starts, so every migration must work with the revision already running:
  add first, remove in a later deploy.
- The repository variable `DEPLOYS_ENABLED` is the kill switch: set it to anything but `true` and every deploy job
  is skipped.

## Prerequisites (once, on the owner's machine)

```powershell
winget install -e --id Microsoft.AzureCLI; winget install -e --id GitHub.cli
az bicep install
az login                                   # the owner's account, the one that becomes the SQL admin
az account set --subscription <subscription id>
foreach ($ns in 'Microsoft.App', 'Microsoft.OperationalInsights', 'Microsoft.Sql', 'Microsoft.ManagedIdentity', 'Microsoft.Consumption') { az provider register --namespace $ns --wait }
az extension add --name containerapp --upgrade
gh auth login --web -s workflow -s write:packages -s read:packages; gh auth setup-git
```

## An environment's first deploy (staging first; production the same with `production`)

1. **Bootstrap** (owner only; the only template with role assignments):

   ```powershell
   az deployment sub create --location eastus2 --name bootstrap-staging --parameters infra/staging/bootstrap.bicepparam --query properties.outputs
   ```

   Keep the outputs: `deployClientId`, `monitorClientId`, `tenantId`, `subscriptionId`, `resourceGroupName`.

2. **Shared resources.** The owner's identity and email are never committed; export them for the compile:

   ```powershell
   $me = az ad signed-in-user show --query "{id:id, upn:userPrincipalName}" | ConvertFrom-Json
   $env:SQL_ADMIN_LOGIN = $me.upn; $env:SQL_ADMIN_OBJECT_ID = $me.id; $env:BUDGET_EMAIL = '<the owner's email>'
   az deployment group create --resource-group rg-wrg-staging --name main-first --parameters infra/staging/main.bicepparam --query properties.outputs
   ```

   Keep `environmentDefaultDomain`, `customDomainVerificationId`, `sqlServerName`, `sqlServerFqdn`. On a subscription
   younger than 48 hours the budget can be refused: rerun with `$env:BUDGET_ENABLED = 'false'`, and once more with
   `'true'` a couple of days later. **The first free database fixes the region of every later one**: staging's is the
   commitment to East US 2. Check the offer took effect:

   ```powershell
   az sql db show --resource-group rg-wrg-staging --server <sqlServerName> --name WorldRankGuesser --query "{free: useFreeLimit, whenSpent: freeLimitExhaustionBehavior, sku: currentSku.name}"
   ```

3. **Database users.** In the portal, open the SQL database, **Query editor**, sign in with Entra (the owner is the
   admin), paste `infra/staging/bootstrap.sql` and run it. It says the view does not exist yet; that is expected.
   Run it again after step 6.

4. **GitHub environment and variables** (once per environment; the repository-level ones once):

   ```powershell
   gh api -X PUT repos/joseph-leo/WorldRankGuesser/environments/staging -F 'deployment_branch_policy[protected_branches]=false' -F 'deployment_branch_policy[custom_branch_policies]=true'
   foreach ($p in 'main', 'stage/*', 'stage/*/*') { gh api -X POST repos/joseph-leo/WorldRankGuesser/environments/staging/deployment-branch-policies -f name=$p -f type=branch }
   gh variable set AZURE_CLIENT_ID --env staging --body <deployClientId>
   gh variable set AZURE_RESOURCE_GROUP --env staging --body rg-wrg-staging
   gh secret set ALLOWED_IPS --env staging --body '["<the owner's public IPv4>/32"]'     # staging only
   gh variable set AZURE_TENANT_ID --body <tenantId>; gh variable set AZURE_SUBSCRIPTION_ID --body <subscriptionId>
   gh variable set AZURE_MONITOR_CLIENT_ID_STAGING --body <monitorClientId>
   gh secret set SQL_ADMIN_LOGIN --body $env:SQL_ADMIN_LOGIN; gh secret set SQL_ADMIN_OBJECT_ID --body $env:SQL_ADMIN_OBJECT_ID; gh secret set BUDGET_EMAIL --body $env:BUDGET_EMAIL
   gh variable set DEPLOYS_ENABLED --body true
   ```

   Production: the environment `production` admits only `prod`; its variables are `AZURE_CLIENT_ID` and
   `AZURE_RESOURCE_GROUP=rg-wrg-production`; no `ALLOWED_IPS`; `AZURE_MONITOR_CLIENT_ID_PRODUCTION` at repository level.

5. **Production only: DNS, before the first game deploy.** At the DNS host of `foweeti.com` (Google), add a CNAME
   `games` pointing at `ca-wrg-production-game.<environmentDefaultDomain>` and a TXT `asuid.games` whose value is
   `<customDomainVerificationId>`. Wait until both resolve (`nslookup -type=CNAME games.foweeti.com`,
   `nslookup -type=TXT asuid.games.foweeti.com`). The CNAME must point directly at the app, not through another
   CNAME. The certificate is issued by the first game deploy; if that deploy fails at the certificate, the records
   had not propagated: run it again.

6. **First deploys**, as manual dispatches, scraper first because the game's smoke test needs rankings:

   ```powershell
   gh workflow run scraper-check.yml --ref main -f environment=staging; gh run watch --exit-status   # must FAIL: no execution yet, and the email must arrive
   gh workflow run deploy-scraper.yml --ref main; gh run list --workflow deploy-scraper.yml --limit 1
   gh run watch <run id> --exit-status
   ```

   Then step 3 again (the view exists now), then:

   ```powershell
   gh workflow run deploy-game.yml --ref main; gh run watch <run id> --exit-status
   ```

   For production, `prod` must exist first (`git push origin <accepted sha>:refs/heads/prod`, then the ruleset), and
   the dispatches use `--ref prod` (`-f run_job=true` on the scraper, so the database has rankings).

   The first image push creates the two GHCR packages; they must be **public** (package settings, Danger Zone,
   change visibility) before Container Apps can pull them. Going public (plan 2c, Task 16) creates them ahead of time.

## Everyday operations

- **Promote:** `git push origin main:prod`. Watch `gh run list --workflow deploy-game.yml --branch prod`.
- **Roll back the game:** `gh workflow run deploy-game.yml --ref prod -f staged_tag=staged-<hash>` with an earlier
  tag from `gh api /users/joseph-leo/packages/container/worldrankguesser-game/versions --jq '.[].metadata.container.tags[]'`.
  Migrations are skipped; the earlier image must work with the current schema (add first, remove later).
- **Scrape on demand:** `az containerapp job start --name caj-wrg-<env>-scraper --resource-group rg-wrg-<env>`, then
  `bash .github/scripts/check-job.sh caj-wrg-<env>-scraper rg-wrg-<env> 3600`.
- **Admit another address to staging** (a phone on mobile data) until the next deploy resets the list:
  `az containerapp ingress access-restriction set --name ca-wrg-staging-game --resource-group rg-wrg-staging --rule-name phone --ip-address <ip>/32 --action Allow`.
  The owner's own address changed? Update the `ALLOWED_IPS` secret and dispatch `deploy-game.yml` on `main`.
- **Read the logs:** Log Analytics, table `ContainerAppConsoleLogs_CL`, filter `ContainerAppName_s`; or
  `az containerapp logs show --name ca-wrg-<env>-game --resource-group rg-wrg-<env> --tail 200`.
- **Cost stage 2** (production has players): `sqlSku = 'basic'` in `infra/production/main.bicepparam` and
  `minReplicas = 1` in `infra/production/game.bicepparam`, merged and promoted; about $10 a month fixed. A free
  database converts in place and cannot go back.
- **Reset staging** (a `stage/*` branch left a migration behind, or anything else): delete the group and redo the
  first deploy from step 1: `az group delete --name rg-wrg-staging --yes` (the free database slot returns within an
  hour). Nothing in staging is worth keeping.
- **Renaming or transferring the repository** breaks the federated credentials silently (GitHub's OIDC subject
  carries the repository name): redeploy `bootstrap.bicep` with the new `githubRepository`.
- **The allowance ran out** (the front end says the server is resting until the 1st): expected on the free tier;
  production moves to stage 2 when that happens to real players.
````

- [ ] **Step 2: README.md**

Replace the last line of `README.md` (`Design and plans are in \`docs/superpowers/\`.`) with:

```markdown
The game is live at https://games.foweeti.com. Design and plans are in `docs/superpowers/`; how it is deployed is in
`infra/README.md`.

No licence is granted: the code is published to be read, and all rights are reserved. Nobody may rehost the game.
```

- [ ] **Step 3: CLAUDE.md**

1. In "What this is", replace `Built so far: phases 0–1 (practice mode). Not built yet: deployment, the daily challenge, the timer, streaks, leaderboards, sign-in.` with `Built so far: phases 0–2 (practice mode, live at https://games.foweeti.com through a staging environment; \`infra/README.md\` is the runbook). Not built yet: the daily challenge, the timer, streaks, leaderboards, sign-in.`
2. In "Commands", after the `docker build ...` line, add:

   ```
   bash .github/actions/promotion-guard/test.sh                           # the promotion guard's tests (git, docker, yq); also CI's guard job
   actionlint                                                             # the workflows; also CI's actions job
   az bicep build --file infra/main.bicep --stdout | Out-Null; az bicep lint --file infra/main.bicep   # one template; CI's infra job does every template and parameter file
   gh workflow run deploy-game.yml --ref main                             # a deploy by hand; --ref prod for production, -f staged_tag=... to roll back
   git push origin main:prod                                              # promote: production deploys the images that passed staging, and never builds
   ```

3. Add a paragraph after "Hosting" (before "Two migration sets"):

   ```
   **Deployment.** Two Azure environments from the same Bicep (`infra/`: `bootstrap.bicep` owner-only with the role assignments, `main.bicep` the shared resources, `game.bicep` the Container App, `scraper.bicep` the weekly Job; one parameter folder per environment). `main` and `stage/*` deploy to staging, private behind an IP allow-list; `prod`, only ever fast-forwarded to a commit of `main`, deploys to production at `games.foweeti.com`. **Production never builds:** each deploy workflow hashes its push-filter paths (`env.PATHS`, pinned equal by the guard's tests) and the promotion guard (`.github/actions/promotion-guard`) tags the image `staged-<hash>` only after staging's smoke test (the Playwright game through the real ingress; for the scraper, one Job run), then resolves that tag on `prod` or refuses. Migrations run from the runner as self-contained EF bundles through a temporary SQL firewall rule, `dbo` before `game`; no SQL password exists (Entra-only, managed identities, GitHub OIDC). All three probes use `/healthz`. `scraper-check.yml` alerts by a failed run when the weekly scrape did not succeed. `DEPLOYS_ENABLED` (repository variable) is the kill switch. `infra/README.md` is the runbook.
   ```

4. In "Tests", append: `` The promotion guard's tests are bash (`test.sh`), run locally in Git Bash and by CI's `guard` job; the templates are checked by `az bicep build` in CI's `infra` job; there is no test of a deploy except the deploy. ``

- [ ] **Step 4: The spec**

In `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md`:

1. Replace `2c not yet written (sections 7 and 8, and the promotion-guard action of section 9).` in the status line with `2c \`docs/superpowers/plans/2026-09-22-phase-2c-azure-pipelines-going-public.md\` (sections 7 and 8, the promotion-guard action of section 9; the custom domain and its certificate deploy in one pass with \`bindingType: 'Auto'\`; consumption-only environments being legacy, the environment uses the built-in Consumption workload profile).`
2. In section 2's "Custom domain" row, replace `The hostname is a value in \`infra/production/game.bicepparam\`.` with `The hostname is \`games.foweeti.com\`, a value in \`infra/production/game.bicepparam\` (decided 2026-09-22; the DNS is at Google).`

- [ ] **Step 5: Commit**

```powershell
git add infra/README.md README.md CLAUDE.md docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md
git commit -F - @'
Add the Azure runbook; document the release flow and the licence

infra/README.md is what the owner follows once per environment and for
every unusual act afterwards: promotion, rollback, a scrape on demand,
another address on staging, cost stage 2, resetting staging. The README
says the game is live and that all rights are reserved; CLAUDE.md gains
the release flow and the deployment paragraph plan 2a left out.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
```

---

### Task 15: The pull request, green CI, the merge

Everything in Part A lands on `main` through one pull request with the six CI jobs green. The deploy workflows also trigger on that merge, and are skipped by the kill switch, because no environment exists yet.

**Files:** none.

- [ ] **Step 1: The whole suite once more, then push**

```powershell
dotnet build WorldRankGuesser.slnx
dotnet test WorldRankGuesser.slnx
cd src/WorldRankGuesser.Web; npm run check; npm test; cd ../..
actionlint
bash .github/actions/promotion-guard/test.sh
git status --short
git push -u origin phase-2c-azure-pipelines
```

Expected: build with 0 warnings; all tests pass (the API's and the scraper's; 36 Vitest); no lint output; the guard tests pass; `git status` prints nothing (in particular nothing under `src/WorldRankGuesser.Web/openapi`); the push succeeds (`gh auth setup-git` in Task 1 made git use the CLI's token).

- [ ] **Step 2: The pull request**

```powershell
gh pr create --base main --head phase-2c-azure-pipelines --title "Add the Azure templates, the deploy pipelines and the promotion guard" --body-file - @'
Phase 2c, part A, of docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md (sections 7, 8 and the promotion guard of 9): everything that needs no Azure account.

- Bicep for one environment shape: bootstrap (owner only, the role assignments), main (Log Analytics, Container Apps environment on the Consumption profile, Entra-only SQL with the free database, identities, budget), game (the app, by digest, custom hostname bound in one pass), scraper (the weekly Job); parameter folders for staging and production; the database users per environment.
- Four workflows: deploy-game, deploy-scraper (staging on main and stage/*, production on prod, which never builds), infra, scraper-check; a composite promotion guard with bash tests; the migration bundle and the runner scripts.
- CI: infra (bicep build and lint), actions (actionlint), guard (the guard's tests); the runner pinned to ubuntu-24.04.
- Both projects reference Microsoft.Data.SqlClient.Extensions.Azure, without which SqlClient 7 cannot authenticate with an identity.
- The front end says "waking" when the first readiness check hangs; an empty E2E_BASE_URL counts as unset.
- The runbook (infra/README.md), the release flow in CLAUDE.md, the licence position in the README.

The deploy jobs stay skipped until the repository variable DEPLOYS_ENABLED is true (part B).

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01BQ56r4edcySRfWPwY2maPq
'@
gh pr checks --watch
```

Expected: the six checks `api`, `web`, `images`, `infra`, `actions`, `guard` pass. A first-run failure is fixed on this branch with a commit carrying the trailers, then `git push`.

- [ ] **Step 3: Merge**

```powershell
gh pr merge --rebase --delete-branch
git switch main; git pull --ff-only
gh run list --limit 6
```

Expected: `main` has the branch's commits; the runs listed for the merge show `CI` in progress or passed and the three deploy workflows with every job skipped (grey), none red.

---

### Task 16: Going public

Spec section 8.8, in an order that works: the keys are revoked and the history scanned before anything is visible; secret scanning is a public-repository feature, so it is switched on right after the visibility change; the packages are created now so the first deploy finds them public.

**Files:** none in the repository (the old repository gets a README pointer).

- [ ] **Step 1: Owner: revoke the Sportradar trial keys**

The keys are in this repository's history. Show them to the owner (redacted to the first six characters):

```powershell
git log --all -p -S sportradar -i | Select-String -Pattern '[A-Za-z0-9]{32,}' | ForEach-Object { ($_ -split '[^A-Za-z0-9]')[1..99] | Where-Object { $_.Length -ge 32 } } | Sort-Object -Unique | ForEach-Object { $_.Substring(0, 6) + '…' }
```

**Owner:** at Sportradar's developer portal, revoke (delete) both trial keys, and confirm in the session. Revocation is the fix; history is not rewritten.

- [ ] **Step 2: Scan the full history**

```powershell
gitleaks git --log-opts="--all" --redact --report-format json --report-path artifacts/gitleaks.json .
```

Expected: exit 1 with findings, every one of which is a Sportradar key (revoked in Step 1) or the development password `Rankings_Dev1!` (a container bound to `127.0.0.1`, never reused). Any other finding stops this task: the owner decides whether the secret is live and rotates it first.

- [ ] **Step 3: Create the packages, so they can be public before the first deploy**

```powershell
gh auth token | docker login ghcr.io -u joseph-leo --password-stdin
docker build -t ghcr.io/joseph-leo/worldrankguesser-game:bootstrap .
docker build -f src/SportsRankingService/Dockerfile -t ghcr.io/joseph-leo/worldrankguesser-scraper:bootstrap .
docker push ghcr.io/joseph-leo/worldrankguesser-game:bootstrap
docker push ghcr.io/joseph-leo/worldrankguesser-scraper:bootstrap
docker logout ghcr.io
```

**Owner:** on GitHub, for each package (https://github.com/joseph-leo?tab=packages), Package settings, Danger Zone, Change visibility, Public. Then:

```powershell
gh api /users/joseph-leo/packages/container/worldrankguesser-game --jq .visibility
gh api /users/joseph-leo/packages/container/worldrankguesser-scraper --jq .visibility
```

Expected: `public` twice. (A push from a workflow later adds tags to these packages and cannot change their visibility.)

- [ ] **Step 4: Visibility, scanning, approvals**

```powershell
gh repo edit joseph-leo/WorldRankGuesser --visibility public --accept-visibility-change-consequences
gh repo edit joseph-leo/WorldRankGuesser --enable-secret-scanning --enable-secret-scanning-push-protection
gh api -X PUT repos/joseph-leo/WorldRankGuesser/actions/permissions/fork-pr-contributor-approval -f approval_policy=all_external_contributors
gh repo view joseph-leo/WorldRankGuesser --json visibility --jq .visibility
gh api repos/joseph-leo/WorldRankGuesser --jq '.security_and_analysis'
```

Expected: `PUBLIC`; `secret_scanning` and `secret_scanning_push_protection` both `enabled`.

- [ ] **Step 5: Protect `main`**

```powershell
gh api -X POST repos/joseph-leo/WorldRankGuesser/rulesets --input - @'
{
  "name": "main",
  "target": "branch",
  "enforcement": "active",
  "bypass_actors": [],
  "conditions": { "ref_name": { "include": ["~DEFAULT_BRANCH"], "exclude": [] } },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" },
    { "type": "pull_request", "parameters": {
        "required_approving_review_count": 0,
        "dismiss_stale_reviews_on_push": true,
        "require_code_owner_review": false,
        "require_last_push_approval": false,
        "required_review_thread_resolution": false,
        "allowed_merge_methods": ["rebase", "squash"] } },
    { "type": "required_status_checks", "parameters": {
        "strict_required_status_checks_policy": true,
        "do_not_enforce_on_create": false,
        "required_status_checks": [
          { "context": "api" }, { "context": "web" }, { "context": "images" },
          { "context": "infra" }, { "context": "actions" }, { "context": "guard" } ] } }
  ]
}
'@
gh api repos/joseph-leo/WorldRankGuesser/rulesets --jq '.[] | {name, enforcement}'
```

Expected: the ruleset `main`, `active`. From now on `main` changes only by pull request with the six checks green, with no exception for the owner; the plan's later fixes go through `stage/*` branches and pull requests.

- [ ] **Step 6: The old repository**

Give the old scraper repository a README pointer and archive it; it stays private.

```powershell
$old = Join-Path $env:TEMP 'SportsRankingService-archive'
git clone https://github.com/joseph-leo/SportsRankingService.git $old
Set-Content -Path (Join-Path $old 'README.md') -Encoding utf8 -Value @'
# SportsRankingService (archived)

This scraper moved into https://github.com/joseph-leo/WorldRankGuesser on 2026-09-21 (`src/SportsRankingService`),
with its history. Nothing here is maintained; the tag `scraper-net8-baseline` there marks the last state of this repository.
'@
git -C $old add README.md
git -C $old commit -m "Point at the WorldRankGuesser repository; archived"
git -C $old push
gh repo archive joseph-leo/SportsRankingService -y
gh repo view joseph-leo/SportsRankingService --json isArchived,visibility
Remove-Item -Recurse -Force $old
```

Expected: `isArchived: true`, `visibility: PRIVATE`. If the old repository's `main` is protected, the owner pushes the README from the GitHub web editor instead.

---
## Part B: staging

From here on the owner's Azure account is in play. Every command runs from this machine after `! az login` (owner) as the owner's account, which is what `bootstrap.bicep` needs (role assignments) and what becomes the SQL admin. Each task is a checkpoint: it ends when its expected state is observed, not when its commands ran. First-run fixes to code go on a `stage/<topic>` branch (deploys to staging with no CI) and then through a pull request, since `main` is protected.

### Task 17: The subscription and staging's shared resources

**Files:** none.

**Interfaces:**
- Produces: the resource group `rg-wrg-staging` with everything `main.bicep` declares; the outputs the next task needs, saved to the scratchpad file `staging-outputs.json` (never to the repository).

- [ ] **Step 1: Owner: the account, the subscription, the login**

**Owner:** create the Microsoft account and an Azure pay-as-you-go subscription in the portal (the free trial if offered; nothing here needs its credit), then in the session `! az login` and choose that subscription. Then the implementer confirms:

```powershell
az account show --query "{subscription: id, name: name, tenant: tenantId, user: user.name}"
```

Expected: the new subscription, the owner's account.

- [ ] **Step 2: Register the providers (a new subscription has none)**

```powershell
foreach ($ns in 'Microsoft.App', 'Microsoft.OperationalInsights', 'Microsoft.Sql', 'Microsoft.ManagedIdentity', 'Microsoft.Consumption') { az provider register --namespace $ns --wait; az provider show --namespace $ns --query "{ns: namespace, state: registrationState}" -o tsv }
az extension add --name containerapp --upgrade
```

Expected: five lines ending in `Registered`.

- [ ] **Step 3: Bootstrap staging (owner rights: the role assignments)**

```powershell
az deployment sub create --location eastus2 --name bootstrap-staging --parameters infra/staging/bootstrap.bicepparam --query properties.outputs > "$env:TEMP\staging-bootstrap.json"
Get-Content "$env:TEMP\staging-bootstrap.json"
```

Expected: JSON with `deployClientId`, `monitorClientId`, `tenantId`, `subscriptionId`, `resourceGroupName` (`rg-wrg-staging`). A `RoleAssignmentUpdateNotPermitted` or `PrincipalNotFound` error is a replication delay: run the same command again.

- [ ] **Step 4: The shared resources (this commits the subscription to East US 2)**

```powershell
$me = az ad signed-in-user show --query "{id:id, upn:userPrincipalName}" | ConvertFrom-Json
$env:SQL_ADMIN_LOGIN = $me.upn; $env:SQL_ADMIN_OBJECT_ID = $me.id
$env:BUDGET_EMAIL = '<the owner's email, asked in the session>'
az deployment group create --resource-group rg-wrg-staging --name main-first --parameters infra/staging/main.bicepparam --query properties.outputs > "$env:TEMP\staging-main.json"
Get-Content "$env:TEMP\staging-main.json"
```

Expected: JSON with `environmentDefaultDomain`, `customDomainVerificationId`, `sqlServerName`, `sqlServerFqdn`, `gameIdentityClientId`, `scraperIdentityClientId`. Known first-run failures and what to do:

- The budget is refused (`BudgetCreationFailed`, Cost Management not ready on a subscription younger than 48 h): `$env:BUDGET_ENABLED = 'false'` and rerun; in Task 20 set it back to `'true'` and rerun.
- The SQL server is refused in the region (`ProvisioningDisabled`, `LocationNotAvailableForResourceType`): spec section 10's fallback is another US region chosen before any free database exists; change `location` in `infra/staging/bootstrap.bicepparam`, `infra/production/bootstrap.bicepparam` and the runbook, delete `rg-wrg-staging`, and restart from Step 3. That change is a pull request.
- The free offer is refused (`FreeLimitNotAvailable` or similar): the offer is created by the same properties the Azure CLI uses; retry once, and if it persists, the runbook records the CLI command `az sql db create ... --use-free-limit true --free-limit-exhaustion-behavior AutoPause` as the way to create the database, followed by the same deployment, which then only updates it.

Then confirm the offer and the pause behaviour:

```powershell
$o = Get-Content "$env:TEMP\staging-main.json" | ConvertFrom-Json
az sql db show --resource-group rg-wrg-staging --server $o.sqlServerName.value --name WorldRankGuesser --query "{free: useFreeLimit, whenSpent: freeLimitExhaustionBehavior, sku: currentSku.name, pauseAfterMinutes: autoPauseDelay}"
```

Expected: `free: true`, `whenSpent: AutoPause`, `sku: GP_S_Gen5`, `pauseAfterMinutes: 15`.

- [ ] **Step 5: Owner: the database users (first run)**

**Owner:** portal, the `WorldRankGuesser` database in `rg-wrg-staging`, **Query editor (preview)**, sign in with Entra as the admin, paste `infra/staging/bootstrap.sql`, Run.

Expected: the results grid lists `id-wrg-staging-deploy` (`db_owner`), `id-wrg-staging-scraper` and `id-wrg-staging-game`, and the message "dbo.CurrentCountryRankings does not exist yet: run this file again after the first scraper deploy." A `Principal 'id-wrg-staging-…' could not be found` error means the identity's name is wrong or the tenant has not replicated it yet: check `az identity list --resource-group rg-wrg-staging --query "[].name"` and retry after a minute.

---

### Task 18: The GitHub `staging` environment and the kill switch

**Files:** none.

**Interfaces:**
- Consumes: `staging-bootstrap.json` and `staging-main.json` from Task 17.
- Produces: the GitHub environment `staging` admitting `main`, `stage/*` and `stage/*/*`, with `AZURE_CLIENT_ID`, `AZURE_RESOURCE_GROUP` and the secret `ALLOWED_IPS`; repository variables `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_MONITOR_CLIENT_ID_STAGING`, `DEPLOYS_ENABLED`; repository secrets `SQL_ADMIN_LOGIN`, `SQL_ADMIN_OBJECT_ID`, `BUDGET_EMAIL`.

- [ ] **Step 1: The environment and its branch policies**

```powershell
$b = Get-Content "$env:TEMP\staging-bootstrap.json" | ConvertFrom-Json
gh api -X PUT repos/joseph-leo/WorldRankGuesser/environments/staging -F 'deployment_branch_policy[protected_branches]=false' -F 'deployment_branch_policy[custom_branch_policies]=true' --jq .name
foreach ($p in 'main', 'stage/*', 'stage/*/*') { gh api -X POST repos/joseph-leo/WorldRankGuesser/environments/staging/deployment-branch-policies -f name=$p -f type=branch --jq .name }
```

Expected: `staging`, then `main`, `stage/*`, `stage/*/*`.

- [ ] **Step 2: Variables and secrets**

The owner's public IPv4 address: **Owner:** `! curl -s https://api.ipify.org` in the session (the address of the network the owner plays from; a phone on mobile data is admitted later by hand).

```powershell
gh variable set AZURE_CLIENT_ID --env staging --body $b.deployClientId.value
gh variable set AZURE_RESOURCE_GROUP --env staging --body rg-wrg-staging
gh secret set ALLOWED_IPS --env staging --body '["<the owner's IPv4>/32"]'
gh variable set AZURE_TENANT_ID --body $b.tenantId.value
gh variable set AZURE_SUBSCRIPTION_ID --body $b.subscriptionId.value
gh variable set AZURE_MONITOR_CLIENT_ID_STAGING --body $b.monitorClientId.value
gh secret set SQL_ADMIN_LOGIN --body $env:SQL_ADMIN_LOGIN
gh secret set SQL_ADMIN_OBJECT_ID --body $env:SQL_ADMIN_OBJECT_ID
gh secret set BUDGET_EMAIL --body $env:BUDGET_EMAIL
gh variable set DEPLOYS_ENABLED --body true
gh variable list; gh variable list --env staging; gh secret list; gh secret list --env staging
```

Expected: the lists show every name above. (The three `SQL_ADMIN_*` and `BUDGET_EMAIL` values are secrets, not variables, so a public repository's logs mask them; `infra.yml` reads them from `secrets`.)

- [ ] **Step 3: The check that must fail**

Spec section 9's acceptance: `scraper-check.yml`, dispatched before the first Job run, fails and its email arrives.

```powershell
gh workflow run scraper-check.yml --ref main -f environment=staging
Start-Sleep -Seconds 20
$run = gh run list --workflow scraper-check.yml --limit 1 --json databaseId --jq '.[0].databaseId'
gh run watch $run --exit-status; $LASTEXITCODE
```

Expected: the run logs in as the monitor identity (proving the federated credential and the Reader role) and fails at "The latest execution succeeded within a day" with `caj-wrg-staging-scraper has no execution yet`; `$LASTEXITCODE` is 1. **Owner:** confirm the "Run failed" email arrived. A failure at `azure/login` instead is a wrong `AZURE_MONITOR_CLIENT_ID_STAGING` or a wrong federated subject (`repo:joseph-leo/WorldRankGuesser:ref:refs/heads/main`): check with `az identity federated-credential list --identity-name id-wrg-staging-monitor --resource-group rg-wrg-staging`.

---

### Task 19: Staging's first deploys

Spec section 8.7: manual dispatches, the scraper first, because the game's smoke test plays a game and needs rankings in the view.

**Files:** none, unless a first-run fix is needed (then a `stage/<topic>` branch and a pull request).

**Interfaces:**
- Produces: `ghcr.io/joseph-leo/worldrankguesser-scraper:staged-<hash>` and `...-game:staged-<hash>`; staging serving a playable game at its default hostname, to the allowed address only.

- [ ] **Step 1: The scraper**

```powershell
gh workflow run deploy-scraper.yml --ref main
Start-Sleep -Seconds 20
$run = gh run list --workflow deploy-scraper.yml --limit 1 --json databaseId --jq '.[0].databaseId'
gh run watch $run --exit-status
```

Expected, in order: the guard prints `inputs hash <12 hex>`; the image is pushed as `sha-<sha>`; the bundle applies the `dbo` migrations (the firewall rule appears and disappears); `scraper.bicep` deploys; the Job runs and its execution reaches `Succeeded` (10 to 20 minutes: 54 feeds, then the save); the tag step prints `tagged ...:staged-<hash>`. Confirm the data:

**Owner:** run `infra/staging/bootstrap.sql` again in the query editor (the view exists now: the last message is gone and the game user has its grant), then in the same editor: `SELECT COUNT(*) FROM dbo.CurrentCountryRankings;`

Expected: about 3,600 rows (the local view had 3,590 on 2026-09-22; the comparison is `docker exec worldrankguesser-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -d WorldRankGuesser -Q "SELECT COUNT(*) FROM dbo.CurrentCountryRankings"`, from PowerShell, since Git Bash rewrites the `/opt` path). A difference of a few percent is feeds that answered differently from Azure and is recorded in the runbook's first-deploy notes; a large difference means feeds failed, and the run would not have succeeded.

Where a step fails:

- `azure/login`: the environment's `AZURE_CLIENT_ID` or the federated subject `repo:joseph-leo/WorldRankGuesser:environment:staging`.
- The bundle cannot log in to SQL (`Login failed for user '<token-identified principal>'`): `bootstrap.sql` did not run, or the deploy identity's user is missing.
- The bundle cannot connect at all: the firewall step; check `az sql server firewall-rule list --resource-group rg-wrg-staging --server <sqlServerName>` shows a `gha-…` rule during the run.
- The Job execution fails: the console logs, `az containerapp job execution list --name caj-wrg-staging-scraper --resource-group rg-wrg-staging -o table`, then Log Analytics (`ContainerAppConsoleLogs_CL | where ContainerJobName_s == 'caj-wrg-staging-scraper' | order by TimeGenerated desc`). A `Login failed` there is the scraper identity's user; a curl or Cloudflare refusal on a feed is spec section 6's second risk (a datacenter address): the run exits 1, and the fix is the scraper's convention, disable that feed with a dated note on a `stage/` branch, then a pull request.
- The image cannot be pulled (`ImagePullBackOff` on the execution): the package is not public (Task 16, Step 3).

- [ ] **Step 2: The game**

```powershell
gh workflow run deploy-game.yml --ref main
Start-Sleep -Seconds 20
$run = gh run list --workflow deploy-game.yml --limit 1 --json databaseId --jq '.[0].databaseId'
gh run watch $run --exit-status
```

Expected: build and push; the `game` migrations; the deploy (the first one creates the app: two to three minutes); the runner admitted on the ingress; `/readyz` 200 within 180 s; both Playwright tests pass through the real ingress, over HTTPS with the `Secure` cookie, against Azure SQL with the managed identity; the runner removed; `tagged ...:staged-<hash>`. Where a step fails:

- `/readyz` stays 503: the app cannot load the rankings. `az containerapp logs show --name ca-wrg-staging-game --resource-group rg-wrg-staging --tail 100`: a `Login failed` is the game identity's user or its `SELECT` on the view (the second `bootstrap.sql` run); a missing ISO2 code names a country to add to `Countries/countries.json` or `NotDrawable` (a code change through `stage/` and a pull request).
- Playwright fails: the report is uploaded as the run's artifact; `gh run download $run --name playwright-report`.
- The ingress step fails with "all rules must be the same action": the secret `ALLOWED_IPS` holds a Deny rule or is malformed; it must be a JSON array of CIDR strings.

- [ ] **Step 3: The app is private**

**Owner:** open `https://<fqdn>` (the run's environment URL, `gh run view $run --json jobs --jq '.jobs[0].url'` shows the run; `az containerapp show --name ca-wrg-staging-game --resource-group rg-wrg-staging --query properties.configuration.ingress.fqdn -o tsv` the hostname) from the allowed network: the start screen appears and a practice game can be played. From a phone on mobile data: the page is refused (`RBAC: Access Denied`). To admit the phone until the next deploy: `az containerapp ingress access-restriction set --name ca-wrg-staging-game --resource-group rg-wrg-staging --rule-name phone --ip-address <phone ip>/32 --action Allow`.

- [ ] **Step 4: Record the first-deploy facts in the runbook**

Whatever differed from the plan (a region fallback, the budget delay, the parameter-file form for `allowedIps`, a feed that answers differently from Azure) goes into `infra/README.md` under a new heading `## First-deploy notes (staging, <date>)`, in a few lines each, on a branch `stage/runbook-notes` merged by pull request. If nothing differed, the heading says so in one line.

---

### Task 20: Staging accepted

Spec section 9, "Staging". Each item is observed once and recorded in the runbook's first-deploy notes.

**Files:** `infra/README.md` (the notes).

- [ ] **Step 1: A cold start plays a game within 90 seconds**

Leave staging alone for 20 minutes (the database pauses after 15, the app scales to zero after 5). First, spec section 10's question, whether a refused request wakes the app: `az containerapp ingress access-restriction remove --name ca-wrg-staging-game --resource-group rg-wrg-staging --rule-name phone` (if the phone was admitted in Task 19), then **Owner:** request the page from the phone on mobile data (refused), then:

```powershell
az containerapp replica list --name ca-wrg-staging-game --resource-group rg-wrg-staging --revision (az containerapp show --name ca-wrg-staging-game --resource-group rg-wrg-staging --query properties.latestRevisionName -o tsv) --query "length(@)"
```

Expected: `0`: the ingress refused the request without starting a replica, so strangers cannot spend staging's allowance. (A `1` means they can; record it in the runbook and keep the allow-list anyway, since it still keeps the database asleep only if no app replica connects.) Then **Owner:** open the start page from the allowed network. Expected: "Waking up the server…" within about 2 seconds (Task 2's change: the first `/readyz` hangs while the database resumes), the Practice button enabled within 90 seconds, a full game plays. Record the time to ready.

- [ ] **Step 2: The per-IP limit keys on the caller, not the ingress**

`Hosting__TrustForwardedHeaders=true` with the ingress rewriting `X-Forwarded-For`. The proof is the rate limit: anonymous starts with no cookie create a new player each, so the per-player limit never trips, and the per-IP limit (120 an hour) does. From the allowed network, in Git Bash:

```bash
fqdn=$(az containerapp show --name ca-wrg-staging-game --resource-group rg-wrg-staging --query properties.configuration.ingress.fqdn -o tsv)
for i in $(seq 1 121); do code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "https://$fqdn/api/games" -H 'Content-Type: application/json' -d '{"mode":"practice"}'); [[ $i -ge 119 ]] && echo "$i -> $code"; done
```

Expected: `119 -> 200`, `120 -> 200`, `121 -> 429` (a start answers 200 with the game state). Then **Owner:** from the phone on mobile data (admitted as in Task 19, Step 3) start one game: it succeeds, because the phone's address has its own limit. If the 121st start were also refused from the phone, the limit is keyed on the ingress's address and forwarded headers are not being honoured: check the `Hosting__TrustForwardedHeaders` value on the running revision (`az containerapp show ... --query "properties.template.containers[0].env"`). The 121 practice games stay in staging's database.

- [ ] **Step 3: The Job's results match a local run**

`az containerapp job execution list --name caj-wrg-staging-scraper --resource-group rg-wrg-staging -o table` shows one `Succeeded` execution; in Log Analytics, `ContainerAppConsoleLogs_CL | where ContainerJobName_s == 'caj-wrg-staging-scraper' | where Log_s contains 'feeds:' | project TimeGenerated, Log_s` shows `54 feeds: … 0 failed`. Compare the per-feed `Parsed N rows` lines with a local `dotnet run --project src/SportsRankingService`: differences are recorded.

- [ ] **Step 4: Path filters do what the spec says**

Two pull requests, one after the other, each merged: one that touches only `src/SportsRankingService/README.md` (a sentence in "Scheduling a weekly run" saying the Azure Job is scheduled by `infra/scraper.bicep`) and one that touches only `docs/superpowers/plans/2026-09-22-phase-2c-azure-pipelines-going-public.md` (a line at the top: `Status: part B in progress, staging accepted <date>.`). After each merge: `gh run list --limit 5`. Expected: after the first, `Deploy scraper` ran (and passed: the Job ran again) and `Deploy game` did not; after the second, only `CI` ran.

- [ ] **Step 5: The budget exists**

```powershell
az rest --method get --url "https://management.azure.com/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-wrg-staging/providers/Microsoft.Consumption/budgets/budget-wrg-staging?api-version=2024-08-01" --query "{amount: properties.amount, grain: properties.timeGrain}"
```

Expected: `amount: 1`, `grain: Monthly`. If Task 17 deployed with `BUDGET_ENABLED=false`, rerun that deployment with `'true'` now (the subscription is older than 48 hours by this point) and check again.

- [ ] **Step 6: Notes**

Add the observations (time to ready, the rate-limit proof, the feed comparison, the run list of Step 4, the budget) to `infra/README.md`'s first-deploy notes, on a `stage/` branch, by pull request. Staging is accepted when every step above was observed.

---

## Part C: production

### Task 21: Production's shared resources and the DNS records

The same as Task 17 with `production`, plus the two DNS records the custom hostname needs before the first game deploy. Production stays quiet until `prod` exists: no workflow targets it.

**Files:** none.

**Interfaces:**
- Produces: `rg-wrg-production` with everything `main.bicep` declares; `production-bootstrap.json` and `production-main.json` in the scratchpad; the DNS records `games.foweeti.com` (CNAME) and `asuid.games.foweeti.com` (TXT).

- [ ] **Step 1: Bootstrap and the shared resources**

```powershell
az deployment sub create --location eastus2 --name bootstrap-production --parameters infra/production/bootstrap.bicepparam --query properties.outputs > "$env:TEMP\production-bootstrap.json"
$me = az ad signed-in-user show --query "{id:id, upn:userPrincipalName}" | ConvertFrom-Json
$env:SQL_ADMIN_LOGIN = $me.upn; $env:SQL_ADMIN_OBJECT_ID = $me.id; $env:BUDGET_EMAIL = '<the owner's email>'
az deployment group create --resource-group rg-wrg-production --name main-first --parameters infra/production/main.bicepparam --query properties.outputs > "$env:TEMP\production-main.json"
Get-Content "$env:TEMP\production-bootstrap.json"; Get-Content "$env:TEMP\production-main.json"
$o = Get-Content "$env:TEMP\production-main.json" | ConvertFrom-Json
az sql db show --resource-group rg-wrg-production --server $o.sqlServerName.value --name WorldRankGuesser --query "{free: useFreeLimit, whenSpent: freeLimitExhaustionBehavior, region: location}"
```

Expected: the same outputs as staging's; the database `free: true`, `whenSpent: AutoPause`, region `eastus2` (the second free database is bound to the first one's region). The budget is $5 here (`amount` 5 in the parameter file). If the second Container Apps environment is refused by quota (`Managed Environment Count`; a new subscription's default is not published), request the quota in the portal (Quotas, provider Azure Container Apps, East US 2) and rerun; if that is refused, spec section 10's fallback, both apps and both Jobs in one environment, is a template change designed as its own piece of work before continuing.

- [ ] **Step 2: Owner: the database users (first run)**

**Owner:** the query editor on `rg-wrg-production`'s database, `infra/production/bootstrap.sql`, as in Task 17 Step 5. Expected: the three `id-wrg-production-…` users and the "run again" message.

- [ ] **Step 3: Owner: the DNS records**

The CNAME target is deterministic: the app's name plus the environment's default domain.

```powershell
"CNAME games -> ca-wrg-production-game.$($o.environmentDefaultDomain.value)"
"TXT   asuid.games -> $($o.customDomainVerificationId.value)"
```

**Owner:** in the DNS console for `foweeti.com` at Google, add the two records exactly as printed (the CNAME with the target's trailing dot if the console wants one; the TXT value without quotes). Then the implementer waits for propagation:

```powershell
nslookup -type=CNAME games.foweeti.com 8.8.8.8
nslookup -type=TXT asuid.games.foweeti.com 8.8.8.8
```

Expected: the CNAME answers with `ca-wrg-production-game.<default domain>` and the TXT with the verification id. Google's DNS usually propagates within minutes; the certificate is issued only once both resolve.

---

### Task 22: The `production` environment, the `prod` branch, the first production deploys

Spec sections 8.1 and 8.7: the GitHub environment admits only `prod`; `prod` is created at the commit staging accepted, protected against force pushes, deletion and merge commits; the first deploys are dispatches on `prod`, because creating a branch at an existing commit may trigger nothing. Both resolve their `staged-` tags and build nothing.

**Files:** none.

- [ ] **Step 1: The GitHub environment**

```powershell
$b = Get-Content "$env:TEMP\production-bootstrap.json" | ConvertFrom-Json
gh api -X PUT repos/joseph-leo/WorldRankGuesser/environments/production -F 'deployment_branch_policy[protected_branches]=false' -F 'deployment_branch_policy[custom_branch_policies]=true' --jq .name
gh api -X POST repos/joseph-leo/WorldRankGuesser/environments/production/deployment-branch-policies -f name=prod -f type=branch --jq .name
gh variable set AZURE_CLIENT_ID --env production --body $b.deployClientId.value
gh variable set AZURE_RESOURCE_GROUP --env production --body rg-wrg-production
gh variable set AZURE_MONITOR_CLIENT_ID_PRODUCTION --body $b.monitorClientId.value
gh variable list --env production
```

Expected: `production`, `prod`, then the two environment variables listed.

- [ ] **Step 2: `prod`, at the commit staging accepted, and its ruleset**

The accepted commit is the tip of `main` at the end of Task 20 (the runbook-notes merge included).

```powershell
git switch main; git pull --ff-only
$sha = git rev-parse main
git push origin "${sha}:refs/heads/prod"
gh api -X POST repos/joseph-leo/WorldRankGuesser/rulesets --input - @'
{
  "name": "prod",
  "target": "branch",
  "enforcement": "active",
  "bypass_actors": [],
  "conditions": { "ref_name": { "include": ["refs/heads/prod"], "exclude": [] } },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" },
    { "type": "required_linear_history" }
  ]
}
'@
gh run list --limit 4
```

Expected: `prod` exists at `main`'s commit; the ruleset `prod` is active (no pull-request rule: promotion is a fast-forward push from the command line, which GitHub's merge button cannot produce). The run list shows either nothing new for `prod` or runs that went straight to the production job; a production run that started by itself and failed at its smoke test (no rankings yet) is rerun after the scraper's Job in Step 3.

- [ ] **Step 3: The scraper, then the game**

```powershell
gh workflow run deploy-scraper.yml --ref prod -f run_job=true
Start-Sleep -Seconds 20
$run = gh run list --workflow deploy-scraper.yml --branch prod --limit 1 --json databaseId --jq '.[0].databaseId'
gh run watch $run --exit-status
```

Expected: the production job (the staging job skipped): the guard prints `<image>:staged-<hash> -> sha256:…` and **no build step runs**; the `dbo` migrations; the Job deploys; the Job runs once (`run_job`) and succeeds. Then **Owner:** `infra/production/bootstrap.sql` once more in the query editor (the view's grant). Then:

```powershell
gh workflow run deploy-game.yml --ref prod
Start-Sleep -Seconds 20
$run = gh run list --workflow deploy-game.yml --branch prod --limit 1 --json databaseId --jq '.[0].databaseId'
gh run watch $run --exit-status
```

Expected: the guard resolves `staged-<hash>` (the same hash staging tagged, because the sources are the same tree entries); the `game` migrations; the deployment creates the app with `games.foweeti.com` and issues the managed certificate (the deployment can take several minutes at the certificate); `/readyz` 200 at the default hostname. If the deployment fails at the certificate (`InvalidDomainControlValidation`, "CNAME record not found"): the records had not propagated to Azure's resolver; wait ten minutes and dispatch again. If the guard refuses ("never passed staging"): `prod` is not at a commit whose game inputs staging tagged; compare `bash .github/actions/promotion-guard/hash.sh <the PATHS list>` on `prod` and on `main`.

---

### Task 23: Production accepted; the phase closed

Spec section 9, "Production", then the housekeeping the phase leaves behind.

**Files:**
- Modify: `infra/README.md` (production's first-deploy notes)
- Modify: `docs/superpowers/plans/2026-09-22-phase-2c-azure-pipelines-going-public.md` (status)

- [ ] **Step 1: A game at the hostname, from a cold start**

Leave production alone for 20 minutes. **Owner:** open `https://games.foweeti.com` (from any network, including the phone: no allow-list). Expected: the certificate is valid (issued to `games.foweeti.com`); "Waking up the server…" then a full practice game within 90 seconds. Record the time to ready. `curl -sI https://games.foweeti.com/healthz` shows `HTTP/2 200` and `strict-transport-security` is absent (Container Apps terminates TLS; nothing to change).

- [ ] **Step 2: Both deploys built nothing**

```powershell
gh run list --branch prod --workflow deploy-game.yml --limit 1 --json databaseId --jq '.[0].databaseId' | ForEach-Object { gh run view $_ --json jobs --jq '.jobs[] | select(.conclusion == "success") | .steps[] | select(.conclusion == "success") | .name' }
```

Expected: the step names include "Resolve the staged tag, or refuse", "Apply the game migrations", "Deploy the app by digest" and "Wait for /readyz", and no "Build and push" step. The same for `deploy-scraper.yml`.

- [ ] **Step 3: Monday's Job and its check**

A calendar checkpoint: after the first Monday 06:00 UTC, `az containerapp job execution list --name caj-wrg-production-scraper --resource-group rg-wrg-production -o table` shows a `Succeeded` execution started that morning, and `gh run list --workflow scraper-check.yml --limit 2` shows Monday's 09:00 UTC run passed (and the previous Friday's staging run passed). Until then this step is open; the rest of the task proceeds.

- [ ] **Step 4: The budget, the packages, the repositories**

```powershell
az rest --method get --url "https://management.azure.com/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-wrg-production/providers/Microsoft.Consumption/budgets/budget-wrg-production?api-version=2024-08-01" --query "{amount: properties.amount}"
gh api /users/joseph-leo/packages/container/worldrankguesser-game --jq .visibility
gh api /users/joseph-leo/packages/container/worldrankguesser-scraper --jq .visibility
gh repo view joseph-leo/WorldRankGuesser --json visibility --jq .visibility
gh repo view joseph-leo/SportsRankingService --json isArchived --jq .isArchived
```

Expected: `amount: 5`; `public`, `public`; `PUBLIC`; `true`.

- [ ] **Step 5: Close the phase**

On a `stage/close-2c` branch, by pull request (`main` is protected):

1. `infra/README.md`: `## First-deploy notes (production, <date>)` with the time to ready, the certificate issuance time, anything that differed.
2. The plan's status line at the top (after the header blockquote): `Status: executed; staging accepted <date>, production accepted <date>; Monday's check: <pending | passed <date>>.`
3. Merge; then promote the docs commit too, so `prod` and `main` stay identical: `git push origin main:prod` (docs only: nothing deploys).

Phase 2c is complete when every step of Tasks 20 and 23 was observed. What it leaves for later, deliberately: the `within` slack in `RankingsRefreshServiceTests`, one Competitor and Points value in `RankingsSeed`, the Caddy sketch in `docker-compose.yml`, the BenchmarkDotNet runtime-async spike, a `gitleaks` job in CI, and cost stage 2 when the game has players.
