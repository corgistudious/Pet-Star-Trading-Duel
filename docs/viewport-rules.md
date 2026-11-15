# Viewport rules: Web, Android, Windows

This is the official Pet Star rule. The game is **not** landscape everywhere.

| Area | Orientation |
|---|---|
| Login, Lobby, Pet Home, Game Hub, Booster/pack opening, Marketplace, Auction, Profile, Friends, Guild, Chat, Events, Gift code, Missions, Settings, Web Admin Panel | **Portrait 9:16** |
| PvE duel, ranked/friend duel, live PvP duel | **Landscape 16:9** |
| Leaving a duel | back to Portrait; UI state restored (lobby/duel hub reloads from the server) |

## Web (implemented: `web-client/src/state/viewport.tsx`, `components/Frames.tsx`)

**Portrait frame (`PortraitFrame`)**
- Phones (taller than 9:16) use the full width.
- Wider screens (tablets, PCs, landscape windows) get a centered 9:16 column on `#000000`.
- The column is a CSS size container. All breakpoints are container queries on the column, not on the screen, so a
  PC never switches to a "wide" layout.

**Duel stage (`BattleFrame`)**
- An exact 16:9 stage, `width = min(100cqw, 100cqh × 16/9)`, inside the safe area and letterboxed on `#000000`.
- 3 Frontline + 2 Backline per side: my Backline | my Frontline ‖ enemy Frontline | enemy Backline.
- Slots are sized in stage units, so they are square at every size. Nothing is distorted or cut, and End turn is
  always inside the stage.

**Rotate overlay (phones only: touch and short screen side under 600 px)**
- Shown when a duel is open while the phone is portrait, and when a non-duel screen is open while the phone is
  landscape.
- Look:
  - Background exactly `#000000`; no gradient, artwork, logo, header or footer.
  - A white phone icon with a gentle rotate animation (static when the user prefers reduced motion).
  - Centered VI + EN text.
- The content behind it is `inert` (no taps, no keyboard focus, hidden from screen readers) and the overlay
  swallows every pointer event.
- Tablets and PCs are never blocked; they letterbox instead.

**Orientation source**
- `screen.orientation.type` is used, with legacy `window.orientation` as the iOS fallback.
- "Fits" means the device is landscape **and** the layout viewport is really wider than tall. This handles
  mid-rotation and split view.
- The keyboard never flips the orientation.

**iPhone Safari / PWA**
- Listens to `visualViewport` resize and scroll, window resize, `orientationchange` (re-read after 250/600 ms
  because iOS reports late), `screen.orientation` change, and focus in/out.
- The frame follows the visual viewport (height and offsetTop), so the keyboard never covers inputs.
- `viewport-fit=cover` with `env(safe-area-inset-*)` padding.
- We do **not** claim to control the iOS status bar or home-indicator area. The page, frame, letterbox bars and
  overlay are all `#000000`, so any strip next to system UI matches exactly. `theme-color` is `#000000`.
- **Installed PWA**:
  - Manifest `orientation: portrait`.
  - During a duel, `screen.orientation.lock('landscape')` is attempted. This works in an installed Android PWA;
    iOS rejects it, and the overlay rule still applies.
  - On leaving a duel the lock is released.

## Android native (Unity, `client-unity/Assets/Scripts/UI/Navigator.cs`)

- Every screen declares `IsDuel` (only `DuelBattleScreen` is true). Before a screen is built, the Navigator sets the
  orientation. The rule follows game state, not the sensor:
  - outside duels: portrait only, auto-rotate to landscape off;
  - duels: landscape left and right.
- On leaving a duel it returns to portrait and rebuilds the screen with the portrait layout.
- The layout frame is 9:16 or 16:9, with black bars where the screen is wider than the frame. Tall phones use
  the full screen for portrait.

## Windows (Unity)

- The physical display is never rotated.
- **Windowed mode:** the window is reshaped on each transition:
  - portrait: ≈85% of display height, 9:16;
  - duel: ≈80% of display width, 16:9.
- **Fullscreen:** the 9:16 or 16:9 frame is pillar- or letterboxed on black.
- Layout mode comes from the game state, so Lobby and other screens stay portrait even on a wide monitor.

## Tests

The web tests below have run. The Unity code is written but **not compiled**; it needs the Unity Editor/license.

**e2e** (`web-client/e2e/viewport.spec.ts`, Chromium with iPhone 13 emulation):
1. Portrait → enter duel → black overlay: `rgb(0,0,0)`, no background image, covers the viewport, every hit-test
   point lands on the overlay, stage `inert`. Then rotate → the board fits (3+2 each side, square slots, all inside
   the viewport, stage 16:9).
2. Rotate portrait/landscape 4 more times; the duel is still playable.
3. Leave the duel while landscape → portrait frame plus the rotate-to-portrait overlay; rotate → lobby.
4. Opened while already landscape: the lobby asks for portrait; reloading straight into a duel in landscape → no
   overlay.
5. Keyboard: Android-style viewport shrink in chat keeps the input and the Send button visible, with no overlay.
   iOS-style (only `visualViewport` shrinks) is covered by unit tests, because Chromium cannot emulate it.
6. PWA:
   - manifest fields and icons;
   - the service worker installs without reloading the page;
   - offline reload shows the cached shell plus the offline banner, and keeps the session;
   - back online, the game resumes.

**e2e** (`web-client/e2e/desktop.spec.ts`): PC window resizes at 1280×720, 1920×1080, 700×1000 and 360×640.
- The portrait column is exactly `min(W, H×9/16)`, centered.
- The duel stage is `min(W, H×16/9)` at 16:9.
- There is never a rotate overlay.
- Bottom navigation and End turn are always in view.

**Unit** (`src/state/viewport.test.ts`): orientation sources, fit rule, iOS and Android keyboards, tablets and PCs
never blocked, standalone/iOS detection.

**Not testable here:** real WebKit / Safari, real iOS standalone mode, and a real on-screen keyboard. Please do one
manual pass on an iPhone (Safari and home-screen PWA) after the preview deploy.
