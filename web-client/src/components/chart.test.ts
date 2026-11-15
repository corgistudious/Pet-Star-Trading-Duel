import { describe, expect, it } from 'vitest';
import { buildPath } from './PriceChart';
import { formatDuration } from './ui';

describe('price chart path', () => {
  it('scales points into the view box', () => {
    const r = buildPath([{ t: 0, v: 10 }, { t: 10, v: 20 }], 100, 50, 0);
    expect(r.d).toBe('M0.0,50.0 L100.0,0.0');
    expect([r.min, r.max]).toEqual([10, 20]);
  });
  it('handles a single point and empty input', () => {
    expect(buildPath([], 100, 50).d).toBe('');
    expect(buildPath([{ t: 5, v: 5 }], 100, 50, 0).d).toBe('M50.0,25.0');
  });
});

describe('countdown format', () => {
  it('formats durations', () => {
    expect(formatDuration(59_000)).toBe('0:59');
    expect(formatDuration(3_725_000)).toBe('1h 02m');
    expect(formatDuration(90_000_000)).toBe('1d 1h');
  });
});
