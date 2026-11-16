# One domain: https://petstar.pages.dev

Players use a single address. The Cloudflare Pages project **`petstar`** (already created) serves:

| Path | What | Where it runs |
|---|---|---|
| `/` | Unity WebGL game, favicon, PWA manifest and icons, Open Graph image | Cloudflare Pages (static) |
| `/api/*` | REST API: login, pets/cards, packs, PvP, market/trading, sync | Pages Function → backend on the Contabo VPS |
| `/ws` | Realtime WebSocket: live PvP, chat, notifications | Pages Function → backend on the Contabo VPS |

ASP.NET Core, PostgreSQL and Redis stay on the VPS. Nothing moves to Cloudflare except the static files and
two small proxy functions in `cloudflare/pages/functions`. No other Pages project or public domain is created.
The Unity build only knows `/api`; it never contains the VPS address.

## Cloudflare settings (dashboard, once): Pages → `petstar` → Settings → Variables and Secrets

| Name | Type | Value |
|---|---|---|
| `API_ORIGIN` | Variable | HTTPS origin of the backend on the VPS, e.g. `https://<vps-api-hostname>` (no path) |
| `PROXY_SHARED_SECRET` | **Secret** (encrypted) | same value as `PROXY_SHARED_SECRET` in `/opt/petstar/petstar.env` |

Set both for **Production** and **Preview**. Until `API_ORIGIN` exists, every `/api` call answers
`503 {"code":"server_not_configured"}`. The game then shows **"Chưa kết nối máy chủ"** with a retry button
instead of failing. Pages Functions run on the free Workers plan (100,000 requests/day). Static files don't count
toward that, and nothing paid is enabled.

What the proxy guarantees (tested in `cloudflare/pages/test/proxy.test.mjs`, which runs in CI):

- `API_ORIGIN` must be plain `https://host`; anything else counts as not configured.
- Headers the browser sends to fake trust (`X-PetStar-*`, `X-Forwarded-For`) are removed. The player IP from
  Cloudflare and the shared secret are then added.
- WebSocket upgrades on `/ws` pass straight through. The `Origin` header stays `https://petstar.pages.dev`, which
  the backend allow-lists through `WEB_ORIGIN`.
- If the VPS is down or returns an HTML error page, the game gets `{"code":"server_unavailable"}`.

## VPS side (needs your approval before anything is changed there)

Cloudflare Workers cannot call a bare IP address, and the backend must answer over HTTPS. The VPS API therefore
needs a hostname with a valid certificate. That hostname is server-to-server only and is never shown to players:

- **No domain of your own (free):** a wildcard-DNS name that maps to the VPS IP, such as `<ip-with-dashes>.sslip.io`.
  It gets a Let's Encrypt certificate through the existing one-file Nginx site (`deployment/vps/nginx-site.conf.example`).
- **With a domain on Cloudflare later:** a Cloudflare Tunnel (`docker compose --profile tunnel`, already prepared).
  The VPS then needs no open port.

Then, in `/opt/petstar/petstar.env`:

```
WEB_ORIGIN=https://petstar.pages.dev
PROXY_SHARED_SECRET=<openssl rand -hex 32, generated on the VPS>
PROXY_REQUIRE_SECRET=false   # set true once /api works through Cloudflare: direct calls to the VPS are then refused
```

The API trusts `X-PetStar-Client-IP` only when the secret matches (`EdgeProxyMiddleware`, unit-tested). Rate limits
therefore stay per player and do not apply to Cloudflare's shared IPs.

## Publishing (manual, preview first)

GitHub Actions → **"Unity WebGL publish"** → `preview` (→ `https://preview.petstar.pages.dev`). It downloads the
latest successful Unity Build Automation WebGL build, adds the brand files and deploys it with the Functions.
`production` (→ `https://petstar.pages.dev`) needs the `production` environment approval. The React web-client
workflow refuses to deploy into `petstar`, so it can never overwrite the game.
