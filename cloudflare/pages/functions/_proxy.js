// Same-origin API for https://petstar.pages.dev: forwards /api/* and /ws (WebSocket) to the Pet Star backend on the
// VPS. Configure in Cloudflare (Pages project "petstar" > Settings > Variables and Secrets), never in the repository:
//   API_ORIGIN           https origin of the backend, e.g. https://<vps-api-hostname> (no path, no trailing slash)
//   PROXY_SHARED_SECRET  (secret, optional but recommended) the same value as PROXY_SHARED_SECRET on the VPS; the
//                        backend then trusts the player IP below and can refuse requests that bypass Cloudflare.
// Without API_ORIGIN every call answers 503 {"code":"server_not_configured"} and the game shows "not connected".

const TRUST_HEADERS = ["x-petstar-proxy-auth", "x-petstar-client-ip", "x-forwarded-for", "x-real-ip", "forwarded"];
const HOP_HEADERS = ["host", "connection", "keep-alive", "proxy-connection", "transfer-encoding", "te", "trailer"];

export function json(status, code) {
  return new Response(JSON.stringify({ code }), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
  });
}

/** Returns the backend origin or null when API_ORIGIN is missing or not a plain https origin. */
export function backendOrigin(env) {
  const raw = String((env && env.API_ORIGIN) || "").trim().replace(/\/+$/, "");
  if (!raw) return null;
  let u;
  try { u = new URL(raw); } catch { return null; }
  if (u.protocol !== "https:" || (u.pathname !== "/" && u.pathname !== "") || u.search || u.hash || u.username) return null;
  return u.origin;
}

export async function proxy({ request, env }, fetchImpl = fetch) {
  const origin = backendOrigin(env);
  if (!origin) return json(503, "server_not_configured");

  const incoming = new URL(request.url);
  const target = origin + incoming.pathname + incoming.search;
  const headers = new Headers(request.headers);
  const upgrade = (request.headers.get("upgrade") || "").toLowerCase() === "websocket";
  for (const h of TRUST_HEADERS) headers.delete(h); // never trust what the browser sent
  for (const h of HOP_HEADERS) if (!(upgrade && h === "connection")) headers.delete(h);
  const ip = request.headers.get("cf-connecting-ip");
  if (ip) headers.set("x-petstar-client-ip", ip);
  if (env.PROXY_SHARED_SECRET) headers.set("x-petstar-proxy-auth", env.PROXY_SHARED_SECRET);
  headers.set("x-forwarded-proto", "https");

  const hasBody = !["GET", "HEAD"].includes(request.method) && !upgrade;
  let res;
  try {
    res = await fetchImpl(target, { method: request.method, headers, body: hasBody ? request.body : undefined, redirect: "manual" });
  } catch {
    return json(502, "server_unavailable");
  }
  if (upgrade) return res; // 101 Switching Protocols carries the WebSocket pair through unchanged
  // Nginx/Cloudflare error pages are HTML; give the game a code it can show ("server unavailable").
  const type = res.headers.get("content-type") || "";
  if ([502, 503, 504, 521, 522, 523, 524, 525, 526].includes(res.status) && !type.includes("json"))
    return json(res.status === 503 ? 503 : 502, "server_unavailable");
  const out = new Response(res.body, res);
  out.headers.set("cache-control", res.headers.get("cache-control") || "no-store");
  return out;
}
