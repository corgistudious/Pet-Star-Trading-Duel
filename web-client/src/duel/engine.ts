// TypeScript port of shared-contracts/Runtime/Rules/Duel/DuelEngine.cs for the Interactive Web Preview.
// The C# engine is authoritative (server). This port must stay line-for-line equivalent: same validation order,
// same integer truncation, same RNG consumption. engine.test.ts replays C#-generated fixtures to prove it.
import { nextInt } from './rng';
import {
  DuelRuleError, finished, toAct,
  type ChainLink, type DuelAction, type DuelCard, type DuelPassive, type DuelPlayerState, type DuelRules, type DuelSkill, type DuelState,
  type DuelUnit, type PetFormStats, type SetCard, type TacticDef, type TacticEffect,
} from './model';

const idiv = (a: number, b: number) => Math.trunc(a / b);
/** Stable sort (LINQ OrderBy semantics) on a copy. */
const sortBy = <T>(xs: T[], ...keys: ((x: T) => number)[]) =>
  xs.map((x, i) => ({ x, i })).sort((p, q) => { for (const k of keys) { const d = k(p.x) - k(q.x); if (d !== 0) return d; } return p.i - q.i; }).map((o) => o.x);
const isAttackKind = (k: string) => k === 'attack' || k === 'skill' || k === 'ultimate';

function R(st: DuelState, n: number): number { const [s, v] = nextInt(st.rng, n); st.rng = s; return v; }
function log(st: DuelState, type: string, p = -1, a = 0, b = 0, n = 0, s = '') { st.log.push({ type, p, a, b, n, s }); }
const card = (st: DuelState, id: number) => st.cards[id - 1];

// ------------------------------------------------------------------ setup

export interface SetupPlayer { name: string; deck: Omit<DuelCard, 'id'>[] }

export function create(rules: DuelRules, a: SetupPlayer, b: SetupPlayer, seed: bigint): DuelState {
  const st: DuelState = { rng: seed, cards: [], players: [], turn: 0, active: 0, first: 0, phase: 'main', chain: [], priority: -1, lastPassed: false,
    event: null, eventPlayer: -1, eventUnit: 0, winner: null, endReason: null, nextUid: 1, version: 0, log: [] };
  for (const s of [a, b]) {
    const pl: DuelPlayerState = { name: s.name, lp: rules.config.startLp, deck: [], hand: [], grave: [], units: [], zones: [], energy: 0, maxEnergy: 0,
      normalSummonUsed: false, turnsTaken: 0, usedThisTurn: [], usedThisDuel: [], timeouts: 0 };
    for (let z = 0; z < rules.config.spellTrapZones; z++) pl.zones.push(null);
    for (const c of s.deck) {
      const copy: DuelCard = { id: st.cards.length + 1, kind: c.kind, tacticId: c.tacticId ?? null, cardInstanceId: c.cardInstanceId, forms: c.forms };
      st.cards.push(copy); pl.deck.push(copy.id);
    }
    st.players.push(pl);
  }
  for (let p = 0; p < 2; p++) shuffle(st, st.players[p].deck);
  st.first = R(st, 2);
  log(st, 'coin', st.first);
  for (let p = 0; p < 2; p++) draw(st, p, rules.config.startHand, false);
  startTurn(rules, st, st.first);
  return st;
}

function shuffle(st: DuelState, deck: number[]) {
  for (let i = deck.length - 1; i > 0; i--) { const j = R(st, i + 1); [deck[i], deck[j]] = [deck[j], deck[i]]; }
}

// ------------------------------------------------------------------ turn flow

function startTurn(rules: DuelRules, st: DuelState, p: number) {
  const cfg = rules.config;
  st.active = p; st.turn++;
  const pl = st.players[p];
  pl.turnsTaken++;
  if (pl.turnsTaken > cfg.maxTurnsPerPlayer) { finishByLimit(st); return; }
  pl.maxEnergy = Math.min(cfg.maxEnergy, cfg.startEnergy + (pl.turnsTaken - 1) * cfg.energyPerTurn);
  pl.energy = pl.maxEnergy;
  pl.normalSummonUsed = false;
  pl.usedThisTurn = [];
  for (const u of pl.units) { u.acted = false; u.moved = false; }
  st.phase = 'main';
  log(st, 'turn', p, st.turn, pl.turnsTaken);
  if (pl.deck.length === 0) { finish(st, 1 - p, 'deck_out'); return; }
  draw(st, p, 1, true);
}

function draw(st: DuelState, p: number, count: number, phase: boolean) {
  const pl = st.players[p];
  let drawn = 0;
  for (let i = 0; i < count && pl.deck.length > 0; i++) { pl.hand.push(pl.deck[0]); pl.deck.splice(0, 1); drawn++; }
  if (drawn > 0) log(st, phase ? 'draw' : 'draw_extra', p, 0, 0, drawn);
}

