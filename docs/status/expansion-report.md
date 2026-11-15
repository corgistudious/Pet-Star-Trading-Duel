# Expansion report — full catalog + card economy, marketplace & social (2026-11-15)

## Delivered
* **Content:** 80 evolution lines / 200 forms (50×3, 20×2, 10 standalone; 10 lines per element), unique names,
  mechanically distinct skill kits (330 skills), role/stage-based stats, en/vi lore. **All 200 artworks generated**
  (gpt-image-1.5, transparent PNG) and synced to Unity (640 px). Zipmunk line recolored teal/indigo (IP distance).
* **Phase A:** 12-char CSPRNG serials + permanent registry, immutable Card Passport, initial/estimated valuation with
  confidence, price history snapshots, both currencies with ledgers per server; real-money IAP disabled.
* **Phase B:** marketplace (listing locks, atomic purchase, 3% fee to system sink, search/filters/sections, history).
* **Phase C:** Pet Feed (card snapshots with live owner), likes/comments/saves/shares, follow/friends/block/mute,
  reports, content guard, rate limits, moderation tools and restrictions.
* **Phase D:** auction house (held funds, outbid refunds, anti-sniping, single settlement), wishlist + alerts,
  price history (trades only), fraud signals (wash/round-trip, pair trading, linked accounts, anomalies).
* **Phase E:** notifications (deduped), mailbox (claim once), achievements & discovery rewards (once per member),
  albums (system + custom), skill training + team synergies, async ranked seasons (Elo, leaderboard), friend duels,
  admin economy/moderation APIs with RBAC (owner-only compensating adjustments), ownership audit.
* **Unity (uncompiled):** Game Hub bottom navigation; Market home/search/listing/sell/history/wishlist, Auction house/
  detail with server-time countdown, Price history chart (UI geometry), Pet Feed/Create post/Post detail, Friends,
  Player profile/showcase, Card Passport certificate, Notification Center, Mailbox, Collection hub, Albums,
  Achievements, Battle hub, Ranked, Battle history, Moderation reports; serial on card footer; Card Details actions
  (passport, sell, share, train). Localization: 1,093 keys en/vi, coverage check passes.

## Tests (run in this environment)
| Suite | Result |
|---|---|
| Rules unit tests (incl. 200-form catalog structure, unique skill kits) | 46 / 46 passed |
| API integration & security (incl. 1M-serial uniqueness, serial stable after evolution/trade, double-buy, insufficient funds, own-listing purchase, foreign/locked card listing, cross-server isolation, listing ≠ price evidence, wash trade exclusion, auction holds/refunds/single settlement, concurrent bidding, feed owner correctness, block/report/moderation, private data, mailbox once, training cap, ranked, no withdrawal/exchange routes, owner-only adjustments, ownership audit) | 51 / 51 passed |
| Load scenario (40 users: packs, listings, 60 concurrent buys, 100 concurrent bids, 400 likes, 10 concurrent PvE battles; invariants: one sale per listing, ledger = wallets, no held funds left, no duplicate serials, like counters exact) | passed |
| Localization coverage | passed |
| Unity compile / device tests | **not run** (needs Unity license in CI) |

Load-test latency on this 4-core sandbox (p50/p95 ms): pack open 980/1010, buy 510/640, bid 200/250, like 460/555,
battle round 16/107. A first run exposed connection-pool exhaustion (PostgreSQL max 100); fixed with an explicit
pool cap (`Maximum Pool Size`, compose `DB_POOL_SIZE`); PgBouncer recommended for production scale.
Pack opening under burst is the slowest path (serial reservation + passport + achievement checks per card) — an
optimization candidate (batch serial reservation, async achievement evaluation).
One rules test failed once while PostgreSQL was down after a sandbox restart and could not be reproduced (3 clean reruns).

## Asset budget (actual)
Pets **$9.44 / $15** (200 artworks incl. 10 prototypes and 2 recolors), UI **$1.23 / $5**, total **$10.66 / $20**
(+$0.05 open reservation from an interrupted call, still counted by the guard). Remaining: $5.56 pets, $3.77 UI.

## Review notes
* **Twinkbun / Starhare / Constellapin** (cream-yellow long-eared bunnies) — suggest an IP-similarity review; a recolor
  costs ~$0.14.
* 6 artworks touch the canvas edge (flames/horns); the Unity import trims and re-pads them.
* Duel board backgrounds still show a 4+3 slot layout (regeneration ~$0.13 if desired).

## Not yet done
Guilds + guild feed, real-time PvP (WebSocket), chat, giftcodes, events, admin web UI, Unity build/device QA,
staging deployment (awaits owner approval and secrets).
