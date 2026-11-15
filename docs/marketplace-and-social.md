# Card economy, marketplace & social (expansion)

All features are per Global server, server-authoritative, and in-game only. There is **no** real-money sale,
withdrawal, cash-out or currency↔fiat/currency↔currency exchange anywhere in the system (enforced by a test that
scans every API route). Real-money IAP code exists but is not mapped (`Iap:Enabled=false`) until the owner approves a
separate payment system.

## Card serial
* 12 characters A–Z0–9 from the OS CSPRNG (~62 bits). Reserved first in `card_serial_registry` (PK); a collision
  simply retries. The registry is permanent, so serials are never reused.
* `card_instances.serial` has a UNIQUE index + format CHECK. It never changes (evolution, training, trades).
* It is a collectible identifier only — no endpoint accepts a serial to transfer ownership. Internal `id` stays the key.
* Shown bottom-right in the card footer (Unity `CardView`) and on the Passport.

## Card Passport
`card_passports` is written once at creation (serial, issuing server, origin, form/rarity/quality/finish at issue,
initial reference values). Passport views add live data: owner nickname, transfer count, evolution & upgrade history,
listing/auction history, completed trades (counterparties hidden), reference values, lock status.
Owner view (`/api/cards/{id}/passport`) also returns the internal id and origin reference; public view
(`/api/passport/{serial}`, same server only) hides them.

## Valuation (`database/seed/valuation.json`)
* **Initial Reference Value** per currency (independent tables, no exchange rate):
  `base[rarity] × quality × finish × stage × origin × scarcity(supply)`; scarcity uses the real number of cards of
  that form+rarity on the server (clamped 0.8–2.0). Serial numbers never affect value.
* **Current Estimated Value**: completed trades only (listings are never evidence), last 30 days, prices normalized by
  quality×finish, 20% trimmed mean, blended with the initial value by sample size; requires ≥3 distinct traders;
  confidence `initial_only/low/medium/high`. Daily snapshots → price history charts.
* **Listing Price**: set by the seller within configured limits; prices far from the reference require confirmation.
* Values are informational. Nothing is ever paid out according to a reference value.

## Marketplace
* Listing (idempotent): card locked `FOR UPDATE`, must be owned, not player-locked, not busy → `busy_state =
  listing:{id}`; partial unique index guarantees one active listing per card. Busy cards cannot be evolved, trained,
  listed again, auctioned or used to start battles.
* Buying (idempotent, one transaction): lock listing + card, check status/expiry/self-purchase/blocks, debit buyer,
  credit seller `price − fee`, fee (default 3%, `market.json`) to `economy_system_ledger` (sink, not a person),
  transfer card (clears decks/showcase/albums of the seller), write `market_transactions`, `market_price_history`,
  ownership history, notifications, achievements. Ledger refs make double charging/crediting impossible.
* Expiry is handled by `MarketMaintenanceService` (server time).
* Search: name, serial, rarity, quality, finish, element, stage, currency, price range; sort price/newest/popular/
  rarity; sections new, rare, recommended (Codex gaps + wishlist), wishlist matches, mine; purchase/sales history.

## Auction house
* Bids hold the full amount (`reserved_balances`, ledger `auction_hold`); being outbid releases it (`auction_release`).
  You can never bid more than your spendable balance. Seller cannot bid; bids from accounts linked to the seller
  (shared public IP) are flagged for review.
* Minimum increment 5%, optional buy-now, anti-sniping extension (60 s, max 10), server time only.
* Settlement exactly once (auction row lock + `auction_settlements` PK): card → winner, hold captured, seller paid net,
  fee to sink. No bids → card released.

## Integrity & fraud signals
Round trips, repeated pair trading, linked accounts, and price anomalies mark a trade `suspicious`: it still completes
(nobody loses assets) but is excluded from valuation and queued in `economy_audit_events`. GMs can fraud-lock a card
(`busy_state = fraud_review`). Ledgers are append-only; corrections are owner-only compensating entries with audit.
Admin ownership audit checks: one owner per card consistent with history, no duplicate serials, no cross-server owners.

## Pet Feed & social
Posts: text, card showcase (1–5 owned cards, server-side snapshot), pack opening, evolution, duel, achievement,
album, internal share. Card posts always display the **current** owner of the live card (and "now owned by" when it
changed). Likes, comments, saves, follows, friends, blocks (hide both ways), mutes, reports. Content guard: length,
profanity list (en/vi), links (also blocks off-platform trade offers), spam patterns; rate limits for posts/comments;
duplicate-post window. Moderators: reports queue, hide/feature posts, restrictions (social/market/all), all logged.

## Progression & rewards
Skill training (levels 1–5, scrolls + beans, capped, server-validated), team synergies (3+ same element: +ATK;
4+ elements: +HP; guardian front + support back: +DEF). Achievements and discovery rewards (first pack/rare/evolution/
win/sale/purchase/auction win/post, Codex milestones, family/element completion) are granted once per membership via
ledger refs; Codex discovery is permanent, so trading cards back and forth cannot farm rewards.
Ranked seasons: asynchronous PvP vs the defender's saved deck (server AI, same rules), Elo (K=32, defender half),
matchmaking by rating + team power, max 3 rated games per opponent per day, leaderboard. Friend duels are unrated.

## Not yet implemented (next milestones)
Guilds (+ guild feed/announcements), live real-time PvP over WebSocket, chat, giftcodes, events, admin web UI.
