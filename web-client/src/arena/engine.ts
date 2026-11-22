// TypeScript port of shared-contracts/Runtime/Rules/Arena/ArenaEngine.cs for the Interactive Web Preview.
// The C# engine is authoritative (server). This port keeps the same validation order, integer math and RNG
// consumption; engine.test.ts replays C#-generated matches and requires identical event logs.
import { nextInt } from '../duel/rng';
import {
  ArenaRuleError, amt, attacksOf, effectsOf, inPlay, isBasic,
  type ArenaAction, type ArenaAttack, type ArenaCardDef, type ArenaEffect, type ArenaMod, type ArenaPet, type ArenaPlayerState, type ArenaRules,
  type ArenaSetupPlayer, type ArenaState,
} from './model';
import { promoteChoice, setupChoice } from './ai';

const err = (code: string) => new ArenaRuleError(code);
export const card = (st: ArenaState, id: number) => st.cards[id - 1];
const sortBy = <T>(xs: T[], ...keys: ((x: T) => number)[]) =>
  xs.map((x, i) => ({ x, i })).sort((p, q) => { for (const k of keys) { const d = k(p.x) - k(q.x); if (d !== 0) return d; } return p.i - q.i; }).map((o) => o.x);
const remove = <T>(xs: T[], x: T) => { const i = xs.indexOf(x); if (i >= 0) xs.splice(i, 1); return i >= 0; };
const sum = (xs: number[]) => xs.reduce((a, b) => a + b, 0);

export function R(st: ArenaState, n: number): number { const [s, v] = nextInt(st.rng, n); st.rng = s; return v; }
function log(st: ArenaState, type: string, p = -1, a = 0, n = 0, s = '') { st.log.push({ type, p, a, b: 0, n, s }); }

// ------------------------------------------------------------------ creation & setup

export function defaultEnergy(rules: ArenaRules, defIds: string[]): string[] {
  const need = new Map<string, number>();
  for (const id of defIds) {
    const d = rules.def(id);
    if (d.kind !== 'pet') continue;
    for (const c of attacksOf(d).flatMap((a) => a.cost).filter((c) => c !== 'colorless')) need.set(c, (need.get(c) ?? 0) + 1);
  }
  const list = [...need.entries()].sort((x, y) => (y[1] - x[1]) || (x[0] < y[0] ? -1 : x[0] > y[0] ? 1 : 0)).map((kv) => kv[0]).slice(0, rules.config.maxEnergyTypes);
  return list.length > 0 ? list : [rules.data.elements[0] ?? 'colorless'];
}

export function create(rules: ArenaRules, a: ArenaSetupPlayer, b: ArenaSetupPlayer, seed: bigint, pve = false): ArenaState {
  const cfg = rules.config;
  const st: ArenaState = {
    rng: seed, defsVersion: rules.version, cards: [], players: [], turn: 0, active: 0, first: 0, phase: 'setup', continuation: null,
    maxTurns: pve ? cfg.pveMaxTurnsPerPlayer : cfg.maxTurnsPerPlayer, actionsThisTurn: 0, winner: null, endReason: null, nextUid: 1, version: 0, log: [],
  };
  [a, b].forEach((sp, p) => {
    const pl: ArenaPlayerState = {
      name: sp.name, deck: [], hand: [], discard: [], active: null, bench: [], points: 0,
      energyTypes: sp.energyTypes.length > 0 ? [...sp.energyTypes] : defaultEnergy(rules, sp.deck.map((d) => d.defId)),
      zoneCurrent: null, zoneNext: null, energyAttached: false, supportersUsed: 0, retreats: 0, turnsTaken: 0, setupDone: false, mustPromote: false, timeouts: 0, turnMods: [],
    };
    for (const c of sp.deck) { const id = st.cards.length + 1; st.cards.push({ id, defId: c.defId, owner: p, cardInstanceId: c.cardInstanceId }); pl.deck.push(id); }
    st.players.push(pl);
  });
  for (let p = 0; p < 2; p++) { shuffle(st, st.players[p].deck); dealOpening(rules, st, p); }
  st.first = R(st, 2);
  log(st, 'first', st.first);
  for (let p = 0; p < 2; p++) st.players[p].zoneNext = rollEnergy(st, st.players[p]);
  return st;
}

function dealOpening(rules: ArenaRules, st: ArenaState, p: number) {
  const pl = st.players[p];
  for (let attempt = 0; attempt < 64; attempt++) {
    for (let i = 0; i < rules.config.startHand && pl.deck.length > 0; i++) pl.hand.push(pl.deck.shift()!);
    if (pl.hand.some((c) => isBasic(rules.def(card(st, c).defId)))) { if (attempt > 0) log(st, 'redeal', p, attempt); return; }
    pl.deck.push(...pl.hand); pl.hand.length = 0;
    shuffle(st, pl.deck);
  }
  for (let i = 0; i < rules.config.startHand && pl.deck.length > 0; i++) pl.hand.push(pl.deck.shift()!);
  const bi = pl.deck.findIndex((c) => isBasic(rules.def(card(st, c).defId)));
  if (bi >= 0 && pl.hand.length > 0) { const back = pl.hand[pl.hand.length - 1]; pl.hand[pl.hand.length - 1] = pl.deck[bi]; pl.deck[bi] = back; }
  log(st, 'redeal', p, 64);
}