function endTurn(rules: DuelRules, st: DuelState) {
  const cfg = rules.config;
  const p = st.active;
  const pl = st.players[p];
  for (const u of sortBy(pl.units, (x) => x.zone)) {
    if (has(u, 'burn')) applyDamage(rules, st, u, Math.max(1, idiv(u.maxHp * cfg.burnPct, 100)), 'burn');
    if (has(u, 'poison')) applyDamage(rules, st, u, Math.max(1, idiv(u.maxHp * cfg.poisonPct, 100)), 'poison');
    const pas = passive(rules, u);
    if (pas && pas.type === 'regen_round_end' && u.hp > 0) heal(st, u, idiv(u.maxHp * pas.pct, 100));
  }
  processDeaths(rules, st);
  if (finished(st)) return;
  for (const pp of st.players)
    for (const u of pp.units) {
      u.buffs = u.buffs.filter((bf) => !(bf.untilTurn <= st.turn));
      u.statuses = u.statuses.filter((s) => !(s.type === 'guard' || s.type === 'endure'));
      if (u.owner === p) {
        for (const s of u.statuses) s.remaining--;
        u.statuses = u.statuses.filter((s) => !(s.remaining <= 0));
      }
    }
  while (pl.hand.length > cfg.maxHand) {
    const c = pl.hand[pl.hand.length - 1];
    pl.hand.splice(pl.hand.length - 1, 1);
    pl.grave.push(c);
    log(st, 'discard', p, c);
  }
  log(st, 'end_turn', p, st.turn);
  startTurn(rules, st, 1 - p);
}

function finish(st: DuelState, winner: number, reason: string) {
  if (finished(st)) return;
  st.phase = 'finished'; st.winner = winner; st.endReason = reason; st.priority = -1; st.chain = []; st.event = null;
  log(st, 'finish', winner, 0, 0, 0, reason);
}

function finishByLimit(st: DuelState) {
  const lp0 = st.players[0].lp, lp1 = st.players[1].lp;
  if (lp0 !== lp1) { finish(st, lp0 > lp1 ? 0 : 1, 'turn_limit_lp'); return; }
  const hp0 = st.players[0].units.reduce((a, u) => a + u.hp, 0), hp1 = st.players[1].units.reduce((a, u) => a + u.hp, 0);
  if (hp0 !== hp1) { finish(st, hp0 > hp1 ? 0 : 1, 'turn_limit_hp'); return; }
  finish(st, -1, 'turn_limit_draw');
}

// ------------------------------------------------------------------ public API

export function apply(rules: DuelRules, st: DuelState, p: number, a: DuelAction) {
  if (finished(st)) throw new DuelRuleError('duel_finished');
  if (p < 0 || p > 1) throw new DuelRuleError('invalid_player');
  if (a.type === 'surrender') { finish(st, 1 - p, 'surrender'); st.version++; return; }
  if (p !== toAct(st)) throw new DuelRuleError('not_your_turn');
  if (st.priority >= 0) {
    switch (a.type) {
      case 'pass': pass(rules, st, p); break;
      case 'activate': activateTrap(rules, st, p, a); break;
      case 'spell': activateSpell(rules, st, p, a); break;
      default: throw new DuelRuleError('reaction_window_open');
    }
  } else {
    switch (a.type) {
      case 'summon': summon(rules, st, p, a); break;
      case 'spell': activateSpell(rules, st, p, a); break;
      case 'set': setCard(rules, st, p, a); break;
      case 'evolve': evolve(rules, st, p, a); break;
      case 'move': move(rules, st, p, a); break;
      case 'battle': enterBattle(st, p); break;
      case 'attack': case 'skill': case 'ultimate': attack(rules, st, p, a); break;
      case 'end': endTurn(rules, st); break;
      default: throw new DuelRuleError('invalid_action');
    }
  }
  autoPass(rules, st);
  st.version++;
}

export function timeout(rules: DuelRules, st: DuelState) {
  if (finished(st)) return;
  const p = toAct(st);
  st.players[p].timeouts++;
  log(st, 'timeout', p, st.players[p].timeouts);
  if (st.players[p].timeouts >= rules.config.timeoutsToForfeit) { finish(st, 1 - p, 'timeout'); st.version++; return; }
  if (st.priority >= 0) pass(rules, st, p); else endTurn(rules, st);
  autoPass(rules, st);
  st.version++;
}

export function unitOf(st: DuelState, uid: number): DuelUnit | null {
  for (const pl of st.players) for (const u of pl.units) if (u.uid === uid) return u;
  return null;
}

// ------------------------------------------------------------------ main phase actions

function requireMain(st: DuelState, p: number) {
  if (st.active !== p || st.priority >= 0) throw new DuelRuleError('not_your_turn');
  if (st.phase !== 'main') throw new DuelRuleError('not_main_phase');
}

export function tributesFor(rules: DuelRules, f: PetFormStats): number {
  const t = rules.config.tributes;
  if (f.divine) return t.divine;
  return f.stage >= 3 ? t.stage3 : f.stage === 2 ? t.stage2 : t.stage1;
}

function summon(rules: DuelRules, st: DuelState, p: number, a: DuelAction) {
  requireMain(st, p);
  const pl = st.players[p];
  const c = a.card ?? 0, zoneIn = a.zone ?? -1;
  if (!pl.hand.includes(c)) throw new DuelRuleError('card_not_in_hand');
  const cd = card(st, c);
  if (cd.kind !== 'pet') throw new DuelRuleError('not_a_pet');
  if (pl.normalSummonUsed) throw new DuelRuleError('summon_used');
  const form = cd.forms[0];
  const need = tributesFor(rules, form);
  const tributes = [...new Set(a.tributes ?? [])];
  if (tributes.length !== need) throw new DuelRuleError('tributes_required');
  for (const uid of tributes) if (!pl.units.some((u) => u.uid === uid)) throw new DuelRuleError('invalid_tribute');
  const zonesFree = rules.config.petZones - pl.units.length + tributes.length;
  if (zonesFree <= 0) throw new DuelRuleError('field_full');
  if (zoneIn < 0 || zoneIn >= rules.config.petZones) throw new DuelRuleError('invalid_zone');
  if (pl.units.some((u) => u.zone === zoneIn && !tributes.includes(u.uid))) throw new DuelRuleError('zone_occupied');
  for (const uid of tributes) {
    const u = pl.units.find((x) => x.uid === uid)!;
    pl.units.splice(pl.units.indexOf(u), 1); pl.grave.push(u.cardId);
    log(st, 'tribute', p, u.uid, u.cardId);
  }
  pl.hand.splice(pl.hand.indexOf(c), 1);
  pl.normalSummonUsed = true;
  const unit = place(rules, st, p, cd, 0, zoneIn, 100, 'normal');
  st.event = 'summon'; st.eventPlayer = p; st.eventUnit = unit.uid;
  openWindow(st, 1 - p);
}

