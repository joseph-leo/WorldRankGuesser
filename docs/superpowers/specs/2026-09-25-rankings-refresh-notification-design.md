# A scrape reaches players at once: the refresh endpoint and the scraper's notification

Date: 2026-09-25
Status: **implemented in staging 2026-09-25** (PRs #14 and #16; the plan is `docs/superpowers/plans/2026-09-25-rankings-refresh-notification.md`). Every section was approved in discussion on 2026-09-25; the URL decision in section 2 was revised the same day (the internal hostname, section 7). Verified in staging at 04:31 UTC: the Job's "Game notified: 3587 rows, 218 drawable countries", the game's "Rankings loaded" one second later, no restart. Production follows with phase 2c Part C.

Parent designs: `2026-09-19-server-authoritative-rebuild-design.md` (the rankings pipeline and the anti-cheat invariants) and `2026-09-19-phase-2-go-live-design.md`, section 7 (the two environments, the free serverless database). Follows `2026-09-24-wbsc-proxy-egress-design.md`, whose first deploy exposed the gap.

## 1. Summary

The game API reads the `dbo.CurrentCountryRankings` view into memory on startup and then every `Rankings:RefreshMinutes`, 720 in Azure, because every read wakes the paused free database. On 2026-09-25 a merge deployed the game and the scraper together: the new game revision loaded its snapshot at 00:47 UTC, the Job saved the five WBSC lists at 00:48, and new boards had no baseball ranks until the revision was restarted by hand. A restart is not acceptable as the way to publish rankings: it interrupts the site and needs a person.

This design adds one endpoint to the API, `POST /api/rankings/refresh`, guarded by a shared secret, that re-reads the view and swaps the in-memory snapshot in place, exactly as the timer does, with no restart and no effect on games in progress; and one notification from the scraper: at the end of a run that stored at least one new release, the Job calls that endpoint once. The weekly scrape, an on-demand scrape and a deploy then all reach players within seconds, with nobody involved. The 12-hour timer stays as the safety net.

## 2. Decisions

| Decision | Choice | Why |
|---|---|---|
| Mechanism | A token-guarded refresh endpoint plus a notification from the scraper | The owner wants no restarts and no manual step. Rejected: restarting the game's revision from the deploy workflow (an interruption, and only for deploys, not the weekly scrape); a shorter refresh interval (every read wakes the paused database and spends the free allowance); polling the view for a change (the same cost as refreshing). |
| Caller | The scraper Job, at the end of a run with at least one new release | It is the one component that knows a list changed, and it runs inside the same Container Apps environment as the game. The deploy workflows and the runbook do not call the endpoint: the Job's own run already does, in a deploy too. |
| Runtime coupling | A second, one-way link from the scraper to the game: an HTTP call with no shared code | The view stays the only data link; the notification carries no data, only "read again". `WorldRankGuesser.Api` still never references `SportsRankingService`. The rule in both `CLAUDE.md` files is restated with this exception. |
| When to notify | `Inserted > 0`, whatever the failed count | A run in which some feeds failed and others stored new releases has changed the view; the game should see the change. A run with nothing new does not call. |
| A failed notification | Logged as a warning; never changes the run's exit code | The Job's success means "the feeds were saved"; the timer still refreshes within 12 hours. The scheduled check watches the Job, not the notification. |
| Without a token | The endpoint is not mapped at all; the scraper's notifier does nothing but log once | A local run of either program needs no secret and exposes no route. |
| The secret | One GitHub repository secret, `RANKINGS_REFRESH_TOKEN`, read by both deploy workflows, carried as a Container App secret on the game and on the Job | The same shape as `PROXY_TOKEN`: one source, two consumers, rotation is one secret change and two deploys. |
| Token check | Constant-time comparison; 401 for a missing or wrong token, before anything else | The endpoint is public on production's ingress. |
| Concurrency | One refresh at a time; a call that arrives during a refresh awaits that refresh and gets its result | A refresh reads the whole view; two at once would double the database wake for nothing. |
| What the response reveals | Row count, drawable-country count, load time | Nothing about any country's rank, so the anti-cheat invariants hold. The route is excluded from the OpenAPI document: it is not part of the front end's contract, and `schema.d.ts` does not change. |
| The URL the Job calls | The game app's **internal** hostname, `ca-wrg-<env>-game.internal.<environment default domain>`, over HTTPS (revised 2026-09-25; the design as approved said the public default hostname) | The public hostname was refused by staging's IP allow-list (403 from the Job's first notification): the request leaves the environment through its outbound pool, about 170 rotating addresses, so the approved fallback of allow-listing "the environment's outbound address" has nothing to name. A probe from a one-off Job execution showed the internal hostname reaching the app over HTTPS (401 to a tokenless request, the endpoint's own answer) while the public one got 403. The internal name works in every environment, allow-list or not, and the call never leaves the environment. Section 7 records the probe. |

## 3. The API (approved)

**Configuration.** `RankingsOptions` gains `RefreshToken` (string, null by default). Unset locally and in tests that do not set it.

**The refresher.** The logic now inside `RankingsRefreshService.RefreshAsync` moves to a new singleton `RankingsRefresher` with one method, `RefreshAsync(CancellationToken)`, returning a `RefreshResult` record: `Loaded` (bool), `Rows`, `DrawableCountries`, `LoadedAt`, and `Error` (the message when not loaded). It reads the view in its own scope, builds the snapshot, refuses a snapshot with fewer drawable countries than categories, stores it, and logs as today: "Rankings loaded" on success; a warning when there is no snapshot yet and an error when a previous snapshot is kept. It holds one lock: a call that arrives while a refresh runs awaits that refresh's task and returns its result rather than starting another. `RankingsRefreshService` keeps the startup backoff and the timer and calls the refresher.

**The endpoint.** `RankingsEndpoints.MapRankingsEndpoints` maps `POST /api/rankings/refresh` only when `Rankings:RefreshToken` is set; otherwise the route does not exist and answers 404 like any unknown path. The handler reads the `X-Refresh-Token` header, compares it in constant time (`CryptographicOperations.FixedTimeEquals` over UTF-8 bytes, a missing header compared as empty), answers 401 with no body on a mismatch, then calls the refresher. 200 with `{ rows, drawableCountries, loadedAt }` when loaded; 503 with `{ error }` when not, the previous snapshot kept. The route is excluded from the OpenAPI description, needs no player cookie, and sits outside the game-start rate limit.

**Not included.** No GET, no status route, no list of what changed, no authentication beyond the token.

## 4. The scraper (approved)

**Configuration.** A `Notify` section, `NotifyOptions`: `Url` (the endpoint's absolute URL) and `Token`. In Azure the Job's environment variables `Notify__Url` and `Notify__Token`; unset locally.

**The notifier.** `RefreshNotifier.NotifyAsync(UpdateSummary, CancellationToken)`: when `Url` is blank, logs one information line that no game is configured to notify and returns; when `summary.Inserted` is 0, returns without a request; otherwise POSTs to the URL with the token in `X-Refresh-Token` through a named `HttpClient` with a 120 second timeout (a refresh may wait for the paused database to resume, about 40 seconds on staging), logs "Game notified: N rows, M drawable countries" from the 200 body, and logs a warning with the status (or the transport error) otherwise. It never throws.

**Where it runs.** `Program.cs`, after `UpdateAllAsync` returns and before the exit code is decided, on every run including `--only`. A cancelled run does not notify.

**Not included.** No retry (the timer is the retry), no notification on a run with nothing new.

## 5. Secrets, Bicep and deployment (approved)

- GitHub repository secret `RANKINGS_REFRESH_TOKEN`: 32 random bytes as hex, `openssl rand -hex 32 | gh secret set RANKINGS_REFRESH_TOKEN`.
- `game.bicep`: `@secure() param rankingsRefreshToken string`; a Container App secret `rankings-refresh-token`; env `Rankings__RefreshToken` as a `secretRef`. Both `game.bicepparam` files read it with `readEnvironmentVariable('RANKINGS_REFRESH_TOKEN', 'placeholder')`.
- `scraper.bicep`: the same secure parameter; a Job secret; env `Notify__Token` as a `secretRef`, and `Notify__Url` as `'https://ca-wrg-${env}-game.internal.${cae.properties.defaultDomain}/api/rankings/refresh'` (the internal hostname since 2026-09-25, section 2): the game's app name under the environment's domain, and the template already references the environment, so nothing depends on the game app existing and the first-deploy order (scraper before game) stands. Both `scraper.bicepparam` files read the token the same way.
- `deploy-game.yml` and `deploy-scraper.yml`: both deploy steps of each export `RANKINGS_REFRESH_TOKEN`, guarded by `require-env.sh` like `PROXY_TOKEN`. Neither workflow calls the endpoint. `env.PATHS` of both workflows is unchanged: the templates and parameter files are already listed.
- Rotation (runbook): change the secret, run the game deploy and the scraper deploy for each environment; until both have run, the Job's notification fails with 401 and the timer covers.

## 6. Tests and verification (approved)

**API, no database:** `RankingsRefresherTests`: a load stores the snapshot and returns its counts; a failed read returns `Loaded = false` with the error and keeps the previous snapshot; a snapshot with too few drawable countries is refused; two concurrent calls make one read and both get its result. `RankingsRefreshServiceTests` keep passing on the refresher.

**API, with the SQL fixture:** `RankingsRefreshEndpointTests`: with no token configured the route answers 404; with a token, a missing header and a wrong header answer 401 and trigger no read; the right header answers 200 with the counts of the seeded view and a fresh `loadedAt`; after a new row is written through the scraper's repository, a refresh makes the next board able to draw it (the snapshot changed in place, no restart); the response body names no country. `AntiCheatTests` unchanged and green.

**Scraper, no database:** `RefreshNotifierTests` on a recording handler: no request when the URL is blank, and one information line; no request when `Inserted` is 0; the request is a POST to the URL with the token header when `Inserted > 0`; a non-success status logs a warning and does not throw; a transport failure logs a warning and does not throw. `ServiceConfigTests` and every existing test green.

**Before merge, from this machine:** the API run locally with a token set, the scraper run locally with `Notify__Url` pointing at it and one feed forced to insert; the API log shows "Rankings loaded" twice, the scraper log "Game notified".

**After deploy, in staging:** one Job start after a fresh deploy; the Job's log shows "Game notified", the game's log shows a "Rankings loaded" line at that time with the new row count, with no "Application started" line between. If the game's log shows nothing and the Job's log shows a 403 or a timeout, the ingress refused intra-environment traffic: section 7's fallback.

## 7. To verify during implementation

- Whether staging's IP allow-list admits a request from the Job inside the same environment to the game's public FQDN. **Verified 2026-09-25: it does not.** The first forced notification got 403; a one-off Job execution running curl showed the Job's egress address as one of the environment's roughly 170 outbound addresses (`az containerapp job show --query properties.outboundIpAddresses`, a rotating pool; `properties.staticIp` on the environment is the inbound address), 403 from the public FQDN, and 401 (the endpoint's answer to a missing token, so the app was reached) from both `https://ca-wrg-staging-game.internal.<default domain>` and `http://ca-wrg-staging-game`. The fallback in the approved design, allow-listing the outbound address, has nothing to name, so `Notify__Url` uses the internal hostname instead (section 2).
- That `cae.properties.defaultDomain` on the `existing` managed environment yields the domain the game's FQDN uses (`az containerapp env show --query properties.defaultDomain` against staging, compared with the game's `ingress.fqdn`). **Verified 2026-09-25:** the Job's `Notify__Url` and the game's `ingress.fqdn` carry the same `braveground-3515d637.centralus.azurecontainerapps.io`.
