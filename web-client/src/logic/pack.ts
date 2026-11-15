// Pack opening interaction model (pure, unit-tested). The server has already rolled and persisted the five
// cards before any animation starts; this only drives presentation, so Skip / reload never changes results.
export type Phase = 'sealed' | 'tearing' | 'torn' | 'revealing' | 'summary';

export interface PackState { phase: Phase; tear: number; revealed: boolean[]; current: number }

export const TEAR_COMPLETE = 0.72; // fraction of the pack width the finger must travel

export function initialPack(n = 5): PackState {
  return { phase: 'sealed', tear: 0, revealed: Array(n).fill(false), current: 0 };
}

/** Horizontal drag along the tear line. Progress never goes backwards; completing it opens the pack. */
export function dragTear(s: PackState, dxFraction: number): PackState {
  if (s.phase !== 'sealed' && s.phase !== 'tearing') return s;
  const tear = Math.min(1, Math.max(s.tear, dxFraction));
  if (tear >= TEAR_COMPLETE) return { ...s, tear: 1, phase: 'torn' };
  return { ...s, tear, phase: tear > 0 ? 'tearing' : 'sealed' };
}

/** Releasing before the threshold snaps the foil back (not torn). */
export function releaseTear(s: PackState): PackState {
  return s.phase === 'tearing' ? { ...s, tear: 0, phase: 'sealed' } : s;
}

export function startReveal(s: PackState): PackState {
  return s.phase === 'torn' ? { ...s, phase: 'revealing', current: 0 } : s;
}

/** First tap flips the current card face-up; the next tap moves on. After the fifth card the summary shows. */
export function flipNext(s: PackState): PackState {
  if (s.phase !== 'revealing') return s;
  if (!s.revealed[s.current]) {
    const revealed = [...s.revealed];
    revealed[s.current] = true;
    return { ...s, revealed };
  }
  const next = s.current + 1;
  return next >= s.revealed.length ? { ...s, phase: 'summary' } : { ...s, current: next };
}

export function skipAll(s: PackState): PackState {
  return { ...s, tear: 1, revealed: s.revealed.map(() => true), phase: 'summary' };
}

const HIGH = ['SSR', 'UR', 'LR', 'MR', 'GR', 'EX', 'Secret'];
/** Rare reveal intensity: 0 none, 1 sparkle (SSR/UR), 2 burst (LR/MR/GR), 3 legendary (EX/Secret). */
export function rareLevel(rarity: string): 0 | 1 | 2 | 3 {
  const i = HIGH.indexOf(rarity);
  if (i < 0) return 0;
  return i < 2 ? 1 : i < 5 ? 2 : 3;
}
