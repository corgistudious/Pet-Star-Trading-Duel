# Interactive Web Preview (owner review before the official Unity build)

A login-free, browser-only preview of Pet Star: Trading Duel, built from the existing React/Vite web client
(`web-client/preview.html`, `web-client/src/preview/`). It reuses the real card component, catalog, VI/EN texts and
**all real assets**: 215 Pets including the 15 Divine Pets, the 9 pack arts, materials, crests, backgrounds and the
official logo.

**Safety:** everything is simulated in the browser (`src/preview/sim.ts`). There is no login, no API call and no
database. The sandbox state lives in `sessionStorage` (this tab only) and disappears when the tab closes.
The automated test confirms that the preview makes zero `/api` requests. No Unity Cloud Build or GitHub Actions
build is triggered.

## Open it

- **Shared link (private Artifact):** https://claude.ai/artifact/YAWXgwGv6JQdXHrVTCAKPM. Only the owner can open it
  until they share it from the page's Share menu. It is built from `npm run build:preview`; the artifact page itself
  is `web-client/preview-artifact.html`, which is not committed and is regenerated for each publish.
- **Locally:** run these from `web-client`:

  ```
  npm ci
  npm run dev:preview      # opens http://localhost:5173/preview.html
  npm run build:preview    # static folder web-client/dist-preview/ (relative paths, works on any static host)
  ```

- **Cloudflare Pages:** not deployed (it needs the owner's approval). The `dist-preview/` folder could later be
  published to a preview branch of the existing `petstar` project.

## What it covers

| Screen | Interactive | Mirrors the real game |
|---|---|---|
| Lobby (portrait 9:16) | player, Level 65, VIP 7, wallet, menu, VI/EN switch, reset test data | layout and assets |
| Shop: 9 packs | buy (Level/VIP/event/invitation conditions), open, odds per card and per 5-card pack | same prices, odds tables, divine pools and conditions as `packs.json` |
| Pack opening | 5 face-down cards, flip one by one or all, rare glow, Divine Pet reveal | same RNG tables (browser CSPRNG here; the server's CSPRNG in the game) |
| Collection and Pet Hub | filters; stars 1–10, enhancement +0–+20, evolution, breakthrough, awakening, skill 1–10, "+10 Lv" test button | same costs, gates and stat formulas as `Progression.cs` |
| 15 Divine Pet Cards | gallery by set, 10★, real art, frames and crests, owned marker | — |
| Marketplace | buy (stars or beans), list own card (locked while listed), cancel | 3 % fee; the real market is server-side |
| Tactical Duel (landscape 16:9) | Full 40-card duel vs AI: coin flip, hand fan, 5 Pet + 3 Spell/Trap zones, tributes, Spells/Traps, reaction chain, in-duel evolution, 2,500 LP with −500 per destroyed Pet, turn/reaction timers, log, result + rematch | real rules: TypeScript port of the server engine, verified event-by-event against C# fixtures; decks come from the simulated preview collection |
| Preview status | lists what is done and what is missing | — |

## Missing or simulated only (also shown inside the preview)

- **Tactical Duel:** runs the TypeScript port of the server's DuelEngine (`src/duel/engine.ts`), not a mock. `engine.test.ts`
  replays 8 C#-generated AI-vs-AI duels and asserts identical events, and that `catalog.json` → `duelRules` equals the
  server rules. Preview decks use the simulated collection; nothing is sent to a server. See [duel-rules.md](duel-rules.md).
- **Engravings and Reset:** implemented in the real game (Unity and web), not simulated in the preview.
- **Per-pack sounds and per-pack opening animations:** not made yet; all packs share one effect.
- **Real game only:** login, servers, realtime PvP, Ranked, guilds and chat.
- **Unity WebGL:** still untested in a real Unity Editor.
