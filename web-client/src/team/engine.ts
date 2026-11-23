// TypeScript port of shared-contracts/Runtime/Rules/Team/TeamEngine.cs + TeamAi.cs for the Interactive Web Preview.
// The C# engine is authoritative on the server; engine.test.ts replays C#-generated battles and requires identical logs.
import { nextInt } from '../duel/rng';
import { TeamRuleError, alive, type TeamAction, type TeamOrder, type TeamRules, type TeamSetupPlayer, type TeamSkill, type TeamState, type TeamUnit } from './model';

const HARD_CC = new Set(['stun', 'freeze', 'silence']);
const DEBUFFS = new Set(['burn', 'poison', 'stun', 'freeze', 'silence', 'weaken']);
const DOTS = new Set(['burn', 'poison']);
const err = (c: string) => new TeamRuleError(c);
const idiv = (a: number, b: number) => Math.trunc(a / b);
const sortBy = <T>(xs: T[], ...keys: ((x: T) => number)[]) =>
  xs.map((x, i) => ({ x, i })).sort((p, q) => { for (const k of keys) { const d = k(p.x) - k(q.x); if (d !== 0) return d; } return p.i - q.i; }).map((o) => o.x);

function R(st: TeamState, n: number): number { const [s, v] = nextInt(st.rng, n); st.rng = s; return v; }
function log(st: TeamState, type: string, a: number, b: number, n: number, s: string) { st.log.push({ type, round: st.round, a, b, n, s }); }

export function create(rules: TeamRules, a: TeamSetupPlayer, b: TeamSetupPlayer, seed: bigint): TeamState {
  const cfg = rules.config;
  const st: TeamState = { rng: seed, defsVersion: rules.version, units: [], players: [], round: 1, phase: 'planning', winner: null, endReason: null, version: 0, log: [] };
  [a, b].forEach((sp, side) => {
    st.players.push({ name: sp.name, plan: null, locked: false, timeouts: 0 });
    if (sp.units.length !== cfg.teamSize) throw err('team_size');
    sp.units.forEach((s, i) => {
      const kit = rules.kits[s.formId]; if (!kit) throw err('unknown_pet');
      st.units.push({
        uid: st.units.length + 1, side, slot: i, formId: s.formId, cardInstanceId: s.cardInstanceId, element: kit.element, role: kit.role,
        maxHp: s.hp, hp: s.hp, atk: s.atk, def: s.def, mag: s.mag, res: s.res, spd: s.spd, critBp: s.critBp,
        en: Math.min(cfg.maxEn, cfg.startEn + (kit.passive?.op === 'start_en' ? kit.passive.amount ?? 0 : 0)), shield: 0, acted: false, ccImmune: 0,
        basic: kit.basic, skill: kit.skill, ultimate: kit.ultimate, passive: kit.passive, cooldowns: {}, statuses: [],
      });
    });
  });
  log(st, 'start', 0, 0, 0, '');
  return st;
}

export const team = (st: TeamState, side: number) => st.units.filter((u) => u.side === side);
export const mustAct = (st: TeamState, p: number) => st.phase !== 'finished' && !st.players[p].locked;
export const has = (u: TeamUnit, s: string) => u.statuses.some((x) => x.type === s && x.remaining > 0);
export function skillFor(rules: TeamRules, u: TeamUnit, slot: string): TeamSkill | null {
  const id = slot === 'basic' ? u.basic : slot === 'skill' ? u.skill : slot === 'ultimate' ? u.ultimate : null;
  return id && rules.skills[id] ? rules.skills[id] : null;
}

export function orderProblem(rules: TeamRules, st: TeamState, p: number, o: TeamOrder): string | null {
  const u = st.units.find((x) => x.uid === o.unit);
  if (!u || u.side !== p) return 'invalid_unit';
  if (!alive(u)) return 'unit_down';
  if (o.slot === 'guard') return null;
  const sk = skillFor(rules, u, o.slot);
  if (!sk) return 'no_such_skill';
  if (sk.kind !== 'basic' && has(u, 'silence')) return 'silenced';
  if (u.en < sk.cost) return 'not_enough_en';
  if ((u.cooldowns[sk.id] ?? 0) > 0) return 'on_cooldown';
  if (o.target !== 0) {
    const t = st.units.find((x) => x.uid === o.target);
    if (!t || !alive(t)) return 'invalid_target';
    const enemyTarget = sk.target.startsWith('enemy');
    if (enemyTarget && t.side === p) return 'invalid_target';
    if (!enemyTarget && t.side !== p) return 'invalid_target';
  }
  return null;
}

