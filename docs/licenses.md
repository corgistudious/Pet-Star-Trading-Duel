# Dependencies, licenses and asset sources

This is an engineering inventory, not legal advice. Before a commercial release, have qualified counsel review it, including trademark, patent and trade-dress questions for the battle mechanics and the presentation.

## Battle engines: original code

- The Tactical Duel engine (`shared-contracts/Runtime/Rules/Duel/`) and the Battle 3v3 engine (`shared-contracts/Runtime/Rules/Team/`) were written for this project.
- Their TypeScript ports (`web-client/src/duel/`, `web-client/src/team/`) were also written for this project.
- The former 20-card Arena (inspired by card-battle mechanics) was removed on 2026-11-23.
- No code was copied from Pokémon TCG Pocket, DeckGym (Core) or any other card-game engine.
- No AGPL / GPL / LGPL code is used anywhere in the repository.
- Battle 3v3 uses generic turn-based RPG mechanics (stats, energy, cooldowns, statuses). It uses no Pokémon names, data, images, sounds or UI layouts. DeckGym was not used.

## Runtime dependencies

| Component | Dependency | License |
|---|---|---|
| Backend (.NET 10) | ASP.NET Core, EF Core, Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | MIT |
| | Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | PostgreSQL License (permissive) |
| | EFCore.NamingConventions 10.0.1 | Apache-2.0 |
| | StackExchange.Redis 2.13.23 | MIT |
| Database / cache | PostgreSQL 16 | PostgreSQL License |
| | Redis (optional, realtime fan-out) | Check the version you deploy: Redis 7.4+ is RSALv2/SSPLv1 (or AGPLv3 from Redis 8). It is used as a separate server only, with no code linked. Valkey (BSD-3) is a drop-in alternative. |
| Web client | react 18.3.1, react-dom 18.3.1, react-router-dom 6.30.1 | MIT |
| Unity client | Unity 6000.0.58f2 and Unity packages (Addressables, Input System, URP, uGUI, 2D Sprite/Animation, Test Framework) | Unity Companion / Unity Terms of Service (needs a valid Unity license for the publisher) |
| | com.unity.nuget.newtonsoft-json (Json.NET) | MIT |
| Fonts | Be Vietnam Pro (Regular / SemiBold / Bold / ExtraBold) — Unity TTF and web woff2 (`web-client/public/fonts`, converted with fontTools, licence text in `OFL.txt` next to the files) | SIL Open Font License 1.1 |

## Development-only dependencies (not shipped)

| Dependency | License |
|---|---|
| xunit 2.9.3, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing | Apache-2.0 / MIT |
| typescript, vite, @vitejs/plugin-react, vitest, jsdom, @types/react(-dom) | Apache-2.0 / MIT |
| @playwright/test | Apache-2.0 |

## Game assets and their sources

| Asset | Source | Notes |
|---|---|---|
| Pet artwork (215 forms incl. evolutions and 15 Divine Pets) | Generated for this project with OpenAI `gpt-image-1.5` from Pet Star's own written character descriptions (`database/seed/pets.json` → `art`) and style bible. Ledger and costs: [asset-budget.md](asset-budget.md). | Each Pet has its own design brief (silhouette, colors, markings). Evolutions reuse the identity notes of their family. No reference images from other games were used. Per OpenAI's terms, rights in the outputs belong to the user, but they may not be unique; review before trademarking characters. |
| Chibi Trainers (boy/girl: idle, blink, happy), main lobby background, podium, 8 lobby icons | Same pipeline (`asset-pipeline/ui_assets.json`; provenance in `asset-manifests/`). Blink frames were aligned with `tools/align_blink_frame.py`. | Written from original prompts. The user's reference image was used only as a layout description; nothing from it was copied or used as an image input. |
| Lobby hexagon emblems (`ui_hex_market/games/world/battle/social`) and Trainer skins (`skin_boy_starlight`, `skin_girl_blossom`, idle/blink/happy) | Same pipeline (`asset-pipeline/ui_assets.json`); skins are edits of the project's own Trainer art; blink frames aligned with `tools/align_blink_frame.py` | Original prompts; no external images |
| Avatar frames, chat bubbles, effects, guild emblems/banners, podium | Procedural vector/CSS designs written for this project (`Cosmetics.tsx`, `Guild.tsx`, `social.css`, `database/seed/cosmetics.json`) | Original |
| UI art (backgrounds, board, icons, packs, crests, materials) | Same pipeline (category `ui`), from project-written prompts | Same as above |
| Logo / brand | Pet Star's official logo pipeline (`tools/brand/`) | Owner-provided brand |
| Card template | Pet Star Master Card Template (9:16) built in-project (`CardView.cs`, `render_card_preview.py`) | Original layout. Rarity, foil and holographic effects are procedural. |
| Battle boards and effects | Procedural UI (Unity uGUI and CSS gradients / animations); no third-party art | — |
| Sounds | None shipped yet | To do: use only self-made or commercially licensed audio, and record the source here. |
| Card texts and names (Pets, Spells, Traps, 3v3 skills) | Written for this project | — |

**Rule for new assets.** Add a row here: the source, the license, proof of the license, and whether commercial use is allowed. Use only self-made or commercially licensed assets. Never use assets from DeckGym, Pokémon or any other game.