function place(rules: DuelRules, st: DuelState, p: number, cd: DuelCard, formIndex: number, zone: number, hpPct: number, how: string): DuelUnit {
  const f = cd.forms[formIndex];
  const u: DuelUnit = { uid: st.nextUid++, cardId: cd.id, owner: p, zone, formIndex: 0, formId: '', stage: 0, divine: false, element: '', role: '', maxHp: 0, hp: 0,
    atk: 0, def: 0, spd: 0, critBp: 0, shield: 0, skill: null, ultimate: null, passive: null, summonedTurn: st.turn, acted: false, moved: false, evolvedTurn: 0, statuses: [], buffs: [] };
  setForm(rules, u, f, formIndex);
  u.hp = Math.max(1, idiv(u.maxHp * hpPct, 100));
  st.players[p].units.push(u);
  log(st, 'summon', p, u.uid, cd.id, zone, how + '|' + f.formId);
  const pas = passive(rules, u);
  if (pas && pas.type === 'battle_start_energy') st.players[p].energy += pas.amount;
  return u;
}

function setForm(rules: DuelRules, u: DuelUnit, f: PetFormStats, formIndex: number) {
  u.formIndex = formIndex; u.formId = f.formId; u.stage = f.stage; u.divine = f.divine; u.element = f.element; u.role = f.role;
  u.maxHp = f.hp; u.atk = f.atk; u.def = f.def; u.spd = f.spd; u.critBp = f.critBp;
  u.skill = f.skill ?? null; u.ultimate = f.ultimate ?? null; u.passive = f.passive ?? null;
  const pas = passive(rules, u);
  if (pas && pas.type === 'stat_bonus') {
    if (pas.stat === 'atk') u.atk = idiv(u.atk * (100 + pas.pct), 100);
    if (pas.stat === 'def') u.def = idiv(u.def * (100 + pas.pct), 100);
    if (pas.stat === 'spd') u.spd = idiv(u.spd * (100 + pas.pct), 100);
    if (pas.stat === 'crit') u.critBp = Math.min(5000, u.critBp + pas.pct * 100);
  }
}

function setCard(rules: DuelRules, st: DuelState, p: number, a: DuelAction) {
  requireMain(st, p);
  const pl = st.players[p];
  const c = a.card ?? 0, z = a.zone ?? -1;
  if (!pl.hand.includes(c)) throw new DuelRuleError('card_not_in_hand');
  const cd = card(st, c);
  if (cd.kind !== 'tactic' || rules.tactics[cd.tacticId!].type !== 'trap') throw new DuelRuleError('only_traps_can_be_set');
  if (z < 0 || z >= pl.zones.length) throw new DuelRuleError('invalid_zone');
  if (pl.zones[z] !== null) throw new DuelRuleError('zone_occupied');
  pl.hand.splice(pl.hand.indexOf(c), 1);
  pl.zones[z] = { cardId: c, tacticId: cd.tacticId!, setTurn: st.turn };
  log(st, 'set', p, z);
}

function evolve(rules: DuelRules, st: DuelState, p: number, a: DuelAction) {
  requireMain(st, p);
  const pl = st.players[p];
  const u = pl.units.find((x) => x.uid === a.unit);
  if (!u) throw new DuelRuleError('invalid_unit');
  if (u.summonedTurn === st.turn) throw new DuelRuleError('summoned_this_turn');
  if (u.evolvedTurn === st.turn) throw new DuelRuleError('already_evolved');
  const cd = card(st, u.cardId);
  if (u.formIndex + 1 >= cd.forms.length) throw new DuelRuleError('final_form');
  const ec = rules.config.evolveCost;
  const cost = ec[Math.min(ec.length - 1, Math.max(1, u.stage))];
  if (pl.energy < cost) throw new DuelRuleError('not_enough_energy');
  pl.energy -= cost;
  const oldMax = u.maxHp, oldHp = u.hp, from = u.formId;
  setForm(rules, u, cd.forms[u.formIndex + 1], u.formIndex + 1);
  u.hp = Math.max(1, idiv(oldHp * u.maxHp, oldMax));
  u.evolvedTurn = st.turn;
  log(st, 'evolve', p, u.uid, u.formIndex, u.hp, from + '>' + u.formId);
}

function move(rules: DuelRules, st: DuelState, p: number, a: DuelAction) {
  requireMain(st, p);
  const pl = st.players[p];
  const u = pl.units.find((x) => x.uid === a.unit);
  if (!u) throw new DuelRuleError('invalid_unit');
  if (u.moved) throw new DuelRuleError('already_moved');
  const z = a.zone ?? -1;
  if (z < 0 || z >= rules.config.petZones || z === u.zone) throw new DuelRuleError('invalid_zone');
  const other = pl.units.find((x) => x.zone === z);
  if (other) { other.zone = u.zone; other.moved = true; }
  u.zone = z; u.moved = true;
  log(st, 'move', p, u.uid, z);
}