export function apply(rules: ArenaRules, st: ArenaState, p: number, a: ArenaAction) {
  if (st.phase === 'finished') throw err('match_finished');
  if (p !== 0 && p !== 1) throw err('invalid_player');
  if (a.type === 'surrender') { finish(st, 1 - p, 'surrender'); st.version++; return; }
  if (st.phase === 'setup') { if (a.type !== 'setup') throw err('setup_required'); setup(rules, st, p, a); st.version++; return; }
  const pl = st.players[p];
  if (anyPromote(st)) {
    if (!pl.mustPromote) throw err('waiting_for_opponent');
    if (a.type !== 'promote') throw err('promote_required');
    promote(rules, st, p, a.uid ?? 0);
    st.version++; return;
  }
  if (st.active !== p) throw err('not_your_turn');
  if (++st.actionsThisTurn > rules.config.maxActionsPerTurn) throw err('too_many_actions');
  switch (a.type) {
    case 'bench': playBasic(rules, st, p, a.card ?? 0); break;
    case 'evolve': evolve(rules, st, p, a.card ?? 0, a.uid ?? 0); break;
    case 'attach': attach(st, p, a.uid ?? 0); break;
    case 'item': case 'supporter': case 'tool': playTrainer(rules, st, p, a); break;
    case 'ability': useAbility(rules, st, p, a); break;
    case 'retreat': retreat(rules, st, p, a.uid ?? 0); break;
    case 'attack': attack(rules, st, p, a.index ?? 0, a.targets ?? []); break;
    case 'end': endTurn(rules, st, p); break;
    default: throw err('invalid_action');
  }
  st.version++;
}

function setup(rules: ArenaRules, st: ArenaState, p: number, a: ArenaAction) {
  const pl = st.players[p];
  if (pl.setupDone) throw err('setup_done');
  const bench = a.bench ?? [];
  const picks = [a.card ?? 0, ...bench];
  if (new Set(picks).size !== picks.length || bench.length > rules.config.benchSize) throw err('invalid_setup');
  for (const c of picks) if (!pl.hand.includes(c) || !isBasic(rules.def(card(st, c).defId))) throw err('setup_needs_basic');
  pl.active = newPet(st, p, a.card ?? 0, 0);
  for (const c of bench) pl.bench.push(newPet(st, p, c, 0));
  for (const c of picks) remove(pl.hand, c);
  pl.setupDone = true;
  log(st, 'setup', p, pl.bench.length);
  if (st.players.every((x) => x.setupDone)) { st.phase = 'main'; startTurn(rules, st, st.first); }
}

const newPet = (st: ArenaState, p: number, c: number, turn: number): ArenaPet => ({
  uid: st.nextUid++, owner: p, cards: [c], defId: card(st, c).defId, damage: 0, energy: [], tool: 0, poisoned: false, burned: false, special: null,
  specialTurn: 0, playedTurn: turn, evolvedTurn: 0, abilityTurn: 0, mods: [],
});

// ------------------------------------------------------------------ turn flow

function startTurn(rules: ArenaRules, st: ArenaState, p: number) {
  const cfg = rules.config, pl = st.players[p];
  if (pl.turnsTaken >= st.maxTurns) { finish(st, null, 'turn_limit'); return; }
  st.active = p; st.turn++; st.actionsThisTurn = 0; st.continuation = null;
  pl.turnsTaken++; pl.energyAttached = false; pl.supportersUsed = 0; pl.retreats = 0;
  expireMods(st);
  log(st, 'turn', p, st.turn);
  const firstTurnOfFirst = p === st.first && pl.turnsTaken === 1;
  if (!firstTurnOfFirst || cfg.firstPlayerDraws) draw(rules, st, p, 1);
  if (!cfg.energyCarryOver || pl.zoneCurrent === null) pl.zoneCurrent = firstTurnOfFirst && !cfg.firstPlayerEnergy ? null : pl.zoneNext;
  pl.zoneNext = rollEnergy(st, pl);
  log(st, 'energy_zone', p, 0, 0, pl.zoneCurrent ?? '-');
}

function expireMods(st: ArenaState) {
  for (const pl of st.players) {
    pl.turnMods = pl.turnMods.filter((m) => !(m.untilTurn < st.turn));
    for (const pet of inPlay(pl)) pet.mods = pet.mods.filter((m) => !(m.untilTurn < st.turn));
  }
}

function endTurn(rules: ArenaRules, st: ArenaState, p: number) {
  const pl = st.players[p];
  if (!rules.config.energyCarryOver) pl.zoneCurrent = null;
  log(st, 'end_turn', p);
  st.continuation = 'checkup';
  cont(rules, st);
}

