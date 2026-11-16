# Card system: 9 packs, 15 Divine Pets, card progression

Server-authoritative. Odds, prices, pools and conditions live in `database/seed/packs.json`, and admins can change them, with every version kept in `pack_type_versions`. Every pack opening records the odds version it used. The CSPRNG runs on the server only. The stat formulas live in `shared-contracts/Runtime/Rules/Progression.cs`, and the server recomputes stats for every battle.

## The 9 packs

| Pack (id) | VI | Price | Pool | Conditions |
|---|---|---|---|---|
| Standard Pack (`basic_pack`) | Gói Tiêu Chuẩn | 100 Mini Green Beans | stages 1 | — |
| Advanced Pack (`premium_pack`) | Gói Cao Cấp | 300 Mini Green Beans | stages 1/2 | — |
| Transcendent Pack (`transcendent_pack`) | Gói Siêu Việt | 800 Mini Green Beans | stages 1/2/3 | — |
| Epic Pack (`epic_pack`) | Gói Sử Thi | 20 Rainbow Stars | stages 1/2/3 | — |
| Legendary Pack (`legendary_pack`) | Gói Huyền Thoại | 50 Rainbow Stars | stages 1/2/3 | — |
| Mythic Pack (`mythic_pack`) | Gói Thần Thoại | 120 Rainbow Stars | stages 1/2/3 | — |
| Premium Pack (`divine_premium_pack`) | Gói Premium | 200 Rainbow Stars | stages 1/2/3 + 5 Divine | Level 30+, VIP 3+ |
| Limited Pack (`divine_limited_pack`) | Gói Limited | 250 Rainbow Stars | stages 1/2/3 + 5 Divine | Level 40+, VIP 4+, limited event |
| Star Pack (`divine_star_pack`) | Gói Star | 500 Rainbow Stars | stages 1/2/3 + 5 Divine | Level 60+, VIP 7+, admin invitation |

The ids `basic_pack`, `premium_pack` and `legendary_pack` are kept for existing inventories and history. They are now Standard, Advanced and Legendary. Each pack holds 5 cards.

## Official odds: per card (every one of the 5 slots uses the same table, so there is no hidden guarantee)

| Pack | N | R | SR | SSR | UR | EX | DV |
|---|---|---|---|---|---|---|---|
| Standard Pack | 60% | 28% | 9% | 2.5% | 0.5% | 0% | 0% |
| Advanced Pack | 35% | 38% | 19% | 6% | 1.8% | 0.2% | 0% |
| Transcendent Pack | 15% | 35% | 32% | 13% | 4.5% | 0.5% | 0% |
| Epic Pack | 5% | 25% | 40% | 22% | 7% | 1% | 0% |
| Legendary Pack | 0% | 15% | 35% | 34% | 14% | 2% | 0% |
| Mythic Pack | 0% | 5% | 25% | 42% | 24% | 4% | 0% |
| Premium Pack | 0% | 5% | 25% | 42% | 24% | 3.8% | 0.2% |
| Limited Pack | 0% | 5% | 24% | 42% | 24.85% | 4% | 0.15% |
| Star Pack | 0% | 4% | 22% | 42% | 26.9% | 5% | 0.1% |

**Per pack:** the chance of at least one card of that tier among the 5 cards, 1 − (1 − p)^5 (also served by `GET /api/packs/odds`).

| Pack | N | R | SR | SSR | UR | EX | DV |
|---|---|---|---|---|---|---|---|
| Standard Pack | 98.98% | 80.65% | 37.6% | 11.89% | 2.475% | 0% | 0% |
| Advanced Pack | 88.4% | 90.84% | 65.13% | 26.61% | 8.682% | 0.996% | 0% |
| Transcendent Pack | 55.63% | 88.4% | 85.46% | 50.16% | 20.56% | 2.475% | 0% |
| Epic Pack | 22.62% | 76.27% | 92.22% | 71.13% | 30.43% | 4.901% | 0% |
| Legendary Pack | 0% | 55.63% | 88.4% | 87.48% | 52.96% | 9.608% | 0% |
| Mythic Pack | 0% | 22.62% | 76.27% | 93.44% | 74.64% | 18.46% | 0% |
| Premium Pack | 0% | 22.62% | 76.27% | 93.44% | 74.64% | 17.61% | 0.996% |
| Limited Pack | 0% | 22.62% | 74.64% | 93.44% | 76.03% | 18.46% | 0.7478% |
| Star Pack | 0% | 18.46% | 71.13% | 93.44% | 79.13% | 22.62% | 0.499% |

