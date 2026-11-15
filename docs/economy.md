# Economy

## Currencies

| Id | Name (vi) | Type | Sources | Sinks (current) |
|---|---|---|---|---|
| `RAINBOW_STAR` | Sao Ngũ Sắc | Premium | Verified store purchase, events, giftcodes, audited admin grants | None yet; cosmetic sinks are planned (see the open decision below) |
| `MINI_GREEN_BEAN` | Đậu Xanh Mini | Gameplay | Starter grant, PvE, missions, achievements, events, login rewards | Booster packs, evolution |

"Pet Coin" from the original brief is fully replaced by these two currencies.

* Wallets are per **server membership** (`economy_wallets`, PK `(membership_id, currency)`, `CHECK balance >= 0`,
  `CHECK currency IN (...)`), so balances are separated by Global server.
* Every change writes `economy_ledger` (delta, balance_after, reason, ref_id, actor) — unique per business reference.
* Items (essences) and pack inventory use the same pattern (`inventory_items`, `pack_inventory`, `item_ledger`).
* The client never sends amounts; it sends intents and renders the balances returned by the server.

## Real-money purchases (Rainbow Star)

`POST /api/iap/verify {platform, productId, purchaseToken}`:

1. The backend verifies the token with the store (Google Play Developer API `purchases.products.get`, service account
   from a mounted secret). Pending/cancelled/consumed purchases are rejected.
2. `iap_purchases` stores the SHA-256 of the token, **unique per platform** → a token can grant at most once; replays
   from another account are refused and audited.
3. The grant is a ledger entry referencing the purchase id; then the purchase is consumed with the store.

Not live yet: it needs Play Console products and a service account, plus the Unity IAP client integration (Microsoft
Store for Windows is a separate decision). The shop UI says purchases are unavailable until then.

### ⚠ Owner decision needed

The brief says *"no real-money purchase of combat advantage in the first phase"*. If Rainbow Star could buy booster
packs, that would be pay-for-power (cards have stats). **Current default:** packs and evolution are priced only in
Mini Green Bean, and Rainbow Star has no gameplay sink. Please confirm what Rainbow Star may buy (e.g. cosmetics, card
finishes, avatar frames, a battle pass with cosmetic rewards only).

## Pack pricing and odds (baseline, configurable)

| Pack | Price | Pool | Notes |
|---|---|---|---|
| Basic | 100 MINI_GREEN_BEAN | Stage 1 + standalone | Card 5 uses its own table (N 15%, R 35%, SR 26%, SSR 14%, …) |
| Premium | 300 MINI_GREEN_BEAN | Stages 1–2 + standalone | Card 5 cannot be N |
| Legendary | 1000 (disabled) | All stages | |

Odds are published at `GET /api/packs/{id}/odds` and in the shop. Configurations are versioned (`pack_types.version`);
every opening records the version it used.

## Faucets and anti-farm

* Starter: 500 MINI_GREEN_BEAN + 3 Basic packs, granted once per membership (ledger ref `membership:{id}`).
* PvE: rewards per stage, first-clear bonus once, max 10 rewarded clears per stage per day (configurable).
