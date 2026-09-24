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

The `.sh` scripts and the guard's tests run in Git Bash. From PowerShell, plain `bash` can resolve to WSL's launcher,
which sees none of the Windows tools: call `& "C:\Program Files\Git\bin\bash.exe" <script>` instead.

## An environment's first deploy (staging first; production the same with `production`)

1. **Bootstrap** (owner only; the only template with role assignments):

   ```powershell
   az deployment sub create --location centralus --name bootstrap-staging --parameters infra/staging/bootstrap.bicepparam --query properties.outputs
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
   commitment to Central US (East US 2 refused new SQL servers on this subscription on 2026-09-23). Check the offer took effect:

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

   Production, once its bootstrap outputs exist (`$b` below is the parsed JSON of `az deployment sub create ... --query properties.outputs`):

   ```powershell
   gh api -X PUT repos/joseph-leo/WorldRankGuesser/environments/production -F 'deployment_branch_policy[protected_branches]=false' -F 'deployment_branch_policy[custom_branch_policies]=true'
   gh api -X POST repos/joseph-leo/WorldRankGuesser/environments/production/deployment-branch-policies -f name=prod -f type=branch
   gh variable set AZURE_CLIENT_ID --env production --body <deployClientId>
   gh variable set AZURE_RESOURCE_GROUP --env production --body rg-wrg-production
   gh variable set AZURE_MONITOR_CLIENT_ID_PRODUCTION --body <monitorClientId>
   ```

   No `ALLOWED_IPS` for production, ever.

5. **Production only: DNS, before the first game deploy.** At the DNS host of `foweeti.com` (Google), add a CNAME
   `games` pointing at `ca-wrg-production-game.<environmentDefaultDomain>` and a TXT `asuid.games` whose value is
   `<customDomainVerificationId>`. Wait until both resolve (`nslookup -type=CNAME games.foweeti.com`,
   `nslookup -type=TXT asuid.games.foweeti.com`). The CNAME must point directly at the app, not through another
   CNAME. The certificate is issued by the first game deploy; if that deploy fails at the certificate, the records
   had not propagated: run it again.

   If it fails a second time with the app refusing the hostname or the certificate never binding, fall back to two
   passes and record it in the first-deploy notes: deploy once without the domain, add the hostname, create the
   certificate, bind it, and let the next pipeline deploy declare the bound hostname again (`bindingType: 'Auto'`
   keeps an existing binding):

   ```powershell
   $env:GAME_IMAGE = '<the image by digest the workflow would deploy>'
   az deployment group create --resource-group rg-wrg-production --name game-first --parameters infra/production/game.bicepparam --parameters customDomain=
   az containerapp hostname add --hostname games.foweeti.com --resource-group rg-wrg-production --name ca-wrg-production-game
   az containerapp env certificate create --name cae-wrg-production --resource-group rg-wrg-production --hostname games.foweeti.com --validation-method CNAME --certificate-name cert-games-foweeti-com
   az containerapp hostname bind --hostname games.foweeti.com --resource-group rg-wrg-production --name ca-wrg-production-game --environment cae-wrg-production --certificate cert-games-foweeti-com
   ```

   (`az containerapp env certificate create --help` if a flag has moved.)

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
   change visibility) before Container Apps can pull them. Going public (plan 2c, Task 16) creates them ahead of time; a package created outside Actions must also grant the repository write access (package settings, Manage Actions access, add `WorldRankGuesser` with the Write role), which the images' `org.opencontainers.image.source` label also arranges for packages the workflows create.

## Everyday operations

- **Promote:** `git push origin main:prod`. Watch `gh run list --workflow deploy-game.yml --branch prod`.
- **Roll back the game:** `gh workflow run deploy-game.yml --ref prod -f staged_tag=staged-<hash>` with an earlier
  tag from `gh api /users/joseph-leo/packages/container/worldrankguesser-game/versions --jq '.[].metadata.container.tags[]'`.
  Migrations are skipped; the earlier image must work with the current schema (add first, remove later).
- **Scrape on demand:** `bash .github/scripts/run-job.sh caj-wrg-<env>-scraper rg-wrg-<env> 3900` starts the Job
  and waits for the execution to succeed (`check-job.sh` only reads the latest execution; it is the scheduled
  check's tool, not a wait).
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

## First-deploy notes (staging, 2026-09-24)

What differed from the plan, in the order it was met. Each item is fixed in the templates, the scripts or this
runbook; the notes are the record.

- **Region.** East US 2 refused the SQL server (`ProvisioningDisabled`, "provisioning is restricted in this region")
  on this subscription; Central US accepted it, so `infra/bootstrap.bicep` defaults to `centralus` (PR #3). Nothing
  created in East US 2 survived: the half-built group was deleted and staging bootstrapped again.
- **A subscription-level deployment keeps its region.** Rerunning `bootstrap-staging` in another region fails with
  `InvalidDeploymentLocation` until the old deployment record is removed:
  `az deployment sub delete --name bootstrap-staging` (history only; it deletes no resource).
- **The Container Apps environment takes a while to delete.** `az group delete` removed everything else within
  ten minutes and left the environment in `ScheduledForDelete` for about 25 minutes more; the group is gone when
  `az group show` says so.
- **The free database refuses a custom auto-pause delay.** `autoPauseDelay: 15` failed with
  "Only default value for auto pause delay is allowed for Free Limit database with auto pause exhaustion behavior";
  the template leaves the default, and the database reports 60 minutes (PR #4).
- **The budget** was created on the first try, on a subscription a day old.
- **The bundle script needs `dotnet restore`** before `dotnet ef migrations bundle`: `dotnet ef` 11 reads the
  project's metadata without restoring, so a fresh checkout fails with NETSDK1004 (PR #5). Locally it had always
  passed because `obj` existed.
- **`scraper-check.yml` before the first scraper deploy** fails at the execution check with Azure's
  `ResourceNotFound` for the Job, not with the script's "no execution yet": the Job does not exist yet. Either way
  the run fails, which is the point of the acceptance step.
- **WBSC blocks Azure.** `www.wbsc.org` sits behind CloudFront, which answers 403 "Request blocked" to the Job's
  address for every User-Agent and for curl too (probed with a one-off execution of the Job:
  `az containerapp job start --yaml`, a template with `command: [/bin/sh]` and the probe as `args`). A residential
  address is fine. The five WBSC feeds are disabled with a dated note (PR #6); the game's baseball category has no
  other source and is empty until WBSC gets another egress or another source. 49 feeds run, in about 40 seconds;
  the view has 3,286 rows and 215 drawable countries (the local database had 3,590 rows with WBSC on 2026-09-22).
- **JSON-valued secrets are set from Git Bash.** `ALLOWED_IPS` set with `gh secret set --body '["…/32"]'` from
  PowerShell 5.1 reached GitHub without its double quotes (PowerShell passes embedded quotes to a program
  unescaped), and the game deploy failed with BCP186 in `game.bicepparam`. `printf '%s' '["…/32"]' | gh secret set
  ALLOWED_IPS --env staging` from Git Bash is the form that works.
- **The portal's query editor hides `PRINT` output** when the script returns a grid: `bootstrap.sql`'s "run this
  file again" message is not shown. Run it twice anyway; the second run is idempotent.
- **A probe or manual execution of the Job counts** as its latest execution for `scraper-check.yml` until the next
  real run.
- **The kill switch was off while fix pull requests merged**, so a merge to `main` started no deploy; each deploy
  was then dispatched by hand. That avoids one deploy per fix merge plus one per dispatch.
- **First game deploy:** creating the app took 34 seconds (the plan allowed two to three minutes) and `/readyz`
  answered 200 on the first poll, 16 seconds after the deploy finished; the Playwright game passed in 10 seconds
  through the ingress. Rankings loaded: 3,286 rows, 215 drawable countries.
- **The rate limit.** A cookie-less caller's *player* limit is keyed on their address (30 starts an hour), so a burst
  of cookie-less starts is refused from the 31st, not the 121st the plan expected; the per-address limit (120) sits
  behind it. A refused request (an address off the list, the app at zero replicas) did not wake the app: the ingress
  answered "access denied" and the replica count stayed at zero. A game plays from the phone over mobile data while
  the owner's network is at its limit, so the limit is keyed on the caller, not the ingress.
- **The database pauses early.** It reports a 60-minute auto-pause delay and paused about 13 minutes after the last
  request in practice, so a cold start meets a paused database far more often than the delay suggests.
- **The first cold start killed the container.** With the database paused, the first container logged nothing for
  30 seconds, not even "Now listening", and the startup probe killed it; the second container, started after the
  database had resumed, listened within 3 seconds. Data protection registers a hosted service that preloads its key
  ring at startup; with the keys in the database that login blocks for as long as the resume takes, and Kestrel starts
  only after the hosted services. The registration is removed right before the host is built (PR #9).
- **Cold start, measured after the fix** from zero replicas and a paused database: the replica was assigned 5 seconds
  after the request, the image pulled in 1 second, the container started 14 seconds in and listened 1 second later;
  the first page byte arrived at 17.5 seconds, `/readyz` turned 200 at 70 seconds (the database resumed in about
  55 seconds, during which the front end shows its waking-up state) and a game started at 72 seconds. Within the
  90 seconds the spec asks for; the page itself is not "about 2 seconds" from a cold start, because the ingress
  holds the first request until the replica listens.
- **Path filters:** a merge touching only the scraper's folder ran `Deploy scraper` and not `Deploy game`; a merge
  touching only a plan file ran CI alone.