The special-pet rate is the group total for 5 pets, split evenly: Premium 0.20 % per card (0.04 % per pet), Limited 0.15 % (0.03 %), Star 0.10 % (0.02 %). The rarities LR, MR, GR and Secret remain valid on existing cards but are 0 % in these packs, because the owner's table uses N/R/SR/SSR/UR/EX. Quality and finish (cosmetic only) keep the existing per-pack tables. The rates are tested statistically on 300,000 slots per pack, and the divine group and per-pet rates on 1,250,000 slots per special pack (5σ bounds).

## The 15 Divine Pets (Thần Pet Card, rarity `DV`, created at 10★)

| Set | Id | EN | VI | Element | Role | Skill / Ultimate |
|---|---|---|---|---|---|---|
| premium | `pet_dv_aurelion` | Aurelion | Kim Sư Aurelion | light | guardian | Khiên Kim Cương / Gầm Vương Giả |
| premium | `pet_dv_diamaris` | Diamaris | Ngọc Hồ Diamaris | water | mage | Sóng Lăng Kính / Thác Ngọc Rực Rỡ |
| premium | `pet_dv_regalwing` | Regalwing | Phượng Kim Regalwing | fire | striker | Vuốt Mạ Vàng / Hỏa Diễm Đế Vương |
| premium | `pet_dv_opulynx` | Opulynx | Linh Miêu Hoàng Ngọc | lightning | controller | Tia Hoàng Ngọc / Bão Mạ Vàng |
| premium | `pet_dv_jadeheart` | Jadeheart | Rùa Ngọc Bích | nature | support | Ngọc Dược / Phúc Lành Ngọc Lục |
| limited | `pet_dv_lanternix` | Lanternix | Hồ Ly Đèn Lồng | fire | support | Ánh Đèn Hội / Ngàn Đèn Lồng |
| limited | `pet_dv_frostbloom` | Frostbloom | Tuần Lộc Hoa Tuyết | water | guardian | Màn Tuyết / Thành Đông |
| limited | `pet_dv_sakuranne` | Sakuranne | Thỏ Anh Đào | nature | mage | Mưa Cánh Hoa / Bão Ngắm Hoa |
| limited | `pet_dv_galekite` | Galekite | Diều Gió Hạ | wind | striker | Lưỡi Gió / Giông Hạ |
| limited | `pet_dv_harvestmoon` | Harvestmoon | Cú Trăng Thu | dark | controller | Ánh Nhìn Trăng / Nguyệt Thực Mùa Gặt |
| star | `pet_dv_nebulon` | Nebulon | Long Tinh Vân | dark | striker | Nanh Tinh Vân / Lao Siêu Tân Tinh |
| star | `pet_dv_astravel` | Astravel | Kỳ Lân Sao Băng | light | support | Sao Chổi Chữa Lành / Ngôi Sao Ước Nguyện |
| star | `pet_dv_quasark` | Quasark | Cá Voi Ngân Hà | water | guardian | Khiên Quỹ Đạo / Thủy Triều Ngân Hà |
| star | `pet_dv_solflare` | Solflare | Hỏa Tinh Solflare | fire | mage | Bùng Nổ Vành Nhật / Bão Mặt Trời |
| star | `pet_dv_orbiwisp` | Orbiwisp | Linh Thể Quỹ Đạo | lightning | controller | Tia Vòng Sao / Xung Hấp Dẫn |

They are original designs, and their skills use only the existing battle effects. Each has its own serial and passport; the passport shows that it came from the pack. They can be traded on the Market for Rainbow Stars, and are never exchangeable for real money. 10★ is not maxed: level, enhancement, skill, breakthrough and awakening still apply. Balance tests show that a maxed divine set beats the best maxed regular team about 50 % of the time (bounds 25–80 %), and that a fresh 10★ divine set loses to a developed regular team. The best divine pet of each role is at most +12 % power above the best regular pet of that role.

## Progression (all values capped, server-side, idempotent, ledgered, logged in `card_progress_events`)

