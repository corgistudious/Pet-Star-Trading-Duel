import { describe, expect, it } from 'vitest';
import { TEAR_COMPLETE, dragTear, flipNext, initialPack, rareLevel, releaseTear, skipAll, startReveal } from './pack';

describe('pack opening state machine', () => {
  it('tears only after the swipe passes the threshold', () => {
    let s = dragTear(initialPack(), 0.3);
    expect(s.phase).toBe('tearing');
    s = dragTear(s, 0.1); // never goes backwards
    expect(s.tear).toBeCloseTo(0.3);
    s = dragTear(s, TEAR_COMPLETE);
    expect(s.phase).toBe('torn');
    expect(s.tear).toBe(1);
  });
  it('releasing early snaps back', () => {
    expect(releaseTear(dragTear(initialPack(), 0.5)).phase).toBe('sealed');
  });
  it('flips each card then advances, summary after the fifth', () => {
    let s = startReveal(dragTear(initialPack(), 1));
    expect(s.phase).toBe('revealing');
    for (let i = 0; i < 5; i++) {
      expect(s.current).toBe(i);
      s = flipNext(s); // face up
      expect(s.revealed[i]).toBe(true);
      s = flipNext(s); // next
    }
    expect(s.phase).toBe('summary');
    expect(s.revealed.every(Boolean)).toBe(true);
  });
  it('skip reveals everything at any point', () => {
    const s = skipAll(initialPack());
    expect(s.phase).toBe('summary');
    expect(s.revealed).toEqual([true, true, true, true, true]);
  });
  it('rare effect intensity by rarity', () => {
    expect(rareLevel('N')).toBe(0);
    expect(rareLevel('SR')).toBe(0);
    expect(rareLevel('SSR')).toBe(1);
    expect(rareLevel('UR')).toBe(1);
    expect(rareLevel('LR')).toBe(2);
    expect(rareLevel('GR')).toBe(2);
    expect(rareLevel('EX')).toBe(3);
    expect(rareLevel('Secret')).toBe(3);
  });
});
