# WBSC through a Cloudflare Worker: a proxy fetcher for feeds that block hosting addresses

Date: 2026-09-24
Status: **approved design.** Every section was approved in discussion on 2026-09-24. Plan: `docs/superpowers/plans/2026-09-24-wbsc-proxy-egress.md`.

Parent design: `2026-09-19-phase-2-go-live-design.md`, section 7 (the scraper as a Container Apps Job). This document adds one component outside Azure and one fetcher to the scraper; nothing about the game, the view or the promotion flow changes.

## 1. Summary

Staging's first Job run on 2026-09-24 saved 49 of 54 feeds. The five WBSC feeds (baseball men and women, softball men and women, Baseball5) failed because www.wbsc.org sits behind CloudFront, which answers 403 "Request blocked" to Azure addresses for every User-Agent and for curl too, while a residential address is fine. They were disabled with a dated note, and the game's baseball category, whose only source they are, is empty in staging.

The fix is a small HTTP proxy outside Azure: a Cloudflare Worker on the free plan, in this repo under `proxy/`, that forwards a request to an allow-listed host with a shared token. The scraper gets a fourth fetcher, "Proxy", selected per item like Http and Curl, and the resolver's preliminary request follows the item's fetcher instead of always going direct. The Job receives the Worker's URL and the token through the existing Bicep. A spike on 2026-09-24 proved the route: through a throwaway Worker, CloudFront served the WBSC release-date page and two feeds with status 200 from the same edge that refuses Azure.

## 2. Decisions

| Decision | Choice | Why |
|---|---|---|
| Egress | A Cloudflare Worker on the free plan | $0, no hardware of the owner's, nothing changes about the Job or the deploys, and the spike proved CloudFront accepts Cloudflare's egress. Rejected: Wikipedia's "WBSC World Rankings" article (top 20 only per discipline, so every country ranked 21st or worse would score the 150 cap, changing the game rather than restoring it); a self-hosted GitHub runner on the owner's spare Debian machine (needs the machine on, and GitHub advises against self-hosted runners on public repos); an Azure NAT gateway or a VPS (about $30 a month, and an Azure address is very likely still blocked). |
| Shape | A generic proxy fetcher, chosen by the item's `Fetcher` | Any future feed that blocks hosting addresses is one config word away. Rejected: a Worker that mirrors the WBSC endpoints (the URLs and the release-date logic would split between the config and the Worker); routing all scraper traffic through the Worker (BWF and Wikimedia depend on the Curl fetcher, every federation would see a new address, and one bad Worker deploy would take down all 54 feeds). |
| Resolver requests | Follow the item's fetcher | rankings.wbsc.org now redirects permanently to www.wbsc.org/en/rankings, the blocked host, so the release-date page must go through the proxy too. The "always Http" rule for resolvers goes away. |
| Allow-list | A constant in the Worker's code: `www.wbsc.org` and `rankings.wbsc.org` | Adding a host is a code change and a deploy, deliberately, so the Worker never becomes a general relay. |
| Environments | One Worker shared by staging and production | The free plan gives one, and the Worker is stateless. A Worker change reaches both at once, outside the promotion guard; staging's Friday Job run still sees a broken Worker before production's Monday one. |
| Secrets | One GitHub repository secret, `PROXY_TOKEN`, is the source for both ends | The Worker and the Job can never disagree. Rotation is: change the secret, run the proxy deploy and the scraper deploy. |
| Unconfigured proxy | The fetcher goes direct with a warning | `dotnet run` from home keeps working without a Worker, and a wrong configuration in Azure stays visible: the feed fails with its 403 and the warning says why. |
| Repo visibility | Unchanged, public | Considered on 2026-09-24 and deferred: GitHub Free does not allow environments on private repos, and the deploy identities are federated to `environment:staging` and `environment:production`, so private needs GitHub Pro ($4 a month). The two packages would stay public either way: private packages get 500 MB of storage and 1 GB of transfer a month on Free, and the images are 99 MB and 126 MB with about ten versions each kept. |

## 3. The Worker (approved)