function enterBattle(st: DuelState, p: number) {
  requireMain(st, p);
  if (p === st.first && st.players[p].turnsTaken === 1) throw new DuelRuleError('first_turn_no_battle');
  st.phase = 'battle';
  log(st, 'battle', p);
}

// ------------------------------------------------------------------ battle

export function legalTargets(st: DuelState, attacker: number): DuelUnit[] {
  const enemy = st.players[1 - attacker].units;
  const taunt = enemy.filter((u) => has(u, 'taunt'));
  if (taunt.length > 0) return sortBy(taunt, (u) => u.zone);
  const front = enemy.filter((u) => u.zone < 3);
  return sortBy(front.length > 0 ? front : enemy, (u) => u.zone);
}

export const canAct = (st: DuelState, u: DuelUnit) => u.summonedTurn !== st.turn && !u.acted && !has(u, 'stun') && u.hp > 0;

function attack(rules: DuelRules, st: DuelState, p: number, a: DuelAction) {
  if (st.active !== p || st.priority >= 0) throw new DuelRuleError('not_your_turn');
  if (st.phase !== 'battle') throw new DuelRuleError('not_battle_phase');
  const pl = st.players[p];
  const u = pl.units.find((x) => x.uid === a.unit);
  if (!u) throw new DuelRuleError('invalid_unit');
  if (u.summonedTurn === st.turn) throw new DuelRuleError('summoned_this_turn');
  if (u.acted) throw new DuelRuleError('already_acted');
  if (has(u, 'stun')) throw new DuelRuleError('unit_stunned');
  const kind = a.type;
  let sk: DuelSkill | null = null;
  if (kind !== 'attack') {
    const id = kind === 'skill' ? u.skill : u.ultimate;
    if (!id || !rules.skills[id]) throw new DuelRuleError('no_such_skill');
    sk = rules.skills[id];
    if (pl.energy < sk.cost) throw new DuelRuleError('not_enough_energy');
  }
  const offensive = sk === null || sk.target.startsWith('enemy');
  let target = 0;
  if (offensive) {
    const legal = legalTargets(st, p);
    if (legal.length === 0) throw new DuelRuleError('no_target');
    const single = sk === null || sk.target === 'enemy_single';
    if (single) {
      if (!legal.some((t) => t.uid === a.target)) throw new DuelRuleError('invalid_target');
      target = a.target!;
    } else target = legal[0].uid;
  }
  if (sk) pl.energy -= sk.cost;
  u.acted = true;
  log(st, kind, p, u.uid, target, 0, sk?.id ?? '');
  addLink(rules, st, { player: p, kind, cardId: 0, tacticId: null, targets: [], attacker: u.uid, target, negated: false });
}

// ------------------------------------------------------------------ spells & traps

function lastMatches(st: DuelState, p: number, what: string): boolean {
  if (st.chain.length === 0) return false;
  const last = st.chain[st.chain.length - 1];
  if (last.player === p) return false;
  return what === 'spell' ? last.kind === 'spell' : what === 'trap' ? last.kind === 'trap' : isAttackKind(last.kind);
}
function negateUsable(st: DuelState, p: number, d: TacticDef) {
  const neg = d.effects.find((e) => e.op === 'negate');
  return !neg || lastMatches(st, p, neg.what ?? '');
}
const limitsOk = (pl: DuelPlayerState, d: TacticDef) => !(d.oncePerTurn && pl.usedThisTurn.includes(d.id)) && !(d.oncePerDuel && pl.usedThisDuel.includes(d.id));

function activateSpell(rules: DuelRules, st: DuelState, p: number, a: DuelAction) {
  const pl = st.players[p];
  const c = a.card ?? 0;
  if (!pl.hand.includes(c)) throw new DuelRuleError('card_not_in_hand');
  const cd = card(st, c);
  if (cd.kind !== 'tactic') throw new DuelRuleError('not_a_spell');
  const d = rules.tactics[cd.tacticId!];
  if (d.type !== 'spell') throw new DuelRuleError('not_a_spell');
  if (d.speed === 'normal') {
    if (st.priority >= 0 || st.active !== p) throw new DuelRuleError('quick_only_now');
    if (st.phase !== 'main') throw new DuelRuleError('not_main_phase');
  }
  const targets = a.targets ?? [];
  if (pl.energy < d.cost) throw new DuelRuleError('not_enough_energy');
  if (!limitsOk(pl, d)) throw new DuelRuleError('card_limit_reached');
  if (!negateUsable(st, p, d)) throw new DuelRuleError('nothing_to_negate');
  if (!targetsValid(rules, st, p, d, targets)) throw new DuelRuleError('invalid_target');
  pl.energy -= d.cost;
  pl.hand.splice(pl.hand.indexOf(c), 1); pl.grave.push(c);
  if (d.oncePerTurn) pl.usedThisTurn.push(d.id);
  if (d.oncePerDuel) pl.usedThisDuel.push(d.id);
  log(st, 'spell', p, c, 0, d.cost, d.id);
  addLink(rules, st, { player: p, kind: 'spell', cardId: c, tacticId: d.id, targets: [...targets], attacker: 0, target: 0, negated: false });
}

