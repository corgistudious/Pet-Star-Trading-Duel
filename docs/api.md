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

Real-time PvP (friend duel / matchmaking) will use a WebSocket channel (`/ws/battle`) with the same engine and round protocol.
