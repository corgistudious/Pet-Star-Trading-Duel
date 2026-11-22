// Preview decks for the Pet Battle Arena. The player's deck uses the simulated collection of this browser session
// (no server, no real ownership); the AI deck is built from one element's evolution lines. Both follow the real deck
// rules (20 cards, max 2 copies, at least one Basic) so the engine runs exactly as on the server.
import { attacksOf, isBasic, type ArenaDeckCard, type ArenaRules } from '../arena/model';
import type { SimState } from './sim';

const TRAINER_ORDER = ['su_scholar', 'at_star_lantern', 'at_herb_salve', 'su_medic', 'su_captain', 'su_guide', 'at_leaf_armor', 'su_strategist', 'at_wind_boots',
  'at_lucky_coin', 'su_smith', 'at_thorn_band', 'at_purity_charm', 'at_ember_charm', 'su_tracker', 'su_warden'];

function score(rules: ArenaRules, id: string) { const d = rules.def(id); return (d.hp ?? 0) + attacksOf(d).reduce((m, a) => Math.max(m, a.damage), 0) * 2 + (d.ex ? 80 : 0); }

/** Strongest evolution lines first (Basic ×2, Stage 1 ×2, Stage 2 ×1), then trainers up to 20 cards. */
export function buildDeck(rules: ArenaRules, pets: { defId: string; inst: number }[], petTarget = 12): ArenaDeckCard[] {
  const byDef = new Map<string, number[]>();
  for (const p of pets) if (rules.has(p.defId)) byDef.set(p.defId, [...(byDef.get(p.defId) ?? []), p.inst]);
  const deck: ArenaDeckCard[] = [];
  const count = (id: string) => deck.filter((d) => d.defId === id).length;
  const add = (id: string, n: number) => {
    for (const inst of byDef.get(id) ?? []) {
      if (n <= 0 || count(id) >= rules.config.maxCopies || deck.length >= petTarget) return;
      if (deck.some((d) => d.cardInstanceId === inst)) continue;
      deck.push({ defId: id, cardInstanceId: inst }); n--;
    }
  };
  const next = (id: string) => [...byDef.keys()].filter((x) => rules.def(x).evolvesFrom === id).sort()[0];
  const line = (b: string) => { let s = score(rules, b), cur: string | undefined = b; for (let i = 0; i < 2 && cur; i++) { cur = next(cur); if (cur) s += score(rules, cur); } return s; };
  const basics = [...byDef.keys()].filter((id) => isBasic(rules.def(id))).sort((a, b) => line(b) - line(a) || (a < b ? -1 : 1));
  for (const b of basics) {
    if (deck.length >= petTarget) break;
    add(b, 2);
    let cur: string | undefined = b;
    for (let i = 0; i < 2 && cur; i++) { cur = next(cur); if (cur) add(cur, i === 0 ? 2 : 1); }
  }
  for (const t of TRAINER_ORDER) for (let i = 0; i < 2 && deck.length < rules.config.deckSize; i++) deck.push({ defId: t, cardInstanceId: 0 });
  return deck;
}

export function playerDeck(rules: ArenaRules, s: SimState): { deck: ArenaDeckCard[]; demo: boolean } {
  const owned = s.cards.filter((c) => !c.listed).map((c) => ({ defId: c.formId, inst: c.id }));
  if (owned.some((o) => rules.has(o.defId) && isBasic(rules.def(o.defId)))) return { deck: buildDeck(rules, owned), demo: false };
  return { deck: aiDeck(rules, 0), demo: true };     // no Basic Pet in the simulated collection yet: a clearly labelled demo deck
}

export function aiDeck(rules: ArenaRules, seed: number): ArenaDeckCard[] {
  const els = rules.data.elements;
  const el = els[seed % els.length];
  let n = 1;
  const pets = rules.data.cards.filter((c) => c.kind === 'pet' && c.element === el && !c.ex).flatMap((c) => [{ defId: c.id, inst: n++ }, { defId: c.id, inst: n++ }]);
  return buildDeck(rules, pets, 11 + (seed % 3)).map((d) => ({ ...d, cardInstanceId: 0 }));
}
