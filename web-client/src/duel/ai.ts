// TypeScript port of shared-contracts/Runtime/Rules/Duel/DuelAi.cs (same deterministic choices).
import { canAct, cardOf, legalTargets, targetsValid, trapReady, tributesFor, unitOf } from './engine';
import type { DuelAction, DuelPlayerState, DuelRules, DuelState, DuelUnit, PetFormStats, TacticDef } from './model';

const idiv = (a: number, b: number) => Math.trunc(a / b);
const sortBy = <T>(xs: T[], ...keys: ((x: T) => number)[]) =>
  xs.map((x, i) => ({ x, i })).sort((p, q) => { for (const k of keys) { const d = k(p.x) - k(q.x); if (d !== 0) return d; } return p.i - q.i; }).map((o) => o.x);
const powerU = (u: DuelUnit) => idiv(u.hp, 4) + u.atk * 2 + idiv(u.def * 3, 2);
const powerF = (f: PetFormStats) => idiv(f.hp, 4) + f.atk * 2 + idiv(f.def * 3, 2);
const act = (type: string): DuelAction => ({ type });
const spell = (card: number, ...targets: number[]): DuelAction => ({ type: 'spell', card, targets });
const isAttackKind = (k: string) => k === 'attack' || k === 'skill' || k === 'ultimate';
const limitsOk = (pl: DuelPlayerState, d: TacticDef) => !(d.oncePerTurn && pl.usedThisTurn.includes(d.id)) && !(d.oncePerDuel && pl.usedThisDuel.includes(d.id));

export function decide(rules: DuelRules, st: DuelState, p: number): DuelAction {
  return st.priority === p ? react(rules, st, p) : st.phase === 'battle' ? battle(rules, st, p) : main(rules, st, p);
}

function react(rules: DuelRules, st: DuelState, p: number): DuelAction {
  const pl = st.players[p];
  for (let z = 0; z < pl.zones.length; z++) if (trapReady(rules, st, p, pl.zones[z])) return { type: 'activate', zone: z };
  const last = st.chain.length > 0 ? st.chain[st.chain.length - 1] : null;
  for (const c of sortBy(pl.hand, (x) => x)) {
    const cd = cardOf(st, c);
    if (cd.kind !== 'tactic') continue;
    const d = rules.tactics[cd.tacticId!];
    if (d.type !== 'spell' || d.speed !== 'quick' || pl.energy < d.cost) continue;
    const neg = d.effects.find((e) => e.op === 'negate');
    if (neg) {
      if (!last || last.player === p) continue;
      const attack = isAttackKind(last.kind);
      if (neg.what === 'attack' && attack) {
        const t = unitOf(st, last.target);
        if (t && t.owner === p && t.hp * 100 < t.maxHp * 60) return spell(c);
      }
      if (neg.what === 'spell' && last.kind === 'spell' && rules.tactics[last.tacticId!].cost >= 2) return spell(c);
      continue;
    }
    if (last && last.player !== p && isAttackKind(last.kind) && d.targeting === 'own_pet') {
      const t = unitOf(st, last.target);
      if (t && t.owner === p && t.hp * 100 < t.maxHp * 50) return spell(c, t.uid);
    }
  }
  return act('pass');
}

function main(rules: DuelRules, st: DuelState, p: number): DuelAction {
  const pl = st.players[p], en = st.players[1 - p], cfg = rules.config;
  for (const u of sortBy(pl.units, (x) => -powerU(x), (x) => x.uid)) {
    const cd = cardOf(st, u.cardId);
    if (u.summonedTurn === st.turn || u.evolvedTurn === st.turn || u.formIndex + 1 >= cd.forms.length) continue;
    const cost = cfg.evolveCost[Math.min(cfg.evolveCost.length - 1, Math.max(1, u.stage))];
    if (pl.energy >= cost) return { type: 'evolve', unit: u.uid };
  }
  if (!pl.normalSummonUsed) {
    const pets = pl.hand.filter((c) => cardOf(st, c).kind === 'pet');
    for (const c of sortBy(pets, (x) => -powerF(cardOf(st, x).forms[0]), (x) => x)) {
      const f = cardOf(st, c).forms[0];
      const need = tributesFor(rules, f);
      const tributes = sortBy(pl.units, powerU, (u) => u.uid).slice(0, need);
      if (tributes.length < need) continue;
      if (need > 0 && powerF(f) * 10 < tributes.reduce((a, u) => a + powerU(u), 0) * 12) continue;
      const zone = pickZone(rules, pl, f.role, tributes.map((t) => t.uid));
      if (zone < 0) continue;
      return { type: 'summon', card: c, zone, tributes: tributes.map((t) => t.uid) };
    }
  }
  for (const c of sortBy(pl.hand, (x) => x)) {
    const cd = cardOf(st, c);
    if (cd.kind !== 'tactic') continue;
    const d = rules.tactics[cd.tacticId!];
    if (d.type !== 'spell' || pl.energy < d.cost) continue;
    const a = spellChoice(st, p, c, d);
    if (a && targetsValid(rules, st, p, d, a.targets ?? []) && limitsOk(pl, d)) return a;
  }
  for (let z = 0; z < pl.zones.length; z++) {
    if (pl.zones[z] !== null) continue;
    const traps = pl.hand.filter((c) => cardOf(st, c).kind === 'tactic' && rules.tactics[cardOf(st, c).tacticId!].type === 'trap');
    const trap = traps.length ? Math.min(...traps) : 0;
    if (trap > 0) return { type: 'set', card: trap, zone: z };
    break;
  }
  const firstTurn = p === st.first && pl.turnsTaken === 1;
  if (!firstTurn && en.units.length > 0 && pl.units.some((u) => canAct(st, u))) return act('battle');
  return act('end');
}

