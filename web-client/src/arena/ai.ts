// TypeScript port of shared-contracts/Runtime/Rules/Arena/ArenaAi.cs (same deterministic choices; checked by fixtures).
import { abilityLocked, attackBanned, attackDamage, attackProblem, card, costPaid, maxHp, retreatCost, trainerPlayable } from './engine';
import { attacksOf, inPlay, isBasic, type ArenaAction, type ArenaCardDef, type ArenaPet, type ArenaRules, type ArenaState } from './model';

const sortBy = <T>(xs: T[], ...keys: ((x: T) => number)[]) =>
  xs.map((x, i) => ({ x, i })).sort((p, q) => { for (const k of keys) { const d = k(p.x) - k(q.x); if (d !== 0) return d; } return p.i - q.i; }).map((o) => o.x);
const maxDamage = (d: ArenaCardDef) => attacksOf(d).reduce((m, a) => Math.max(m, a.damage), 0);

export function decide(rules: ArenaRules, st: ArenaState, p: number): ArenaAction {
  if (st.phase === 'setup') return setupChoice(rules, st, p);
  if (st.players[p].mustPromote) return { type: 'promote', uid: promoteChoice(rules, st, p) };
  return main(rules, st, p) ?? { type: 'end' };
}

function petScore(rules: ArenaRules, pet: ArenaPet): number {
  const d = rules.def(pet.defId);
  const ready = attacksOf(d).filter((a) => costPaid(pet.energy, a.cost)).length;
  return ((d.hp ?? 0) - pet.damage) + ready * 60 + maxDamage(d);
}

export function setupChoice(rules: ArenaRules, st: ArenaState, p: number): ArenaAction {
  const pl = st.players[p];
  const basics = sortBy(pl.hand.filter((c) => isBasic(rules.def(card(st, c).defId))), (c) => { const d = rules.def(card(st, c).defId); return -((d.hp ?? 0) + maxDamage(d)); }, (c) => c);
  return { type: 'setup', card: basics[0], bench: basics.slice(1, 1 + rules.config.benchSize) };
}

export const promoteChoice = (rules: ArenaRules, st: ArenaState, p: number) => sortBy(st.players[p].bench, (b) => -petScore(rules, b), (b) => b.uid)[0].uid;

function main(rules: ArenaRules, st: ArenaState, p: number): ArenaAction | null {
  const pl = st.players[p], opp = st.players[1 - p], cfg = rules.config, act = pl.active;
  const D = (c: number) => rules.def(card(st, c).defId);
  if (!(cfg.noEvolveFirstTurn && pl.turnsTaken === 1))
    for (const pet of sortBy(inPlay(pl), (x) => (x === act ? 0 : 1), (x) => x.uid)) {
      if (pet.playedTurn === st.turn || pet.evolvedTurn === st.turn) continue;
      const evos = pl.hand.filter((c) => D(c).kind === 'pet' && D(c).evolvesFrom === pet.defId);
      if (evos.length) return { type: 'evolve', card: Math.min(...evos), uid: pet.uid };
    }
  if (pl.bench.length < cfg.benchSize) {
    const basic = sortBy(pl.hand.filter((c) => isBasic(D(c))), (c) => -(D(c).hp ?? 0), (c) => c)[0];
    if (basic) return { type: 'bench', card: basic };
  }
  for (const c of sortBy(pl.hand, (x) => x)) {
    const d = D(c);
    if (d.kind === 'pet') continue;
    const a = trainerChoice(rules, st, p, c, d);
    if (!a) continue;
    if (d.kind === 'supporter' && pl.supportersUsed >= cfg.supportersPerTurn) continue;
    if (d.kind === 'supporter' && !cfg.supporterFirstTurn && pl.turnsTaken === 1) continue;
    if (d.kind !== 'tool' && !trainerPlayable(rules, st, p, d)) continue;
    return a;
  }
  for (const pet of sortBy(inPlay(pl), (x) => x.uid)) {
    const ab = rules.def(pet.defId).ability;
    if (!ab || ab.trigger !== 'activated' || pet.abilityTurn === st.turn || abilityLocked(st, pet)) continue;
    if (ab.effects.some((e) => e.cond === 'is_active') && act !== pet) continue;
    if (ab.effects.every((e) => e.op === 'heal') && (act === null || act.damage === 0)) continue;
    return { type: 'ability', uid: pet.uid };
  }
  if (!pl.energyAttached && pl.zoneCurrent !== null && act) {
    const z = pl.zoneCurrent;
    const target = needsEnergy(rules, act, z) ? act : (sortBy(pl.bench.filter((b) => needsEnergy(rules, b, z)), (b) => -(rules.def(b.defId).stage ?? 0), (b) => b.uid)[0] ?? act);
    return { type: 'attach', uid: target.uid };
  }
  if (act && pl.retreats < cfg.retreatsPerTurn && !attackBanned(rules, st, p)) {
    const stuck = bestAttack(rules, st, p) < 0;
    const cost = retreatCost(rules, st, p);
    const ready = sortBy(pl.bench.filter((b) => attacksOf(rules.def(b.defId)).some((at) => costPaid(b.energy, at.cost))), (b) => -petScore(rules, b), (b) => b.uid)[0];
    if (stuck && ready && cost < 50 && act.energy.length >= cost && act.special !== 'asleep' && act.special !== 'paralyzed') return { type: 'retreat', uid: ready.uid };
  }
  const best = bestAttack(rules, st, p);
  if (best >= 0) {
    const targets: number[] = [];
    const atk = attacksOf(rules.def(act!.defId))[best];
    if (atk.effects.some((e) => e.target === 'opp_bench_chosen') && opp.bench.length > 0)
      targets.push(sortBy(opp.bench, (b) => (rules.def(b.defId).hp ?? 0) - b.damage, (b) => b.uid)[0].uid);
    if (atk.effects.some((e) => e.target === 'own_bench_chosen') && pl.bench.length > 0)
      targets.push(sortBy(pl.bench, (b) => -(rules.def(b.defId).stage ?? 0), (b) => b.uid)[0].uid);
    return { type: 'attack', index: best, targets };
  }
  return null;
}

