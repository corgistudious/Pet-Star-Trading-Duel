// Deck builder rules mirrored from DeckRules (size 5 = 3 front + 2 back, unique cards, max 2 per family).
import type { CardDto, DeckSlotDto } from '../api/types';

export const DECK_SIZE = 5;
export const MAX_SAME_FAMILY = 2;
export const FRONT_POSITIONS = [0, 1, 2];
export const BACK_POSITIONS = [3, 4];

export function validateDeck(slots: (CardDto | null)[]): string | null {
  const cards = slots.filter((c): c is CardDto => !!c);
  if (cards.length !== DECK_SIZE) return 'deck_wrong_size';
  if (new Set(cards.map((c) => c.id)).size !== cards.length) return 'deck_duplicate_card';
  const fam = new Map<string, number>();
  for (const c of cards) fam.set(c.familyId, (fam.get(c.familyId) ?? 0) + 1);
  if ([...fam.values()].some((n) => n > MAX_SAME_FAMILY)) return 'deck_too_many_same_family';
  return null;
}

export function toSlots(slots: (CardDto | null)[]): DeckSlotDto[] {
  return slots.flatMap((c, position) => (c ? [{ position, cardId: c.id }] : []));
}

/** Suggests a strong 3+2 line-up: sturdy roles in front, mages/supports behind, highest power first. */
export function autoFill(cards: CardDto[]): (CardDto | null)[] {
  const pool = [...cards].filter((c) => !c.isBusy).sort((a, b) => b.power - a.power);
  const picked: CardDto[] = [];
  const fam = new Map<string, number>();
  for (const c of pool) {
    if (picked.length === DECK_SIZE) break;
    if ((fam.get(c.familyId) ?? 0) >= MAX_SAME_FAMILY) continue;
    picked.push(c);
    fam.set(c.familyId, (fam.get(c.familyId) ?? 0) + 1);
  }
  const backRole = (c: CardDto) => c.role === 'mage' || c.role === 'support';
  const front = picked.filter((c) => !backRole(c));
  const back = picked.filter(backRole);
  while (front.length > 3) back.push(front.pop()!);
  while (back.length > 2) front.push(back.pop()!);
  const out: (CardDto | null)[] = [...front, ...Array(3 - front.length).fill(null), ...back, ...Array(2 - back.length).fill(null)];
  return out.slice(0, DECK_SIZE);
}