function cont(rules: ArenaRules, st: ArenaState) {
  while (st.phase !== 'finished' && !anyPromote(st) && st.continuation !== null) {
    if (st.continuation === 'checkup') { st.continuation = 'next_turn'; checkup(rules, st); resolveKnockouts(rules, st, null); }
    else if (st.continuation === 'next_turn') startTurn(rules, st, 1 - st.active);
  }
}

function checkup(rules: ArenaRules, st: ArenaState) {
  const cfg = rules.config, ended = st.active;
  for (const q of [ended, 1 - ended]) {
    const pet = st.players[q].active; if (!pet) continue;
    if (pet.poisoned) { pet.damage += cfg.poisonDamage; log(st, 'poison', q, pet.uid, cfg.poisonDamage); }
    if (pet.burned) {
      pet.damage += cfg.burnDamage; log(st, 'burn', q, pet.uid, cfg.burnDamage);
      if (cfg.burnCureOnHeads && flip(st, q, 'burn')) { pet.burned = false; log(st, 'recover', q, pet.uid, 0, 'burned'); }
    }
    if (pet.special === 'asleep' && flip(st, q, 'asleep')) { pet.special = null; log(st, 'recover', q, pet.uid, 0, 'asleep'); }
    if (pet.special === 'paralyzed' && pet.owner === ended && pet.specialTurn < st.turn) { pet.special = null; log(st, 'recover', q, pet.uid, 0, 'paralyzed'); }
  }
  for (const q of [ended, 1 - ended]) {
    const pet = st.players[q].active; if (!pet) continue;
    const ab = rules.def(pet.defId).ability;
    if (!ab || ab.trigger !== 'between_turns' || abilityLocked(st, pet)) continue;
    runEffects(rules, st, q, pet, null, ab.effects, [], 'ability:' + ab.id, 1);
  }
}

function promote(rules: ArenaRules, st: ArenaState, p: number, uid: number) {
  const pl = st.players[p];
  const pet = pl.bench.find((b) => b.uid === uid);
  if (!pet) throw err('invalid_target');
  remove(pl.bench, pet); pl.active = pet; pl.mustPromote = false;
  log(st, 'promote', p, pet.uid);
  cont(rules, st);
}

export const anyPromote = (st: ArenaState) => st.players.some((x) => x.mustPromote);

export function mustAct(st: ArenaState, p: number): boolean {
  if (st.phase === 'finished') return false;
  if (st.phase === 'setup') return !st.players[p].setupDone;
  if (anyPromote(st)) return st.players[p].mustPromote;
  return st.active === p;
}

// ------------------------------------------------------------------ main-phase actions

function playBasic(rules: ArenaRules, st: ArenaState, p: number, c: number) {
  const pl = st.players[p];
  if (!pl.hand.includes(c)) throw err('card_not_in_hand');
  if (!isBasic(rules.def(card(st, c).defId))) throw err('not_a_basic_pet');
  if (pl.bench.length >= rules.config.benchSize) throw err('bench_full');
  remove(pl.hand, c);
  const pet = newPet(st, p, c, st.turn);
  pl.bench.push(pet);
  log(st, 'bench', p, pet.uid, c, pet.defId);
}

function evolve(rules: ArenaRules, st: ArenaState, p: number, c: number, uid: number) {
  const pl = st.players[p];
  if (!pl.hand.includes(c)) throw err('card_not_in_hand');
  const def = rules.def(card(st, c).defId);
  if (def.kind !== 'pet' || (def.stage ?? 0) === 0) throw err('not_an_evolution');
  if (rules.config.noEvolveFirstTurn && pl.turnsTaken === 1) throw err('no_evolve_first_turn');
  const pet = inPlay(pl).find((x) => x.uid === uid);
  if (!pet) throw err('invalid_target');
  if (def.evolvesFrom !== pet.defId) throw err('wrong_evolution_line');
  if (pet.playedTurn === st.turn) throw err('played_this_turn');
  if (pet.evolvedTurn === st.turn) throw err('evolved_this_turn');
  remove(pl.hand, c);
  pet.cards.push(c); pet.defId = def.id; pet.evolvedTurn = st.turn;
  clearConditions(pet);
  log(st, 'evolve', p, pet.uid, c, def.id);
  resolveKnockouts(rules, st, null);
}

function attach(st: ArenaState, p: number, uid: number) {
  const pl = st.players[p];
  if (pl.energyAttached) throw err('energy_already_attached');
  if (pl.zoneCurrent === null) throw err('no_energy_available');
  const pet = inPlay(pl).find((x) => x.uid === uid);
  if (!pet) throw err('invalid_target');
  pet.energy.push(pl.zoneCurrent);
  log(st, 'attach', p, pet.uid, 0, pl.zoneCurrent);
  pl.zoneCurrent = null; pl.energyAttached = true;
}

