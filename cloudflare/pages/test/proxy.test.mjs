import { test } from "node:test";
import assert from "node:assert/strict";
import { proxy, backendOrigin } from "../functions/_proxy.js";
import { onRequest as ws } from "../functions/ws.js";

const req = (url, init = {}) => new Request(url, init);

test("missing or unsafe API_ORIGIN answers server_not_configured", async () => {
  for (const env of [{}, { API_ORIGIN: "" }, { API_ORIGIN: "http://1.2.3.4" }, { API_ORIGIN: "https://x.test/path" }, { API_ORIGIN: "nope" }]) {
    const r = await proxy({ request: req("https://petstar.pages.dev/api/version"), env }, () => assert.fail("must not fetch"));
    assert.equal(r.status, 503);
    assert.deepEqual(await r.json(), { code: "server_not_configured" });
  }
  assert.equal(backendOrigin({ API_ORIGIN: "https://api.test/" }), "https://api.test");
});

test("forwards path, query, method, body; replaces trust headers", async () => {
  let seen;
  const fake = async (url, init) => { seen = { url, init, body: await new Response(init.body).text() }; return new Response('{"ok":1}', { status: 200, headers: { "content-type": "application/json" } }); };
  const request = req("https://petstar.pages.dev/api/auth/login?x=1", {
    method: "POST", body: '{"email":"a"}',
    headers: { "content-type": "application/json", "cf-connecting-ip": "203.0.113.7", "x-petstar-client-ip": "6.6.6.6", "x-petstar-proxy-auth": "forged", "authorization": "Bearer t" },
  });
  const r = await proxy({ request, env: { API_ORIGIN: "https://api.test", PROXY_SHARED_SECRET: "s3cret" } }, fake);
  assert.equal(r.status, 200);
  assert.equal(seen.url, "https://api.test/api/auth/login?x=1");
  assert.equal(seen.init.method, "POST");
  assert.equal(seen.body, '{"email":"a"}');
  assert.equal(seen.init.headers.get("x-petstar-client-ip"), "203.0.113.7");
  assert.equal(seen.init.headers.get("x-petstar-proxy-auth"), "s3cret");
  assert.equal(seen.init.headers.get("authorization"), "Bearer t");
  assert.equal(seen.init.redirect, "manual");
});

test("forged trust headers are dropped when no secret is configured", async () => {
  let h;
  await proxy({ request: req("https://petstar.pages.dev/api/x", { headers: { "x-petstar-proxy-auth": "forged", "x-forwarded-for": "6.6.6.6" } }), env: { API_ORIGIN: "https://api.test" } },
    async (_u, init) => { h = init.headers; return new Response("{}", { headers: { "content-type": "application/json" } }); });
  assert.equal(h.get("x-petstar-proxy-auth"), null);
  assert.equal(h.get("x-forwarded-for"), null);
});

test("backend down or HTML error page -> server_unavailable", async () => {
  const env = { API_ORIGIN: "https://api.test" };
  let r = await proxy({ request: req("https://petstar.pages.dev/api/x"), env }, async () => { throw new Error("refused"); });
  assert.equal(r.status, 502); assert.deepEqual(await r.json(), { code: "server_unavailable" });
  r = await proxy({ request: req("https://petstar.pages.dev/api/x"), env }, async () => new Response("<html>bad gateway</html>", { status: 502, headers: { "content-type": "text/html" } }));
  assert.deepEqual(await r.json(), { code: "server_unavailable" });
  // API errors (JSON) pass through unchanged
  r = await proxy({ request: req("https://petstar.pages.dev/api/x"), env }, async () => new Response('{"code":"insufficient_funds"}', { status: 409, headers: { "content-type": "application/json" } }));
  assert.equal(r.status, 409); assert.deepEqual(await r.json(), { code: "insufficient_funds" });
});

test("/ws requires an upgrade and keeps the upgrade headers", async () => {
  const env = { API_ORIGIN: "https://api.test" };
  let r = await ws({ request: req("https://petstar.pages.dev/ws?ticket=t"), env });
  assert.equal(r.status, 426);
  let seen;
  const fake = async (url, init) => { seen = { url, init }; return new Response(null, { status: 200 }); };
  const request = req("https://petstar.pages.dev/ws?ticket=abc", { headers: { upgrade: "websocket", connection: "Upgrade", origin: "https://petstar.pages.dev", "sec-websocket-key": "k" } });
  await proxy({ request, env }, fake);
  assert.equal(seen.url, "https://api.test/ws?ticket=abc");
  assert.equal(seen.init.headers.get("upgrade"), "websocket");
  assert.equal(seen.init.headers.get("origin"), "https://petstar.pages.dev");
  assert.equal(seen.init.body, undefined);
});