export function trapReady(rules: DuelRules, st: DuelState, p: number, s: SetCard | null): boolean {
  if (!s || s.setTurn >= st.turn) return false;
  const d = rules.tactics[s.tacticId];
  const pl = st.players[p];
  return pl.energy >= d.cost && limitsOk(pl, d) && triggerMatches(st, p, d);
}

function triggerMatches(st: DuelState, p: number, d: TacticDef): boolean {
  const last = st.chain.length > 0 ? st.chain[st.chain.length - 1] : null;
  switch (d.trigger) {
    case 'attack': {
      if (!last || last.player === p || !isAttackKind(last.kind)) return false;
      const t = unitOf(st, last.target);
      return t !== null && t.owner === p;
    }
    case 'summon': return st.chain.length === 0 && st.event === 'summon' && st.eventPlayer !== p && unitOf(st, st.eventUnit) !== null;
    case 'spell': return last !== null && last.player !== p && last.kind === 'spell';
    case 'trap': return last !== null && last.player !== p && last.kind === 'trap';
    case 'destroyed': return st.chain.length === 0 && st.event === 'destroyed' && st.eventPlayer === p;
    default: return false;
  }
}

function activateTrap(rules: DuelRules, st: DuelState, p: number, a: DuelAction) {
  const pl = st.players[p];
  const z = a.zone ?? -1;
  if (z < 0 || z >= pl.zones.length || pl.zones[z] === null) throw new DuelRuleError('no_trap_there');
  const s = pl.zones[z]!;
  if (s.setTurn >= st.turn) throw new DuelRuleError('trap_set_this_turn');
  const d = rules.tactics[s.tacticId];
  if (!triggerMatches(st, p, d)) throw new DuelRuleError('trap_condition_not_met');
  if (pl.energy < d.cost) throw new DuelRuleError('not_enough_energy');
  if (!limitsOk(pl, d)) throw new DuelRuleError('card_limit_reached');
  pl.energy -= d.cost;
  pl.zones[z] = null; pl.grave.push(s.cardId);
  if (d.oncePerTurn) pl.usedThisTurn.push(d.id);
  if (d.oncePerDuel) pl.usedThisDuel.push(d.id);
  const link: ChainLink = { player: p, kind: 'trap', cardId: s.cardId, tacticId: d.id, targets: [], attacker: 0, target: 0, negated: false };
  const last = st.chain.length > 0 ? st.chain[st.chain.length - 1] : null;
  if (d.trigger === 'attack' && last) { link.attacker = last.attacker; link.target = last.target; }
  if (d.trigger === 'summon') link.target = st.eventUnit;
  log(st, 'trap', p, s.cardId, z, d.cost, d.id);
  addLink(rules, st, link);
}

export function targetsValid(rules: DuelRules, st: DuelState, p: number, d: TacticDef, t: number[]): boolean {
  const pl = st.players[p], en = st.players[1 - p];
  const maxStage = d.effects.reduce((m, e) => Math.max(m, e.maxStage), 0);
  const zoneFree = pl.units.length < rules.config.petZones;
  switch (d.targeting) {
    case 'none': return true;
    case 'own_pet': return t.length >= 1 && pl.units.some((u) => u.uid === t[0]);
    case 'enemy_pet': return t.length >= 1 && en.units.some((u) => u.uid === t[0]);
    case 'own_two': return t.length >= 2 && t[0] !== t[1] && pl.units.some((u) => u.uid === t[0]) && pl.units.some((u) => u.uid === t[1]);
    case 'hand_pet': return t.length >= 1 && zoneFree && pl.hand.includes(t[0]) && card(st, t[0]).kind === 'pet' && !card(st, t[0]).forms[0].divine && card(st, t[0]).forms[0].stage <= maxStage;
    case 'grave_pet': return t.length >= 1 && zoneFree && pl.grave.includes(t[0]) && card(st, t[0]).kind === 'pet' && !card(st, t[0]).forms[0].divine && card(st, t[0]).forms[0].stage <= maxStage;
    case 'enemy_st': return t.length >= 1 && t[0] >= 0 && t[0] < en.zones.length && en.zones[t[0]] !== null;
    default: return false;
  }
}

export function hasResponse(rules: DuelRules, st: DuelState, p: number): boolean {
  const pl = st.players[p];
  for (let z = 0; z < pl.zones.length; z++) if (trapReady(rules, st, p, pl.zones[z])) return true;
  for (const c of pl.hand) {
    const cd = card(st, c);
    if (cd.kind !== 'tactic') continue;
    const d = rules.tactics[cd.tacticId!];
    if (d.type !== 'spell' || d.speed !== 'quick' || pl.energy < d.cost || !limitsOk(pl, d) || !negateUsable(st, p, d)) continue;
    if (d.targeting === 'none' || (d.targeting === 'own_pet' && pl.units.length > 0) || (d.targeting === 'enemy_pet' && st.players[1 - p].units.length > 0)) return true;
  }
  return false;
}

// ------------------------------------------------------------------ reaction windows & chain

function openWindow(st: DuelState, responder: number) { st.priority = responder; st.lastPassed = false; }

function addLink(rules: DuelRules, st: DuelState, link: ChainLink) {
  if (st.chain.length >= rules.config.chainMaxDepth) throw new DuelRuleError('chain_too_deep');
  st.chain.push(link);
  st.priority = 1 - link.player; st.lastPassed = false;
}

function autoPass(rules: DuelRules, st: DuelState) {
  let guard = 0;
  while (!finished(st) && st.priority >= 0 && guard++ < 64 && !hasResponse(rules, st, st.priority)) pass(rules, st, st.priority);
}

