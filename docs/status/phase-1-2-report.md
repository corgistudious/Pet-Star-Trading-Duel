# Phase 1 + 2 report — Foundation & Art prototype (2026-11-15)

## 1. Environment readiness

| Tool / service | Status | Notes |
|---|---|---|
| GitHub repo `corgistudious/Pet-Star-Trading-Duel` | ✅ Ready | branch `claude/pet-star-trading-duel-luftdb` |
| .NET SDK | ✅ 10.0 LTS installed | .NET 8 reached end of support Nov 2026, so .NET 10 LTS is used |
| PostgreSQL 16 / Redis 7 | ✅ Local | used by integration tests |
| Docker | ⚠ Partial | daemon works, Microsoft registry OK; Docker Hub rate-limited (429) here, so the full compose stack could not be started in this sandbox. API image runtime verified with local DB |
| OpenAI Image API | ✅ Connected | key stored as environment network secret (never in repo). Models visible incl. `gpt-image-2`, `gpt-image-1.5`. `gpt-image-2` rejects transparent backgrounds → **`gpt-image-1.5`** used (transparent PNG, $0.034 list price medium 1024²) |
| Unity Editor / license | ❌ Missing | no Unity in this container; client code is written but **not compiled yet**. Builds run via GitHub Actions once Unity license secrets are added |
| Android signing keystore | ❌ Missing | needed for AAB (owner creates; stored as GitHub secret) |
| Google Play Console / service account | ❌ Missing | needed for real IAP verification and release |
| SMTP provider | ❌ Missing | dev uses in-memory outbox; production needs SMTP credentials |
| Domain + TLS | ❌ Missing | staging/production hostnames are placeholders |

## 2. Delivered

* **Backend** (ASP.NET Core .NET 10, EF Core, PostgreSQL, Redis): auth (register/login/refresh rotation/logout,
  sessions, email verification, password reset, lockout, rate limits, audit), multi-server (PS-1 • Global auto-created,
  admin can create PS-2…; per-server memberships; claims-bound server context; composite FKs), two currencies with
  ledgers, pack shop/opening (server CSPRNG, exactly 5 cards, separate final-slot table, idempotent, recovery),
  Pet Home (filters/sort/copies), favorite/lock, Codex, evolution (same card id, materials once), decks (3+2),
  PvE battles vs rule-abiding AI with rewards and daily anti-farm cap, Rainbow Star IAP verification endpoint
  (Google Play verifier + fake store for tests), admin server management (RBAC owner/admin), health checks.
* **Shared rules**: battle engine (simultaneous planning, priority/SPD/seeded tie-break, energy 3/+3/10, frontline
  protection, taunt/stun/burn/poison/weaken, shields, heals, lifesteal, round limit 20 + tiebreak), AI, stats, packs.
* **Design data**: 8 elements (cycle water>fire>nature>earth>lightning>wind>water; light⇄dark), 10 rarities with the
  exact colors, 7 qualities, 5 cosmetic finishes, 5 roles, 10 sample pets in 5 lines (3+3+2 stages + 2 standalone)
  with unique skills, lore (en/vi), PvE chapter 1 (5 stages).
* **Art**: Art Style Bible, 10 sample pets, Master Card Template (layout JSON, rendered by Unity at runtime; previews
  in `docs/previews/`), 8 currency assets, 2 booster packs, 5 backgrounds.
* **Unity client (uncompiled)**: Splash, Login, Register, Server Selection, Lobby, Shop (packs + Rainbow Star tab, odds),
  Pack Opening (swipe-to-tear top edge on touch / press-drag on mouse, stack of 5, swipe-or-tap flip, rare glow/sparkle,
  final-card suspense beat identical for all rarities, Skip, results with favorite, recovery), Pet Home, Card Details,
  Evolution (preview, confirm, transformation), Codex, Profile, Settings, Duel lobby (auto deck), Duel board (10 cards
  without overlap in portrait/landscape, energy, planning, targets, combat log, damage numbers, shake/lunge, defeat
  greying, victory/defeat). Currency bar with Rainbow Star shimmer/glow and Mini Green Bean reflection, balances from
  the server only. Procedural original SFX. Full en/vi localization (260 keys, checked).
* **Ops**: Docker Compose (API, Postgres, Redis, Nginx TLS, backups), env templates (dev/staging/prod), backup +
  restore test, CI workflows (backend tests on PostgreSQL; Unity APK/AAB/Windows via GameCI).

## 3. Test results (run 2026-11-15 in this environment)

| Suite | Result |
|---|---|
| Rules unit tests (RNG, odds ±5% over 200k packs, final-slot table, 5 cards, stats, EXP, evolution rules, deck rules, element chart, energy, illegal plans, backline protection, taunt, stun, determinism, 200 AI battles terminate, round-limit tiebreak, forfeit) | **45 / 45 passed** |
| API integration & security on PostgreSQL (UID ≥ 100000, duplicate email, hashing, lockout, refresh rotation + reuse detection, logout revocation, verify/reset flows, server required, starter grant once, no negative balance, concurrent double-spend, pack open 5 cards, idempotent replay, key reuse, concurrent duplicate open, over-opening, recovery, public odds, admin RBAC, cross-server isolation, DB composite-FK guard, registration closed, maintenance gate, evolution, deck validation, foreign cards, full PvE battle, stale round replay, locked stage, foreign battle access, IAP grant once, token replay by another account, invalid purchase) | **27 / 27 passed** |
| Localization coverage en/vi | **passed** |
| Production container runtime (health, `/dev` hidden, refuses to start without JWT key) | **passed** |
| Backup → restore → row counts + wallet/ledger reconciliation | **passed** |
| Unity compile / device tests (Android portrait/landscape, Windows, gestures) | **not run** — needs Unity license (CI) |
| Full `docker compose up` | **not run** — Docker Hub rate limit in this sandbox |

## 4. Asset cost (actual)

See [../asset-budget.md](../asset-budget.md). Pets: **$0.467 / $15** (10 images, avg $0.0467). UI: **$1.229 / $5**
(probe + 8 currency + 2 packs + 5 backgrounds). Total **$1.70 / $20**. Projection for 190 more pets ≈ **$8.9**
(+10% regeneration ≈ $9.8) → fits the pet budget with ~$4.7 margin.

## 5. Quality review of the prototype art

* Style is consistent (chibi, big glossy eyes, clean outlines, soft cel shading), transparent backgrounds pass checks.
* Evolution lines read clearly (Emberkit → Cinderlynx → Pyrelynx; Puddlotl → Ripplotl → Tidecrown).
* ⚠ **Zipmunk / Fulgurmunk** (yellow electric rodent) is the design closest to a famous franchise mascot.
  Recommendation: recolor the line (e.g. teal/indigo fur with yellow bolts) before release.
* ⚠ Rainbow Star chest: the stars inside look more like gem blocks; acceptable, can be regenerated (~$0.08).
* ⚠ Duel board backgrounds have painted card slots that don't match our 3+2 layout; regenerate without slots (~$0.13).

## 6. Decisions needed from the owner

1. **Approve the art direction** to start generating the remaining ~190 pet designs (batches of 10–20, ~$0.05 each).
2. **Rainbow Star sinks**: what may it buy without giving combat advantage (cosmetics only?). Packs stay bean-only until decided.
3. Deck rule assumption: max **2 cards of the same evolution family** per deck — keep?
4. Unity license type (Personal/Pro) and adding CI secrets; Android upload keystore.
5. Domain names for staging/production, SMTP provider, Google Play developer account.
