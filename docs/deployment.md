# Build & deployment

> **Web-first / shared VPS:** the isolated `petstar-prod` stack for a VPS that already hosts other projects, plus
> Cloudflare Pages for the web client, is documented in [web-deployment.md](web-deployment.md). The stack below
> (with its own Nginx on 80/443) is for a dedicated host.


## Backend (Docker Compose)

```bash
cd deployment
cp env/staging.env.example env/staging.env     # fill in real secrets (never commit)
docker compose --env-file env/staging.env up -d --build
./scripts/restore-test.sh env/staging.env      # verify backups can be restored
```

Services: `api` (ASP.NET Core, non-root, health check), `postgres` (volume), `redis` (password, AOF), `nginx`
(TLS 1.2/1.3, HSTS, rate limit, public API host blocks `/api/admin` and `/dev`; admin host restricted by
`ADMIN_ALLOW_CIDR`), `backup` (pg_dump every 6 h, sha256, retention).

TLS: staging/production certificates from Let's Encrypt (certbot) mounted at `deployment/nginx/certs`;
`scripts/dev-certs.sh` makes a self-signed cert for local tests only.

Monitoring baseline: container health checks, `/health/ready` (DB check), structured logs, admin server overview
(members, 24 h active, online via Redis presence). Prometheus/Grafana or a hosted APM can be added at Phase 7.

**Production deployment and any change to real data require the owner's explicit approval.**

## Unity builds without a local PC

The owner coordinates from an iPhone, so builds run in GitHub Actions (`.github/workflows/unity-build.yml`, GameCI):

1. Add repository secrets: `UNITY_EMAIL`, `UNITY_PASSWORD` and `UNITY_LICENSE` (Personal: the `.ulf` file content) or
   `UNITY_SERIAL` (Pro). Unity Build Automation is an alternative if preferred (paid, not assumed).
2. For Google Play: create an upload keystore once (Play App Signing keeps the app signing key), then add
   `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME`, `ANDROID_KEYALIAS_PASS`.
3. Run the workflow from the GitHub app (Actions → Unity client builds → Run workflow) and download the APK, AAB and
   Windows build artifacts.

The build method (`PetStar.EditorTools.PetStarBuild`) sets up the project (bootstrap scene, sprite importers,
currency Sprite Atlas, Addressables, TextMeshPro fonts), builds Addressables and the player.
Android: IL2CPP, ARM64, min API 26, target API = latest installed SDK (Google Play requires a recent target API;
confirm the current requirement at release time). AAB builds refuse to run without the release keystore.

## Environments

| | API env | Pack odds sync from seed | Fake store verifier | Dev outbox |
|---|---|---|---|---|
| development | Development | yes | allowed | yes |
| staging | Staging | yes | no | no |
| production | Production | no (admin approval flow) | no | no |
