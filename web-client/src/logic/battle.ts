// Client-side helpers for planning a round. They mirror shared-contracts/Runtime/Rules/Battle.cs only to give
// instant feedback (energy left, legal targets); the server re-validates every plan (Normalize) and resolves it.
import type { ActionType, BattleDto, BattleUnitDto, PlannedAction } from '../api/types';

export interface SkillCost { cost: number; target: string }
export type SkillLookup = (id: string) => SkillCost | undefined;

export const isFront = (u: BattleUnitDto) => u.position < 3;
export const alive = (u: BattleUnitDto) => u.hp > 0;

export function actionCost(u: BattleUnitDto, type: ActionType, skills: SkillLookup): number | null {
  if (type === 'Skill') return u.skillId ? skills(u.skillId)?.cost ?? null : null;
  if (type === 'Ultimate') return u.ultimateId ? skills(u.ultimateId)?.cost ?? null : null;
  return 0;
}

export function planCost(units: BattleUnitDto[], side: number, plan: PlannedAction[], skills: SkillLookup): number {
  return plan.reduce((sum, a) => {
    const u = units.find((x) => x.side === side && x.index === a.unitIndex);
    return sum + (u ? actionCost(u, a.type, skills) ?? 0 : 0);
  }, 0);
}

/** Units of `side` that may receive an order this round. */
export function actableUnits(b: BattleDto, side: number): BattleUnitDto[] {
  return b.units.filter((u) => u.side === side && alive(u) && !u.statuses.includes('stun'));
}

/** Legal single-target enemies: taunting units first, else front row while any front unit lives. */
export function legalEnemyTargets(units: BattleUnitDto[], attackerSide: number): BattleUnitDto[] {
  const enemies = units.filter((u) => u.side !== attackerSide && alive(u));
  const taunt = enemies.filter((u) => u.statuses.includes('taunt'));
  if (taunt.length) return taunt;
  const front = enemies.filter(isFront);
  return front.length ? front : enemies;
}

/** Which side a chosen target belongs to for a given action, or null when the action needs no choice. */
export function targetKind(u: BattleUnitDto, type: ActionType, skills: SkillLookup): 'enemy' | 'ally' | null {
  if (type === 'Defend') return null;
  if (type === 'Basic') return 'enemy';
  const id = type === 'Skill' ? u.skillId : u.ultimateId;
  const t = id ? skills(id)?.target : undefined;
  if (t === 'enemy_single' || t === undefined) return 'enemy';
  if (t === 'ally_single') return 'ally';
  return null; // area / self / automatic targets
}

/** Sets (or replaces) one unit's order; returns null if it would exceed the energy budget. */
export function setOrder(plan: PlannedAction[], order: PlannedAction, units: BattleUnitDto[], side: number, energy: number, skills: SkillLookup): PlannedAction[] | null {
  const next = [...plan.filter((a) => a.unitIndex !== order.unitIndex), order].sort((a, b) => a.unitIndex - b.unitIndex);
  return planCost(units, side, next, skills) <= energy ? next : null;
}

/** Slot layout for the 3+2 board: front row positions 0..2, back row 3..4. */
export function boardRows(units: BattleUnitDto[], side: number): [(BattleUnitDto | null)[], (BattleUnitDto | null)[]] {
  const at = (p: number) => units.find((u) => u.side === side && u.position === p) ?? null;
  return [[at(0), at(1), at(2)], [at(3), at(4)]];
}