function pass(rules: DuelRules, st: DuelState, p: number) {
  if (st.chain.length === 0) { closeWindow(st); return; }
  if (st.lastPassed) { resolveChain(rules, st); return; }
  st.lastPassed = true;
  st.priority = 1 - p;
}

function closeWindow(st: DuelState) { st.priority = -1; st.event = null; st.lastPassed = false; }

function resolveChain(rules: DuelRules, st: DuelState) {
  const destroyedBy = new Set<number>();
  for (let i = st.chain.length - 1; i >= 0 && !finished(st); i--) {
    const link = st.chain[i];
    if (link.negated) { log(st, 'negated', link.player, link.cardId, link.attacker, i, link.tacticId ?? link.kind); continue; }
    resolveLink(rules, st, i);
    for (const owner of processDeaths(rules, st)) destroyedBy.add(owner);
  }
  st.chain = [];
  closeWindow(st);
  if (finished(st)) return;
  for (const owner of [1 - st.active, st.active])
    if (destroyedBy.has(owner) && st.players[owner].zones.some((z) => z !== null && rules.tactics[z.tacticId].trigger === 'destroyed')) {
      st.event = 'destroyed'; st.eventPlayer = owner;
      openWindow(st, owner);
      break;
    }
}

function resolveLink(rules: DuelRules, st: DuelState, i: number) {
  const link = st.chain[i];
  switch (link.kind) {
    case 'attack': {
      const att = unitOf(st, link.attacker), tgt = unitOf(st, link.target);
      if (!att || !tgt) { log(st, 'fizzle', link.player, link.attacker, link.target); return; }
      const dmg = basicDamage(rules, st, att, tgt, 100);
      const dealt = applyDamage(rules, st, tgt, dmg, 'attack');
      const pas = passive(rules, att);
      if (pas && pas.type === 'lifesteal' && dealt > 0) heal(st, att, idiv(dealt * pas.pct, 100));
      return;
    }
    case 'skill': case 'ultimate': resolveSkill(rules, st, link); return;
    default: {
      const d = rules.tactics[link.tacticId!];
      if (link.kind === 'spell' && !targetsValid(rules, st, link.player, d, link.targets) && d.targeting !== 'none') { log(st, 'fizzle', link.player, link.cardId, 0, 0, d.id); return; }
      for (const e of d.effects) resolveTactic(rules, st, i, link, d, e);
    }
  }
}

function resolveSkill(rules: DuelRules, st: DuelState, link: ChainLink) {
  const att = unitOf(st, link.attacker);
  if (!att) { log(st, 'fizzle', link.player, link.attacker); return; }
  const sk = rules.skills[(link.kind === 'skill' ? att.skill : att.ultimate)!];
  const me = st.players[att.owner], en = st.players[1 - att.owner];
  let enemies: DuelUnit[], allies: DuelUnit[];
  switch (sk.target) {
    case 'enemy_single': { const t = unitOf(st, link.target); enemies = t ? [t] : []; break; }
    case 'enemy_all': enemies = sortBy(en.units, (u) => u.zone); break;
    case 'enemy_front_row': { const f = sortBy(en.units.filter((u) => u.zone < 3), (u) => u.zone); enemies = f.length > 0 ? f : sortBy(en.units, (u) => u.zone); break; }
    default: enemies = [];
  }
  switch (sk.target) {
    case 'ally_all': allies = sortBy(me.units, (u) => u.zone); break;
    case 'ally_lowest_hp': allies = sortBy(me.units, (u) => idiv(u.hp * 1000, u.maxHp), (u) => u.zone).slice(0, 1); break;
    case 'self': allies = [att]; break;
    default: allies = [];
  }
  const offensive = sk.target.startsWith('enemy');
  if (offensive && enemies.length === 0) { log(st, 'fizzle', link.player, link.attacker); return; }
  const cfg = rules.config;
  for (const e of sk.effects) {
    switch (e.type) {
      case 'damage':
        for (const t of [...enemies]) if (t.hp > 0) applyDamage(rules, st, t, basicDamage(rules, st, att, t, e.powerPct), link.kind);
        break;
      case 'heal': {
        const targets = offensive ? [att] : allies;
        let amt = idiv(e.powerPct * effAtk(rules, att) * cfg.damageScale, 20000);
        const pas = passive(rules, att);
        if (pas && pas.type === 'heal_bonus') amt = idiv(amt * (100 + pas.pct), 100);
        for (const t of targets) heal(st, t, amt);
        break;
      }
      case 'shield': {
        const targets = offensive ? [att] : allies;
        for (const t of targets) {
          const scale = e.scale ?? 'atk';
          const basis = scale === 'maxhp' ? att.maxHp : idiv((scale === 'def' ? att.def : effAtk(rules, att)) * cfg.damageScale, 100);
          const amt = idiv(e.powerPct * basis, 100);
          t.shield += amt;
          log(st, 'shield', t.owner, t.uid, 0, amt);
        }
        break;
      }
      case 'status': {
        const list = offensive && e.status !== 'taunt' ? enemies : (allies.length > 0 ? allies : [att]);
        for (const t of [...list]) if (t.hp > 0) addStatus(st, t, e.status!, e.duration <= 0 ? 1 : e.duration, e.chance);
        break;
      }
      case 'cleanse': for (const t of allies) cleanse(st, t); break;
      case 'energy': {
        const n = Math.max(1, idiv(e.powerPct, 100));
        me.energy += n;
        log(st, 'energy', att.owner, 0, 0, n);
        break;
      }
    }
  }
}

