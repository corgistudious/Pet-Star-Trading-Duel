import { describe, expect, it } from 'vitest';
import type { CardDto } from '../api/types';
import { autoFill, toSlots, validateDeck } from './deck';

let n = 0;
const card = (familyId: string, role = 'striker', power = 100, extra: Partial<CardDto> = {}) => ({ id: ++n, familyId, role, power, isBusy: false } as CardDto & typeof extra);

describe('deck rules (3 front + 2 back)', () => {
  it('requires exactly five unique cards', () => {
    const a = card('f1'), b = card('f2'), c = card('f3'), d = card('f4'), e = card('f5');
    expect(validateDeck([a, b, c, d, null])).toBe('deck_wrong_size');
    expect(validateDeck([a, b, c, d, a])).toBe('deck_duplicate_card');
    expect(validateDeck([a, b, c, d, e])).toBeNull();
  });
  it('allows at most two cards of one family', () => {
    expect(validateDeck([card('f1'), card('f1'), card('f1'), card('f2'), card('f3')])).toBe('deck_too_many_same_family');
  });
  it('maps slots to positions', () => {
    const a = card('f1');
    expect(toSlots([a, null, null, null, null])).toEqual([{ position: 0, cardId: a.id }]);
  });
  it('auto fill puts mages/supports in the back row and respects family cap', () => {
    const cards = [card('f1', 'mage', 900), card('f1', 'mage', 800), card('f1', 'striker', 700), card('f2', 'guardian', 600), card('f3', 'support', 500), card('f4', 'striker', 400), card('f5', 'striker', 300, { isBusy: true })];
    const d = autoFill(cards);
    expect(d).toHaveLength(5);
    expect(validateDeck(d)).toBeNull();
    expect(d.slice(3).every((c) => c && (c.role === 'mage' || c.role === 'support'))).toBe(true);
    expect(d.some((c) => c?.isBusy)).toBe(false);
  });
});
