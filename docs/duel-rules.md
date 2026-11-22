# Pet Card Tactical Duel: rules and implementation

The Pet Card Tactical Duel replaces the old 5-vs-5 "Basic" battle as the main PvE, PvP and Ranked mode. The classic battle and all of its data are still there under "Classic 5v5 battle".

Every number below lives in `database/seed/duel_cards.json` → `config`. The values can be changed there without code changes. The server, the tests, the Unity client (through `/api/duel/collection`) and the web Preview (through `catalog.json` → `duelRules`) all read the same file.

## 1. One engine, three runtimes

| Where | Code | Role |
|---|---|---|
| Server (authoritative) | `shared-contracts/Runtime/Rules/Duel/DuelEngine.cs`, `DuelAi.cs`, `DuelDecks.cs` | Applies every action. Shuffles. Flips the coin. Runs AI turns and timers. |
| Unity client | Same C# files, compiled into the client | Hints only: legal targets, tribute counts, trap readiness. The server decides. |
| Web Preview | `web-client/src/duel/engine.ts`, `ai.ts` (TypeScript port) | Offline play against the AI. Nothing is sent to a server or saved. |

Both engines use integer math only. Randomness comes from a seeded SplitMix64 whose state is stored inside the duel state.

**Cross-engine check.** `DuelFixtureTests` (C#) plays 8 full AI-vs-AI duels and writes the seeds, the decks and a digest of every log event to `web-client/src/duel/__fixtures__/duel-fixtures.json`. `engine.test.ts` (vitest) replays them and must produce the same events. A second test checks that `catalog.json` → `duelRules` equals the C# rules.

If the two engines drift, the test fails. This was verified by deliberately perturbing one engine. To regenerate the fixtures after an intended rule change:

```bash
PETSTAR_WRITE_DUEL_FIXTURES=1 dotnet test --filter DuelFixture
```

## 2. Decks

- Exactly **40** cards: **10–25 Pets**, the rest Spells and Traps.
- At most **3 copies** of the same card. At most **1 copy** of each Divine Pet form.
- Each Pet is a real owned card instance and may appear only once.
- Spells and Traps are inventory items (`sp_*`, `tr_*`). Every player receives the starter set once, with ledger reference `starter_tactics`.
- The server validates the deck when it is saved **and** when a duel starts. It checks ownership, card locks (listed, in auction, under review), copy limits and Pet counts.
- Error codes: `deck_must_have_40`, `deck_pet_count`, `deck_too_many_copies`, `deck_divine_copies`, `deck_card_not_owned`, `deck_card_busy`, `deck_tactic_not_owned`, …
- Out-of-duel progression is kept: level, stars, breakthrough and skills all feed the Pet stats.
- In Ranked, growth is capped at `rankedGrowthCap` (145% of base stats).

## 3. Setup and turn

- The server shuffles both decks and deals **5** cards to each player.
- A 50/50 coin decides who goes first: **Blue** = you, **Red** = opponent.
- Phases: **Draw → Main → Battle → End**.
- The first player skips the draw on turn 1 and **cannot attack on their first turn**.
- Energy starts at 2, gains +1 per own turn, and is capped at 8. Spells, Traps, skills, ultimates and evolution cost energy.
- **One normal summon per turn.** Stage 1 Pets need no tribute; stage 2 needs 1; stage 3 and Divine Pets need 2.
- A tribute or replacement is **not** a death: no LP is lost.
- Special summons from Spells are limited (`oncePerTurn` / `oncePerDuel`), so summon loops cannot be infinite.
- A Pet summoned this turn cannot attack.
- Field: **5 Pet zones** (3 front, 2 back) and **3 Spell/Trap zones**.
- Front Pets must be defeated before back Pets can be targeted, unless a skill reaches the back row. A taunting Pet must be attacked first.
- The hand limit is **7**. Extra cards are discarded at the End Phase.

## 4. Life, HP and damage

- Each player has **2,500 LP**. Each Pet has its own HP.
- Attacks, skills and Spells damage **Pet HP**, never LP directly.
- Damage is `power% × ATK × ATK / (ATK + DEF) × damageScale / 10000`. Then:
  - element: strong 150%, weak 75%;
  - critical hit: 150%;
  - guard: 50%; weaken: 75%;
  - shields absorb damage first.
- A Pet that reaches 0 HP is destroyed, and its owner loses exactly **500 LP**. This happens once per death.

## 5. Spells, Traps and the reaction chain

There are 20 Spells and 13 Traps; every card has its name and text in Vietnamese and English.

| Kind | Categories (number of cards) |
|---|---|
| Spells | recovery (3), boost (3), tactic (5), elemental (4), summon (2), quick (3) |
| Traps | counter (2), defense (2), control (3), dispel (2), elemental (2), formation (2) |

- Traps are set face-down. They **cannot be activated the turn they are set**.
- Each Trap has a trigger: attack, summon, Spell, Trap or destroyed.
- **Reaction window:** when a player attacks, summons or plays a card, the other player gets priority and may respond with a ready Trap or a Quick Spell. Players alternate until both pass.
  - The chain resolves **last-in, first-out**.
  - Each link's targets and conditions are **checked again at resolution**. A link that no longer applies fizzles.
  - "Negate" cancels the previous link.
  - The maximum chain depth is 8.
- A window opens automatically only when the player has a legal response. Otherwise it passes immediately.

## 6. In-duel evolution

- In the Main Phase, a Pet that was not summoned this turn may evolve into its next form for 3 or 5 energy.
- It stays on the field as the **same Pet**. HP keeps its ratio, statuses are kept, and no copy is created.
- The evolution lasts only for the duel. It is **not** a permanent evolution of the card.

## 7. Timers and endings

- **60 s** per main turn and **15 s** per reaction window.
- When time runs out, the server auto-passes or ends the turn.
- **3 timeouts** lose the duel.
- Reconnect: `GET /api/duels/active` returns the running duel with its current version, and any expired timer is applied first.
- A player who must draw from an empty deck loses (**deck-out**). A player at **0 LP** loses. If both reach 0 LP together, it is a **draw**.
- Each player has at most **40 turns**. At that limit the winner is the player with higher LP, then higher total Pet HP on the field; otherwise it is a draw.
- Divine Pets are strong but not invincible. In the balance test, a deck of 10 fully upgraded Divine Pets beat a level-50 regular deck in 14 of 40 duels (AI vs AI), and the test fails if a Divine deck ever wins every game.

## 8. Server API (all server-authoritative)

| Method and path | Purpose |
|---|---|
| `GET /api/duel/collection` | Rules config, the 33 Spell/Trap definitions and how many you own. The first call grants the starter set. |
| `GET` / `POST /api/duel/decks`, `DELETE /api/duel/decks/{id}`, `POST /api/duel/decks/auto` | 40-card decks. |
| `POST /api/duels/pve` `{stageId}` | PvE duel. The AI moves inside the same request. |
| `POST /api/duels/ranked` | Ranked duel against a matched opponent's deck. Updates the rating. |
| `POST /api/duels/pvp/challenge` `{uid}`, `POST /api/duels/{id}/accept`, `POST /api/duels/{id}/decline` | Friend PvP. |
| `GET /api/duels/active`, `GET /api/duels/{id}` | Reconnect and refresh. |
| `POST /api/duels/{id}/actions` `{version, action}` | Every move. Runs under a row lock and the idempotency service. |

How actions are protected:

- A wrong version returns `stale_state`.
- Illegal moves return readable codes such as `not_your_turn` or `first_turn_no_battle`.
- Clients only see **their own view**: the opponent's hand, every deck order, face-down cards and the RNG state are masked.

Duels are stored in the existing `battles` table (`is_duel = true`; modes `duel_pve`, `ranked`, `live_duel`). Missions, achievements, Ranked records and PvE rewards keep working unchanged. The legacy battle endpoints never serve a duel.

## 9. Clients

- **Unity** (`client-unity/Assets/Scripts/Screens/TacticalDuelScreens.cs`):
  - Lobby: deck status, PvE stages, Ranked, PvP challenge by UID with an accept/decline popup, and resume of an active duel.
  - 40-card Deck Builder.
  - Landscape duel board: hand fan, 5 + 3 zones, LP/energy/HP bars, phase and chain indicator, reaction window, tribute and target selection, floating damage and destroy numbers, Spell/Trap banners, turn timer and result screen.
  - Hub → Battle → PvE opens the Tactical Duel lobby.
- **Web Preview** (`web-client/src/preview/Duel.tsx`): a complete offline duel against the AI on the TypeScript engine, using simulated preview data. See `docs/web-preview.md`.