function pickZone(rules: DuelRules, pl: DuelPlayerState, role: string, freed: number[]): number {
  const back = role === 'mage' || role === 'support';
  const order = back ? [3, 4, 0, 1, 2] : [0, 1, 2, 3, 4];
  for (const z of order) if (z < rules.config.petZones && !pl.units.some((u) => u.zone === z && !freed.includes(u.uid))) return z;
  return -1;
}

function spellChoice(st: DuelState, p: number, c: number, d: TacticDef): DuelAction | null {
  const pl = st.players[p], en = st.players[1 - p];
  const hurt = sortBy(pl.units.filter((u) => u.hp * 100 < u.maxHp * 50), (u) => idiv(u.hp * 1000, u.maxHp), (u) => u.uid)[0];
  const canBattle = !(p === st.first && pl.turnsTaken === 1) && pl.units.some((u) => canAct(st, u)) && en.units.length > 0;
  const below70 = pl.units.filter((u) => u.hp * 100 < u.maxHp * 70).length;
  switch (d.id) {
    case 'sp_herbal_tonic': return hurt ? spell(c, hurt.uid) : null;
    case 'sp_group_remedy': return below70 >= 2 ? spell(c) : null;
    case 'sp_nature_blessing': return below70 >= 2 ? spell(c) : null;
    case 'sp_power_surge': {
      const best = sortBy(pl.units.filter((u) => canAct(st, u)), (u) => -u.atk, (u) => u.uid)[0];
      return canBattle && best ? spell(c, best.uid) : null;
    }
    case 'sp_battle_cry': return canBattle && pl.units.filter((u) => canAct(st, u)).length >= 2 ? spell(c) : null;
    case 'sp_tactical_draw': return pl.hand.length <= 4 ? spell(c) : null;
    case 'sp_energy_well': return pl.hand.filter((h) => cardOf(st, h).kind === 'tactic').length >= 2 ? spell(c) : null;
    case 'sp_scout': return !pl.hand.some((h) => cardOf(st, h).kind === 'pet') ? spell(c) : null;
    case 'sp_fire_storm': return en.units.length >= 2 ? spell(c) : null;
    case 'sp_tidal_wave': return en.units.filter((u) => u.zone < 3).length >= 2 ? spell(c) : null;
    case 'sp_thunderbolt': { const t = sortBy(en.units, (u) => u.hp, (u) => u.uid)[0]; return t ? spell(c, t.uid) : null; }
    case 'sp_call_reinforcement': {
      if (!pl.normalSummonUsed) return null;
      const hs = pl.hand.filter((x) => x !== c && cardOf(st, x).kind === 'pet' && cardOf(st, x).forms[0].stage <= 1 && !cardOf(st, x).forms[0].divine);
      const h = hs.length ? Math.min(...hs) : 0;
      return h > 0 ? spell(c, h) : null;
    }
    case 'sp_second_chance': {
      const gs = pl.grave.filter((x) => cardOf(st, x).kind === 'pet' && cardOf(st, x).forms[0].stage <= 2 && !cardOf(st, x).forms[0].divine);
      const g = gs.length ? Math.max(...gs) : 0;
      return g > 0 ? spell(c, g) : null;
    }
    case 'sp_dispel': { for (let z = 0; z < en.zones.length; z++) if (en.zones[z] !== null) return spell(c, z); return null; }
    default: return null;
  }
}

function battle(rules: DuelRules, st: DuelState, p: number): DuelAction {
  const pl = st.players[p];
  for (const u of sortBy(pl.units.filter((x) => canAct(st, x)), (x) => -x.atk, (x) => x.uid)) {
    const legal = legalTargets(st, p);
    if (legal.length === 0) break;
    const target = sortBy(legal, (t) => t.hp, (t) => t.uid)[0];
    for (const kind of ['ultimate', 'skill'] as const) {
      const id = kind === 'skill' ? u.skill : u.ultimate;
      const sk = id ? rules.skills[id] : undefined;
      if (!sk || pl.energy < sk.cost) continue;
      const offensive = sk.target.startsWith('enemy');
      const allyNeedsHelp = pl.units.some((x) => x.hp * 100 < x.maxHp * 60);
      if (!offensive && !allyNeedsHelp) continue;
      return { type: kind, unit: u.uid, target: target.uid };
    }
    return { type: 'attack', unit: u.uid, target: target.uid };
  }
  return act('end');
}
