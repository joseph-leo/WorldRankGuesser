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
