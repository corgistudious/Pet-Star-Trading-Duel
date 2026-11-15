# Database (PostgreSQL 16)

Schema is managed by EF Core migrations (`backend/src/PetStar.Api/Data/Migrations`). Migrations are additive; player
data is never dropped on upgrade. Naming: snake_case.

## Implemented tables (migration `InitialCreate`)

| Area | Table | Key constraints |
|---|---|---|
| Identity | `accounts` | id from `account_uid_seq` (start 100000); unique `email_normalized` |
| | `account_roles` | PK (account_id, role); role ∈ owner/admin/gm/moderator |
| | `sessions` | unique `refresh_token_hash`; revoked_at/reason |
| | `email_tokens` | unique `token_hash`; single use (`used_at`) |
| Servers | `game_servers` | unique `code`; status check |
| | `server_memberships` | unique (account_id, server_id); unique (server_id, nickname); alternate key (id, server_id) |
| Catalog | `pet_families`, `pet_forms`, `pet_skills` | synced from `database/seed`; unique (family_id, stage) |
| Cards | `card_instances` | id from global `card_instance_seq`; FK (owner_membership_id, server_id) → membership; checks on level/rarity/quality/finish |
| | `card_ownership_history` | every ownership change (pack_open, trade, merge…) |
| | `card_evolution_history` | from/to form, cost |
| | `codex_entries` | PK (membership_id, form_id) — kept even after the card leaves |
| | `card_showcase` | PK (membership_id, slot 0–4); unique card_id; cascade on card removal |
| Decks | `decks`, `deck_slots` | one default per membership (partial unique index); slot positions 0–4; unique (deck, card) |
| Packs | `pack_types` | versioned JSON config |
| | `pack_inventory` | PK (membership, pack); `quantity >= 0` |
| | `pack_openings` | unique (membership_id, idempotency_key); config version |
| | `pack_opening_results` | PK (opening, slot 1–5); unique card_id |
| Economy | `economy_wallets` | PK (membership, currency); `balance >= 0`; currency check |
| | `economy_ledger` | unique (membership, currency, reason, ref_id) |
| | `inventory_items`, `item_ledger` | same pattern for essences and packs |
| | `iap_purchases` | unique (platform, purchase_token_hash) |
| Battles | `battles` | state JSON, secret seed, status |
| | `battle_actions` | PK (battle_id, round): both sides' actions + resolved events |
| | `battle_results` | PK battle_id; rewards JSON (granted once) |
| | `pve_progress` | PK (membership, stage); daily rewarded-clear counter |
| Infra | `idempotency_records` | PK (account_id, key); request hash + stored response |
| | `admin_audit_logs` | actor, action, target, server, data, ip, time |

## Planned tables (Phase 6–7, same conventions: membership-scoped, server_id + composite FK)

`friendships`, `friend_requests`, `guilds` (unique (server_id, name)), `guild_members`, `trades`, `trade_items`
(card locked via `card_instances.busy_state = 'trade:{id}'` while a trade is open), `missions`, `mission_progress`,
`achievements`, `achievement_progress`, `rankings` (season snapshots), `mailbox_messages` (+ attachments, claim once
via ledger ref), `giftcodes`, `giftcode_redemptions` (unique (giftcode, account) and per-server scope), `events`.

## Transaction boundaries

| Operation | Transaction contains |
|---|---|
| Join server | membership insert + starter wallet/pack grants |
| Purchase pack | idempotency record + currency debit + pack credit |
| Open pack | idempotency record + pack debit + 5 card inserts + results + ownership history + codex |
| Evolve | idempotency record + `SELECT … FOR UPDATE` card + essence debit + bean debit + form change + history + codex |
| Battle round | `SELECT … FOR UPDATE` battle + round row + state update (+ rewards, EXP, progress on finish) |
| IAP | purchase row (unique token) + currency credit + audit |
| Trade (planned) | lock both cards FOR UPDATE ordered by id + owner swap + history, atomically |

## Backups

`deployment/backup/backup.sh` (pg_dump custom format, sha256, retention) runs every 6 h in the `backup` container.
`deployment/scripts/restore-test.sh` restores the newest dump into a scratch DB and checks row counts and
wallet/ledger reconciliation.