function playTrainer(rules: ArenaRules, st: ArenaState, p: number, a: ArenaAction) {
  const pl = st.players[p], c = a.card ?? 0;
  if (!pl.hand.includes(c)) throw err('card_not_in_hand');
  const def = rules.def(card(st, c).defId);
  if (def.kind !== a.type) throw err('wrong_card_kind');
  if (def.kind === 'supporter') {
    if (pl.supportersUsed >= rules.config.supportersPerTurn) throw err('supporter_used');
    if (!rules.config.supporterFirstTurn && pl.turnsTaken === 1) throw err('no_supporter_first_turn');
  }
  if (def.kind === 'tool') {
    const holder = inPlay(pl).find((x) => x.uid === a.uid);
    if (!holder) throw err('invalid_target');
    if (holder.tool !== 0) throw err('tool_already_attached');
    remove(pl.hand, c);
    holder.tool = c;
    log(st, 'tool', p, holder.uid, c, def.id);
    return;
  }
  if (!trainerPlayable(rules, st, p, def)) throw err('no_valid_target');
  checkChoices(st, p, effectsOf(def), a.targets ?? []);
  remove(pl.hand, c);
  if (def.kind === 'supporter') pl.supportersUsed++;
  log(st, def.kind, p, 0, c, def.id);
  runEffects(rules, st, p, pl.active, null, effectsOf(def), a.targets ?? [], def.kind + ':' + def.id, 0);
  pl.discard.push(c);
  resolveKnockouts(rules, st, null);
}

export function trainerPlayable(_rules: ArenaRules, st: ArenaState, p: number, def: ArenaCardDef): boolean {
  const pl = st.players[p], opp = st.players[1 - p], effs = effectsOf(def);
  const heals = effs.some((e) => e.op === 'heal' && e.target === 'own_chosen'), cures = effs.some((e) => e.op === 'cure');
  if (heals && !inPlay(pl).some((x) => x.damage > 0 || (cures && (x.poisoned || x.burned || x.special !== null)))) return false;
  for (const e of effs) {
    if (e.target === 'own_bench_chosen' && (pl.bench.length === 0 || pl.active === null)) return false;
    if (e.target === 'opp_bench_chosen' && (opp.bench.length === 0 || opp.active === null)) return false;
    if (e.op === 'energy_transfer' && !pl.bench.some((b) => b.energy.length > 0)) return false;
    if (e.op === 'energy_discard' && (opp.active === null || opp.active.energy.length === 0)) return false;
    if (e.op === 'switch_self' && pl.active === null) return false;
  }
  return true;
}

function useAbility(rules: ArenaRules, st: ArenaState, p: number, a: ArenaAction) {
  const pl = st.players[p];
  const pet = inPlay(pl).find((x) => x.uid === a.uid);
  if (!pet) throw err('invalid_target');
  const ab = rules.def(pet.defId).ability;
  if (!ab || ab.trigger !== 'activated') throw err('no_ability');
  if (pet.abilityTurn === st.turn) throw err('ability_used');
  if (abilityLocked(st, pet)) throw err('ability_locked');
  if (ab.effects.some((e) => e.cond === 'is_active') && pl.active !== pet) throw err('must_be_active');
  if (ab.effects.every((e) => e.op === 'heal') && (pl.active === null || pl.active.damage === 0)) throw err('nothing_to_heal');
  checkChoices(st, p, ab.effects, a.targets ?? []);
  pet.abilityTurn = st.turn;
  log(st, 'ability', p, pet.uid, 0, ab.id);
  runEffects(rules, st, p, pet, null, ab.effects, a.targets ?? [], 'ability:' + ab.id, 0);
}

export function retreatCost(rules: ArenaRules, st: ArenaState, p: number): number {
  const pl = st.players[p], pet = pl.active; if (!pet) return 99;
  const c = (rules.def(pet.defId).retreat ?? 0) + sum(pl.turnMods.filter((m) => m.kind === 'retreat_mod').map((m) => m.amount)) + sum(activeMods(st, pet, 'retreat_mod').map((m) => m.amount));
  return Math.max(0, c);
}

function retreat(rules: ArenaRules, st: ArenaState, p: number, uid: number) {
  const pl = st.players[p], pet = pl.active;
  if (!pet) throw err('no_active');
  if (pl.retreats >= rules.config.retreatsPerTurn) throw err('already_retreated');
  if (pet.special === 'asleep' || pet.special === 'paralyzed') throw err('cannot_retreat_now');
  const cost = retreatCost(rules, st, p);
  if (cost >= 50) throw err('retreat_blocked');
  if (pet.energy.length < cost) throw err('not_enough_energy');
  const to = pl.bench.find((b) => b.uid === uid);
  if (!to) throw err('invalid_target');
  for (let i = 0; i < cost; i++) pet.energy.pop();
  swapIn(pl, to);
  pl.retreats++;
  log(st, 'retreat', p, pet.uid, to.uid, String(cost));
}

