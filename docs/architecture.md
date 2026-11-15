# Architecture

## Overview

```
Unity client (Android / Windows) ──HTTPS REST (+WebSocket for PvP, next phase)──► Nginx (TLS, rate limit)
                                                                                 │
                                                       ASP.NET Core API (stateless, N replicas)
                                                         │                 │
                                                  PostgreSQL 16       Redis 7 (presence, matchmaking, cache)
```

* **Server-authoritative.** The client sends intents (open pack, evolve, battle actions). Balances, card ownership,
  RNG, damage, battle results and rewards are computed and stored by the server only.
* **Shared rules** (`shared-contracts/`): the battle engine, AI, stat formulas and pack roller are plain C#
  (netstandard2.1). The backend runs them authoritatively; the client may use them only for display.
* **Unity client** builds all UI from code (no hand-authored scene content), with separate layouts for portrait,
  landscape and desktop (`Navigator.LayoutMode`), never a stretched portrait layout.

## Authentication

* Passwords: PBKDF2-HMAC-SHA512 (ASP.NET Core Identity v3 format, 100k iterations, per-password salt).
* Access token: JWT (15 min) with `sub` (UID), `sid` (session), optional `srv`/`mid` (selected server + membership).
* Refresh token: random 384-bit, stored as SHA-256, **rotated on every use**; reuse of a rotated token revokes every
  session of the account (theft detection). Sessions can be listed/revoked; logout/password reset revoke immediately
  (session check cache is invalidated).
* Email verification and password reset use single-use hashed tokens (48 h / 1 h). Forgot-password responses are
  identical whether or not an account exists.
* Lockout after 5 failed logins (15 min), IP rate limiting on auth routes, per-user token-bucket on game routes.
* UID = `accounts.id`, generated from sequence `account_uid_seq` starting at **100000**; global and immutable.

## Multi-server (PS-1, PS-2, …)

* `game_servers` holds each Global server (status: `coming_soon`, `online`, `maintenance`, `merged`; registration flag).
* One account → zero or more `server_memberships` (one per server). A membership is the player's independent progress
  profile: level, wallets, cards, decks, codex, PvE progress, and later guilds, trades, rankings.
* **Selecting a server issues a token bound to (server, membership).** Every gameplay query filters by those claims,
  never by client-supplied ids → no cross-server reads/writes even with tampered requests (covered by tests).
* **Database guard:** gameplay tables reference memberships through a composite FK `(membership_id, server_id)`, so the
  DB itself rejects a card owned by a membership of another server (covered by a test).
* Card instance ids come from a single global sequence (`card_instance_seq`) → unique across all servers, so a future
  merge never renumbers cards.
* Opening PS-2: `POST /api/admin/servers {id:2, code:"PS-2"}` (owner/admin, audited). Maintenance/registration toggles
  are immediate (gameplay gate cache ≤ 5 s).

### Server merge (designed, not executed)

Merges are never automatic. Procedure (to be implemented as an audited admin tool, Phase 7):

1. Owner approval recorded in `admin_audit_logs`; announce to players via mailbox.
2. Set both servers to maintenance; full backup + verified restore test.
3. **Dry-run** in a restored copy: detect collisions (nicknames, guild names, accounts present on both servers).
4. Policy for accounts with progress on both servers — **requires owner approval before any merge**. The schema allows
   keeping both profiles (`server_memberships.status = 'merged'`, `origin_server_id` preserved) instead of summing or
   overwriting assets.
5. Move rows by updating `server_id` (card ids stay unchanged; `card_ownership_history` gets a `server_merge` entry).
6. Reconcile: per-membership ledger sums must equal wallet balances; card counts before/after must match.
7. Any failed check → rollback (restore backup). Source server → status `merged`, `merged_into_server_id` set.

## Economy

See [economy.md](economy.md). Two currencies, `RAINBOW_STAR` (premium) and `MINI_GREEN_BEAN` (gameplay), per membership,
each with an append-only ledger.

## Idempotency & anti-duplication

* Mutations that create value (`/packs/open`, `/packs/purchase`, `/cards/{id}/evolve`, `/battles/pve`) require an
  `Idempotency-Key`. The key row is inserted in the **same transaction** as the business change: retries return the
  stored response, concurrent duplicates block on the unique key, failures leave no record.
* Every balance change inserts a ledger row first; `(membership, currency, reason, ref_id)` is unique, so the same
  business reference (pack opening, battle, evolution of a card from a form, IAP purchase) can never apply twice.
* Balances change through a single `UPDATE … SET balance = balance + Δ WHERE balance + Δ >= 0` plus a `CHECK (balance >= 0)`.
* Battle rounds: `(battle_id, round)` is a primary key; stale round submissions replay the stored result.

## Battle

Simultaneous planning, secret until resolved; resolution order: priority desc → SPD desc → seeded coin flip.
Energy: start 3, +3 per round, max 10 (configurable). Frontline protects backline unless taunt. Round limit 20,
tiebreak: surviving cards → remaining HP % → draw. Seed comes from the server CSPRNG and is never sent to clients.
The PvE AI uses the same legal-action validation and only its own energy.