function tacticTargets(st: DuelState, link: ChainLink, target: string | null | undefined, owner: number): DuelUnit[] {
  const me = st.players[owner], en = st.players[1 - owner];
  const list: DuelUnit[] = [];
  const addU = (uid: number) => { const u = unitOf(st, uid); if (u) list.push(u); };
  switch (target) {
    case 't0': if (link.targets.length > 0) addU(link.targets[0]); break;
    case 'own_all': list.push(...sortBy(me.units, (u) => u.zone)); break;
    case 'enemy_all': list.push(...sortBy(en.units, (u) => u.zone)); break;
    case 'enemy_front': { const f = sortBy(en.units.filter((u) => u.zone < 3), (u) => u.zone); list.push(...(f.length > 0 ? f : sortBy(en.units, (u) => u.zone))); break; }
    case 'attacker': addU(link.attacker); break;
    case 'attack_target': case 'summoned': addU(link.target); break;
  }
  return list;
}

function resolveTactic(rules: DuelRules, st: DuelState, i: number, link: ChainLink, d: TacticDef, e: TacticEffect) {
  const p = link.player;
  const pl = st.players[p];
  const prev = i > 0 ? st.chain[i - 1] : null;
  switch (e.op) {
    case 'heal':
      for (const u of tacticTargets(st, link, e.target === 'own_element' ? 'own_all' : e.target, p))
        if (e.target !== 'own_element' || u.element === e.element) heal(st, u, idiv(u.maxHp * e.pct, 100));
      break;
    case 'buff':
      for (const u of tacticTargets(st, link, e.target, p)) {
        u.buffs.push({ stat: e.stat ?? 'atk', pct: e.pct, untilTurn: st.turn + Math.max(1, e.turns) - 1 });
        log(st, 'buff', u.owner, u.uid, 0, e.pct, e.stat ?? 'atk');
      }
      break;
    case 'shield':
      for (const u of tacticTargets(st, link, e.target, p)) {
        const amt = idiv(u.maxHp * e.pct, 100);
        u.shield += amt;
        log(st, 'shield', u.owner, u.uid, 0, amt);
      }
      break;
    case 'cleanse': for (const u of tacticTargets(st, link, e.target, p)) cleanse(st, u); break;
    case 'draw': draw(st, p, e.n, false); break;
    case 'energy': pl.energy += e.n; log(st, 'energy', p, 0, 0, e.n); break;
    case 'swap': {
      const a = unitOf(st, link.targets[0]), b = unitOf(st, link.targets[1]);
      if (a && b && a.owner === p && b.owner === p) { [a.zone, b.zone] = [b.zone, a.zone]; log(st, 'swap', p, a.uid, b.uid); }
      break;
    }
    case 'search_pet': {
      const idx = pl.deck.findIndex((c) => card(st, c).kind === 'pet');
      if (idx >= 0) { const c = pl.deck[idx]; pl.deck.splice(idx, 1); pl.hand.push(c); log(st, 'search', p, 0, 0, 1); }
      break;
    }
    case 'damage':
      for (const u of tacticTargets(st, link, e.target, p)) {
        if (u.hp <= 0) continue;
        const el = e.element === 'target' ? unitOf(st, link.target)?.element ?? null : e.element ?? null;
        const mult = el === null ? 100 : elementPct(rules, el, u.element);
        applyDamage(rules, st, u, Math.max(1, idiv(idiv(u.maxHp * e.pct, 100) * mult, 100)), d.id);
      }
      break;
    case 'status':
      for (const u of tacticTargets(st, link, e.target, p)) if (u.hp > 0) addStatus(st, u, e.status!, Math.max(1, e.turns), e.chance);
      break;
    case 'special_summon': {
      const c = link.targets.length > 0 ? link.targets[0] : 0;
      const zone = freeZone(rules, pl);
      if (pl.hand.includes(c) && zone >= 0) { pl.hand.splice(pl.hand.indexOf(c), 1); place(rules, st, p, card(st, c), 0, zone, 100, 'special'); }
      break;
    }
    case 'revive': {
      const c = link.targets.length > 0 ? link.targets[0] : 0;
      const zone = freeZone(rules, pl);
      if (pl.grave.includes(c) && zone >= 0) { pl.grave.splice(pl.grave.indexOf(c), 1); place(rules, st, p, card(st, c), 0, zone, e.pct, 'revive'); }
      break;
    }
    case 'negate':
      if (prev && !prev.negated && prev.player !== p && (e.what === 'spell' ? prev.kind === 'spell' : e.what === 'trap' ? prev.kind === 'trap' : isAttackKind(prev.kind))) {
        prev.negated = true; log(st, 'negate', p, i - 1, 0, 0, e.what ?? '');
      }
      break;
    case 'reflect': {
      const att = prev ? unitOf(st, prev.attacker) : null, tgt = prev ? unitOf(st, prev.target) : null;
      if (att && tgt) applyDamage(rules, st, att, Math.max(1, idiv(basicDamage(rules, st, att, tgt, 100), 2)), 'reflect');
      break;
    }
    case 'destroy_st': {
      const en = st.players[1 - p];
      const z = link.targets.length > 0 ? link.targets[0] : -1;
      if (z >= 0 && z < en.zones.length && en.zones[z] !== null) { en.grave.push(en.zones[z]!.cardId); en.zones[z] = null; log(st, 'destroy_st', p, z); }
      break;
    }
    case 'redirect':
      if (prev) {
        const cur = unitOf(st, prev.target);
        const cand = sortBy(pl.units.filter((u) => !cur || u.uid !== cur.uid), (u) => -u.hp, (u) => u.uid)[0];
        if (cand) { prev.target = cand.uid; log(st, 'redirect', p, cand.uid); }
      }
      break;
  }
}