export function apply(rules: TeamRules, st: TeamState, p: number, a: TeamAction) {
  if (st.phase === 'finished') throw err('match_finished');
  if (p !== 0 && p !== 1) throw err('invalid_player');
  if (a.type === 'surrender') { finish(st, 1 - p, 'surrender'); st.version++; return; }
  if (a.type !== 'plan') throw err('invalid_action');
  if (st.players[p].locked) throw err('plan_locked');
  const orders: TeamOrder[] = [];
  for (const o of a.orders) {
    if (orders.some((x) => x.unit === o.unit)) throw err('duplicate_order');
    const problem = orderProblem(rules, st, p, o);
    if (problem) throw err(problem);
    orders.push({ unit: o.unit, slot: o.slot, target: o.target ?? 0 });
  }
  st.players[p].timeouts = 0;
  lock(rules, st, p, orders);
  st.version++;
}

function lock(rules: TeamRules, st: TeamState, p: number, orders: TeamOrder[]) {
  const pl = st.players[p];
  for (const u of sortBy(team(st, p).filter((x) => alive(x) && orders.every((o) => o.unit !== x.uid)), (x) => x.slot)) orders.push({ unit: u.uid, slot: 'basic', target: 0 });
  pl.plan = orders; pl.locked = true;
  log(st, 'locked', 0, 0, p, '');
  if (st.players.every((x) => x.locked)) resolve(rules, st);
}

export function timeout(rules: TeamRules, st: TeamState) {
  if (st.phase === 'finished') return;
  const late = [0, 1].filter((p) => !st.players[p].locked);
  const round = st.round;
  for (const p of late) {
    const pl = st.players[p];
    pl.timeouts++;
    log(st, 'timeout', 0, 0, p, String(pl.timeouts));
    if (pl.timeouts >= rules.config.timeoutsToForfeit) { finish(st, 1 - p, 'timeout'); break; }
  }
  for (const p of late) {
    if ((st.phase as string) === 'finished' || st.round !== round || st.players[p].locked) break;
    lock(rules, st, p, decide(rules, st, p).orders);
  }
  st.version++;
}

export const effSpd = (rules: TeamRules, u: TeamUnit) => (has(u, 'haste') ? idiv(u.spd * (100 + rules.config.hastePct), 100) : u.spd);

function resolve(rules: TeamRules, st: TeamState) {
  const orders = st.players.flatMap((pl) => pl.plan!);
  const tie = new Map<number, number>();
  for (const o of sortBy(orders, (o) => o.unit)) tie.set(o.unit, R(st, 1_000_000));
  const unitOf = (uid: number) => st.units.find((x) => x.uid === uid)!;
  const queue = sortBy(orders.map((o) => ({ o, u: unitOf(o.unit), sk: o.slot === 'guard' ? null : skillFor(rules, unitOf(o.unit), o.slot) })),
    (x) => -(x.o.slot === 'guard' ? 9 : x.sk!.priority), (x) => -effSpd(rules, x.u), (x) => tie.get(x.o.unit)!);
  log(st, 'round', 0, 0, st.round, '');
  for (const { o, u, sk: sk0 } of queue) {
    if (st.phase === 'finished') break;
    if (!alive(u)) continue;
    if (has(u, 'stun') || has(u, 'freeze')) { log(st, 'skip', u.uid, 0, 0, has(u, 'stun') ? 'stun' : 'freeze'); u.acted = true; continue; }
    if (o.slot === 'guard') { u.statuses = u.statuses.filter((s) => s.type !== 'guard'); u.statuses.push({ type: 'guard', remaining: 1, stacks: 1 }); log(st, 'guard', u.uid, 0, 0, ''); u.acted = true; continue; }
    let sk = sk0!;
    if (sk.kind !== 'basic' && has(u, 'silence')) { log(st, 'silenced', u.uid, 0, 0, sk.id); sk = rules.skills[u.basic]; }
    if (u.en < sk.cost || (u.cooldowns[sk.id] ?? 0) > 0) { log(st, 'fizzle', u.uid, 0, 0, sk.id); sk = rules.skills[u.basic]; }
    u.en -= sk.cost;
    if (sk.kind === 'basic') u.en = Math.min(rules.config.maxEn, u.en + rules.config.basicEnGain);
    if (sk.cooldown > 0) u.cooldowns[sk.id] = sk.cooldown + 1;
    act(rules, st, u, sk, o.target);
    u.acted = true;
    checkEnd(st);
  }
  if (st.phase !== 'finished') roundEnd(rules, st);
}

