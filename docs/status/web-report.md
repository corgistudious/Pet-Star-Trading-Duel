# Web-first report: live features, web client, deployment (2026-11-15)

## Update: one domain (petstar.pages.dev), first WebGL build without an API, official logo pipeline

**First WebGL build without a backend**
- The placeholder hosts (`*.petstar.example`) were removed from `client-config.json`.
  - `staging` is now the same origin, `/api`.
  - `production` is empty and must be set explicitly. The validator fails a production build without it, and
    rejects placeholder or raw-IP hosts.
- `ClientConfig.Resolve` handles all three forms:
  - `/api` becomes the WebGL page origin;
  - `https://host/api` becomes `https://host`;
  - empty means not configured.

  The resolve cases were checked by a scratch run.
- Behaviour with no server:
  - requests fail fast with `server_not_configured`, without crashing or retry loops;
  - realtime doesn't connect;
  - the splash checks `GET /api/version` and shows a new status screen (not configured, offline, unavailable or
    maintenance) with a retry button. Maintenance responses during play also open it.
- The Bootstrap scene and `EditorBuildSettings.asset` are committed. The logo import is batched.

**Same-origin API through Cloudflare Pages Functions** (`cloudflare/pages`, 5 Node tests)
- `/api/*` and `/ws` (WebSocket) are forwarded to `API_ORIGIN`, a Cloudflare variable.
- Trust headers sent by the browser are stripped, and the shared secret plus the player IP are added.
- An HTML 502 or an unreachable VPS becomes `server_unavailable`.
- Backend `EdgeProxyMiddleware` (3 unit tests): it trusts the player IP only with `PROXY_SHARED_SECRET`;
  `PROXY_REQUIRE_SECRET=true` locks `/api` and `/ws` to Cloudflare. All 125 backend tests pass.
- The publish workflow deploys the Functions with the build into the existing project `petstar`. The web-client
  workflow can no longer deploy into `petstar`.