**Where it lives.** A new top-level folder `proxy/`: the Worker source, its wrangler configuration with the name `wrg-proxy` and a pinned compatibility date, a `package.json` pinning wrangler as a dev dependency with `deploy` and `test` scripts, and a test file. The handler logic sits in its own module, exported as a function of request, environment and a fetch implementation, so tests run under plain `node --test` with no Cloudflare emulator. The Worker's URL is `https://wrg-proxy.foweeti.workers.dev`; the workers.dev subdomain `foweeti` is renamed once in the dashboard before the first deploy (the probe's first deploy auto-named it `wbsc-probe`).

**Contract.** One route, `GET /fetch?url=<absolute URL>`, with the shared secret in an `X-Proxy-Token` header. Any other method answers 405. A missing or wrong token answers 401 before anything else is looked at. A missing or unparsable url answers 400. A host outside the allow-list answers 403.

**Forwarding.** The Worker fetches the target with the same browser User-Agent and Accept headers the scraper's HTTP fetcher uses, follows redirects hop by hop with every hop checked against the allow-list (rankings.wbsc.org redirects to www.wbsc.org; an open redirect must not make the Worker a relay), and disables Cloudflare's cache, since the scraper already deduplicates a page within a run. It returns the upstream status and body unchanged with the upstream content type, so the scraper's fetcher treats the answer exactly like a direct one: success gives the body, anything else gives null with the status logged. A transport failure to the upstream answers 502 with a one-line text body.

**Token check.** A constant-time comparison against the `PROXY_TOKEN` Worker secret. If the secret is unset, every request answers 500 rather than letting the Worker run open.

