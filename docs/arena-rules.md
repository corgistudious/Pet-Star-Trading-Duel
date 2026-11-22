# Pet Battle Arena: rules, architecture, API and operations

The Pet Battle Arena is Pet Star's compact card battle. It uses 20-card decks, 1 Active Pet and 3 Bench Pets, an Energy Zone outside the deck, and knockout points: 3 points win, and an EX knockout is worth 2.

The rules are inspired by the mechanics of Pokémon TCG Pocket.

- **Code:** all of it is original. No third-party battle engine is used: no DeckGym, no AGPL/GPL code. See [licenses.md](licenses.md).
- **Content:** every Pet, card, text, image and UI element is Pet Star's own.

The Arena is a new mode. It does **not** replace the Tactical Duel ([duel-rules.md](duel-rules.md)) or the classic 5v5 battle. All three stay available until the owner decides which mode is the main one.

## 1. Rules

Every value in this section is set in `database/seed/arena_cards.json` → `config`, which `tools/build_arena_cards.py` generates.

| Topic | Rule (default) |
|---|---|
| Deck | Exactly 20 cards. No Energy cards. Max 2 copies of the same card (same card definition id). At least 1 Basic Pet. Several decks per player. Pets must be the player's own free card instances (not listed, in auction or under review). Trainer cards must be owned items. |
| Energy | Each deck chooses 1–3 Energy types (default: the types its attacks need most). Each turn the Energy Zone makes one Energy of a random chosen type, and shows the next one in advance. One manual attach per turn, to Active or Bench. Unused Energy is lost at the end of the turn (`energyCarryOver=false`). Attacking does not spend Energy. Retreating discards Energy equal to the Retreat Cost. Evolution keeps attached Energy. |
| Board | 1 Active + 3 Bench. Hand limit 10 (drawing stops at 10). Discard pile. Points counter. |
| Setup | The server shuffles with a seed from a cryptographic RNG. Each player draws 5. If an opening hand has no Basic, the hand goes back, the deck is reshuffled and the player redraws; no card is created and decks are never mixed. Each player secretly picks an Active and up to 3 Bench Basics. A server coin flip decides who goes first. |
| First turns | The first player does not draw and gets no Energy on turn 1. Nobody attacks on their own first turn. Nobody evolves on their own first turn. Supporters are allowed on turn 1. |
| Turn | TURN_START → DRAW → ENERGY (zone) → MAIN → ATTACK resolution → KNOCKOUT check (+ promotion) → BETWEEN TURNS (checkup) → KNOCKOUT check → next TURN_START, or MATCH_FINISHED. |
| Main phase | In any order: put Basics on the Bench, evolve, attach 1 Energy, play Items (unlimited), 1 Supporter, Tools (1 per Pet), activated Abilities (1 per Pet per turn), retreat once, then attack or end the turn. An attack ends the turn after all of its effects resolve. |
| Evolution | Only along its own line (Basic → Stage 1 → Stage 2). Not on a Pet put down this turn. Not twice on the same Pet in one turn. Energy, Tool and damage carry over. Max HP, attacks and Ability change. Special Conditions are cleared. A knockout check runs after evolving. |
| Damage | Base + coin damage + conditional bonuses + "this turn" boosts − attacker's weakening. Then **+20 if the target is weak to the attacker's element**. Then shields and damage reduction, then prevention. Bench damage ignores weakness. |
| Special Conditions | Poisoned (10 at every checkup). Burned (20 at every checkup, then a coin flip; heads cures it). Asleep (can't attack or retreat; heads at checkup wakes it). Paralyzed (can't attack or retreat; cured at the end of its owner's next turn). Confused (coin flip on attack; tails = the attack does nothing, self-damage configurable and 0 by default). Poisoned and Burned stack with each other and with one of Asleep / Paralyzed / Confused, which replace each other. Moving to the Bench or evolving clears all of them. |
| Knockouts & points | A Pet with damage ≥ HP is knocked out. Its cards go to the discard pile and the opponent scores 1 point, or 2 for an EX. A player whose Active was knocked out promotes a Bench Pet before play continues. |
| Winning | 3 points, or the opponent has no Pet in play. If both players reach a win condition in the same resolution step, the result is a **draw** (`simultaneousWin`). Surrender loses. 3 timeouts lose. 30 turns per player (50 in PvE) ends in a draw. An empty deck only stops drawing; it does not make you lose. |
| Timers | 90 s per turn, 60 s for setup. When time runs out, the server auto-sets up, auto-promotes or ends the turn. |

### Verification status (no official Pocket rulebook was available)

Public sources that describe Pokémon TCG Pocket's rules disagree on some points, and no official rule document could be found. Every point below is therefore a **configuration value**, and none is claimed as "the official rule".

| Rule | Default here | Source situation |
|---|---|---|
| First player draws on turn 1 | no (`firstPlayerDraws=false`) | Guides disagree. |
| First player gets Energy on turn 1 | no | Guides agree. |
| Attack ban on the first turn | both players (`attackBanFirstTurn=both`, as the owner's spec asks) | Some guides say the second player may attack on their first turn → set `first_player`. |
| Supporter on turn 1 | allowed | Most guides say allowed. |
| Burn | 20, then coin flip to cure | Guides disagree on the amount. |
| Confusion | tails = attack fails, 0 self-damage | The physical TCG uses self-damage; Pocket guides differ. |
| Both players reach a win condition at once | draw | Most guides say draw; one guide says the player with Bench Pets wins. |
| Turn limit | 30 per player (50 in PvE) → draw | Guides agree on 30, and 50 for solo play. |
| Unused Energy | lost at end of turn | Not confirmed by a primary source. |

Before commercial release, have someone check these values against the current in-game rules and decide the house rules. Then change the config, bump `VERSION` in `tools/build_arena_cards.py` and regenerate the fixtures (section 5).

## 2. Cards

- **Pet cards.** All **215** existing Pet forms, including evolutions and the 15 Divine Pets, have battle data built from their existing design: element, family, evolution line, skill, ultimate, passive and stats.
  - HP is 50–200.
  - 1–2 attacks with typed + colorless costs.
  - Weakness is the element strong against the Pet's own element.
  - Retreat Cost is 0–3, based on role.
  - Final forms with a suitable passive get an Ability.
  - The **15 Divine Pets are EX** (2 points).
- **Trainer cards.** 16 original Pet Star cards: 4 Items, 4 Tools and 8 Supporters, each with a Vietnamese and English name and text. Every player receives a starter set of 2 of each, once (ledger reference `starter_arena`).
- **Card versions.** Every match stores the card-definition version it was played with (`arena_card_versions`). A replay always uses those definitions, even after rebalancing.
- **Data stays separate.** The Arena does not change `pets.json`, the collection, packs, odds, currencies or economy.

## 3. Effect engine

Card effects are data: closed lists of `op`, `target`, `amount`, `status`, `cond`, `turns`, `coins`, `count` and `element`. They are interpreted by `ArenaEngine.RunEffects`, with no `eval` and no code from data.

| Group | Ops |
|---|---|
| Damage & HP | damage (attack base), coin_damage, bonus (conditional), bench_damage, heal, max_hp |
| Conditions | status (poisoned, burned, asleep, paralyzed, confused), cure, status_immunity |
| Defense | shield (damage reduction), prevent (damage prevention), reduce_taken (Ability), attack_reduce |
| Cards | draw, search (random Basic from deck), shuffle |
| Board | switch_self, force_switch, retreat_mod (cheaper, or blocked) |
| Energy | energy_attach (from the zone), energy_discard, energy_transfer |
| Locks | ability_lock, attack_lock_self, attack_lock_target |
| Triggers | retaliate (Tool, on being damaged), on_ko_damage (Tool, knockout trigger), on_attack and between_turns Abilities, damage_boost |

How resolution works:

- **Order.** Effects resolve in card order (their priority is their index). Pre-damage modifiers are collected first, then damage, then the rest.
- **Timed effects.** Each one is an `ArenaMod` with `FromTurn` / `UntilTurn` (duration and expiry). The same source refreshes instead of stacking; different sources stack.
- **Triggers.** A triggered effect cannot trigger again, and depth is capped at `maxTriggerDepth`. Knockout resolution loops at most that many times.
- **Choices.** Player choices (`targets`) are validated **before** anything changes, so an action is never half-applied.
- **Log.** Every step writes an event to the match log.

## 4. Architecture

| Part | Where |
|---|---|
| Engine (authoritative) | `shared-contracts/Runtime/Rules/Arena/` — ArenaModel, ArenaEngine, ArenaAi, ArenaDecks. Compiled into the server and the Unity client. |
| Server | `backend/src/PetStar.Api/Infrastructure/ArenaService.cs`, `Features/ArenaEndpoints.cs` (API + `ArenaMatchmaker` background service) |
| Database | `Data/EntitiesArena.cs`, migration `*_ArenaBattle` (additive only) |
| Unity | `client-unity/Assets/Scripts/Screens/ArenaScreens.cs` — lobby, 20-card deck builder, portrait battle board. Hub → Battle → Pet Battle Arena. |
| Web preview | `web-client/src/arena/` (TypeScript port + AI) and `web-client/src/preview/Arena.tsx` |
| Card data | `tools/build_arena_cards.py` → `database/seed/arena_cards.json` (copied to `web-client/public/data/`) |

**Server-authoritative play.**

- Clients only send intents: `POST /api/arena/matches/{id}/actions` with `{actionId, version, action}`.
- The server runs each action in a transaction with a `FOR UPDATE` row lock. It applies expired timers first, then the engine, then any AI moves.
- **Action ids** are unique per match: a retried request returns the current view and is not applied twice.
- A **wrong version** is refused with `stale_state`.
- The server checks: turn order, ownership (when the deck is built and when the match starts), Energy, targets, conditions, and whether the match is already finished.

**Realtime.**

- The existing WebSocket hub pushes `arena_update`, `arena_match_found` and `arena_invite`. Clients then fetch their view.
- Reconnecting is a plain `GET`: it applies expired timers and returns the current state.
- Actions use HTTPS (idempotent, authenticated) rather than WebSocket messages.

**Hidden information.** Each player gets a filtered view:

- the opponent's hand ids are 0;
- every deck order is 0;
- the opponent's setup choice is hidden until both have set up;
- the RNG state is 0;
- unseen cards have no definition id.

Discard piles and Pets in play are public.

**Replay.** `seed + decks (in their original order) + card-definition version + action log (incl. timeouts)` reproduces the match exactly. The API test rebuilds a finished match on the server and compares every event. Replays are only served to participants, and only after the match ends.

### Modes

| Mode | How it starts |
|---|---|
| PvE vs AI | `POST /api/arena/pve {stageId, deckId?}`. Uses the existing PvE stages; the stage's Pet families form the AI deck. |
| Casual | `POST /api/arena/queue {mode:"casual"}`. Pairs with any waiting player on the same server. |
| Ranked | `POST /api/arena/queue {mode:"ranked"}`. The rating window widens with waiting time (150 → 1000). Elo K=32 per ranked season. |
| Friend Battle | `POST /api/arena/invite {uid}`, then `POST /api/arena/matches/{id}/accept`, or `.../decline`. Invitations expire after 2 minutes. |
| Private Room | `POST /api/arena/rooms`, which returns a 6-character code. The friend calls `POST /api/arena/rooms/{code}/join`. Rooms expire after 10 minutes. |

Other endpoints:

- `GET /api/arena/cards`, `GET /api/arena/collection`
- `GET/POST /api/arena/decks`, `DELETE /api/arena/decks/{id}`, `POST /api/arena/decks/auto`
- `GET /api/arena/active` (match / queue / room)
- `GET /api/arena/matches/{id}`
- `GET /api/arena/history`
- `GET /api/arena/matches/{id}/replay`
- `GET /api/arena/ratings`
- `DELETE /api/arena/queue`

One live match per player. A player who is queued can't start another match.

### Database (PostgreSQL, additive migration)

| Requested entity | Table here |
|---|---|
| users | existing `accounts` / `server_memberships` |
| pet_cards | card definitions in `arena_cards.json` + stored versions in `arena_card_versions` |
| card_ownership | existing `card_instances` (Pets) and `items` (trainer cards) |
| decks / deck_cards | `arena_decks` / `arena_deck_cards` (FK to card_instances, unique per deck + instance) |
| battle_matches | `arena_matches` (state, version, seed, card version, deadline, status) |
| battle_players | `arena_match_players` (side, member or AI, exact deck used, rating before/after) |
| battle_events | `arena_events` (seq, side, action id, action; unique (match, seq) and (match, action id)) |
| battle_snapshots | `arena_snapshots` (every 25 events and at the end) |
| ranked_seasons | existing `ranked_seasons` |
| player_ratings | `arena_ratings` (season, member) |
| battle_rewards | `arena_rewards` (unique (match, member): a reward can never be paid twice) |
| matchmaking queue | `arena_queue` |

**Rewards.**

- **PvE win:** the existing stage rewards, sharing the existing daily cap (`pve_progress`), so the economy is unchanged. Ledger reference `arena:<match>:<n>`.
- **Ranked:** rating only.
- **Casual / friend / private:** no currency.
- The match result, rewards and rating are written in the same transaction as the final state.
- No request can create cards or currency.

## 5. Tests

| Suite | What it checks |
|---|---|
| `backend/tests/PetStar.Rules.Tests/ArenaTests.cs` | Card pool. Valid / invalid decks. Shuffle, deal and coin fairness. Opening hand always has a Basic. Setup. First-turn rules (also the configurable variants). Energy Zone. Evolution. Weakness and reductions. All 5 Special Conditions and stacking. EX knockouts and points. Promotion. Simultaneous knockouts → draw. Retreat. Turn limit. Timeouts forfeit. Surrender. Chosen-target regression. Replay determinism. **10,000 simulated matches** (mixed random-legal and AI play, many decks and seeds) with invariants checked after every action: card conservation, bench ≤ 3, hand ≤ 10, HP bounds, ownership, Active-or-promotion. Every match finishes. |
| `ArenaFixtureTests.cs` | 16 C# matches written as fixtures. The preview must ship the same card file. |
| `web-client/src/arena/engine.test.ts` | The TypeScript engine replays every fixture to the identical event log, and the TypeScript AI makes the same choices. |
| `backend/tests/PetStar.Api.Tests/ArenaApiTests.cs` | Real API + PostgreSQL: starter grant once; deck ownership, lock and rule errors; hidden information; stale version; duplicate action id applied once; reconnect; a full PvE match; rewards written once; snapshots; replay rebuilt on the server identical to the stored match; history; timers → forfeit; casual matchmaking with two players playing a full match; ranked matchmaking + Elo; private room by code; friend invite; one live match per player. |

Regenerate the fixtures after an intended rules change:

```bash
cd backend && PETSTAR_WRITE_ARENA_FIXTURES=1 dotnet test tests/PetStar.Rules.Tests --filter Arena
```

## 6. Install, run, deploy

```bash
python3 tools/build_arena_cards.py && cp database/seed/arena_cards.json web-client/public/data/
```

```bash
cd backend && dotnet test
```

```bash
cd web-client && npm ci && npm test && npm run build:preview
```

```bash
bash tools/unity-compile-check/run.sh
```

- The backend test suite needs PostgreSQL; `PETSTAR_TEST_PG` overrides the connection.
- The Unity check is an offline compile check, not a Unity build.
- **Run locally:** start the API as described in [deployment.md](deployment.md). The migration `ArenaBattle` applies automatically on startup, like every earlier migration, and it only adds tables.
- **Deploy:** nothing has been deployed. Production deployment (VPS / Cloudflare) needs the owner's approval after testing, following [deployment.md](deployment.md). Unity Cloud Build and GitHub Actions Unity builds were not run.
