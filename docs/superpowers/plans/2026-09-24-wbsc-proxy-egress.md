# WBSC Proxy Egress Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the five WBSC feeds (baseball men and women, softball men and women, Baseball5) back in Azure by fetching them through a Cloudflare Worker, since CloudFront refuses Azure addresses.

**Architecture:** A Cloudflare Worker in `proxy/` forwards `GET /fetch?url=...` to an allow-listed host when the caller presents a shared token. The scraper gains a fourth fetcher, "Proxy", chosen per item like Http and Curl, and a resolver's preliminary request now follows the item's fetcher instead of always going direct. The Job gets the Worker's URL and the token through the existing Bicep; a new workflow deploys the Worker; nothing about the game, the promotion flow or the database changes.

**Tech Stack:** Cloudflare Workers (free plan) with wrangler 4.139.0 pinned; Node 24 and `node --test` for the Worker's tests; .NET 11 (`global.json`), `IHttpClientFactory`, xUnit 2.9 with `Microsoft.Extensions.Diagnostics.Testing` for a fake logger; Bicep 0.47.16 through Azure CLI, Microsoft.App/jobs `2026-01-01`; GitHub Actions on `ubuntu-24.04` (`actions/checkout@v7`, `actions/setup-node@v7`); actionlint; the promotion guard's bash tests.

**Spec:** `docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md`. Parent: `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md`, section 7.

## Global Constraints

- **Names.** Worker `wrg-proxy` at `https://wrg-proxy.foweeti.workers.dev` (the workers.dev subdomain `foweeti`, renamed by the owner in the dashboard; Task 5 checks it). Route `GET /fetch?url=<absolute URL>`, header `X-Proxy-Token`, Worker secret `PROXY_TOKEN`. Scraper fetcher name `Proxy`, settings `Proxy:Url` and `Proxy:Token` (environment `Proxy__Url`, `Proxy__Token`). GitHub: secret `CLOUDFLARE_API_TOKEN`, variable `CLOUDFLARE_ACCOUNT_ID`, secret `PROXY_TOKEN`, all repository-level; the owner created all three on 2026-09-24.
- **Allow-list** is a constant in the Worker's code: exactly `www.wbsc.org` and `rankings.wbsc.org`, https only. Adding a host is a code change.
- **No secret in the repo.** The token exists only as the GitHub secret, the Worker secret and the Job secret. Parameter files read it with `readEnvironmentVariable('PROXY_TOKEN', 'placeholder')` so `az bicep build` works without it. The Worker URL is public and committed.
- **One Worker for both environments**, deployed by `deploy-proxy.yml` on a push to `main` touching `proxy/`, outside the promotion guard. `DEPLOYS_ENABLED` gates it like every deploy.
- **Unconfigured proxy goes direct** with one warning per run; an empty `Proxy:Url` (what compose passes when the shell variable is unset) counts as unconfigured.
- **The scraper deploy's path filter and `env.PATHS` do not change**; the guard's tests pin them. The feeds ride in the image and reach production through the usual staging pass and promotion.
- **Versions:** wrangler `4.139.0` exact in `proxy/package.json`; Node 24 in CI; Bicep pinned `v0.47.16` in the workflows; Microsoft.App API version `2026-01-01`.
- **Every commit passes** `dotnet build WorldRankGuesser.slnx` and `dotnet test tests/SportsRankingService.Tests` (no Docker needed for the scraper's tests; curl on the PATH); `npm test` in `proxy/` when the Worker changed; `az bicep build --file <template> --stdout | Out-Null` and `az bicep build-params --file <params> --stdout | Out-Null` with no output when `infra/` changed; `actionlint` when `.github/` changed; `bash .github/actions/promotion-guard/test.sh` when a deploy workflow changed. Commit messages carry no attribution lines.
- **Run from the repo root** `C:\Users\Josep\OneDrive\Documents\GitHub\WorldRankGuesser` in PowerShell unless a step says Git Bash.

## Review Focus

- A target URL whose query carries `&` and `=` (every WBSC feed) must reach the upstream byte for byte: the scraper encodes it once into the `url` parameter and the Worker decodes it once. Tests: Task 1 (`forwards the exact target, query included`), Task 3 (`Sends_the_target_encoded_in_the_query_with_the_token_header`).
- A `url` on an allowed host over plain `http:`, or on a look-alike host such as `www.wbsc.org.evil.example`, must be refused with 403, never forwarded. Tests: Task 1 (`refuses http on an allowed host`, `refuses a look-alike host`).
- A request to any path but `/fetch`, with a valid token, answers 404 and forwards nothing. Test: Task 1 (`answers 404 off the route`).
- `Proxy:Url` with a trailing slash must not produce `//fetch`. Test: Task 3 (the URL in `Sends_the_target_encoded_in_the_query_with_the_token_header` ends in `/`).
- An upstream 403 (what CloudFront answers a blocked address) must pass through as 403 so the scraper logs the real status, not a Worker error. Tests: Task 1 (`passes an upstream 403 through`), Task 3 (`A_non_success_status_logs_it_and_returns_null`).

---

### Task 1: The Worker

**Files:**
- Create: `proxy/package.json`, `proxy/wrangler.toml`, `proxy/src/handler.js`, `proxy/src/index.js`, `proxy/test/handler.test.js`
- Modify: `.gitignore` (append), `.dockerignore` (append)

**Interfaces:**
- Consumes: nothing in the repo.
- Produces: the HTTP contract the scraper's fetcher (Task 3) calls: `GET <worker>/fetch?url=<encoded absolute URL>` with header `X-Proxy-Token`; answers the upstream status, body and `Content-Type` unchanged; 405, 401, 500, 404, 400, 403, 502 as below. `handle(request, env, fetchImpl)` exported from `proxy/src/handler.js` for tests.

- [ ] **Step 1: Create the package and wrangler configuration**

`proxy/package.json`:

```json
{
  "name": "wrg-proxy",
  "version": "1.0.0",
  "private": true,
  "type": "module",
  "description": "The Cloudflare Worker the scraper fetches through for a feed whose site refuses hosting addresses (WBSC).",
  "scripts": {
    "test": "node --test test/",
    "deploy": "wrangler deploy"
  },
  "devDependencies": {
    "wrangler": "4.139.0"
  }
}
```

`proxy/wrangler.toml`:

```toml
# The proxy the scraper fetches WBSC through (docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md).
# One Worker serves staging and production. Deployed by .github/workflows/deploy-proxy.yml; the PROXY_TOKEN secret is
# set there from the GitHub secret of the same name, and the account comes from CLOUDFLARE_ACCOUNT_ID.
name = "wrg-proxy"
main = "src/index.js"
compatibility_date = "2026-09-01"
```

Run:

```powershell
cd proxy; npm install; cd ..
```

Expected: `proxy/package-lock.json` and `proxy/node_modules/` appear; `npx --prefix proxy wrangler --version` prints `4.139.0`.

- [ ] **Step 2: Ignore wrangler's local state and keep the folder out of the images**

Append to `.gitignore`:

```
# wrangler's local state for the proxy Worker
proxy/.wrangler/
```

Append to `.dockerignore`, after the `tools` line:

```
proxy
```

- [ ] **Step 3: Write the failing tests**

`proxy/test/handler.test.js`:

```js
import { test } from "node:test";
import assert from "node:assert/strict";
import { handle, ALLOWED_HOSTS, USER_AGENT } from "../src/handler.js";

const TOKEN = "secret-token";
const env = { PROXY_TOKEN: TOKEN };
const WORKER = "https://wrg-proxy.example.workers.dev";

function request(url, { method = "GET", token = TOKEN } = {}) {
  const headers = token === null ? {} : { "X-Proxy-Token": token };
  return new Request(url, { method, headers });
}

/** A fake upstream: records the call and answers with the given status, body and content type. */
function upstream({ status = 200, body = "{}", type = "application/json", throws = null } = {}) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, init });
    if (throws) throw throws;
    return new Response(body, { status, headers: { "Content-Type": type } });
  };
  return { fetchImpl, calls };
}

const target = "https://www.wbsc.org/api/v1/rankings/sport/show?sportId=baseball-m&date=2026-09-15&fullView=1&preview=&lang=en";
const fetchUrl = `${WORKER}/fetch?url=${encodeURIComponent(target)}`;

test("the allow-list is exactly the two WBSC hosts", () => {
  assert.deepEqual(ALLOWED_HOSTS, ["www.wbsc.org", "rankings.wbsc.org"]);
});

test("answers 405 to any method but GET, before looking at the token", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(fetchUrl, { method: "POST", token: null }), env, fetchImpl);
  assert.equal(response.status, 405);
  assert.equal(calls.length, 0);
});

test("answers 500 when the secret is unset, so the Worker never runs open", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(fetchUrl), {}, fetchImpl);
  assert.equal(response.status, 500);
  assert.equal(calls.length, 0);
});

test("answers 401 to a missing token", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(fetchUrl, { token: null }), env, fetchImpl);
  assert.equal(response.status, 401);
  assert.equal(calls.length, 0);
});

test("answers 401 to a wrong token, before looking at the url", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(`${WORKER}/fetch?url=not-a-url`, { token: "wrong" }), env, fetchImpl);
  assert.equal(response.status, 401);
  assert.equal(calls.length, 0);
});

test("answers 404 off the route", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(`${WORKER}/other?url=${encodeURIComponent(target)}`), env, fetchImpl);
  assert.equal(response.status, 404);
  assert.equal(calls.length, 0);
});

test("answers 400 to a missing url", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(`${WORKER}/fetch`), env, fetchImpl);
  assert.equal(response.status, 400);
  assert.equal(calls.length, 0);
});

test("answers 400 to a url that is not absolute", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(`${WORKER}/fetch?url=${encodeURIComponent("/api/v1/rankings")}`), env, fetchImpl);
  assert.equal(response.status, 400);
  assert.equal(calls.length, 0);
});

test("refuses a host off the allow-list", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(`${WORKER}/fetch?url=${encodeURIComponent("https://example.com/")}`), env, fetchImpl);
  assert.equal(response.status, 403);
  assert.equal(calls.length, 0);
});

test("refuses a look-alike host", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(`${WORKER}/fetch?url=${encodeURIComponent("https://www.wbsc.org.evil.example/api")}`), env, fetchImpl);
  assert.equal(response.status, 403);
  assert.equal(calls.length, 0);
});

test("refuses http on an allowed host", async () => {
  const { fetchImpl, calls } = upstream();
  const response = await handle(request(`${WORKER}/fetch?url=${encodeURIComponent("http://www.wbsc.org/api")}`), env, fetchImpl);
  assert.equal(response.status, 403);
  assert.equal(calls.length, 0);
});

test("forwards the exact target, query included, with the browser User-Agent, following redirects and bypassing the cache", async () => {
  const { fetchImpl, calls } = upstream({ body: '{"rankings":[]}' });
  const response = await handle(request(fetchUrl), env, fetchImpl);
  assert.equal(response.status, 200);
  assert.equal(await response.text(), '{"rankings":[]}');
  assert.equal(response.headers.get("Content-Type"), "application/json");
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, target);
  assert.equal(calls[0].init.method, "GET");
  assert.equal(calls[0].init.redirect, "follow");
  assert.equal(calls[0].init.cache, "no-store");
  assert.equal(calls[0].init.headers["User-Agent"], USER_AGENT);
  assert.equal(calls[0].init.headers["Accept"], "*/*");
});

test("passes an upstream 403 through unchanged", async () => {
  const { fetchImpl } = upstream({ status: 403, body: "Request blocked", type: "text/html" });
  const response = await handle(request(fetchUrl), env, fetchImpl);
  assert.equal(response.status, 403);
  assert.equal(await response.text(), "Request blocked");
  assert.equal(response.headers.get("Content-Type"), "text/html");
});

test("answers 502 when the upstream fetch throws", async () => {
  const { fetchImpl } = upstream({ throws: new TypeError("fetch failed") });
  const response = await handle(request(fetchUrl), env, fetchImpl);
  assert.equal(response.status, 502);
  assert.match(await response.text(), /fetch failed/);
});
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `cd proxy; npm test; cd ..`

Expected: every test fails with `Cannot find module '../src/handler.js'`.

- [ ] **Step 5: Write the handler and the entry point**

`proxy/src/handler.js`:

```js
// The proxy's whole logic, as a pure function of the request, the environment and a fetch, so it runs under
// node --test without Cloudflare. The Worker (index.js) hands it the real fetch.
//
// Contract (docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md, section 3):
//   GET /fetch?url=<absolute URL>  with header X-Proxy-Token: <PROXY_TOKEN>
//   405 any other method; 500 secret unset; 401 missing or wrong token (checked before anything else);
//   404 any other path; 400 missing or relative url; 403 host off the allow-list or not https;
//   otherwise the upstream status, body and Content-Type unchanged; 502 when the upstream fetch throws.

/** Adding a host is a code change and a deploy, deliberately: the Worker is never a general relay. */
export const ALLOWED_HOSTS = ["www.wbsc.org", "rankings.wbsc.org"];

/** The scraper's HttpFetcher User-Agent, so the upstream sees the same client it would directly. */
export const USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36";

export async function handle(request, env, fetchImpl) {
  if (request.method !== "GET") {
    return text(405, "GET only");
  }
  if (!env.PROXY_TOKEN) {
    return text(500, "PROXY_TOKEN is not set");
  }
  if (!tokenMatches(request.headers.get("X-Proxy-Token") ?? "", env.PROXY_TOKEN)) {
    return text(401, "missing or wrong X-Proxy-Token");
  }

  const url = new URL(request.url);
  if (url.pathname !== "/fetch") {
    return text(404, "the only route is /fetch");
  }

  const target = url.searchParams.get("url");
  let parsed;
  try {
    parsed = new URL(target ?? "");
  } catch {
    return text(400, "url must be an absolute URL");
  }
  if (parsed.protocol !== "https:" || !ALLOWED_HOSTS.includes(parsed.hostname)) {
    return text(403, `host not allowed: ${parsed.hostname}`);
  }

  let upstream;
  try {
    upstream = await fetchImpl(parsed.toString(), {
      method: "GET",
      redirect: "follow",
      cache: "no-store",
      headers: { "User-Agent": USER_AGENT, "Accept": "*/*" },
    });
  } catch (error) {
    return text(502, `upstream fetch failed: ${error.message}`);
  }

  return new Response(upstream.body, {
    status: upstream.status,
    headers: { "Content-Type": upstream.headers.get("Content-Type") ?? "application/octet-stream" },
  });
}

function text(status, body) {
  return new Response(body, { status, headers: { "Content-Type": "text/plain; charset=utf-8" } });
}

/** Constant time over the longer of the two, so a wrong token costs the same whatever its prefix. */
function tokenMatches(given, expected) {
  const a = new TextEncoder().encode(given);
  const b = new TextEncoder().encode(expected);
  let diff = a.length ^ b.length;
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    diff |= (a[i] ?? 0) ^ (b[i] ?? 0);
  }
  return diff === 0;
}
```

`proxy/src/index.js`:

```js
import { handle } from "./handler.js";