function freeZone(rules: DuelRules, pl: DuelPlayerState) {
  for (let z = 0; z < rules.config.petZones; z++) if (!pl.units.some((u) => u.zone === z)) return z;
  return -1;
}

// ------------------------------------------------------------------ damage, statuses, deaths

export function elementPct(rules: DuelRules, attacker: string, defender: string) {
  const aStrong = (rules.strong[attacker] ?? []).includes(defender);
  const dStrong = (rules.strong[defender] ?? []).includes(attacker);
  return aStrong ? rules.config.strongPct : dStrong ? rules.config.weakPct : 100;
}

export function effAtk(rules: DuelRules, u: DuelUnit) {
  let atk = idiv(u.atk * (100 + u.buffs.filter((b) => b.stat === 'atk').reduce((a, b) => a + b.pct, 0)), 100);
  if (has(u, 'weaken')) atk = idiv(atk * rules.config.weakenPct, 100);
  const pas = passive(rules, u);
  if (pas && pas.type === 'low_hp_stat_bonus' && pas.stat === 'atk' && u.hp * 100 <= u.maxHp * 50) atk = idiv(atk * (100 + pas.pct), 100);
  return Math.max(1, atk);
}

export function basicDamage(rules: DuelRules, st: DuelState, att: DuelUnit, tgt: DuelUnit, powerPct: number) {
  const cfg = rules.config;
  const atk = effAtk(rules, att);
  let dmg = idiv(powerPct * atk * atk * cfg.damageScale, (atk + Math.max(0, tgt.def)) * 10000);
  dmg = idiv(dmg * elementPct(rules, att.element, tgt.element), 100);
  if (att.critBp > 0 && R(st, 10000) < att.critBp) dmg = idiv(dmg * cfg.critPct, 100);
  return Math.max(1, dmg);
}

export function applyDamage(rules: DuelRules, st: DuelState, u: DuelUnit, amount: number, source: string) {
  if (u.hp <= 0) return 0;
  let dmg = amount;
  if (has(u, 'guard')) dmg = idiv(dmg * rules.config.guardPct, 100);
  const pas = passive(rules, u);
  if (pas && pas.type === 'damage_reduction') dmg = idiv(dmg * (100 - pas.pct), 100);
  dmg = Math.max(1, dmg);
  const absorbed = Math.min(u.shield, dmg);
  u.shield -= absorbed;
  const rest = dmg - absorbed;
  const before = u.hp;
  u.hp -= rest;
  if (has(u, 'endure') && u.hp < 1) u.hp = 1;
  log(st, 'damage', u.owner, u.uid, absorbed, dmg, source);
  return before - Math.max(0, u.hp);
}

function heal(st: DuelState, u: DuelUnit, amount: number) {
  if (u.hp <= 0 || amount <= 0) return;
  const before = u.hp;
  u.hp = Math.min(u.maxHp, u.hp + amount);
  if (u.hp > before) log(st, 'heal', u.owner, u.uid, 0, u.hp - before);
}

function cleanse(st: DuelState, u: DuelUnit) {
  const before = u.statuses.length;
  u.statuses = u.statuses.filter((s) => !(s.type === 'stun' || s.type === 'weaken' || s.type === 'burn' || s.type === 'poison'));
  const n = before - u.statuses.length;
  if (n > 0) log(st, 'cleanse', u.owner, u.uid, 0, n);
}

export const has = (u: DuelUnit, status: string) => u.statuses.some((s) => s.type === status && s.remaining > 0);

function addStatus(st: DuelState, u: DuelUnit, status: string, turns: number, chance: number) {
  if (chance < 100 && R(st, 100) >= chance) return;
  if (u.owner === st.active && (status === 'stun' || status === 'weaken' || status === 'taunt')) turns++;
  const cur = u.statuses.find((s) => s.type === status);
  if (cur) cur.remaining = Math.max(cur.remaining, turns);
  else u.statuses.push({ type: status, remaining: turns });
  log(st, 'status', u.owner, u.uid, 0, turns, status);
}

const passive = (rules: DuelRules, u: DuelUnit): DuelPassive | null => (u.passive && rules.passives[u.passive]) || null;

function processDeaths(rules: DuelRules, st: DuelState): number[] {
  const owners: number[] = [];
  for (let p = 0; p < 2; p++) {
    const pl = st.players[p];
    for (const u of sortBy(pl.units.filter((x) => x.hp <= 0), (x) => x.zone)) {
      pl.units.splice(pl.units.indexOf(u), 1);
      pl.grave.push(u.cardId);
      pl.lp -= rules.config.deathPenalty;
      owners.push(p);
      log(st, 'destroyed', p, u.uid, u.cardId, rules.config.deathPenalty);
    }
  }
  const dead0 = st.players[0].lp <= 0, dead1 = st.players[1].lp <= 0;
  if (dead0 && dead1) finish(st, -1, 'both_lp_zero');
  else if (dead0) finish(st, 1, 'lp_zero');
  else if (dead1) finish(st, 0, 'lp_zero');
  return owners;
}

export { card as cardOf };
