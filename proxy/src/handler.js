// The proxy's whole logic, as a pure function of the request, the environment and a fetch, so it runs under
// node --test without Cloudflare. The Worker (index.js) hands it the real fetch.
//
// Contract (docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md, section 3):
//   GET /fetch?url=<absolute URL>  with header X-Proxy-Token: <PROXY_TOKEN>
//   405 any other method; 500 secret unset; 401 missing or wrong token (checked before the route or url are looked at);
//   404 any other path; 400 missing or relative url; 403 host off the allow-list or not https;
//   otherwise the upstream status, body and Content-Type unchanged; 502 when the upstream fetch throws.
//   Redirects are followed by hand so every hop is checked against the allow-list (rankings.wbsc.org redirects to
//   www.wbsc.org; an open redirect must not turn the Worker into a relay): 403 for a hop off the list, 502 after
//   MAX_REDIRECTS hops.

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
  if (!allowed(parsed)) {
    return text(403, `host not allowed: ${parsed.hostname}`);
  }

  let current = parsed;
  let upstream;
  for (let hop = 0; ; hop++) {
    try {
      upstream = await fetchImpl(current.toString(), {
        method: "GET",
        redirect: "manual",
        cache: "no-store",
        headers: { "User-Agent": USER_AGENT, "Accept": "*/*" },
      });
    } catch (error) {
      return text(502, `upstream fetch failed: ${error.message}`);
    }

    const location = upstream.headers.get("Location");
    if (!REDIRECT_STATUSES.includes(upstream.status) || !location) {
      break;
    }
    if (hop >= MAX_REDIRECTS) {
      return text(502, `too many redirects from ${parsed.hostname}`);
    }

    let next;
    try {
      next = new URL(location, current);
    } catch {
      return text(502, `bad redirect from ${current.hostname}: ${location}`);
    }
    if (!allowed(next)) {
      return text(403, `redirect to a host not allowed: ${next.hostname}`);
    }
    current = next;
  }

  return new Response(upstream.body, {
    status: upstream.status,
    headers: { "Content-Type": upstream.headers.get("Content-Type") ?? "application/octet-stream" },
  });
}

const REDIRECT_STATUSES = [301, 302, 303, 307, 308];
const MAX_REDIRECTS = 5;

function allowed(url) {
  return url.protocol === "https:" && ALLOWED_HOSTS.includes(url.hostname);
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