function targets(st: TeamState, u: TeamUnit, sk: TeamSkill, chosen: number): TeamUnit[] {
  const enemies = sortBy(team(st, 1 - u.side).filter(alive), (x) => x.slot);
  const allies = sortBy(team(st, u.side).filter(alive), (x) => x.slot);
  switch (sk.target) {
    case 'enemy_single': {
      if (enemies.length === 0) return [];
      const taunt = enemies.find((x) => has(x, 'taunt'));
      if (taunt) return [taunt];
      const pick = enemies.find((x) => x.uid === chosen);
      return [pick ?? sortBy(enemies, (x) => x.hp, (x) => x.slot)[0]];
    }
    case 'enemy_all': return enemies;
    case 'ally_all': return allies;
    case 'ally_lowest': return allies.length === 0 ? allies : [sortBy(allies, (x) => idiv(x.hp * 1000, x.maxHp), (x) => x.slot)[0]];
    default: return [u];
  }
}

function act(rules: TeamRules, st: TeamState, u: TeamUnit, sk: TeamSkill, chosen: number) {
  const cfg = rules.config;
  const tg = targets(st, u, sk, chosen);
  const offensive = sk.target.startsWith('enemy');
  log(st, 'act', u.uid, tg[0]?.uid ?? 0, u.en, sk.id);
  for (const e of sk.effects) {
    switch (e.op) {
      case 'damage': for (const t of tg.filter(alive)) hit(rules, st, u, t, sk, e.power ?? 0); break;
      case 'heal':
        for (const t of (offensive ? [u] : tg).filter(alive)) {
          const bonus = u.passive?.op === 'heal_bonus' ? u.passive.pct ?? 0 : 0;
          heal(st, t, idiv(idiv((e.power ?? 0) * u.mag, 100) * (100 + bonus), 100), u.uid);
        }
        break;
      case 'shield':
        for (const t of (offensive ? [u] : tg).filter(alive)) {
          const basis = e.scale === 'res' ? u.res : u.def;
          const amount = Math.min(idiv(t.maxHp * cfg.shieldCapPct, 100), idiv(idiv((e.power ?? 0) * basis, 100) * cfg.damageScalePct, 100));
          if (amount > t.shield) { t.shield = amount; log(st, 'shield', u.uid, t.uid, amount, ''); }
        }
        break;
      case 'cleanse':
        for (const t of (offensive ? [u] : tg).filter(alive)) {
          const before = t.statuses.length; t.statuses = t.statuses.filter((s) => !DEBUFFS.has(s.type));
          const n = before - t.statuses.length; if (n > 0) log(st, 'cleanse', u.uid, t.uid, n, '');
        }
        break;
      case 'energy':
        for (const t of sortBy(team(st, u.side).filter((x) => alive(x) && x.uid !== u.uid), (x) => x.slot)) { t.en = Math.min(cfg.maxEn, t.en + (e.amount ?? 0)); log(st, 'energy', u.uid, t.uid, e.amount ?? 0, ''); }
        break;
      case 'status': {
        const on = e.on === 'self' ? [u] : e.on === 'ally' ? (offensive ? [u] : tg) : (offensive ? tg : []);
        for (const t of on.filter(alive)) if (R(st, 100) < (e.chance ?? 100)) addStatus(rules, st, u, t, e.status!, e.turns ?? 1);
        break;
      }
    }
  }
}