export default {
  fetch(request, env) {
    return handle(request, env, fetch);
  },
};
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd proxy; npm test; cd ..`

Expected: 14 tests, 0 failures.

- [ ] **Step 7: Check that wrangler accepts the Worker without an account**

Run: `cd proxy; npx wrangler deploy --dry-run --outdir dist; Remove-Item -Recurse -Force dist; cd ..`

Expected: `--dry-run: exiting now.` after `Total Upload:` with no error. If wrangler complains that `cache: "no-store"` is unsupported at this compatibility date (it is supported since 2024), replace that option in `handler.js` with `cf: { cacheTtl: 0 }` and change the test's assertion `assert.equal(calls[0].init.cache, "no-store")` to `assert.deepEqual(calls[0].init.cf, { cacheTtl: 0 })`.

- [ ] **Step 8: Commit**

```powershell
git add proxy/package.json proxy/package-lock.json proxy/wrangler.toml proxy/src proxy/test .gitignore .dockerignore
git commit -m "Proxy Worker: forward allow-listed WBSC requests behind a shared token"
```

---

### Task 2: A resolver's request follows the item's fetcher

**Files:**
- Modify: `src/SportsRankingService/Services/UrlResolvers/IUrlResolver.cs`
- Modify: `src/SportsRankingService/Services/UrlResolvers/IdentityUrlResolver.cs`
- Modify: `src/SportsRankingService/Services/UrlResolvers/FifaDateIdResolver.cs`
- Modify: `src/SportsRankingService/Services/UrlResolvers/SvnsSeriesResolver.cs`
- Modify: `src/SportsRankingService/Services/UrlResolvers/WbscReleaseDateResolver.cs`
- Modify: `src/SportsRankingService/Services/RankingSourceRunner.cs:70`
- Modify: `src/SportsRankingService/Models/RankingItem.cs` (the `Fetcher` doc comment)
- Modify: `src/SportsRankingService/Services/RankingPipelineServiceCollectionExtensions.cs:23-29`
- Test: `tests/SportsRankingService.Tests/Services/RankingSourceRunnerTests.cs`, `tests/SportsRankingService.Tests/Services/FifaDateIdTests.cs`, `tests/SportsRankingService.Tests/Services/WbscReleaseDateResolverTests.cs`, `tests/SportsRankingService.Tests/Services/RankingPipelineRegistrationTests.cs`

**Interfaces:**
- Consumes: `IHttpFetcher` (unchanged), `RankingItem.Fetcher`.
- Produces: `Task<ResolvedUrl> IUrlResolver.ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken)`; the resolvers have parameterless constructors. Task 3 registers its fetcher in the DI block this task rewrites.

- [ ] **Step 1: Write the failing runner test**

In `tests/SportsRankingService.Tests/Services/RankingSourceRunnerTests.cs`, add after `An_item_is_fetched_through_the_fetcher_it_names`:

```csharp
    /// <summary>
    /// WBSC's release-date page sits on the same blocked host as its feeds, so a resolver's preliminary request must
    /// go through whatever fetcher the item names, not always the direct one.
    /// </summary>
    [Fact]
    public async Task A_resolvers_preliminary_request_goes_through_the_fetcher_the_item_names()
    {
        var http = new FakeFetcher([]);
        var curl = new FakeFetcher(new()
        {
            ["https://inside.fifa.com/fifa-rankings/world-ranking/men"] = Sample.Read("Fifa_WorldRanking_Men.html"),
            ["http://fifa/api?id=FRS_Male_Football_20260611"] = Sample.Read("Fifa_V3_Men.json"),
        }, name: "Curl");
        var item = new RankingItem { Sport = "Soccer", Gender = "Men", Url = "http://fifa/api?id={0}", Source = "FifaV3", UrlResolver = "FifaDateId", Fetcher = "Curl" };

        RankingSnapshot? snapshot = await Runner(http, curl).RunAsync(item, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(["https://inside.fifa.com/fifa-rankings/world-ranking/men", "http://fifa/api?id=FRS_Male_Football_20260611"], curl.Requested);
        Assert.Empty(http.Requested);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~RankingSourceRunnerTests.A_resolvers_preliminary_request"`

Expected: FAIL. The FIFA page is requested from `http` (the resolver's injected fetcher), so `curl.Requested` lacks it and the snapshot is null.

- [ ] **Step 3: Change the interface and the four resolvers**

`src/SportsRankingService/Services/UrlResolvers/IUrlResolver.cs`:

```csharp
using SportsRankingService.Models;

namespace SportsRankingService.Services.UrlResolvers;

/// <summary>
/// Turns a configured <see cref="RankingItem"/> into the URL to fetch. Most feeds are static
/// (<see cref="IdentityUrlResolver"/>); some need a preliminary request to discover an id or date
/// that the ranking URL depends on. That request goes through the fetcher the runner hands in, the one the
/// item's <c>Fetcher</c> names, so a preliminary page on a host that refuses one client is read with the client
/// the feed itself uses (WBSC's release-date page sits behind the same CloudFront block as its feeds).
/// Registered as plain singletons; the runner indexes them by <see cref="Name"/>, the value of
/// <c>RankingItem.UrlResolver</c> in serviceconfig.json.
/// </summary>
public interface IUrlResolver
{
    string Name { get; }

    Task<ResolvedUrl> ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken);
}
```

`IdentityUrlResolver.cs`: change the method to

```csharp
    public Task<ResolvedUrl> ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken) =>
        Task.FromResult(new ResolvedUrl(item.Url));
```

`FifaDateIdResolver.cs`: the class line becomes `public sealed class FifaDateIdResolver : IUrlResolver` (no primary constructor), and `ResolveAsync` becomes

```csharp
    public async Task<ResolvedUrl> ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken)
    {
        string page = item.Gender == "Women" ? WomenPage : MenPage;
        string html = (await fetcher.GetStringAsync(page, cancellationToken))
            ?? throw new InvalidOperationException($"FIFA ranking page {page} could not be fetched");

        SoccerRankDate latest = ExtractLatestDate(html);

        return new ResolvedUrl(string.Format(CultureInfo.InvariantCulture, item.Url, latest.id), ToRankingDate(latest));
    }
```

`SvnsSeriesResolver.cs`: the class line becomes `public sealed class SvnsSeriesResolver : IUrlResolver`, and `ResolveAsync` becomes

```csharp
    public async Task<ResolvedUrl> ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken)
    {
        string wanted = item.Gender == "Women" ? "wrs" : "mrs";

        string html = (await fetcher.GetStringAsync(StandingsPage, cancellationToken))
            ?? throw new InvalidOperationException($"SVNS standings page {StandingsPage} could not be fetched");

        foreach (string id in ExtractSeriesIds(html))
        {
            string? series = await fetcher.GetStringAsync(string.Format(SeriesApi, id), cancellationToken);
            if (series is not null && ReadSportCode(series) == wanted)
            {
                return new ResolvedUrl(string.Format(item.Url, id));
            }
        }

        throw new InvalidOperationException($"No SVNS series with sport code '{wanted}' found on {StandingsPage}");
    }
```

`WbscReleaseDateResolver.cs`: the class line becomes `public sealed partial class WbscReleaseDateResolver : IUrlResolver`, the page constant becomes the address rankings.wbsc.org redirects to since 2026-09-24 (one hop fewer through the proxy), with its comment updated:

```csharp
    // rankings.wbsc.org redirects here permanently since 2026-09-24; the dropdown objects are embedded in this page.
    private const string RankingsPage = "https://www.wbsc.org/en/rankings";
```

and `ResolveAsync` becomes

```csharp
    public async Task<ResolvedUrl> ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken)
    {
        Match sport = SportIdQuery().Match(item.Url);
        if (!sport.Success)
        {
            throw new InvalidOperationException($"WBSC URL has no sportId query parameter: {item.Url}");
        }

        string html = (await fetcher.GetStringAsync(RankingsPage, cancellationToken))
            ?? throw new InvalidOperationException($"WBSC rankings page {RankingsPage} could not be fetched");

        string releaseDate = ExtractLatestReleaseDate(html, sport.Groups[1].Value);

        return new ResolvedUrl(
            string.Format(CultureInfo.InvariantCulture, item.Url, releaseDate),
            DateOnly.ParseExact(releaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
```

Also change the class's summary first line from `The WBSC rankings API only answers for an exact release date. rankings.wbsc.org embeds its` to `The WBSC rankings API only answers for an exact release date. www.wbsc.org/en/rankings embeds its`.

- [ ] **Step 4: Pass the fetcher from the runner and fix the comments**

In `src/SportsRankingService/Services/RankingSourceRunner.cs`, line 70 becomes:

```csharp
        ResolvedUrl resolved = await resolver.ResolveAsync(item, fetcher, cancellationToken);
```

In `src/SportsRankingService/Models/RankingItem.cs`, the `Fetcher` property's doc comment becomes:

```csharp
        /// <summary>
        /// <see cref="Services.IHttpFetcher.Name"/> of the client that fetches <see cref="Url"/> and every preliminary request the
        /// <see cref="UrlResolver"/> makes: "Http" (.NET HttpClient) unless a feed only answers another client (BWF: "Curl") or its
        /// site refuses hosting addresses (WBSC: "Proxy").
        /// </summary>
```

In `src/SportsRankingService/Services/RankingPipelineServiceCollectionExtensions.cs`, replace lines 23 to 29 (the comment and the four fetcher registrations) with:

```csharp
        // The runner indexes the fetchers by Name and hands the item's one to its resolver too, so nothing injects a single
        // IHttpFetcher and the order here does not matter. Every fetcher is only handed out behind CachingFetcher, so feeds
        // and resolvers sharing a page request it once per run; the concrete types are registered as themselves for it to wrap.
        services.AddSingleton<CurlFetcher>();
        services.AddSingleton<HttpFetcher>();
        services.AddSingleton<IHttpFetcher>(sp => new CachingFetcher(sp.GetRequiredService<CurlFetcher>()));
        services.AddSingleton<IHttpFetcher>(sp => new CachingFetcher(sp.GetRequiredService<HttpFetcher>()));
```

- [ ] **Step 5: Adapt the existing tests**

`tests/SportsRankingService.Tests/Services/RankingSourceRunnerTests.cs`, the `Runner` helper:

```csharp
    private static RankingSourceRunner Runner(FakeFetcher fetcher, params FakeFetcher[] otherFetchers) =>
        new([fetcher, .. otherFetchers],
            [new FihParser(), new FifaV3Parser(), new FigParser(), new WtaParser(), new FakeParser()],
            [new IdentityUrlResolver(), new FifaDateIdResolver()],
            new FakeTimeProvider(Now),
            NullLogger<RankingSourceRunner>.Instance);
```

`tests/SportsRankingService.Tests/Services/FifaDateIdTests.cs`, in `Resolve_formats_the_id_into_the_url_and_returns_the_ranking_date`:

```csharp
        ResolvedUrl resolved = await new FifaDateIdResolver().ResolveAsync(item, fetcher, CancellationToken.None);
```

`tests/SportsRankingService.Tests/Services/WbscReleaseDateResolverTests.cs`: the class summary's first line becomes `/// www.wbsc.org/en/rankings (where rankings.wbsc.org redirects) embeds its release-date dropdown as {"date","sport","year","formatted"}`, and `Resolve_formats_the_newest_date_into_the_url_and_returns_it` becomes:

```csharp
    [Fact]
    public async Task Resolve_formats_the_newest_date_into_the_url_and_returns_it()
    {
        var fetcher = new FakeFetcher(new() { ["https://www.wbsc.org/en/rankings"] = Sample.Read("Wbsc_Rankings.html") });
        var item = new RankingItem { Sport = "Baseball", Gender = "Men", Url = "http://wbsc/api?sportId=baseball-m&date={0}", Source = "Wbsc", UrlResolver = "WbscReleaseDate" };

        ResolvedUrl resolved = await new WbscReleaseDateResolver().ResolveAsync(item, fetcher, CancellationToken.None);

        Assert.Equal("http://wbsc/api?sportId=baseball-m&date=2026-03-26", resolved.Url);
        Assert.Equal(new DateOnly(2026, 3, 26), resolved.RankingDate);
    }
```

`tests/SportsRankingService.Tests/Services/RankingPipelineRegistrationTests.cs` becomes (the single-dependency test goes, since nothing injects a single fetcher any more; Task 3 adds the third name):

```csharp
using Microsoft.Extensions.DependencyInjection;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

public class RankingPipelineRegistrationTests
{
    private static ServiceProvider Provider() =>
        new ServiceCollection().AddLogging().AddRankingPipeline().BuildServiceProvider();

    [Fact]
    public void Every_fetcher_is_registered_under_its_name()
    {
        using ServiceProvider provider = Provider();

        IEnumerable<string> names = provider.GetServices<IHttpFetcher>().Select(f => f.Name);

        Assert.Equal(["Curl", "Http"], names.Order());
    }

    /// <summary>
    /// Feeds sharing a page (FIG, Wikipedia IIHF) and resolvers sharing a preliminary page (WBSC, SVNS)
    /// request it once per run, and only because every fetcher they can be handed is the caching one.
    /// </summary>
    [Fact]
    public void Every_fetcher_is_handed_out_behind_the_cache()
    {
        using ServiceProvider provider = Provider();

        Assert.All(provider.GetServices<IHttpFetcher>(), fetcher => Assert.IsType<CachingFetcher>(fetcher));
    }
}
```

- [ ] **Step 6: Build and run the scraper's tests**

Run: `dotnet build WorldRankGuesser.slnx; dotnet test tests/SportsRankingService.Tests`

Expected: build with no warnings from the scraper project; every test passes, including the new runner test. (`tools/SimulateBoards` and the benchmarks do not call `ResolveAsync`; if the build names another caller, give it the item's fetcher the same way.)

- [ ] **Step 7: Commit**

```powershell
git add src/SportsRankingService tests/SportsRankingService.Tests
git commit -m "Resolvers fetch their preliminary page through the item's fetcher"
```

---

### Task 3: The Proxy fetcher

**Files:**
- Create: `src/SportsRankingService/Configuration/ProxyOptions.cs`
- Create: `src/SportsRankingService/Services/ProxyFetcher.cs`
- Modify: `src/SportsRankingService/Services/RankingPipelineServiceCollectionExtensions.cs` (the fetcher block from Task 2, and the HttpClient registrations)
- Modify: `src/SportsRankingService/Program.cs:37` (bind the options)
- Modify: `tests/SportsRankingService.Tests/SportsRankingService.Tests.csproj` (fake logger package)
- Test: `tests/SportsRankingService.Tests/Services/ProxyFetcherTests.cs`, `tests/SportsRankingService.Tests/Services/RankingPipelineRegistrationTests.cs`

**Interfaces:**
- Consumes: the Worker contract from Task 1; `HttpFetcher` (registered as itself) as the direct fallback.
- Produces: `ProxyFetcher : IHttpFetcher` with `Name == "Proxy"`, `ProxyFetcher.FetcherName`, `ProxyFetcher.ClientName == "proxy"`, `ProxyFetcher.TokenHeader == "X-Proxy-Token"`; `ProxyOptions { string? Url; string? Token; const string SectionName = "Proxy" }`, bound from configuration section `Proxy`.

- [ ] **Step 1: Add the fake logger package**

In `tests/SportsRankingService.Tests/SportsRankingService.Tests.csproj`, after the `Microsoft.Extensions.TimeProvider.Testing` line add:

```xml
    <PackageReference Include="Microsoft.Extensions.Diagnostics.Testing" Version="10.10.0" />
```

Run: `dotnet restore tests/SportsRankingService.Tests`

Expected: restore succeeds. If that version is not on the feed, use the newest `10.x` that `dotnet package search Microsoft.Extensions.Diagnostics.Testing --exact-match` lists.

- [ ] **Step 2: Write the failing tests**

`tests/SportsRankingService.Tests/Services/ProxyFetcherTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SportsRankingService.Configuration;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// The fetcher for a feed whose site refuses hosting addresses: every request goes to the Worker in proxy/ as
/// GET &lt;Proxy:Url&gt;/fetch?url=&lt;target&gt; with the shared token, and the Worker's answer is the upstream's. Without a
/// configured URL (a run from the owner's machine) it fetches directly through the HTTP fetcher and says so once.
/// </summary>
public class ProxyFetcherTests
{
    private const string Target = "https://www.wbsc.org/api/v1/rankings/sport/show?sportId=baseball-m&date=2026-09-15&fullView=1&preview=&lang=en";

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Exception? Throws { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Throws is not null)
            {
                throw Throws;
            }

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ProxyFetcher.ClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class DirectFetcher(string? body) : IHttpFetcher
    {
        public string Name => HttpFetcher.FetcherName;

        public List<string> Requested { get; } = [];

        public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            Requested.Add(url);
            return Task.FromResult(body);
        }
    }

    private static (ProxyFetcher Fetcher, RecordingHandler Handler, DirectFetcher Direct, FakeLogger<ProxyFetcher> Log) Build(
        ProxyOptions options, HttpStatusCode status = HttpStatusCode.OK, string body = "{\"rankings\":[]}", Exception? throws = null)
    {
        var handler = new RecordingHandler(status, body) { Throws = throws };
        var direct = new DirectFetcher("direct body");
        var log = new FakeLogger<ProxyFetcher>();
        return (new ProxyFetcher(new Factory(handler), direct, options, log), handler, direct, log);
    }

    [Fact]
    public async Task Sends_the_target_encoded_in_the_query_with_the_token_header()
    {
        var (fetcher, handler, direct, _) = Build(new ProxyOptions { Url = "https://wrg-proxy.example.workers.dev/", Token = "t0k" });

        string? body = await fetcher.GetStringAsync(Target, CancellationToken.None);

        Assert.Equal("{\"rankings\":[]}", body);
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://wrg-proxy.example.workers.dev/fetch?url=" + Uri.EscapeDataString(Target), request.RequestUri!.ToString());
        Assert.Equal(["t0k"], request.Headers.GetValues(ProxyFetcher.TokenHeader));
        Assert.Empty(direct.Requested);
    }

    [Fact]
    public async Task A_non_success_status_logs_it_and_returns_null()
    {
        var (fetcher, _, _, log) = Build(new ProxyOptions { Url = "https://wrg-proxy.example.workers.dev", Token = "t0k" }, HttpStatusCode.Forbidden, "Request blocked");

        string? body = await fetcher.GetStringAsync(Target, CancellationToken.None);

        Assert.Null(body);
        FakeLogRecord warning = Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Contains("403", warning.Message);
        Assert.Contains(Target, warning.Message);
    }

    [Fact]
    public async Task A_transport_failure_returns_null()
    {
        var (fetcher, _, _, log) = Build(new ProxyOptions { Url = "https://wrg-proxy.example.workers.dev", Token = "t0k" }, throws: new HttpRequestException("connection refused"));

        string? body = await fetcher.GetStringAsync(Target, CancellationToken.None);

        Assert.Null(body);
        Assert.Contains(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Error && r.Message.Contains(Target));
    }

    /// <summary>The compose stack passes an empty string when the shell variable is unset, so empty means unconfigured too.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_a_url_it_fetches_directly_and_warns_once(string? url)
    {
        var (fetcher, handler, direct, log) = Build(new ProxyOptions { Url = url, Token = "t0k" });

        string? first = await fetcher.GetStringAsync(Target, CancellationToken.None);
        string? second = await fetcher.GetStringAsync("https://www.wbsc.org/en/rankings", CancellationToken.None);

        Assert.Equal("direct body", first);
        Assert.Equal("direct body", second);
        Assert.Equal([Target, "https://www.wbsc.org/en/rankings"], direct.Requested);
        Assert.Empty(handler.Requests);
        Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public void Its_name_is_Proxy()
    {
        var (fetcher, _, _, _) = Build(new ProxyOptions());

        Assert.Equal("Proxy", fetcher.Name);
        Assert.Equal(ProxyFetcher.FetcherName, fetcher.Name);
    }
}
```

And in `tests/SportsRankingService.Tests/Services/RankingPipelineRegistrationTests.cs`, `Every_fetcher_is_registered_under_its_name` expects the third name:

```csharp
        Assert.Equal(["Curl", "Http", "Proxy"], names.Order());
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~ProxyFetcherTests|FullyQualifiedName~RankingPipelineRegistrationTests"`

Expected: the build fails on `ProxyOptions` and `ProxyFetcher` not existing.

- [ ] **Step 4: Write the options and the fetcher**

`src/SportsRankingService/Configuration/ProxyOptions.cs`:

```csharp
namespace SportsRankingService.Configuration;

/// <summary>
/// The Worker in proxy/ that feeds with <c>"Fetcher": "Proxy"</c> are requested through. Bound from the <c>Proxy</c>
/// configuration section: in Azure the Job's environment variables <c>Proxy__Url</c> and <c>Proxy__Token</c>; locally
/// unset, which makes those feeds go direct (fine from a residential address).
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>The Worker's origin, e.g. https://wrg-proxy.foweeti.workers.dev. Null, empty or blank: no proxy.</summary>
    public string? Url { get; set; }

    /// <summary>The shared token the Worker checks in the X-Proxy-Token header.</summary>
    public string? Token { get; set; }
}
```

`src/SportsRankingService/Services/ProxyFetcher.cs`:

```csharp
using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;

namespace SportsRankingService.Services;

/// <summary>
/// Fetches through the Cloudflare Worker in proxy/, for a feed whose site refuses hosting addresses: www.wbsc.org sits
/// behind CloudFront, which answers 403 to Azure (2026-09-24) but serves Cloudflare's egress. The Worker forwards
/// GET /fetch?url=... to an allow-listed host and returns the upstream status and body unchanged, so a non-success
/// status is a failed fetch exactly as it would be directly, and the log names the target, not the Worker.
/// With no Proxy:Url configured (a run from the owner's machine) the request goes directly through the HTTP fetcher,
/// with one warning per run: from a residential address that works, and in Azure the feed's 403 plus the warning say
/// what is missing.
/// </summary>
public sealed class ProxyFetcher : IHttpFetcher
{
    public const string FetcherName = "Proxy";
    public const string ClientName = "proxy";
    public const string TokenHeader = "X-Proxy-Token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpFetcher _direct;
    private readonly ProxyOptions _options;
    private readonly ILogger<ProxyFetcher> _logger;
    private int _warned;

    public ProxyFetcher(IHttpClientFactory httpClientFactory, HttpFetcher direct, IOptions<ProxyOptions> options, ILogger<ProxyFetcher> logger)
        : this(httpClientFactory, (IHttpFetcher)direct, options.Value, logger)
    {
    }

    internal ProxyFetcher(IHttpClientFactory httpClientFactory, IHttpFetcher direct, ProxyOptions options, ILogger<ProxyFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _direct = direct;
        _options = options;
        _logger = logger;
    }

    public string Name => FetcherName;

    public async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
            {
                _logger.LogWarning("Proxy:Url is not configured: feeds with Fetcher \"{Fetcher}\" are fetched directly, which a hosting address may be refused for", FetcherName);
            }

            return await _direct.GetStringAsync(url, cancellationToken);
        }

        _logger.LogInformation("Fetching {Url} through the proxy", url);
        string request = $"{_options.Url.TrimEnd('/')}/fetch?url={Uri.EscapeDataString(url)}";

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(ClientName);
            using HttpRequestMessage message = new(HttpMethod.Get, request);
            message.Headers.TryAddWithoutValidation(TokenHeader, _options.Token ?? "");
            using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            _logger.LogWarning("Fetch failed. {Url} returned {StatusCode} through the proxy", url, (int)response.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogError(ex, "Fetch failed through the proxy. {Url}", url);
            return null;
        }
    }
}
```

- [ ] **Step 5: Register the fetcher and bind the options**

In `src/SportsRankingService/Services/RankingPipelineServiceCollectionExtensions.cs`, after the `federations` `AddHttpClient` block add:

```csharp
        // The proxy Worker sets its own upstream headers; this client only needs the timeout.
        services.AddHttpClient(ProxyFetcher.ClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
```

and in the fetcher block from Task 2 add `ProxyFetcher` so it reads:

```csharp
        services.AddSingleton<CurlFetcher>();
        services.AddSingleton<HttpFetcher>();
        services.AddSingleton<ProxyFetcher>();
        services.AddSingleton<IHttpFetcher>(sp => new CachingFetcher(sp.GetRequiredService<CurlFetcher>()));
        services.AddSingleton<IHttpFetcher>(sp => new CachingFetcher(sp.GetRequiredService<HttpFetcher>()));
        services.AddSingleton<IHttpFetcher>(sp => new CachingFetcher(sp.GetRequiredService<ProxyFetcher>()));
```

In `src/SportsRankingService/Program.cs`, after `builder.Services.Configure<RankingSourcesOptions>(builder.Configuration);` add:

```csharp
// Unset locally (those feeds go direct); the Job sets Proxy__Url and Proxy__Token (infra/scraper.bicep).
builder.Services.Configure<ProxyOptions>(builder.Configuration.GetSection(ProxyOptions.SectionName));
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet build WorldRankGuesser.slnx; dotnet test tests/SportsRankingService.Tests`

Expected: no warnings from the scraper; every test passes, the five new ones and the registration test with three names included.

- [ ] **Step 7: Commit**

```powershell
git add src/SportsRankingService tests/SportsRankingService.Tests
git commit -m "Proxy fetcher: request a feed through the Worker, or directly when no proxy is configured"
```

---

### Task 4: Re-enable the WBSC feeds and pin the configuration

**Files:**
- Modify: `src/SportsRankingService/serviceconfig.json:19-33`
- Create: `tests/SportsRankingService.Tests/Configuration/ServiceConfigTests.cs`
- Modify: `src/SportsRankingService/README.md:79-84`, `src/SportsRankingService/CLAUDE.md` (Architecture points 4 and "Fetchers"; "Current state")

**Interfaces:**
- Consumes: `ProxyFetcher.FetcherName` (Task 3), `AddRankingPipeline`.
- Produces: the five WBSC items enabled with `"Fetcher": "Proxy"`; a test that every item in `serviceconfig.json` names a registered parser, resolver and fetcher.

- [ ] **Step 1: Write the failing configuration test**

`tests/SportsRankingService.Tests/Configuration/ServiceConfigTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SportsRankingService.Configuration;
using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Tests.Configuration;

/// <summary>
/// The committed serviceconfig.json (copied next to the test binaries with the scraper's output) names only
/// registered parsers, resolvers and fetchers, so a typo fails here rather than in the weekly run.
/// </summary>
public class ServiceConfigTests
{
    private static List<RankingItem> Items()
    {
        var options = new RankingSourcesOptions();
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "serviceconfig.json"))
            .Build()
            .Bind(options);
        return options.Rankings;
    }

    [Fact]
    public void Every_item_names_a_registered_parser_resolver_and_fetcher()
    {
        using ServiceProvider provider = new ServiceCollection().AddLogging().AddRankingPipeline().BuildServiceProvider();
        HashSet<string> parsers = provider.GetServices<IRankingParser>().Select(p => p.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> resolvers = provider.GetServices<IUrlResolver>().Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> fetchers = provider.GetServices<IHttpFetcher>().Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<RankingItem> items = Items();

        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            Assert.Contains(item.Source, parsers);
            Assert.Contains(item.UrlResolver, resolvers);
            Assert.Contains(item.Fetcher, fetchers);
        });
    }

    /// <summary>CloudFront refuses Azure addresses for www.wbsc.org (2026-09-24); the Worker in proxy/ is their egress.</summary>
    [Fact]
    public void The_five_wbsc_feeds_are_enabled_through_the_proxy()
    {
        List<RankingItem> wbsc = Items().Where(i => i.Source == "Wbsc").ToList();

        Assert.Equal(5, wbsc.Count);
        Assert.All(wbsc, item =>
        {
            Assert.True(item.Enabled, item.Describe());
            Assert.Equal(ProxyFetcher.FetcherName, item.Fetcher);
        });
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~ServiceConfigTests"`

Expected: `The_five_wbsc_feeds_are_enabled_through_the_proxy` fails on `Enabled` being false; the other passes. If both fail with `FileNotFoundException` for `serviceconfig.json`, the referenced project's content did not flow to the test output: add to the test csproj's last `ItemGroup`

```xml
    <None Include="..\..\src\SportsRankingService\serviceconfig.json" Link="serviceconfig.json" CopyToOutputDirectory="PreserveNewest" />
```

and run again.

- [ ] **Step 3: Re-enable the five items**

In `src/SportsRankingService/serviceconfig.json`, replace the five WBSC items (lines 19 to 33) with:

```json
    { "Sport": "Baseball", "Gender": "Men", "Source": "Wbsc", "UrlResolver": "WbscReleaseDate", "Fetcher": "Proxy",
      "Note": "www.wbsc.org (CloudFront) answers 403 Request blocked to hosting addresses, Azure's included, for every user agent and for curl too (2026-09-24, staging's first Job run), yet serves Cloudflare's egress: fetched through the Worker in proxy/, the release-date page included.",
      "Url": "https://www.wbsc.org/api/v1/rankings/sport/show?sportId=baseball-m&date={0}&fullView=1&preview=&lang=en" },
    { "Sport": "Baseball", "Gender": "Women", "Source": "Wbsc", "UrlResolver": "WbscReleaseDate", "Fetcher": "Proxy",
      "Note": "Same host and reasons as Baseball Men.",
      "Url": "https://www.wbsc.org/api/v1/rankings/sport/show?sportId=baseball-w&date={0}&fullView=1&preview=&lang=en" },
    { "Sport": "Softball", "Gender": "Men", "Source": "Wbsc", "UrlResolver": "WbscReleaseDate", "Fetcher": "Proxy",
      "Note": "Same host and reasons as Baseball Men.",
      "Url": "https://www.wbsc.org/api/v1/rankings/sport/show?sportId=softball-m&date={0}&fullView=1&preview=&lang=en" },
    { "Sport": "Softball", "Gender": "Women", "Source": "Wbsc", "UrlResolver": "WbscReleaseDate", "Fetcher": "Proxy",
      "Note": "Same host and reasons as Baseball Men.",
      "Url": "https://www.wbsc.org/api/v1/rankings/sport/show?sportId=softball-w&date={0}&fullView=1&preview=&lang=en" },
    { "Sport": "Baseball5", "Gender": "Mixed", "Source": "Wbsc", "UrlResolver": "WbscReleaseDate", "Fetcher": "Proxy",
      "Note": "Same host and reasons as Baseball Men.",
      "Url": "https://www.wbsc.org/api/v1/rankings/sport/show?sportId=baseball5-coed&date={0}&fullView=1&preview=&lang=en" },
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SportsRankingService.Tests`

Expected: every test passes.

- [ ] **Step 5: Update the scraper's own docs**

In `src/SportsRankingService/README.md`, the paragraph at lines 79 to 84 that starts `A feed is fetched with .NET's HttpClient unless its item says "Fetcher": "Curl"` gets this paragraph after it:

```markdown
A third fetcher, `"Fetcher": "Proxy"`, sends the request to the Cloudflare Worker in `proxy/` (`GET /fetch?url=...`
with the shared token in `X-Proxy-Token`), which forwards it from Cloudflare's addresses and returns the upstream
answer unchanged. The five WBSC feeds need it: www.wbsc.org sits behind CloudFront, which refuses hosting addresses
(Azure's Job got 403 for every client on 2026-09-24) but serves Cloudflare. The Worker is configured by `Proxy__Url`
and `Proxy__Token` (the Job sets them from `infra/scraper.bicep`); with no URL configured, as in a local run, those
feeds are fetched directly with one warning, which works from a residential address. A resolver's preliminary
request goes through the item's fetcher too, so the WBSC release-date page takes the same route as its feeds.
```

In `src/SportsRankingService/CLAUDE.md`:

- In the Architecture list, point 4, replace the sentence `HttpFetcher is registered last on purpose: the resolvers inject a single IHttpFetcher, DI hands a single dependency the last registration, and their preliminary requests must keep going through HttpClient (RankingPipelineRegistrationTests pins this). Both fetchers are handed out only behind CachingFetcher (see Fetchers), the concrete types being registered as themselves for it to wrap.` with `The runner hands the item's fetcher to its resolver too (a resolver's preliminary page takes the feed's route; RankingSourceRunnerTests pins this), so nothing injects a single IHttpFetcher and registration order does not matter. All three fetchers are handed out only behind CachingFetcher (see Fetchers), the concrete types being registered as themselves for it to wrap.`
- In the **Fetchers** paragraph, after the CurlFetcher description and before `CachingFetcher decorates`, insert: `ProxyFetcher ("Proxy") sends the request to the Cloudflare Worker in proxy/ (GET /fetch?url=<target> with the shared token in X-Proxy-Token; spec docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md), which forwards it to an allow-listed host (www.wbsc.org, rankings.wbsc.org) and returns the upstream status and body unchanged, so a non-success status is a failed fetch like any other, logged with the target. It reads Proxy:Url and Proxy:Token (Proxy__Url, Proxy__Token in the Job); with no URL, an empty one included (compose), it delegates to HttpFetcher with one warning per run, so a local run needs no Worker. ProxyFetcherTests drive it with a recording HttpMessageHandler and a FakeLogger.` Then change `CachingFetcher decorates each of the two` to `CachingFetcher decorates each of the three`.
- In **Current state**, replace the first sentence (from `Live on 2026-09-24 from Azure` to `until WBSC gets another egress or another source.`) with: `Live on 2026-09-24 from Azure (staging's Container Apps Job): 54 feeds enabled, 1 disabled (ATP doubles). Staging's first Job run that day saved 49: the five WBSC feeds (baseball men and women, softball men and women, Baseball5) got 403 "Request blocked" from CloudFront in front of www.wbsc.org, for every User-Agent and for curl too (probed from the Job), while a residential address is fine; a throwaway Cloudflare Worker proved CloudFront serves Cloudflare's egress, so since 2026-09-24 those five items say "Fetcher": "Proxy" and go through the Worker in proxy/, their release-date page included (rankings.wbsc.org redirects permanently to www.wbsc.org/en/rankings since that day, which the resolver now reads directly).` Also change `Disabled with a note in serviceconfig.json: ATP doubles (Cloudflare challenge) and the five WBSC feeds (CloudFront blocks Azure).` to `Disabled with a note in serviceconfig.json: ATP doubles (Cloudflare challenge).`

- [ ] **Step 6: Commit**

```powershell
git add src/SportsRankingService tests/SportsRankingService.Tests
git commit -m "Re-enable the five WBSC feeds through the proxy; pin serviceconfig.json's names"
```

---

### Task 5: Checkpoint: the Worker deployed by hand and the five feeds through it from this machine

The only end-to-end check that needs no Azure. The Worker's first deploy is by hand from this machine (wrangler is signed in with the owner's OAuth login since 2026-09-24); every later deploy is the workflow's. Needs the local database up (`docker compose up -d --wait`) with the scraper's migrations applied.

**Files:** none.

- [ ] **Step 1: Confirm the subdomain**

Run (Git Bash):

```bash
TOKEN=$(grep -o 'oauth_token = "[^"]*"' "$APPDATA/xdg.config/.wrangler/config/default.toml" | cut -d'"' -f2)
curl -sS -H "Authorization: Bearer $TOKEN" https://api.cloudflare.com/client/v4/accounts/bcd3da3c05cafdc2be799b70825d0ce5/workers/subdomain
```

Expected: `"subdomain": "foweeti"`. If it still says `wbsc-probe`, the owner renames it in the dashboard (Workers & Pages overview, "Your subdomain", Change) before Step 2; the URL in Task 6's parameter files must match.

- [ ] **Step 2: Deploy the Worker and set its secret**

The owner pastes the `PROXY_TOKEN` value they stored in GitHub into the shell first. Git Bash throughout this task, and `printf '%s'` rather than a pipe from PowerShell, which would append a newline to the token. Run:

```bash
export PROXY_TOKEN='<the PROXY_TOKEN secret>'
cd proxy
npx wrangler deploy
printf '%s' "$PROXY_TOKEN" | npx wrangler secret put PROXY_TOKEN
cd ..
```

Expected: `Deployed wrg-proxy triggers` with `https://wrg-proxy.foweeti.workers.dev`, then `Success! Uploaded secret PROXY_TOKEN`.

- [ ] **Step 3: Probe the Worker directly**

Run (same shell):

```bash
curl -s -o /dev/null -w "no token: %{http_code}\n" "https://wrg-proxy.foweeti.workers.dev/fetch?url=https%3A%2F%2Fwww.wbsc.org%2Fen%2Frankings"
curl -s -o /dev/null -w "wrong host: %{http_code}\n" -H "X-Proxy-Token: $PROXY_TOKEN" "https://wrg-proxy.foweeti.workers.dev/fetch?url=https%3A%2F%2Fexample.com%2F"
curl -s -o /dev/null -w "rankings page: %{http_code}\n" -H "X-Proxy-Token: $PROXY_TOKEN" "https://wrg-proxy.foweeti.workers.dev/fetch?url=https%3A%2F%2Fwww.wbsc.org%2Fen%2Frankings"
```

Expected: `no token: 401`, `wrong host: 403`, `rankings page: 200`.

- [ ] **Step 4: Run the five feeds through the proxy into the local database**

Run (same shell):

```bash
Proxy__Url='https://wrg-proxy.foweeti.workers.dev' Proxy__Token="$PROXY_TOKEN" dotnet run --project src/SportsRankingService -- --only Baseball --only Softball --only Baseball5
```

Expected: five `Fetching ... through the proxy` lines for the feeds plus one for the rankings page (the caching fetcher requests it once), `Parsed N rows` for each of the five with a federation ranking date, no `Fetch failed`, exit code 0.

- [ ] **Step 5: Check the rows**

Run:

```bash
docker exec worldrankguesser-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -Q "SELECT Sport, Gender, COUNT(*) AS Countries, MAX(RankingDate) AS RankingDate FROM dbo.CurrentCountryRankings WHERE Sport IN ('Baseball','Softball','Baseball5') GROUP BY Sport, Gender ORDER BY Sport, Gender"
```

Expected: five rows, each with dozens of countries (baseball men above 80) and a 2026 ranking date. If the view's column is not named `RankingDate`, read the view's columns with `sp_help 'dbo.CurrentCountryRankings'` and use the date column it has.

- [ ] **Step 6: Run the five feeds without the proxy, to see the direct fallback**

Run: `unset PROXY_TOKEN; dotnet run --project src/SportsRankingService -- --only "Baseball Men"`

Expected: one warning `Proxy:Url is not configured: feeds with Fetcher "Proxy" are fetched directly...`, then the feed parsed and saved (this machine is on a residential address), exit code 0.

Nothing to commit.

---

### Task 6: The Job's Bicep, the scraper deploy, compose

**Files:**
- Modify: `infra/scraper.bicep`
- Modify: `infra/staging/scraper.bicepparam`, `infra/production/scraper.bicepparam`
- Modify: `.github/workflows/deploy-scraper.yml` (the two "Deploy the Job by digest" steps)
- Modify: `docker-compose.yml` (the `scraper` service)

**Interfaces:**
- Consumes: `Proxy__Url`, `Proxy__Token` (Task 3); the GitHub secret `PROXY_TOKEN`.
- Produces: `scraper.bicep` parameters `proxyUrl` (string) and `proxyToken` (`@secure()` string).

- [ ] **Step 1: Add the parameters, the secret and the environment variables to the Job**

In `infra/scraper.bicep`, after the `cron` parameter add:

```bicep
// The Worker in proxy/ that the feeds with "Fetcher": "Proxy" go through (WBSC: CloudFront refuses Azure addresses;
// docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md). Public, so a plain parameter.
param proxyUrl string

// The token the Worker checks, from the GitHub secret PROXY_TOKEN: the one source for both ends.
@secure()
param proxyToken string
```

In the job's `configuration`, after `replicaRetryLimit: 1` add:

```bicep
      secrets: [
        {
          name: 'proxy-token'
          value: proxyToken
        }
      ]
```

In the container's `env` array, after the connection string entry add:

```bicep
            {
              name: 'Proxy__Url'
              value: proxyUrl
            }
            {
              name: 'Proxy__Token'
              secretRef: 'proxy-token'
            }
```

- [ ] **Step 2: Set them in both parameter files**

Append to `infra/staging/scraper.bicepparam` and to `infra/production/scraper.bicepparam`:

```bicep
// One Worker serves both environments. The token comes from the deploy workflow's PROXY_TOKEN; the placeholder only
// lets `az bicep build-params` run without it.
param proxyUrl = 'https://wrg-proxy.foweeti.workers.dev'
param proxyToken = readEnvironmentVariable('PROXY_TOKEN', 'placeholder')
```

- [ ] **Step 3: Build the template and both parameter files**

Run:

```powershell
az bicep build --file infra/scraper.bicep --stdout | Out-Null
az bicep build-params --file infra/staging/scraper.bicepparam --stdout | Out-Null
az bicep build-params --file infra/production/scraper.bicepparam --stdout | Out-Null
az bicep lint --file infra/scraper.bicep
```

Expected: no output from any of the four. A warning is a failure in CI's `infra` job.

- [ ] **Step 4: Export the token in the deploy workflow**

In `.github/workflows/deploy-scraper.yml`, both `Deploy the Job by digest` steps (staging and production) get `PROXY_TOKEN` next to `SCRAPER_IMAGE` in their `env`:

```yaml
      - name: Deploy the Job by digest
        env:
          SCRAPER_IMAGE: ${{ env.IMAGE }}@${{ steps.build.outputs.digest }}
          PROXY_TOKEN: ${{ secrets.PROXY_TOKEN }}
```

and for production:

```yaml
      - name: Deploy the Job by digest
        env:
          SCRAPER_IMAGE: ${{ env.IMAGE }}@${{ steps.guard.outputs.digest }}
          PROXY_TOKEN: ${{ secrets.PROXY_TOKEN }}
```

Also extend the workflow's header comment with one line after `own schedule.`: `# Both jobs pass the proxy token (repository secret PROXY_TOKEN) to the Job; the Worker itself is deployed by deploy-proxy.yml.`

- [ ] **Step 5: Pass the proxy settings through in the compose stack**

In `docker-compose.yml`, the `scraper` service's `environment` becomes:

```yaml
    environment:
      <<: *stack-connection
      # Export PROXY_URL and PROXY_TOKEN in the shell to fetch the "Proxy" feeds (WBSC) through the real Worker;
      # unset, they are fetched directly with a warning, which works from a residential address.
      Proxy__Url: ${PROXY_URL:-}
      Proxy__Token: ${PROXY_TOKEN:-}
```

Run: `docker compose --profile scraper config | Select-String -Pattern "Proxy__"`

Expected: two lines, `Proxy__Url: ""` and `Proxy__Token: ""`.

- [ ] **Step 6: Lint the workflows and run the guard's tests**

Run (Git Bash): `actionlint && bash .github/actions/promotion-guard/test.sh`

Expected: actionlint prints nothing; the guard's tests pass, including the check that `deploy-scraper.yml`'s push filter still equals its `env.PATHS` (neither changed).

- [ ] **Step 7: Commit**

```powershell
git add infra/scraper.bicep infra/staging/scraper.bicepparam infra/production/scraper.bicepparam .github/workflows/deploy-scraper.yml docker-compose.yml
git commit -m "Job: the proxy URL and token for the WBSC feeds; compose passes them through"
```

---

### Task 7: The Worker's deploy workflow and its CI job

**Files:**
- Create: `.github/workflows/deploy-proxy.yml`
- Modify: `.github/workflows/ci.yml` (a `proxy` job after `web`)

**Interfaces:**
- Consumes: `proxy/` (Task 1); GitHub secret `CLOUDFLARE_API_TOKEN`, variable `CLOUDFLARE_ACCOUNT_ID`, secret `PROXY_TOKEN`, variable `DEPLOYS_ENABLED`.
- Produces: the Worker deployed on every push to `main` touching `proxy/`.

- [ ] **Step 1: Write the deploy workflow**

`.github/workflows/deploy-proxy.yml`:

```yaml
name: Deploy proxy

# The Cloudflare Worker in proxy/ that the scraper fetches WBSC through (docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md,
# section 5). One Worker serves staging and production, so a change reaches both at once, outside the promotion guard:
# staging's Friday Job run sees a broken Worker before production's Monday one. Reads no Azure identity. The Worker's
# PROXY_TOKEN secret is set from the GitHub secret of the same name on every deploy (idempotent), the one source for
# both ends; the Job gets it from deploy-scraper.yml.
on:
  push:
    branches: [main]
    paths:
      - 'proxy/**'
      - '.github/workflows/deploy-proxy.yml'
  workflow_dispatch:

permissions:
  contents: read

concurrency:
  group: deploy-proxy
  cancel-in-progress: false

jobs:
  deploy:
    if: vars.DEPLOYS_ENABLED == 'true'
    runs-on: ubuntu-24.04
    defaults:
      run:
        working-directory: proxy
    env:
      CLOUDFLARE_API_TOKEN: ${{ secrets.CLOUDFLARE_API_TOKEN }}
      CLOUDFLARE_ACCOUNT_ID: ${{ vars.CLOUDFLARE_ACCOUNT_ID }}
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-node@v7
        with:
          node-version: 24
          cache: npm
          cache-dependency-path: proxy/package-lock.json
      - run: npm ci
      - run: npm test
      - name: Deploy wrg-proxy
        run: npx wrangler deploy
      - name: Set the shared token
        env:
          PROXY_TOKEN: ${{ secrets.PROXY_TOKEN }}
        run: printf '%s' "$PROXY_TOKEN" | npx wrangler secret put PROXY_TOKEN
```

- [ ] **Step 2: Add the CI job**

In `.github/workflows/ci.yml`, after the `web` job and before the `images` comment, add:

```yaml
  # The proxy Worker's tests, and wrangler's own check of the bundle without an account.
  proxy:
    runs-on: ubuntu-24.04
    defaults:
      run:
        working-directory: proxy
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-node@v7
        with:
          node-version: 24
          cache: npm
          cache-dependency-path: proxy/package-lock.json
      - run: npm ci
      - run: npm test
      - run: npx wrangler deploy --dry-run --outdir dist
```

- [ ] **Step 3: Lint**

Run (Git Bash): `actionlint`

Expected: no output.

- [ ] **Step 4: Commit**

```powershell
git add .github/workflows/deploy-proxy.yml .github/workflows/ci.yml
git commit -m "Deploy the proxy Worker from main; test it in CI"
```

---

### Task 8: Runbook and project notes

**Files:**
- Modify: `infra/README.md` (a step after step 4 in the first-deploy section; an "Everyday operations" bullet)
- Modify: `CLAUDE.md` (the Hosting paragraph, the Deployment paragraph)

**Interfaces:** none.

- [ ] **Step 1: The runbook's Cloudflare step**

In `infra/README.md`, after step 4's production block (the fenced block that ends with the `AZURE_MONITOR_CLIENT_ID_PRODUCTION` line and whatever follows it up to step 5), add a step and renumber the later steps by one:

```markdown
5. **Cloudflare, once for both environments** (the Worker in `proxy/` that the scraper fetches WBSC through; spec
   `docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md`). A free Cloudflare account, with two-factor
   authentication on since it is part of the pipeline. In the dashboard: Workers & Pages → the account's
   workers.dev subdomain set to `foweeti` (the Worker's URL, `https://wrg-proxy.foweeti.workers.dev`, is in both
   `scraper.bicepparam` files); Manage Account → Account API Tokens → Create Token from the **Edit Cloudflare
   Workers** template, account resources limited to this account, no zone, no expiry. Then, from Git Bash:

   ```bash
   gh secret set CLOUDFLARE_API_TOKEN --body '<the cfat_ token>'
   gh variable set CLOUDFLARE_ACCOUNT_ID --body bcd3da3c05cafdc2be799b70825d0ce5
   openssl rand -hex 32 | gh secret set PROXY_TOKEN
   ```

   `deploy-proxy.yml` deploys the Worker and sets its `PROXY_TOKEN` secret; `deploy-scraper.yml` passes the same
   secret to the Job. Both must have run after the secret exists, in either order. Done on 2026-09-24.
```

Add to **Everyday operations**, after the "Scrape on demand" bullet:

```markdown
- **Rotate the proxy token:** `openssl rand -hex 32 | gh secret set PROXY_TOKEN`, then `gh workflow run deploy-proxy.yml --ref main`
  and `gh workflow run deploy-scraper.yml --ref main` (and `--ref prod` for production). Until both have run the
  WBSC feeds fail with 401 and keep their previous release. **Watch the Worker:** `cd proxy; npx wrangler tail`
  (after `npx wrangler login` once on this machine).
```

- [ ] **Step 2: The project's notes**

In the root `CLAUDE.md`:

- In the **Deployment** paragraph, after the sentence ending `dbo` before `game`; no SQL password exists (Entra-only, managed identities, GitHub OIDC).` add: `The five WBSC feeds are fetched through a Cloudflare Worker (`proxy/`, deployed by `deploy-proxy.yml` on a push to `main`, one Worker for both environments, outside the promotion guard), because CloudFront refuses Azure addresses; the Job gets `Proxy__Url` from the parameter files and `Proxy__Token` from the repository secret `PROXY_TOKEN`, which is also the Worker's secret. Spec: `docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md`.`
- In the **Commands** block, after the `bash .github/actions/promotion-guard/test.sh` line add:

```
cd proxy; npm test; cd ..                                              # the proxy Worker's tests (node --test)
```

- [ ] **Step 3: Commit**

```powershell
git add infra/README.md CLAUDE.md
git commit -m "Runbook and notes: the Cloudflare proxy"
```

---

### Task 9: Checkpoint: staging through the pipeline

Owner-run after the branch is merged to `main` (the branch was built on `main` directly if the owner chose so; otherwise merge it). Every step reads the outcome of a workflow.

**Files:** none.

- [ ] **Step 1: Push and watch the three workflows**

Run: `git push origin main; gh run list --limit 6`

Expected: `CI`, `Deploy proxy` and `Deploy scraper` runs start. `Deploy proxy` ends green with `Deployed wrg-proxy triggers` and `Success! Uploaded secret PROXY_TOKEN` in its log. `Deploy scraper` (staging) ends green: its "Scrape once and wait for the execution to succeed" step passes only if all 54 feeds saved, so a green run is the proof.

- [ ] **Step 2: Read the Job's log for the WBSC lines**

Run:

```powershell
$exec = az containerapp job execution list --name caj-wrg-staging-scraper --resource-group rg-wrg-staging --query "[0].name" -o tsv
az containerapp job logs show --name caj-wrg-staging-scraper --resource-group rg-wrg-staging --execution $exec --container scraper --tail 300 | Select-String -Pattern "through the proxy|Baseball|Softball|failed"
```

(Or Log Analytics, table `ContainerAppConsoleLogs_CL`, filtered on the Job's name, if the CLI says the execution's logs are gone.)

Expected: six `Fetching ... through the proxy` lines (the rankings page once, five feeds), five `Parsed N rows` lines for the WBSC feeds, no `Fetch failed`.

- [ ] **Step 3: Check the game sees baseball**

Open staging's game (the owner's address is on the allow-list) and start a practice game: the baseball category is present on the board and a country's baseball rank is revealed at the end. Then `gh workflow run scraper-check.yml -f environment=staging` ends green.

- [ ] **Step 4: Promote when satisfied**

`git push origin main:prod`, then `gh run list --workflow deploy-scraper.yml --branch prod`: the production job resolves the staged tag and deploys the Job with the proxy parameters; Monday's run, or `gh workflow run deploy-scraper.yml --ref prod -f run_job=true`, saves 54 feeds.

Nothing to commit. Update the spec's status line to `implemented 2026-..` in a follow-up commit when Step 4 is done.
