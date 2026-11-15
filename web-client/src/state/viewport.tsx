// Official Pet Star viewport rules (web):
//   * every screen outside a duel is a Portrait 9:16 frame;
//   * only PvE / PvP duels use a Landscape 16:9 stage;
//   * on a phone held the "wrong" way, a pure-black (#000000) rotate overlay covers everything and blocks input.
// Inputs: visualViewport (keyboard, pinch, iOS toolbars), window size, screen.orientation / window.orientation.
// We never claim to control iOS system bars; we keep our own background #000000 so any uncovered strip matches.
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

export interface ViewportInput {
  innerW: number; innerH: number;            // layout viewport
  vvW?: number; vvH?: number; vvTop?: number; // visual viewport (shrinks with the iOS keyboard)
  orientationType?: string;                  // screen.orientation.type
  windowOrientation?: number;                // legacy iOS window.orientation (0 / 90 / -90 / 180)
  screenW: number; screenH: number;
  touch: boolean;
  editableFocused: boolean;
}

export interface ViewportState {
  w: number; h: number; top: number;   // visible area to lay out into
  deviceLandscape: boolean;            // physical orientation (not fooled by the keyboard)
  landscapeFit: boolean;               // device landscape AND the layout viewport really is wider than tall
  keyboard: boolean;
  phone: boolean;                      // touch device whose short screen side is phone-sized
  touch: boolean;
}

export function computeViewport(i: ViewportInput): ViewportState {
  const w = Math.round(i.vvW ?? i.innerW);
  const h = Math.round(i.vvH ?? i.innerH);
  let deviceLandscape: boolean;
  if (i.touch && i.orientationType) deviceLandscape = i.orientationType.startsWith('landscape');
  else if (i.touch && typeof i.windowOrientation === 'number') deviceLandscape = Math.abs(i.windowOrientation) === 90;
  else deviceLandscape = i.innerW > i.innerH;
  // keyboard: visual viewport much shorter than the layout viewport while an input has focus
  const keyboard = i.touch && i.editableFocused && i.innerH - h > 120;
  const phone = i.touch && Math.min(i.screenW, i.screenH) < 600;
  const landscapeFit = deviceLandscape && i.innerW > i.innerH;
  return { w, h, top: Math.round(i.vvTop ?? 0), deviceLandscape, landscapeFit, keyboard, phone, touch: i.touch };
}

/** Which rotate overlay (if any) a screen must show. Tablets and PCs are never blocked: they letterbox. */
export function overlayFor(mode: 'portrait' | 'landscape', v: ViewportState): 'rotate-landscape' | 'rotate-portrait' | null {
  if (!v.phone) return null;
  if (mode === 'landscape') return v.landscapeFit ? null : 'rotate-landscape';
  return v.deviceLandscape && !v.keyboard ? 'rotate-portrait' : null;
}

function read(): ViewportState {
  const vv = window.visualViewport;
  const ae = document.activeElement as HTMLElement | null;
  return computeViewport({
    innerW: window.innerWidth, innerH: window.innerHeight,
    vvW: vv?.width, vvH: vv?.height, vvTop: vv?.offsetTop,
    orientationType: screen.orientation?.type,
    windowOrientation: (window as Window & { orientation?: number }).orientation,
    screenW: screen.width, screenH: screen.height,
    touch: navigator.maxTouchPoints > 0 || matchMedia('(pointer: coarse)').matches,
    editableFocused: !!ae && (ae.tagName === 'INPUT' || ae.tagName === 'TEXTAREA' || ae.isContentEditable),
  });
}

const Ctx = createContext<ViewportState | null>(null);

export function ViewportProvider({ children }: { children: ReactNode }) {
  const [v, setV] = useState<ViewportState>(read);
  useEffect(() => {
    let frame = 0;
    const update = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        const next = read();
        const root = document.documentElement.style;
        root.setProperty('--app-w', next.w + 'px');
        root.setProperty('--app-h', next.h + 'px');
        root.setProperty('--app-top', next.top + 'px');
        setV((prev) => (JSON.stringify(prev) === JSON.stringify(next) ? prev : next));
      });
    };
    // iOS reports the new size only after the rotation animation: re-read a few times after orientationchange
    const late = () => { update(); setTimeout(update, 250); setTimeout(update, 600); };
    update();
    window.visualViewport?.addEventListener('resize', update);
    window.visualViewport?.addEventListener('scroll', update);
    window.addEventListener('resize', update);
    window.addEventListener('orientationchange', late);
    screen.orientation?.addEventListener?.('change', late);
    document.addEventListener('focusin', update);
    document.addEventListener('focusout', late);
    return () => {
      cancelAnimationFrame(frame);
      window.visualViewport?.removeEventListener('resize', update);
      window.visualViewport?.removeEventListener('scroll', update);
      window.removeEventListener('resize', update);
      window.removeEventListener('orientationchange', late);
      screen.orientation?.removeEventListener?.('change', late);
      document.removeEventListener('focusin', update);
      document.removeEventListener('focusout', late);
    };
  }, []);
  return <Ctx.Provider value={v}>{children}</Ctx.Provider>;
}

export function useViewport(): ViewportState {
  const v = useContext(Ctx);
  if (!v) throw new Error('no viewport');
  return v;
}

type Lockable = ScreenOrientation & { lock?: (o: string) => Promise<void>; unlock?: () => void };
/** Best effort: only works in fullscreen / installed PWAs on Android Chromium. iOS rejects it (that is fine). */
export async function tryLockOrientation(o: 'landscape' | 'portrait'): Promise<boolean> {
  try { await (screen.orientation as Lockable | undefined)?.lock?.(o); return true; } catch { return false; }
}
export function unlockOrientation() {
  try { (screen.orientation as Lockable | undefined)?.unlock?.(); } catch { /* not supported */ }
}