function hit(rules: TeamRules, st: TeamState, u: TeamUnit, t: TeamUnit, sk: TeamSkill, power: number) {
  const cfg = rules.config;
  let a = sk.dmg === 'mag' ? u.mag : u.atk;
  const d = sk.dmg === 'mag' ? t.res : t.def;
  if (u.passive?.op === 'rage' && u.hp * 100 < u.maxHp * (u.passive.thresholdPct ?? 0)) a = idiv(a * (100 + (u.passive.pct ?? 0)), 100);
  let dmg = idiv(idiv(idiv(power * a * a, Math.max(1, a + d)), 100) * cfg.damageScalePct, 100);
  let tag = '';
  if ((rules.strong[u.element] ?? []).includes(t.element)) { dmg = idiv(dmg * cfg.strongPct, 100); tag = 'strong'; }
  else if ((rules.strong[t.element] ?? []).includes(u.element)) { dmg = idiv(dmg * cfg.weakPct, 100); tag = 'weak'; }
  if (R(st, 10000) < u.critBp) { dmg = idiv(dmg * cfg.critDamagePct, 100); tag += tag.length > 0 ? '+crit' : 'crit'; }
  if (has(u, 'weaken')) dmg = idiv(dmg * (100 - cfg.weakenPct), 100);
  if (t.passive?.op === 'reduce') dmg = idiv(dmg * (100 - (t.passive.pct ?? 0)), 100);
  if (has(t, 'guard')) dmg = idiv(dmg * (100 - cfg.guardReductionPct), 100);
  const dealt = Math.max(1, dmg);
  const absorbed = Math.min(t.shield, dealt);
  t.shield -= absorbed;
  const hpLoss = Math.min(t.hp, dealt - absorbed);
  t.hp -= hpLoss;
  log(st, 'damage', u.uid, t.uid, dealt, tag + (absorbed > 0 ? `|shield${absorbed}` : ''));
  const before = t.statuses.length;
  if (u.element === 'fire') t.statuses = t.statuses.filter((x) => x.type !== 'freeze');
  if (t.statuses.length < before) log(st, 'thaw', u.uid, t.uid, 0, '');
  if (!alive(t)) log(st, 'down', u.uid, t.uid, 0, '');
  if (u.passive?.op === 'drain' && hpLoss > 0 && alive(u)) heal(st, u, idiv(hpLoss * (u.passive.pct ?? 0), 100), u.uid);
  if (has(t, 'counter') && alive(u) && hpLoss + absorbed > 0) {
    const back = Math.max(1, idiv(dealt * cfg.counterPct, 100));
    const abs2 = Math.min(u.shield, back); u.shield -= abs2;
    u.hp -= Math.min(u.hp, back - abs2);
    log(st, 'counter', t.uid, u.uid, back, '');
    if (!alive(u)) log(st, 'down', t.uid, u.uid, 0, '');
  }
}

function heal(st: TeamState, t: TeamUnit, amount: number, by: number) {
  const h = Math.min(t.maxHp - t.hp, Math.max(0, amount));
  if (h <= 0) return;
  t.hp += h;
  log(st, 'heal', by, t.uid, h, '');
}

function addStatus(rules: TeamRules, st: TeamState, by: TeamUnit, t: TeamUnit, type: string, turns: number) {
  const cfg = rules.config;
  if (HARD_CC.has(type)) {
    if (t.ccImmune > 0 || t.statuses.some((s) => HARD_CC.has(s.type))) { log(st, 'resist', by.uid, t.uid, 0, type); return; }
    turns = cfg.hardCcTurns;
  }
  const remaining = DOTS.has(type) ? turns : turns + (t.acted ? 1 : 0);
  const cur = t.statuses.find((s) => s.type === type);
  if (!cur) t.statuses.push({ type, remaining, stacks: 1 });
  else {
    cur.remaining = Math.max(cur.remaining, remaining);
    if (type === 'poison') cur.stacks = Math.min(cfg.poisonMaxStacks, cur.stacks + 1);
  }
  log(st, 'status', by.uid, t.uid, remaining, type);
}

