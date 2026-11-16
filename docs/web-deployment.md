# Web deployment: Cloudflare Pages + isolated VPS stack

Nothing here has been deployed. Every production step needs the owner's explicit go-ahead. Secrets are created
directly in Cloudflare, GitHub or the VPS and are never pasted into chat, issues or commits.

```
iPhone Safari / PC ──https──▶ Cloudflare Pages (static SPA, petstar.pages.dev once the name is confirmed)
        │
        └──https + wss──▶ api.<your-domain> ──▶ [Cloudflare Tunnel | host Nginx site file] ──▶ 127.0.0.1:18080
                                                       petstar-prod: api ─ postgres ─ redis ─ backup
```

## 1. Cloudflare Pages (web client)

1. In the Cloudflare dashboard, create a Pages project with **Direct Upload**, named `petstar`. Only use
   `petstar.pages.dev` once Cloudflare confirms the name is available; if it is taken, choose another name and
   use that everywhere below.
2. Create an API token: *My Profile → API Tokens → Create Token → Custom*, permission
   **Account · Cloudflare Pages · Edit**, limited to your account.
3. In GitHub, under *Settings → Secrets and variables → Actions*:
   - secrets: `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`
   - variables: `CF_PAGES_PROJECT=petstar` and `PETSTAR_API_BASE=https://api.<your-domain>`
4. Under *Settings → Environments*, create `production` with yourself as a required reviewer, plus `preview`.
5. The `Web client (Cloudflare Pages)` workflow runs typecheck, unit tests, the build, the 25 MiB per-file check
   and Playwright e2e against a real API on every change. It deploys only when `CF_PAGES_PROJECT` is set:
   - pushes to `main` deploy to production, after your approval;
   - other branches deploy previews.

What the build produces:
- `dist/_redirects` for SPA deep links.
- `dist/_headers` with a CSP that allows only the site itself plus `PETSTAR_API_BASE` (https and wss), long
  caching for hashed bundles, and no-cache for data/i18n.
- Size: 455 files, 17.5 MiB, largest file 207 KiB, well under the Pages limits (25 MiB per file, 20,000 files).

Git integration (Cloudflare building from the repo) also works: root `web-client`, build `npm run build`,
output `dist`, env `VITE_API_BASE`, Node 22. The workflow above is recommended because it gates deploys on tests
and approval.

## 2. API on the Contabo VPS (Ubuntu 24.04), isolated from other projects

The design rules, all enforced by `deployment/vps/docker-compose.yml`:
- Compose project `petstar-prod`, network `petstar-prod-net`, volumes `petstar-prod_*`. No external networks or
  volumes are referenced.
- Nothing listens on 80/443. Postgres and Redis are never published. The API is published on
  `127.0.0.1:18080` only.
- CPU, memory and pids limits on every container; log rotation; API runs read-only with `no-new-privileges`.
- No existing container, volume, database or Nginx file is modified.

### Safe first deployment (run on the VPS as a sudo user, after approval)

Steps 1–7 are local to Pet Star.

1. Install Docker, only if it is missing. Check first with `docker --version`; if another project already uses
   Docker, keep its version.
2. Get the code into a separate directory. Use a read-only deploy key for the private repo:
   ```bash
   sudo mkdir -p /opt/petstar/backups && sudo chown -R "$USER" /opt/petstar
   git clone <repo-url> /opt/petstar/repo && cd /opt/petstar/repo && git checkout <commit-sha>
   ```
3. Create the secrets file on the VPS:
   ```bash
   cp deployment/vps/petstar.env.example /opt/petstar/petstar.env && chmod 600 /opt/petstar/petstar.env
   ```
   Then fill it in with `nano`, using `openssl rand -base64 36` / `-base64 48` for the passwords and the JWT key.
   Set `WEB_ORIGIN` to the confirmed Pages origin.
4. Run `bash deployment/vps/preflight.sh`. It is read-only and checks:
   - Docker and memory/disk
   - name collisions with existing containers, networks and volumes
   - that `127.0.0.1:18080` is free
   - that the secrets file has mode 600 and all values filled
