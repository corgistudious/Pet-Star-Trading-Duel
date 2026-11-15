# REST API (v0.1)

JSON, camelCase. Errors: `{ "code": "insufficient_mini_green_bean", "detail": null, "traceId": "…" }` — the client maps
`code` to localized text (`error.{code}`). Mutations marked **(idem)** require header `Idempotency-Key` (8–64 chars).
Game routes need a token from `POST /api/servers/{id}/enter` and an online server.

| Method | Path | Notes |
|---|---|---|
| POST | /api/auth/register | email, password, displayName, language → tokens (UID ≥ 100000) |
| POST | /api/auth/login | lockout after 5 failures |
| POST | /api/auth/refresh | rotating refresh token; header `X-Server-Id` keeps server context |
| POST | /api/auth/verify-email · /forgot-password · /reset-password | single-use tokens |
| POST | /api/auth/resend-verification · /logout | |
| GET/DELETE | /api/auth/sessions[/{id}] | list / revoke devices |
| GET/PATCH | /api/me | profile, language, avatar, frame, title |
| GET | /api/servers | servers + my progress per server |
| POST | /api/servers/{id}/enter | creates membership + starter grants on first join |
| GET | /api/player | level, both currency balances, packs, items, counts |
| GET | /api/economy/ledger | balance history |
| GET | /api/packs | shop |
| GET | /api/packs/{id}/odds | public odds disclosure (no auth) |
| POST | /api/packs/purchase **(idem)** | spend MINI_GREEN_BEAN |
| POST | /api/packs/open **(idem)** | server RNG → exactly 5 cards, stored before response |
| GET | /api/packs/openings[?unacknowledged=true] · /{id} | recovery after disconnect |
| POST | /api/packs/openings/{id}/ack | |
| GET | /api/cards | Pet Home: filters rarity/element/quality/stage/favorite, sort, paging, copies per form |
| GET | /api/cards/{id} | |
| POST | /api/cards/{id}/favorite · /lock | |
| GET | /api/cards/{id}/evolution-preview | requirements + next form stats |
| POST | /api/cards/{id}/evolve **(idem)** | same card id, materials once |
| GET | /api/codex | discovered forms, completion |
| GET/POST/PUT/DELETE | /api/decks[/{id}] · POST /api/decks/{id}/default | 5 cards, 3 front + 2 back |
| GET | /api/pve/stages | unlock state, daily reward allowance |
| POST | /api/battles/pve **(idem)** | start vs AI with default (or chosen) deck |
| GET | /api/battles/{id} | |
| POST | /api/battles/{id}/rounds | `{round, actions[]}`; stale round = replay |
| POST | /api/battles/{id}/surrender | |
| GET | /api/iap/products · POST /api/iap/verify | Rainbow Star store purchases (server verification) |
| GET/POST/PATCH | /api/admin/servers[/{id}] | owner/admin; audited |
| GET | /api/admin/audit | |
| GET | /health/live · /health/ready · /api/version | |

| GET | /api/cards/{id}/passport · /api/passport/{serial} | owner / public Card Passport |
| GET | /api/valuation/history | snapshots + completed trades |
| GET/POST | /api/cards/{id}/training · /train **(idem)** | skill upgrade |
| GET | /api/market/config · /sell-quote | fee, limits, quote |
| GET/POST | /api/market/listings **(idem)** · /{id} · /{id}/cancel | listings & search |
| POST | /api/market/buy **(idem)** | atomic purchase |
| GET | /api/market/history | purchases / sales |
| GET/POST/DELETE | /api/market/wishlist | wishlist + alerts |
| GET/POST | /api/auctions **(idem)** · /{id} · /bid **(idem)** · /{id}/cancel | auction house |
| GET/POST/DELETE | /api/feed, /api/feed/posts/... | feed, likes, saves, comments |
| GET/POST/DELETE/PUT | /api/players/..., /api/friends/..., /api/reports, /api/profile/showcase | social |
| GET/POST | /api/notifications, /api/mailbox/{id}/claim | notification center, mailbox |
| GET/POST/PUT/DELETE | /api/achievements, /api/collections | achievements, albums |
| GET/POST | /api/ranked, /ranked/leaderboard, /ranked/match **(idem)**, /api/duels/friend, /api/battles/history | ranked & friend duels |
| * | /api/admin/reports, /moderation, /restrictions (moderator+); /cards/serial, /cards/ownership-audit, /market, /auctions, /economy/suspicious, /cards/{id}/fraud-lock (gm+); /ledger, /economy/overview, /market/stats (admin+); /economy/adjust, /mail (owner) | admin & moderation |

| GET/POST | /api/guilds, /guilds/mine, /guilds/{id}/join, /guilds/requests/{id}/{accept\|reject}, /guilds/leave, /guilds/members/{uid}/role\|kick, /guilds/announcements, /guilds/feed | guilds (leader > officer > member) + guild-only feed |
| GET/POST | /api/chat/{world\|guild\|dm:{uid}} | 8 msgs / 30 s, 1/s, no duplicates, filter, mute/block; DMs friends-only |
| POST | /api/realtime/ticket → `GET /ws?ticket=` | single-use 60 s ticket; browser Origin must be in `Cors:AllowedOrigins` |
| POST/GET | /api/pvp/challenges, /pvp/challenges/{id}/{accept\|decline\|cancel}, POST/DELETE /api/pvp/queue | live PvP lobby & matchmaking |
| GET/POST | /api/pvp/battles/{id}, /rounds, /surrender | sealed per-round plans, 30 s deadline, forfeit after 3 missed rounds |
| GET/POST | /api/missions, /missions/{id}/claim | daily (00:00 UTC) / weekly (Mon 00:00 UTC); progress derived from authoritative records; one claim per period (PK + ledger ref) |
| POST | /api/giftcodes/redeem | rate-limited; once per account per server |
| GET/POST | /api/rewards/login, /rewards/login/claim, /api/events, /events/{id}/claim | 7-day login cycle, events (ledger-paid) |
| * | /api/admin/giftcodes, /events, /accounts (admin+), /accounts/{uid}/roles (owner), /chat/{id}/hide (moderator+) | live-ops admin |

## Realtime push (WebSocket)

`POST /api/realtime/ticket` (authenticated, server-bound token) returns `{ ticket, expiresIn: 60 }`; connect to
`wss://<api>/ws?ticket=<ticket>`. Messages are JSON `{ type, data, at }`: `hello`, `notification`, `chat`,
`pvp_start`, `pvp_round` (side-aware battle state), `pvp_opponent_ready`, `pvp_declined`. Send `ping` → `pong`.
Pushes are best effort; clients refetch state after (re)connecting. Multiple API instances share pushes through
Redis pub/sub (`petstar:realtime`).