function roundEnd(rules: TeamRules, st: TeamState) {
  const cfg = rules.config;
  for (const u of sortBy(st.units.filter(alive), (x) => x.uid)) {
    for (const s of u.statuses.filter((s) => DOTS.has(s.type))) {
      const dmg = Math.max(1, idiv(u.maxHp * (s.type === 'burn' ? cfg.burnPct : cfg.poisonPct * s.stacks), 100));
      u.hp -= Math.min(u.hp, dmg);
      log(st, s.type, 0, u.uid, dmg, '');
      if (!alive(u)) { log(st, 'down', 0, u.uid, 0, s.type); break; }
    }
    if (!alive(u)) continue;
    if (has(u, 'regen')) heal(st, u, idiv(u.maxHp * cfg.regenPct, 100), u.uid);
    if (u.passive?.op === 'regen') heal(st, u, idiv(u.maxHp * (u.passive.pct ?? 0), 100), u.uid);
  }
  for (const u of sortBy(st.units, (x) => x.uid)) {
    if (u.ccImmune > 0) u.ccImmune--;
    for (const s of u.statuses) s.remaining--;
    for (const gone of u.statuses.filter((s) => s.remaining <= 0)) { if (HARD_CC.has(gone.type)) u.ccImmune = cfg.ccImmunityTurns; }
    u.statuses = u.statuses.filter((s) => s.remaining > 0);
    for (const k of Object.keys(u.cooldowns)) u.cooldowns[k] = Math.max(0, u.cooldowns[k] - 1);
    if (alive(u)) u.en = Math.min(cfg.maxEn, u.en + cfg.roundEnGain);
    u.acted = false;
  }
  checkEnd(st);
  if (st.phase === 'finished') return;
  if (st.round >= cfg.maxRounds) {
    const score = (side: number) => team(st, side).reduce((s, x) => s + idiv(x.hp * 10000, x.maxHp), 0);
    const a = score(0), b = score(1);
    finish(st, a > b ? 0 : b > a ? 1 : null, 'round_limit');
    return;
  }
  st.round++;
  for (const pl of st.players) { pl.plan = null; pl.locked = false; }
  log(st, 'plan', 0, 0, st.round, '');
}

function checkEnd(st: TeamState) {
  if (st.phase === 'finished') return;
  const aDead = team(st, 0).every((x) => !alive(x)), bDead = team(st, 1).every((x) => !alive(x));
  if (aDead && bDead) finish(st, null, 'both_down');
  else if (aDead) finish(st, 1, 'knockout');
  else if (bDead) finish(st, 0, 'knockout');
}

function finish(st: TeamState, winner: number | null, reason: string) {
  if (st.phase === 'finished') return;
  st.phase = 'finished'; st.winner = winner; st.endReason = reason;
  for (const pl of st.players) pl.locked = true;
  log(st, 'finish', 0, 0, winner ?? -1, reason);
}

// ------------------------------------------------------------------ AI (port of TeamAi.cs)

export function decide(rules: TeamRules, st: TeamState, p: number): TeamAction {
  const orders: TeamOrder[] = [];
  const allies = sortBy(team(st, p).filter(alive), (u) => u.slot);
  const enemies = sortBy(team(st, 1 - p).filter(alive), (u) => u.slot);
  if (enemies.length === 0) return { type: 'plan', orders };
  const allyHurt = allies.some((a) => a.hp * 100 < a.maxHp * 55);
  const focus = sortBy(enemies, (e) => idiv(e.hp * 1000, e.maxHp), (e) => e.slot)[0];
  for (const u of allies) {
    let slot = 'basic';
    for (const cand of ['ultimate', 'skill']) {
      const sk = skillFor(rules, u, cand);
      if (!sk) continue;
      if (orderProblem(rules, st, p, { unit: u.uid, slot: cand, target: 0 }) !== null) continue;
      const healing = sk.effects.some((e) => e.op === 'heal') && !sk.target.startsWith('enemy');
      const supportOnly = !sk.target.startsWith('enemy') && !healing;
      if (healing && !allyHurt) continue;
      if (supportOnly && st.round > 1 && cand === 'skill' && u.en < 70) continue;
      slot = cand; break;
    }
    const chosen = skillFor(rules, u, slot)!;
    orders.push({ unit: u.uid, slot, target: chosen.target === 'enemy_single' ? focus.uid : 0 });
  }
  return { type: 'plan', orders };
}