5. Run `bash deployment/vps/deploy.sh`. It:
   - runs preflight
   - backs up the database if one exists
   - builds the image (or pulls the GHCR one)
   - runs `up -d`
   - waits for `/health/ready`
   - prints a rollback hint on failure
6. Check `curl -s http://127.0.0.1:18080/health/ready` → `Healthy`.
7. Ingress. Pick one:
   - **A. Cloudflare Tunnel (recommended; touches no host config).** Requires the API hostname's DNS zone to be on
     Cloudflare. In *Zero Trust → Networks → Tunnels*, create a tunnel and add a public hostname
     `api.<your-domain>` → `http://api:8080`. Put the token in `CLOUDFLARE_TUNNEL_TOKEN` and re-run `deploy.sh`,
     which then starts the `tunnel` profile. HTTPS and WebSocket are handled by Cloudflare.
   - **B. Existing host Nginx.** Add one new file from `deployment/vps/nginx-site.conf.example`:
     1. Edit the hostname.
     2. Link the file into `sites-enabled`.
     3. Run `nginx -t`, and only reload if it passes.
     4. Get the certificate with `certbot certonly --webroot` for that hostname.

     No other site is edited. The `/ws` location upgrades WebSockets and keeps tickets out of access logs.
8. Create the first owner:
   1. Register your account in the web client.
   2. Set `OWNER_EMAIL=<that email>` in `petstar.env`.
   3. Re-run `deploy.sh`. The role is granted at API start and audited.
   4. Sign out and sign in again: the Web Admin Panel tile appears.
   Grant moderators/admins from the panel; only the owner can change roles.

**Web Push (optional).** Generate VAPID keys on the VPS (`... run --rm --no-deps api generate-vapid`), add
`WEBPUSH_*` to `petstar.env`, then re-run `deploy.sh`. See [web-push.md](web-push.md).

### Upgrades, backups, restore

- **Upgrade:** `git fetch && git checkout <sha> && bash deployment/vps/deploy.sh`. It takes a backup first.
  Migrations are additive and run on API start. Alternatively, use the manual GitHub workflow
  `Deploy API to VPS`: type `deploy`, then approve it in the `production` environment. It needs the secrets
  `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY` (a dedicated key), `VPS_KNOWN_HOSTS`, and the variable `VPS_REPO_DIR`.
  GHCR images are private by default: run `docker login ghcr.io` once on the VPS with a read-only
  (`read:packages`) token, or keep building on the VPS.
- **Backups:** the `backup` container runs `pg_dump` every 6 h into `/opt/petstar/backups` (outside Docker
  volumes) with a 14-day retention and sha256 files. Copy them off the VPS regularly (e.g. `rclone` to object
  storage).
- **Restore test (non-destructive):** restores the newest dump into a throw-away database and compares row
  counts:
  ```bash
  PETSTAR_COMPOSE=vps/docker-compose.yml PETSTAR_PROJECT=petstar-prod bash deployment/scripts/restore-test.sh /opt/petstar/petstar.env
  ```
- **Rollback:** set `PETSTAR_API_IMAGE` to the previous image tag (printed by `deploy.sh`) and re-run it.
  Restore a dump only when a migration must be undone; migrations never drop data.
- **Stop Pet Star only:**
  ```bash
  docker compose -p petstar-prod -f deployment/vps/docker-compose.yml --env-file /opt/petstar/petstar.env down
  ```
  Volumes are kept unless you pass `-v`, which deletes Pet Star data. Never run it without a fresh backup.

## 3. CORS / WebSocket / HTTPS checklist

- The API accepts browser requests and WebSocket upgrades only from `WEB_ORIGIN` / `WEB_ORIGIN_2`, exact match.
  Native clients send no Origin header and are allowed.
- WebSocket auth uses a single-use, 60-second ticket from `POST /api/realtime/ticket`. Access tokens never appear
  in URLs.
- HTTPS terminates at Cloudflare (tunnel) or host Nginx. The API sees `X-Forwarded-For/Proto`. It is reachable only
  from loopback, so the forwarded headers can be trusted.
- iOS Safari keeps a WebSocket alive while the tab is visible. The client pings every 20 s, reconnects with
  backoff, and refetches state after reconnecting.