function swapIn(pl: ArenaPlayerState, to: ArenaPet) {
  const old = pl.active;
  remove(pl.bench, to);
  if (old) { clearConditions(old); old.mods = []; pl.bench.push(old); }
  pl.active = to;
}

function clearConditions(pet: ArenaPet) { pet.poisoned = false; pet.burned = false; pet.special = null; }

// ------------------------------------------------------------------ attacks

export function attackBanned(rules: ArenaRules, st: ArenaState, p: number): boolean {
  const pl = st.players[p];
  return rules.config.attackBanFirstTurn === 'both' ? pl.turnsTaken === 1 : (p === st.first && pl.turnsTaken === 1);
}

export function costPaid(energy: string[], cost: string[]): boolean {
  const pool = [...energy];
  for (const c of cost.filter((c) => c !== 'colorless')) { const i = pool.indexOf(c); if (i < 0) return false; pool.splice(i, 1); }
  return pool.length >= cost.filter((c) => c === 'colorless').length;
}

export function attackProblem(rules: ArenaRules, st: ArenaState, p: number, index: number): string | null {
  const pl = st.players[p], pet = pl.active;
  if (!pet) return 'no_active';
  if (attackBanned(rules, st, p)) return 'no_attack_first_turn';
  const atks = attacksOf(rules.def(pet.defId));
  if (index < 0 || index >= atks.length) return 'invalid_attack';
  if (pet.special === 'asleep' || pet.special === 'paralyzed') return 'cannot_attack_now';
  if (activeMods(st, pet, 'attack_lock').length > 0) return 'attack_locked';
  if (!costPaid(pet.energy, atks[index].cost)) return 'not_enough_energy';
  if (st.players[1 - p].active === null) return 'no_target';
  return null;
}

function attack(rules: ArenaRules, st: ArenaState, p: number, index: number, targets: number[]) {
  const problem = attackProblem(rules, st, p, index);
  if (problem) throw err(problem);
  const pl = st.players[p], att = pl.active!, atk = attacksOf(rules.def(att.defId))[index];
  checkChoices(st, p, atk.effects, targets);
  log(st, 'attack', p, att.uid, index, atk.id);
  if (att.special === 'confused' && !flip(st, p, 'confused')) {
    log(st, 'attack_failed', p, att.uid, 0, 'confused');
    if (rules.config.confusedSelfDamage > 0) { att.damage += rules.config.confusedSelfDamage; log(st, 'damage', p, att.uid, rules.config.confusedSelfDamage, 'confused'); }
  } else runEffects(rules, st, p, att, atk, atk.effects, targets, 'attack:' + atk.id, 0);
  resolveKnockouts(rules, st, att);
  if (st.phase !== 'finished') endTurn(rules, st, p);
}

export function attackDamage(rules: ArenaRules, st: ArenaState, p: number, att: ArenaPet, atk: ArenaAttack, def: ArenaPet, baseExtra: number): number {
  let dmg = atk.damage + baseExtra;
  if (dmg <= 0) return 0;
  dmg += sum(st.players[p].turnMods.filter((m) => m.kind === 'damage_boost').map((m) => m.amount));
  const ab = rules.def(att.defId).ability;
  if (ab && ab.trigger === 'passive' && !abilityLocked(st, att))
    for (const e of ab.effects.filter((e) => e.op === 'damage_boost')) if (e.cond !== 'self_damaged' || att.damage > 0) dmg += amt(e);
  dmg -= sum(activeMods(st, att, 'attack_reduce').map((m) => m.amount));
  if (dmg <= 0) return 0;
  if (rules.def(def.defId).weakness === rules.def(att.defId).element) dmg += rules.config.weaknessBonus;
  dmg -= sum(activeMods(st, def, 'shield').map((m) => m.amount));
  const dab = rules.def(def.defId).ability;
  if (dab && dab.trigger === 'passive' && !abilityLocked(st, def)) dmg -= sum(dab.effects.filter((e) => e.op === 'reduce_taken').map(amt));
  if (activeMods(st, def, 'prevent').length > 0) return 0;
  return Math.max(0, dmg);
}

function checkChoices(st: ArenaState, p: number, effects: ArenaEffect[], chosen: number[]) {
  const pl = st.players[p], opp = st.players[1 - p]; let i = 0;
  for (const e of effects) {
    let pool: ArenaPet[] | null = null;
    if (e.op === 'bench_damage' && e.target === 'opp_bench_chosen') pool = opp.bench;
    else if (e.op === 'heal' && e.target === 'own_chosen') pool = inPlay(pl);
    else if (e.op === 'switch_self' || (e.op === 'energy_attach' && e.target === 'own_bench_chosen')) pool = pl.bench;
    else if (e.op === 'force_switch') pool = opp.bench;
    else if (e.op === 'energy_transfer') pool = pl.bench.filter((b) => b.energy.length > 0);
    if (pool === null || pool.length === 0) continue;
    if (i < chosen.length && !pool.some((x) => x.uid === chosen[i])) throw err('invalid_target');
    i++;
  }
}