function needsEnergy(rules: ArenaRules, pet: ArenaPet, type: string): boolean {
  const atks = attacksOf(rules.def(pet.defId));
  if (atks.length === 0) return false;
  const top = atks[atks.length - 1];
  if (costPaid(pet.energy, top.cost)) return false;
  const withE = [...pet.energy, type];
  return atks.some((a) => !costPaid(pet.energy, a.cost) && costPaid(withE, a.cost)) || withE.length <= top.cost.length;
}

export function bestAttack(rules: ArenaRules, st: ArenaState, p: number): number {
  const pl = st.players[p], opp = st.players[1 - p];
  if (!pl.active || !opp.active) return -1;
  const atks = attacksOf(rules.def(pl.active.defId));
  let best = -1, bestVal = -1;
  for (let i = 0; i < atks.length; i++) {
    if (attackProblem(rules, st, p, i) !== null) continue;
    const dmg = attackDamage(rules, st, p, pl.active, atks[i], opp.active, 0);
    const ko = dmg >= maxHp(rules, st, opp.active) - opp.active.damage;
    const val = (ko ? 1000 : 0) + dmg * 3 + atks[i].effects.length * 5;
    if (val > bestVal) { bestVal = val; best = i; }
  }
  return best;
}

function trainerChoice(rules: ArenaRules, st: ArenaState, p: number, c: number, d: ArenaCardDef): ArenaAction | null {
  const pl = st.players[p], opp = st.players[1 - p], act = pl.active;
  const A = (type: string, ...targets: number[]): ArenaAction => ({ type, card: c, targets });
  const hurt = sortBy(inPlay(pl).filter((x) => x.damage >= 20), (x) => -x.damage, (x) => x.uid)[0];
  const readyBench = (b: ArenaPet) => attacksOf(rules.def(b.defId)).some((a) => costPaid(b.energy, a.cost));
  switch (d.id) {
    case 'at_herb_salve': return hurt ? A('item', hurt.uid) : null;
    case 'at_star_lantern': return pl.bench.length < rules.config.benchSize && pl.deck.some((x) => x === 0 || isBasic(rules.def(card(st, x).defId))) ? A('item') : null;
    case 'at_lucky_coin': return pl.hand.length <= 5 && pl.deck.length > 0 ? A('item') : null;
    case 'at_wind_boots':
      return act && pl.retreats === 0 && bestAttack(rules, st, p) < 0 && retreatCost(rules, st, p) > act.energy.length && pl.bench.some(readyBench) ? A('item') : null;
    case 'su_scholar': return pl.hand.length <= 6 && pl.deck.length > 0 ? A('supporter') : null;
    case 'su_medic': return hurt && hurt.damage >= 40 ? A('supporter', hurt.uid) : null;
    case 'su_captain': {
      if (opp.bench.length === 0 || !act) return null;
      const weak = sortBy(opp.bench, (b) => maxHp(rules, st, b) - b.damage, (b) => b.uid)[0];
      return maxHp(rules, st, weak) - weak.damage <= maxDamage(rules.def(act.defId)) && bestAttack(rules, st, p) >= 0 ? A('supporter', weak.uid) : null;
    }
    case 'su_strategist': return bestAttack(rules, st, p) >= 0 ? A('supporter') : null;
    case 'su_guide': {
      if (!act || bestAttack(rules, st, p) >= 0 || attackBanned(rules, st, p)) return null;
      const r = sortBy(pl.bench.filter(readyBench), (b) => b.uid)[0];
      return r ? A('supporter', r.uid) : null;
    }
    case 'su_smith': {
      const donor = sortBy(pl.bench.filter((b) => b.energy.length > 0), (b) => -b.energy.length, (b) => b.uid)[0];
      return donor && act && bestAttack(rules, st, p) < 0 ? A('supporter', donor.uid) : null;
    }
    case 'su_tracker': return opp.active && opp.active.energy.length >= 2 ? A('supporter') : null;
    case 'su_warden': return opp.active && rules.def(opp.active.defId).ability ? A('supporter') : null;
  }
  if (d.kind === 'tool' && act && act.tool === 0) return { type: 'tool', card: c, uid: act.uid };
  if (d.kind === 'tool') { const holder = sortBy(pl.bench.filter((b) => b.tool === 0), (b) => b.uid)[0]; if (holder) return { type: 'tool', card: c, uid: holder.uid }; }
  return null;
}
