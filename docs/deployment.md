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

Unity builds run in **Unity Build Automation** (Unity Cloud), which supplies the Editor license itself, so no
`UNITY_LICENSE`, serial or keystore is needed for WebGL. **WebGL comes first**; Android and Windows are deferred.
The GitHub Actions GameCI workflow was removed. For setup, the pre-export method, environment variables, publishing
to a Cloudflare Pages preview from an iPhone, and the checks that run on every push, see
[unity-cloud-build.md](unity-cloud-build.md).

The Android/Windows build methods remain in `PetStarBuild` for later:
- Android: IL2CPP, ARM64, min API 26.
- AAB builds refuse to run without a release keystore provided by CI secrets.

## Environments

| | API env | Pack odds sync from seed | Fake store verifier | Dev outbox |
|---|---|---|---|---|
| development | Development | yes | allowed | yes |
| staging | Staging | yes | no | no |
| production | Production | no (admin approval flow) | no | no |