// ------------------------------------------------------------------ effect engine

function runEffects(rules: ArenaRules, st: ArenaState, p: number, src: ArenaPet | null, atk: ArenaAttack | null, effects: ArenaEffect[], chosen: number[], source: string, depth: number) {
  if (depth > rules.config.maxTriggerDepth) return;
  const pl = st.players[p], opp = st.players[1 - p];
  let choice = 0;
  const choose = (pool: ArenaPet[]): number => {
    if (pool.length === 0) return 0;
    if (choice < chosen.length) { const uid = chosen[choice++]; if (pool.some((x) => x.uid === uid)) return uid; throw err('invalid_target'); }
    return pool.length === 1 ? pool[0].uid : sortBy(pool, (x) => x.uid)[0].uid;
  };
  let extra = 0, dealt = 0;
  const defender = opp.active;
  if (atk) {
    for (const e of effects) {
      if (e.op === 'coin_damage') { let heads = 0; for (let i = 0; i < Math.max(1, e.coins ?? 0); i++) if (flip(st, p, 'coin_damage')) heads++; extra += heads * amt(e); }
      else if (e.op === 'bonus' && defender && cond(rules, src, defender, e.cond)) extra += amt(e);
    }
    if (defender) {
      dealt = attackDamage(rules, st, p, src!, atk, defender, extra);
      if (dealt > 0 || atk.damage + extra > 0) damage(st, defender, dealt, 'attack');
    }
  }
  for (const e of effects) {
    if (e.op === 'coin_damage' || e.op === 'bonus') continue;
    if (e.cond === 'heads' && !flip(st, p, e.op)) { log(st, 'fizzle', p, 0, 0, e.op); continue; }
    if (e.cond === 'is_active' && src && pl.active !== src) continue;
    switch (e.op) {
      case 'status': if (defender && defender.damage < maxHp(rules, st, defender)) applyStatus(rules, st, defender, e.status!); break;
      case 'bench_damage': {
        const targets = e.target === 'opp_bench_all' ? [...opp.bench] : (() => { const u = choose(opp.bench); return opp.bench.filter((b) => b.uid === u); })();
        for (const b of targets) damage(st, b, amt(e), 'bench');
        break;
      }
      case 'heal': for (const t of healTargets(st, p, src, e.target, choose)) heal(st, t, amt(e)); break;
      case 'cure': {
        const ct = e.target === 'own_chosen' ? (inPlay(pl).find((x) => x.uid === (chosen.length > 0 ? chosen[0] : 0)) ?? src) : src;
        if (ct) { clearConditions(ct); log(st, 'cure', ct.owner, ct.uid); }
        break;
      }
      case 'shield': if (src) addMod(st, src, 'shield', amt(e), st.turn + 1, st.turn + 1, source); break;
      case 'prevent': if (src) addMod(st, src, 'prevent', 0, st.turn + 1, st.turn + 1, source); break;
      case 'attack_reduce': if (defender && opp.active === defender) addMod(st, defender, 'attack_reduce', amt(e), st.turn + 1, st.turn + 1, source); break;
      case 'attack_lock_self': if (src) addMod(st, src, 'attack_lock', 0, st.turn + 1, st.turn + 2, source); break;
      case 'attack_lock_target': if (opp.active) addMod(st, opp.active, 'attack_lock', 0, st.turn + 1, st.turn + 1, source); break;
      case 'ability_lock': if (opp.active) addMod(st, opp.active, 'ability_lock', 0, st.turn + 1, st.turn + 1, source); break;
      case 'retreat_mod':
        if (e.target === 'own_active') pl.turnMods.push({ kind: 'retreat_mod', amount: amt(e), fromTurn: st.turn, untilTurn: st.turn, source });
        else if (opp.active) addMod(st, opp.active, 'retreat_mod', amt(e), st.turn + 1, st.turn + 1, source);
        break;
      case 'damage_boost': if (e.target === 'own_turn') pl.turnMods.push({ kind: 'damage_boost', amount: amt(e), fromTurn: st.turn, untilTurn: st.turn, source }); break;
      case 'draw': draw(rules, st, p, Math.max(1, e.count ?? 0)); break;
      case 'search': {
        const basics = pl.deck.filter((c) => isBasic(rules.def(card(st, c).defId)));
        if (basics.length > 0 && pl.hand.length < rules.config.handLimit) {
          const pick = basics[R(st, basics.length)];
          remove(pl.deck, pick); pl.hand.push(pick);
          log(st, 'search', p, 0, 1);
        } else log(st, 'fizzle', p, 0, 0, 'search');
        shuffle(st, pl.deck);
        break;
      }
      case 'shuffle': shuffle(st, pl.deck); log(st, 'shuffle', p); break;
      case 'switch_self': { const uid = choose(pl.bench); const to = pl.bench.find((b) => b.uid === uid); if (to) { swapIn(pl, to); log(st, 'switch', p, to.uid); } break; }
      case 'force_switch': { const uid = choose(opp.bench); const to = opp.bench.find((b) => b.uid === uid); if (to) { swapIn(opp, to); log(st, 'switch', 1 - p, to.uid); } break; }
      case 'energy_attach': {
        const el = e.element ?? rules.def(src?.defId ?? pl.active!.defId).element!;
        const to = e.target === 'self' ? src : e.target === 'own_active' ? pl.active : (() => { const u = choose(pl.bench); return pl.bench.find((b) => b.uid === u) ?? null; })();
        if (to) for (let i = 0; i < Math.max(1, e.count ?? 0); i++) { to.energy.push(el); log(st, 'energy_gain', p, to.uid, 1, el); }
        break;
      }
      case 'energy_discard':
        if (opp.active && opp.active.energy.length > 0) {
          const i = R(st, opp.active.energy.length), el = opp.active.energy[i];
          opp.active.energy.splice(i, 1); log(st, 'energy_discard', 1 - p, opp.active.uid, 1, el);
        }
        break;
      case 'energy_transfer': {
        const uid = choose(pl.bench.filter((b) => b.energy.length > 0)); const from = pl.bench.find((b) => b.uid === uid);
        if (from && pl.active) { const n = from.energy.length; pl.active.energy.push(...from.energy); from.energy = []; log(st, 'energy_move', p, from.uid, n, String(pl.active.uid)); }
        break;
      }
    }
  }
  if (atk && dealt > 0 && src && depth === 0) {
    const ab = rules.def(src.defId).ability;
    if (ab && ab.trigger === 'on_attack' && !abilityLocked(st, src)) runEffects(rules, st, p, src, null, ab.effects, [], 'ability:' + ab.id, depth + 1);
    if (defender && defender.tool !== 0) {
      const tool = rules.def(card(st, defender.tool).defId);
      for (const te of effectsOf(tool).filter((x) => x.op === 'retaliate')) damage(st, src, amt(te), 'retaliate');
    }
  }
}

