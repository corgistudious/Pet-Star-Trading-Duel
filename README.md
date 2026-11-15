# Pet Star: Trading Duel

Online collectible pet card game (TCG + turn-based tactical duel, PvE/PvP). **Web first** (iPhone Safari + PC,
React/TypeScript on Cloudflare Pages), with the Unity client (Android/Windows) on the same backend later.
Internet required; one account plays everywhere; progress is independent per Global server (PS-1, PS-2, …).

> Status: web client + live features delivered and tested locally; **not deployed** (production deploys need the
> owner's approval). Latest report: [docs/status/web-report.md](docs/status/web-report.md).

## Repository layout

| Path | Contents |
|---|---|
| `backend/` | ASP.NET Core (.NET 10 LTS) API, EF Core migrations (PostgreSQL), tests |
| `shared-contracts/` | Engine-agnostic rules (battle engine, AI, stats, pack roller) + API DTOs. A Unity package **and** compiled by the backend, so client and server share one implementation (server stays authoritative) |
| `web-client/` | React 18 + TypeScript (Vite) web client for iPhone Safari and desktop, incl. Web Admin Panel; WebP art; vitest + Playwright |
| `client-unity/` | Unity 6 LTS client (C#, uGUI built from code, Input System, Addressables, Sprite Atlas, TextMeshPro) |
| `database/seed/` | Design database: pets, skills, elements, rarities, packs, economy, PvE stages |
| `asset-pipeline/` | Design DB → prompt → OpenAI Image API → validation → manifest, with a hard budget guard |
| `assets-source/` | Generated source art, Art Style Bible, Master Card Template layout |
| `asset-manifests/` | Asset manifest (prompt, model, hash, cost, provenance) + append-only cost ledger |
| `tools/` | Unity asset sync, card preview renderer, localization builder/checker, cost report |
| `deployment/` | Docker Compose (API, PostgreSQL, Redis, Nginx/TLS, backups), isolated shared-VPS stack (`deployment/vps/`), env templates, restore test |
| `docs/` | Architecture, database, API, economy, deployment, budget, status reports |
| `.github/workflows/` | Backend CI, web CI + Cloudflare Pages deploy (opt-in), manual VPS deploy, Unity cloud builds |

## Quick start (backend, local)

Requirements: .NET 10 SDK, PostgreSQL 16, Redis 7 (optional in dev).

```bash
createuser -s petstar   # or: CREATE ROLE petstar LOGIN PASSWORD 'petstar_dev_only' CREATEDB;
cd backend && ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PetStar.Api
```

The API migrates the database, creates **PS-1 • Global**, and serves on the configured URL (`/health/ready`).

## Tests

```bash
cd backend
dotnet test tests/PetStar.Rules.Tests        # rules: RNG, odds, packs, stats, evolution, decks, battle, AI
dotnet test tests/PetStar.Api.Tests          # integration + security against a throw-away PostgreSQL DB
python3 ../tools/check_localization.py       # en/vi coverage of every client string
```

## Web client

```bash
cd web-client && npm ci
cp .env.example .env.local          # VITE_API_BASE=http://localhost:5080
npm run dev                         # http://localhost:5173 (allowed by the Development CORS list)
npm test                            # vitest unit tests
npm run e2e                         # Playwright vs a running local API (see web-client/README.md)
python3 tools/build_web_assets.py   # (repo root) regenerate WebP art + catalog + i18n into web-client/public
```

Deployment (Cloudflare Pages + isolated VPS stack): [docs/web-deployment.md](docs/web-deployment.md).

## Asset pipeline

```bash
cd asset-pipeline
python3 -m petstar_assets check              # API connectivity + budget state
python3 -m petstar_assets gen-pets --ids pet_x --dry-run
python3 tools/sync_unity_assets.py           # (from repo root) optimized copies into Unity
python3 tools/cost_report.py                 # docs/asset-budget.md from the real ledger
```

The OpenAI key is never stored in the repository (read from `OPENAI_API_KEY` or injected by the environment).
Every call reserves its worst-case cost first and is refused if it could exceed the $15 pet / $5 UI caps.

## Builds

Unity builds run in GitHub Actions (`Unity client builds` workflow) once Unity license and Android signing secrets
are added — see [docs/deployment.md](docs/deployment.md). Nothing in this repo claims a build that has not run.

## Secrets & environments

Development / staging / production are separate (`deployment/env/*.env.example`). Secrets come from environment
variables / secret stores only; `.gitignore` blocks env files, keystores, certificates and service-account JSON.