| System | Range | Cost / gates | Effect |
|---|---|---|---|
| Stars | 1–10 | owner table: 100…4,000 beans + 5…200 Pet Shards (total 11,550 / 575) | +4 % HP/ATK/DEF per star (+36 % at 10★) |
| Enhancement | +0–+20 | 100…3,000 beans + 1…20 Enhance Stones; +6 needs level 10, +11 needs level 25, +16 needs level 40 | +1 / 1.5 / 2 / 2.5 % per level by tier (+35 % at +20). It always succeeds and never destroys the card. |
| Breakthrough | 0–4 | needs the current level cap, 3/5/7/9★, 5/10/20/40 cores and 2k/5k/10k/20k beans | level cap 50 → 55 → 60 → 65 → 70 |
| Awakening | once | 10★, breakthrough 2, level 60, 10 crystals, 30,000 beans | +6 % stats, +5 % skill power, golden visual |
| Skill | 1–10 | levels 2–5 keep the existing costs; 6–10 need breakthrough 1–4, 12–30 scrolls and 3k–12k beans | +6 % per level up to 5, +3 % per level for 6–10 (real battle damage, heal and shield) |
| Engravings | 3 slots | slots open at level 10 / level 30 / breakthrough 1; at most 2 of one type; crafted for 10 dust + 500 beans; levels 1–5 | ATK/DEF/HP +1…5 % each (cap 12 %), SPD +0.6…3 % (cap 6 %), CRIT +0.5…2.5 points (cap 5) |
| Reset | enhancement, skill | — | refunds 50 % of the materials spent. Stars, breakthroughs, awakening, serial and history are never reset. |

Every request carries the value the player saw (`from`). A double tap or a stale screen gets `stale_state`. A retry with the same `Idempotency-Key` replays the stored result. The card row is locked `FOR UPDATE`. Listed, auctioned or under-review cards cannot be changed. **Trading** keeps stars, enhancement, breakthrough, awakening, skill level, level, serial, history and the divine origin. Equipped engravings travel with the card. Market trades move Rainbow Stars between players and only the fee leaves the economy; no currency is created.

**Material sources (baseline, to be economy-checked):** PvE stages (Pet Shards, Enhance Stones, Engraving Dust; the boss also drops a Breakthrough Core), daily and weekly missions, and owner grants (audited). One 10★ card is about 10 days of daily PvE.

## VIP

VIP points equal the Rainbow Stars spent on system purchases (packs). Admin funding and player-to-player trades never count. The thresholds for VIP 0–10 are 0 / 100 / 300 / 800 / 1,500 / 3,000 / 6,000 / 10,000 / 20,000 / 40,000 / 80,000. VIP is tracked per server. No real money is involved in this phase; real-money purchases stay disabled.

## Limited events and Star invitations

- **Limited:** admins schedule an event (window, total limit, per-player limit, server or all servers). Purchases are allowed only while it runs. Per-player limits are serialised, and total sales are consumed with a guarded update. Ending an event stops sales; cards and packs already owned are never taken back.
- **Star:** admins issue an invitation (id, issuer, recipient, time issued, expiry, number of packs, status, revoke history). The server checks and consumes it when the pack is bought. An invitation counts only on its own server.

## Admin (`/api/admin/cardsys/*`; the backend enforces the roles and every change goes to the audit log)

| Role | Allowed |
|---|---|
| gm | read: packs and odds history, openings, observed vs configured rates, Divine Pets issued, serial progression, invitations, Rainbow Star flows, anomalies, materials, config log |
| admin | + pack price and sale status (new version, reason required), limited events, Star invitations (issue and revoke) |
| owner | + odds tables and divine pools (validated: must sum to 100 % and the pool must match), material grants, VIP point grants |

Nobody can grant roles or mint cards through these endpoints.

## Database (migration `20261116222847_CardSystemExpansion`, additive)

- New columns `card_instances.stars` (default 1), `enhance_level`, `breakthrough` and `awakened` (defaults 0 / false). Existing cards keep exactly their stats; this is tested.
- Check constraints widened: level up to 70, skill up to 10, rarity also allows `DV`.
- New tables: `membership_vip`, `pack_type_versions`, `pack_purchases`, `limited_events`, `star_invitations` and `card_engravings`. Progression history reuses `card_progress_events`, and materials reuse `inventory_items` / `item_ledger`.
- **Rollback:** `deploy.sh` takes a backup before migrating. Down migration (tested on an empty expansion) works only before any DV card or level above 50 exists. Otherwise restore the backup.
- **Production note:** with `SEED_SYNC_PACK_TYPES=false`, the 6 new packs are inserted, but `basic_pack`, `premium_pack` and `legendary_pack` keep their old odds. Run one deploy with `SEED_SYNC_PACK_TYPES=true` (the old version stays in history) or update them through the owner odds endpoint.