function healTargets(st: ArenaState, p: number, src: ArenaPet | null, target: string, choose: (pool: ArenaPet[]) => number): ArenaPet[] {
  const pl = st.players[p];
  switch (target) {
    case 'self': return src ? [src] : [];
    case 'own_active': return pl.active ? [pl.active] : [];
    case 'own_all': return [...inPlay(pl)];
    case 'own_lowest': { const low = sortBy(inPlay(pl).filter((x) => x.damage > 0), (x) => -x.damage, (x) => x.uid)[0]; return low ? [low] : []; }
    case 'own_chosen': { const uid = choose(inPlay(pl)); const t = inPlay(pl).find((x) => x.uid === uid); return t ? [t] : []; }
    default: return [];
  }
}

function cond(rules: ArenaRules, src: ArenaPet | null, target: ArenaPet, c?: string | null): boolean {
  switch (c) {
    case 'target_status': return target.poisoned || target.burned || target.special !== null;
    case 'target_damaged': return target.damage > 0;
    case 'target_ex': return !!rules.def(target.defId).ex;
    case 'self_damaged': return !!src && src.damage > 0;
    default: return true;
  }
}

function applyStatus(rules: ArenaRules, st: ArenaState, pet: ArenaPet, status: string) {
  if (pet.tool !== 0 && effectsOf(rules.def(card(st, pet.tool).defId)).some((e) => e.op === 'status_immunity')) { log(st, 'immune', pet.owner, pet.uid, 0, status); return; }
  if (status === 'poisoned') pet.poisoned = true;
  else if (status === 'burned') pet.burned = true;
  else { pet.special = status; pet.specialTurn = st.turn; }
  log(st, 'status', pet.owner, pet.uid, 0, status);
}

function damage(st: ArenaState, pet: ArenaPet, amount: number, kind: string) {
  if (amount <= 0 && kind !== 'attack') return;
  pet.damage += Math.max(0, amount);
  log(st, 'damage', pet.owner, pet.uid, Math.max(0, amount), kind);
}

function heal(st: ArenaState, pet: ArenaPet, amount: number) {
  const h = Math.min(pet.damage, amount);
  if (h <= 0) return;
  pet.damage -= h;
  log(st, 'heal', pet.owner, pet.uid, h);
}

function addMod(st: ArenaState, pet: ArenaPet, kind: string, amount: number, from: number, until: number, source: string) {
  pet.mods = pet.mods.filter((m) => !(m.kind === kind && m.source === source));
  pet.mods.push({ kind, amount, fromTurn: from, untilTurn: until, source });
  log(st, 'effect', pet.owner, pet.uid, amount, kind);
}

export const activeMods = (st: ArenaState, pet: ArenaPet, kind: string): ArenaMod[] => pet.mods.filter((m) => m.kind === kind && m.fromTurn <= st.turn && st.turn <= m.untilTurn);
export const abilityLocked = (st: ArenaState, pet: ArenaPet) => activeMods(st, pet, 'ability_lock').length > 0;