**Official logo** (the file itself is not in the repository yet; see below)
- The pipeline makes every derivative from the uploaded original, without regenerating or cropping it:
  - `tools/brand/make_brand_assets.py` makes the favicon (ico, 16, 32), PWA 192/512 icons, maskable 192/512 (72 %
    safe zone), Apple Touch 180, loading logos 256/512/768 WebP, Open Graph 1200×630 and `manifest.webmanifest`
    ("Pet Star" / "Pet Star: Trading Duel", Android splash colour from the logo's blue);
  - the Unity pre-export copies the original byte for byte to `ui/brand_logo`.
- Unity uses the logo on:
  - the splash;
  - login and register (above the card in portrait, beside it in landscape);
  - the lobby header (106 px, outside the play area);
  - the new status screens.
- The WebGL page uses it on the loading screen (gold-blue-purple bar, soft glow) and the rotate overlay (small logo,
  still pure black).
- SEO, Open Graph and Apple meta tags were added.
- Tested with a stand-in image in the scratchpad (never committed):
  - outputs for square and non-square originals (letterboxed, never stretched);
  - Chromium iPhone 13 portrait, landscape and desktop: no overflow, correct `no-logo` fallback, rotate overlay with
    the logo.

**Still open**
- The official logo file must be uploaded to `assets-source/brand/petstar-logo-original.png`. The chat image can't
  be read as a file here.
- VPS: an HTTPS hostname for `API_ORIGIN`, plus `PROXY_SHARED_SECRET`. This needs owner approval and was not done.
- A real Unity Cloud build, a real iPhone Home Screen test and a Retina check with the real logo are still untested.

## Update: Unity WebGL via Unity Build Automation

**Automation for Build Automation**
- Pre-export entry point `PetStar.EditorTools.CloudBuild.PreExport`. It configures the project, validates it and
  builds Addressables.
- `PetStarBuild` was made unattended-safe:
  - WebGL player settings;
  - synchronous TMP Essentials extraction (the `.unitypackage` reader was tested on a synthetic package, including
    long names and a path-traversal entry);
  - duel board sprites in their own Addressables group with fixed addresses;
  - one bundle per sprite;
  - a guarded atlas;
  - a safe shader include;
  - the `PETSTAR_ENV` / `PETSTAR_API_BASE` environment.
- `ProjectValidator`: version/path, scenes, board sprites, all 208 runtime addresses, fonts, WebGL template and
  plugins, HTTPS API, JSON.
- WebGL page template `PetStar` (black, safe area, rotate overlay following the official viewport rule) and
  `link.xml`. GameCI workflow removed.

**WebGL runtime fixes**
- Realtime uses a browser WebSocket bridge (`.jslib`); `System.Net.WebSockets` is unavailable in WebGL.
- `Task.Delay` was replaced with a main-thread wait.
- UI input works with either input backend.
- Navigator tells the page whether a duel is open.

**Real bugs found by the new semantic compile check (each would have failed the Unity build)**
- `CurrencyEffects.cs` was missing `using PetStar.Client.UI`.
- `PackOpeningScreen` called `Destroy` from a non-MonoBehaviour.
- `SocialScreens.cs` was missing `using System`.
- A discard/parameter clash in the new WebGL realtime code.
- Asset bug: pack art loaded `ui/ui_basic_pack` instead of `ui/ui_pack_basic`.

**Checks on every push (no Unity license)**
- Asset pre-check.
- C# 9 syntax check.
- Semantic compile in 5 configurations (WebGL, WebGL without Input System, Android, Windows, Editor) against real
  UnityEngine/UnityEditor/UI reference DLLs, with stubs for TMP/Addressables/Input System. All pass.

**Not testable here:** a real Unity Editor import/compile/build (Unity's servers are blocked from this
environment; Build Automation is the first real build), shaders/IL2CPP/WebAssembly, the WebGL build on an iPhone,
and the Build Automation API in the publish workflow. Details: [../unity-cloud-build.md](../unity-cloud-build.md).

## Update: remaining items

- **E-mail links:** verification and reset mails now link to the web client (`App:WebBaseUrl`, default
  `WEB_ORIGIN`), with bilingual text plus the app link and raw code.
  - Before this, mails only had a `petstar://` link, so web players could not verify or reset.
  - The web client handles `?verify=` and `?reset=` whether or not a player is signed in, and removes the token
    from the address bar.
  - New e2e test covers both flows.
- **Placeholders closed:**
  - The feed `guild` tab returns the guild's posts (members only).
  - The Event Collection album lists Pets offered by events and gift codes.
- **Unity live features:** realtime client, live PvP, Missions, Rewards, Guild and Chat screens. New shared
  en/vi keys (1,164 per language). A Unity syntax check was added to CI.
- **Tests:** API 76/76, rules 46/46, web unit 42/42, web e2e 12/12, localization coverage OK, Unity syntax 25
  files OK.

## Update: Web Push

- Backend sends standards-based Web Push: RFC 8291 encryption, verified byte-for-byte against the RFC example,
  and VAPID signing.
- Rules: endpoint allowlist (anti-SSRF), push only when the player is offline, only for committed notifications,
  per-category opt-in, en/vi texts, per-account rate limit, cleanup of revoked subscriptions.
- Settings switch + categories + test button. The iPhone hint appears in Safari tabs (Home Screen app required).
  The service worker shows notifications and opens the right screen on tap.
- Migration `WebPush`.
- Tests: API 73/73, rules 46/46 (12 consecutive runs), web unit 42/42, e2e 11/11 (two consecutive runs).
- Fixed a real race in shared rules code: `GameCatalog.Forms` published a half-built dictionary under concurrent
  first access. This caused an intermittent pack-odds test failure; the Unity client was also exposed.
- Not verifiable here: a real push-service subscription and a real iPhone. Needs one manual check after deploy.
  Details: [../web-push.md](../web-push.md).

## Update: missions, PWA, viewport rules

- **Daily/weekly missions:** server-side, with progress derived from pack openings, battle results, evolutions, the
  ledger and login claims inside the UTC period.
  - Surrender/forfeit and opponent-only battles don't count.
  - One claim per period (PK + ledger ref `mission:{id}:{period}`); rewards are paid through the ledger.
  - Web Missions screen with a lobby badge.
  - Migration `Missions`; API tests cover periods, derivation, concurrent double claims, the all-missions bonus
    and per-server isolation.
- **PWA:**
  - Manifest and icons, a build-generated service worker, an update prompt and an install button. iOS gets
    instructions instead, since Safari has no install prompt.
  - An offline shell that keeps the session.
  - Two bugs found by tests and fixed: the first SW install reloaded the page, and `Vary: Origin` made the
    precache miss.
- **Viewport rules** ([../viewport-rules.md](../viewport-rules.md)):
  - Portrait 9:16 frame outside duels; Landscape 16:9 duel stage (3+2 per side).
  - `#000000` rotate overlay on phones, with `visualViewport` / safe-area / keyboard handling.
  - Unity: Android orientation per game state; Windows windowed resizing with no display rotation. The Unity side
    is **not compiled**.
- **Tests:**
  - Backend API 65/65 (one earlier full run had an intermittent failure in the older concurrent-market test; not
    reproduced in 8 later runs).
  - Rules 46/46.
  - Web unit 39/39.
  - Web e2e 8/8 (gameplay, viewport, PWA, desktop resize).
- Missing (unchanged): real-iPhone manual pass, Unity compile.


Branch `claude/pet-star-trading-duel-luftdb`. Backend live features were committed in `a12daab`, the web client
in `1eabcdb`, and deployment and docs in the commit that adds this file. **Nothing has been deployed** to the VPS
or to Cloudflare. Production deploys wait for the owner's approval.

## 1. Chosen web option: React + TypeScript client on the existing backend

| | Unity WebGL | React/TypeScript (chosen) |
|---|---|---|
| Build today | Unity client was never compiled. Personal-license activation in CI needs a manual desktop step (no bypass allowed) | Builds and tests in CI and here |
| iPhone Safari | Large wasm and memory pressure (tab reloads on older iPhones), slow first load, weak text input and IME | ~70 KB gz first-load JS; screens lazy-loaded; native inputs, Vietnamese IME, safe-area, 100dvh |
| Cloudflare Pages | Data/wasm files often near or over the 25 MiB per-file limit | 455 files, 17.5 MiB, largest 207 KiB |
| Gameplay reuse | Same C# rules | Server is authoritative for every rule. The client mirrors only display helpers (energy, legal targets, deck rules), unit-tested against the C# behaviour |
| Backend | unchanged | unchanged. Same API, assets and localization tables |

Unity stays the path for the Android and Windows apps. Its license and the Android keystore are deferred, as
decided.

## 2. What works, tested against the real backend

E2E runs Chromium with iPhone 13 emulation and desktop, against the real API, PostgreSQL, Redis and WebSocket.
There are no mocks.

**Covered by Playwright e2e:**
- **Account and server:** register; join PS-1; starter grant shown from the server ledger (500 Mini Green Bean,
  0 Rainbow Star).
- **Shop and pack opening:**
  - Buy a pack: balance 500 → 400 from the server response.
  - Swipe-to-tear (pointer drag), then flip 5 cards one by one with rare effects.
  - Reload-safe `/open/<id>`: no second pack is spent. Skip works.
- **Collection and decks:** Pet Home count; deck builder with exactly 3 front + 2 back and auto fill.
- **PvE:** duel on the code-drawn 3+2 board over the new slot-free board art. Skill orders with legal targets,
  played to victory or defeat, rewards shown.
- **Market:** listing with the server fee quote (40 → fee 1, net 39) and the low-price confirmation; a second
  player buys by serial.
  - Buyer ledger shows −40; seller ledger shows +39.
  - The seller gets a live WebSocket "Your card was sold" toast.
- **Chat and live PvP:**
  - World chat delivered live between two players.
  - Live PvP quick match: both players are pushed to the board. Sealed plans: one waits, the second resolves,
    and both see Round 2.
- **Admin and rewards (desktop):**
  - Web Admin Panel (owner) creates a gift code. The player redeems it (+123 via the ledger); a second redeem is
    refused.
  - Daily login +50, then disabled.
- **Guild and language:** create a guild, post an announcement; switch to Vietnamese.

**Implemented and wired to the real API, but not in e2e** (the backend endpoints have integration tests):
- Card detail actions:
  - favorite and lock
  - evolution preview and evolve
  - skill training
  - auction create
  - passport with valuation chart
  - share to feed
- Codex.
- Ranked: match, leaderboard, history.
- Live PvP challenges between friends.
- Marketplace extras:
  - sections: rare, for-you, wishlist, my listings
  - purchase/sales history
  - wishlist editor
- Auctions: list, detail, bids/buy-now, server-time countdown, 10 s refresh.
- Feed: composer with up to 5 cards, likes, saves, comments, reports, tabs.
- Guild management: join/request, accept/reject, promote/demote, kick, leave; guild feed.
- Guild and DM chat.
- Friends, search, profile: follow, block, mute, showcase.
- Notifications with deep links.
- Mailbox claim, events claim, albums, achievements.
- Settings: profile, currency history (ledger), device sessions.
- Admin tabs: economy overview, servers, reports, accounts (ban, roles), events, fraud review and serial lookup,
  owner mail, audit log.

**Both currencies** are shown from server balances and refreshed after every economic response.
- No fake data or local balance math.
- No real-money purchases: the Rainbow Star tab says the store is unavailable, and `/api/iap/*` is disabled.

## 3. Still open (current as of the latest update; reported, not simulated)

**Needs the owner or a device**
- One play-through on a **real iPhone**: Safari tab and Home Screen app, including push. Chromium emulation is not
  WebKit, and headless browsers here cannot reach push services.
- **Production / staging deploy**: needs approval. Docker images cannot be built in this sandbox (registry TLS /
  rate limit); the compose file is validated with `docker compose config`. The first build runs on the VPS or in
  the manual workflow.
- **SMTP credentials**: e-mail sending is implemented. Mails carry a web link, in Vietnamese and English, and are
  tested end to end through the dev outbox. Real delivery needs `SMTP_*` set on the VPS (STARTTLS on port 587).
- **VAPID keys for Web Push**: generated on the VPS with `generate-vapid`.

**Deferred by decision**
- **Unity**: the license is activated and Unity Build Automation (WebGL first) is prepared. The first real cloud
  build has not run yet.
  - The Unity code now also has the live features: realtime socket, Missions, Rewards (login / gift code /
    events), Guild, Chat, live PvP lobby and live duel mode.
  - It is checked only for **syntax** (Roslyn, C# 9, in CI); it has never been compiled against UnityEngine.
  - The board sprites still need Addressables entries in the Editor.
- **Android keystore**: deferred.

**Known limitations (by design)**
- Ranked opponents are snapshots of other players' decks driven by the server AI (asynchronous). Live PvP is the
  real-time mode.
- The web refresh token lives in `localStorage`, mitigated by a strict CSP and rotation with reuse detection.
- The Unity client has no Web Push (native push would need FCM/APNs setup and store builds).

## 4. Test results (run in this environment)

| Suite | Result |
|---|---|
| Backend rules unit tests | 46 / 46 passed |
| Backend API integration and security (incl. guild/chat/WS ticket/live PvP sealed plans, deadline forfeit, queue, giftcode, login/events, ban/roles, CORS) | 61 / 61 passed |
| Backend load scenario (`PETSTAR_LOAD=1`) | passed |
| Localization coverage (1,093 keys en/vi) + 230 web-only strings en/vi | passed |
| Web typecheck + production build + 25 MiB/file check | passed (455 files, 17.5 MiB, max 207 KiB) |
| Web unit tests (vitest) | 29 / 29 passed |
| Web e2e (Playwright, real API) | 3 / 3 passed, three consecutive runs (one earlier run had a test-assertion race; fixed) |
| CSP check (built site served with the generated `_headers`) | no violations; WebSocket connects |
| VPS preflight script (dry run on Ubuntu 24.04 sandbox) | passed |

## 5. AI asset budget

There were no new image generations in this phase. Web art is converted locally (WebP).
- Pets: $9.58 / $15.
- UI: $1.35 / $5.
- Total: **$10.93 / $20**, plus a $0.054 interrupted reservation that the guard counts as spent.

## 6. Cloudflare configuration needed (owner)

1. Confirm the Pages project name `petstar` is available, and create it with Direct Upload.
2. Create an API token with **Account · Cloudflare Pages · Edit**.
3. Add GitHub secrets `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID`.
4. Add GitHub variables `CF_PAGES_PROJECT=petstar` and `PETSTAR_API_BASE=https://api.<your-domain>`.
5. Create the GitHub environment `production` with yourself as required reviewer.
6. For the API hostname, either put its DNS zone on Cloudflare and create a Tunnel (recommended), or use the
   existing VPS Nginx with one new site file.

Full steps: [../web-deployment.md](../web-deployment.md).

## 7. Safe VPS deployment steps (summary)

1. Clone into `/opt/petstar/repo`.
2. Create `/opt/petstar/petstar.env` (mode 600) with secrets generated on the VPS.
3. Run `preflight.sh`. It is read-only and checks name collisions, the port, memory/disk and the secrets file.
4. Run `deploy.sh`, which backs up first, then:
   - starts the isolated `petstar-prod` project (API on 127.0.0.1:18080 only, resource limits, no 80/443);
   - waits for `/health/ready`.
5. Add ingress via a Cloudflare Tunnel profile, or one new Nginx site file (`nginx -t` before reload).
6. Set `OWNER_EMAIL` and re-run to bootstrap the owner.

Backups run every 6 h into `/opt/petstar/backups`. A non-destructive restore test is available, and rollback is by
image tag.

## 8. Next steps (proposed)

1. Owner: Cloudflare Pages name and token, API hostname, VPS approval, then run the steps above.
2. Manual iPhone Safari play-through on the deployed preview.
3. Missions (daily/weekly), Web Push, PWA manifest.
4. Unity: Editor pass for Addressables and compile, once the license step is done.