**Not included.** No rate limiting (the token gates it and a run makes six requests; WBSC's own limit is 60 a minute). No logging beyond wrangler's live tail on demand. No caching, no retries.

## 4. The scraper change (approved)

**A fourth fetcher, "Proxy".** `ProxyFetcher` implements `IHttpFetcher` next to Http and Curl and is registered the same way, behind `CachingFetcher`. It reads `Proxy:Url` and `Proxy:Token` from configuration, so in Azure they arrive as the environment variables `Proxy__Url` and `Proxy__Token`. Fetching sends `GET <Proxy:Url>/fetch?url=<encoded target>` with the token header through a named HttpClient of its own, with the 30 second timeout the other client has. A success status returns the body; anything else logs the status and the target and returns null, the fetcher contract. The log line names the target, not the Worker, so the run's output stays about federations.

**When no proxy is configured.** With `Proxy:Url` unset the fetcher delegates to the HTTP fetcher and logs one warning per run that items are being fetched directly.

**The resolver's request follows the item's fetcher.** Today the three resolvers that make a preliminary request (FIFA, SVNS, WBSC) take a single `IHttpFetcher` from DI, always the HTTP one. `IUrlResolver.ResolveAsync` gains the item's fetcher as a parameter, the runner passes the one it already selected, and the resolvers drop their constructor dependency. FIFA and SVNS keep the HTTP fetcher because their items say nothing, so their behaviour is unchanged. The comment on `RankingItem.Fetcher` that says a resolver's request always goes through Http is corrected, and the DI comment about registration order goes away, since nothing depends on it any more. The WBSC resolver reads `https://www.wbsc.org/en/rankings`, where rankings.wbsc.org redirects permanently since 2026-09-24, so the proxy makes one request for the page rather than two.

**Configuration.** The five WBSC items are re-enabled with `"Fetcher": "Proxy"` and a dated note saying CloudFront blocks hosting addresses and the Worker in `proxy/` is the egress. The disabled-with-reason convention stays for ATP doubles.

**Not included.** No change to `CachingFetcher`, parsers, persistence or the game. No per-item proxy URL: one Worker, one setting.

## 5. Secrets, deployment and the runbook (approved)

**Three GitHub settings, all repository-level**, since one Worker serves both environments:

- `CLOUDFLARE_API_TOKEN`, a secret: an account API token created in the Cloudflare dashboard with only the "Workers Scripts: Edit" permission.
- `CLOUDFLARE_ACCOUNT_ID`, a variable: the account id. It is not sensitive.
- `PROXY_TOKEN`, a secret: a random 32-byte hex string generated once.

**A new workflow, `deploy-proxy.yml`.** Runs on a push to `main` that touches `proxy/`, and by hand. Gated by `DEPLOYS_ENABLED` like the others. Steps: install, run the Worker's tests, `wrangler deploy`, then push `PROXY_TOKEN` into the Worker's secret, which is idempotent. No staging step and no promotion guard. The workflow reads no Azure identity.

**The Job's Bicep.** `scraper.bicep` gains a plain `proxyUrl` parameter and a `@secure()` `proxyToken` parameter. The token lands in the Job's `configuration.secrets` and the container gets `Proxy__Url` as a value and `Proxy__Token` as a secret reference. Both parameter files set the URL literally and read the token from the `PROXY_TOKEN` environment variable, which `deploy-scraper.yml` exports on its two deployment steps from the GitHub secret. Nothing else in the deploy changes: the re-enabled feeds ride in the image, so they reach production through the usual staging pass and promotion. The scraper deploy's path filter, pinned by the promotion guard's tests, does not change.

**CI.** `ci.yml` gets a small `proxy` job that runs the Worker's tests; actionlint covers the new workflow on its own.

**The compose stack.** The scraper service passes `Proxy__Url` and `Proxy__Token` through from the host environment when set, so the proxy path can be exercised locally against the real Worker; unset, it goes direct as today.

**Runbook and notes.** `infra/README.md` gets a Cloudflare section: the account and a recommendation to enable two-factor authentication now that it is part of the pipeline, the API token's permissions, the three settings, the subdomain, rotation, and live tailing with wrangler. Both `CLAUDE.md` files get a line on the fetcher and the Worker; the scraper's "Current state" paragraph records the re-enabling.

**First go-live order.** The owner creates the three settings and renames the subdomain. The merge deploys the Worker; the scraper deploy rebuilds the image with the feeds enabled and passes the proxy parameters; one Job start in staging shows the five WBSC feeds saved before Friday's scheduled run. Production gets it through the usual promotion.

## 6. Tests and verification (approved)

**Worker tests**, plain `node --test`, driving the exported handler with a fake fetch: 405 for any method but GET; 401 for a missing or wrong token, checked before anything else; 500 when the secret is unset; 400 for a missing or unparsable url; 403 for a host off the allow-list; a forwarded request carries the browser User-Agent and follows redirects; status, body and content type pass through unchanged for a 200 and for an upstream 403 alike; 502 when the upstream fetch throws.

**Scraper unit tests**, no database, in `tests/SportsRankingService.Tests`: the proxy fetcher builds the right request (the encoded target in the query, the token header); a success returns the body; a non-success logs the status and returns null; a transport failure returns null; with no URL configured it delegates to the inner fetcher and logs the warning once. The runner passes the item's fetcher to the resolver, checked with an item that names Curl. The three resolvers' existing tests adapt to the new signature. A new configuration test loads the committed `serviceconfig.json` and checks that every item names a registered parser, resolver and fetcher, and that the five WBSC items are enabled through the proxy; no such test existed before.

**Verification before merge.** From the owner's machine, with the two proxy settings exported in the shell, one run of the scraper for the five WBSC feeds against the local database through the real Worker: the only end-to-end check that needs no Azure, and it exercises the resolver's page and the five feeds through the proxy.

**Verification after deploy.** A Job start in staging, then `check-job.sh` and the run's log showing 54 feeds saved.

**Unchanged.** The promotion guard's tests. No test talks to Cloudflare in CI: CI holds no token, and the Worker tests cover the logic.

## 7. To verify during implementation

- Whether wrangler's non-interactive deploy needs the account id only through `CLOUDFLARE_ACCOUNT_ID` or also in the wrangler configuration; the probe deployed with neither because the OAuth login carried the account.
- Whether `wrangler secret put` reads the value from standard input in CI without a prompt (it did in wrangler 3 and 4; confirm on the pinned version).
- The exact Container Apps Job schema for `configuration.secrets` and `secretRef` on the API version `scraper.bicep` uses (`2026-01-01`).