export function maxHp(rules: ArenaRules, st: ArenaState, pet: ArenaPet): number {
  let hp = rules.def(pet.defId).hp ?? 0;
  if (pet.tool !== 0) hp += sum(effectsOf(rules.def(card(st, pet.tool).defId)).filter((e) => e.op === 'max_hp').map(amt));
  return hp;
}

// ------------------------------------------------------------------ knockouts & winning

function resolveKnockouts(rules: ArenaRules, st: ArenaState, attacker: ArenaPet | null) {
  const cfg = rules.config;
  for (let guard = 0; guard <= cfg.maxTriggerDepth; guard++) {
    const all = st.players.flatMap((pl) => inPlay(pl));
    const kos = sortBy(all.filter((x) => x.damage >= maxHp(rules, st, x)), (x) => (x.owner === st.active ? 1 : 0), (x) => x.uid);
    if (kos.length === 0) break;
    for (const ko of kos) {
      const owner = st.players[ko.owner];
      if (owner.active === ko) owner.active = null; else remove(owner.bench, ko);
      owner.discard.push(...ko.cards);
      if (ko.tool !== 0) owner.discard.push(ko.tool);
      const def = rules.def(ko.defId);
      st.players[1 - ko.owner].points += def.koPoints ?? 1;
      log(st, 'knockout', ko.owner, ko.uid, def.koPoints ?? 1, ko.defId);
      if (attacker && ko.owner !== attacker.owner && ko.tool !== 0 && inPlay(st.players[attacker.owner]).includes(attacker))
        for (const te of effectsOf(rules.def(card(st, ko.tool).defId)).filter((e) => e.op === 'on_ko_damage')) damage(st, attacker, amt(te), 'ko_trigger');
    }
  }
  const wins = [0, 1].map((q) => st.players[q].points >= cfg.pointsToWin || (st.phase !== 'setup' && inPlay(st.players[1 - q]).length === 0));
  if (wins[0] && wins[1]) { finish(st, cfg.simultaneousWin === 'draw' ? null : st.active, 'simultaneous'); return; }
  if (wins[0] || wins[1]) { const w = wins[0] ? 0 : 1; finish(st, w, st.players[w].points >= cfg.pointsToWin ? 'points' : 'no_pets'); return; }
  for (let q = 0; q < 2; q++) {
    const pl = st.players[q];
    if (pl.active === null && pl.bench.length > 0 && !pl.mustPromote) { pl.mustPromote = true; log(st, 'must_promote', q); }
  }
}

function finish(st: ArenaState, winner: number | null, reason: string) {
  if (st.phase === 'finished') return;
  st.phase = 'finished'; st.winner = winner; st.endReason = reason;
  for (const pl of st.players) pl.mustPromote = false;
  log(st, 'finish', winner ?? -1, 0, 0, reason);
}

// ------------------------------------------------------------------ timers

export function timeout(rules: ArenaRules, st: ArenaState) {
  if (st.phase === 'finished') return;
  const late = [0, 1].filter((q) => mustAct(st, q));
  for (const q of late) {
    const pl = st.players[q];
    pl.timeouts++;
    log(st, 'timeout', q, pl.timeouts);
    if (pl.timeouts >= rules.config.timeoutsToForfeit) { finish(st, 1 - q, 'timeout'); st.version++; return; }
  }
  for (const q of late) {
    if ((st.phase as string) === 'finished') break;
    if (st.phase === 'setup') setup(rules, st, q, setupChoice(rules, st, q));
    else if (st.players[q].mustPromote) promote(rules, st, q, promoteChoice(rules, st, q));
    else if (st.active === q) endTurn(rules, st, q);
  }
  st.version++;
}

// ------------------------------------------------------------------ helpers

function draw(rules: ArenaRules, st: ArenaState, p: number, n: number) {
  const pl = st.players[p]; let drawn = 0;
  for (let i = 0; i < n; i++) {
    if (pl.deck.length === 0) { log(st, 'deck_empty', p); break; }
    if (pl.hand.length >= rules.config.handLimit) { log(st, 'hand_full', p); break; }
    pl.hand.push(pl.deck.shift()!); drawn++;
  }
  if (drawn > 0) log(st, 'draw', p, drawn);
}

const rollEnergy = (st: ArenaState, pl: ArenaPlayerState) => (pl.energyTypes.length === 0 ? 'colorless' : pl.energyTypes[R(st, pl.energyTypes.length)]);

function flip(st: ArenaState, p: number, why: string): boolean {
  const heads = R(st, 2) === 1;
  log(st, 'coin', p, heads ? 1 : 0, 0, why);
  return heads;
}

function shuffle(st: ArenaState, deck: number[]) {
  for (let i = deck.length - 1; i > 0; i--) { const j = R(st, i + 1); [deck[i], deck[j]] = [deck[j], deck[i]]; }
}
