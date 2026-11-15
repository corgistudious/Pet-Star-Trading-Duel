import { describe, expect, it } from 'vitest';
import type { BattleDto, BattleUnitDto } from '../api/types';
import { actableUnits, boardRows, legalEnemyTargets, planCost, setOrder, targetKind } from './battle';

const skills = (id: string) => ({ s1: { cost: 2, target: 'enemy_single' }, u1: { cost: 5, target: 'enemy_all' }, h1: { cost: 1, target: 'ally_single' } } as Record<string, { cost: number; target: string }>)[id];
const unit = (side: number, index: number, position: number, extra: Partial<BattleUnitDto> = {}): BattleUnitDto => ({
  side, index, position, cardId: index + 1, formId: 'pet_x', element: 'fire', rarity: 'N', level: 1, maxHp: 100, hp: 100, shield: 0, atk: 10, def: 5, spd: 5,
  skillId: 's1', ultimateId: 'u1', passiveId: null, statuses: [], ...extra,
});
const units = [0, 1, 2, 3, 4].flatMap((i) => [unit(0, i, i), unit(1, i, i)]);

describe('battle planning', () => {
  it('computes plan energy from skill costs', () => {
    expect(planCost(units, 0, [{ unitIndex: 0, type: 'Skill' }, { unitIndex: 1, type: 'Ultimate' }, { unitIndex: 2, type: 'Basic' }], skills)).toBe(7);
  });
  it('rejects orders over the energy budget and replaces a unit order', () => {
    let plan = setOrder([], { unitIndex: 0, type: 'Skill' }, units, 0, 3, skills)!;
    expect(plan).toHaveLength(1);
    expect(setOrder(plan, { unitIndex: 1, type: 'Skill' }, units, 0, 3, skills)).toBeNull();
    plan = setOrder(plan, { unitIndex: 0, type: 'Defend' }, units, 0, 3, skills)!;
    expect(plan).toEqual([{ unitIndex: 0, type: 'Defend' }]);
  });
  it('front row must be cleared before the back row can be targeted; taunt first', () => {
    expect(legalEnemyTargets(units, 0).map((u) => u.index)).toEqual([0, 1, 2]);
    const frontDead = units.map((u) => (u.side === 1 && u.position < 3 ? { ...u, hp: 0 } : u));
    expect(legalEnemyTargets(frontDead, 0).map((u) => u.index)).toEqual([3, 4]);
    const taunt = units.map((u) => (u.side === 1 && u.index === 4 ? { ...u, statuses: ['taunt'] } : u));
    expect(legalEnemyTargets(taunt, 0).map((u) => u.index)).toEqual([4]);
  });
  it('stunned or defeated units cannot act', () => {
    const b = { units: units.map((u) => (u.side === 0 && u.index === 1 ? { ...u, statuses: ['stun'] } : u.side === 0 && u.index === 2 ? { ...u, hp: 0 } : u)) } as BattleDto;
    expect(actableUnits(b, 0).map((u) => u.index)).toEqual([0, 3, 4]);
  });
  it('target kind follows the skill target', () => {
    const u = unit(0, 0, 0, { skillId: 'h1' });
    expect(targetKind(u, 'Basic', skills)).toBe('enemy');
    expect(targetKind(u, 'Skill', skills)).toBe('ally');
    expect(targetKind(u, 'Ultimate', skills)).toBeNull();
    expect(targetKind(u, 'Defend', skills)).toBeNull();
  });
  it('lays out exactly 3 front + 2 back slots per side', () => {
    const [front, back] = boardRows(units, 1);
    expect(front).toHaveLength(3);
    expect(back).toHaveLength(2);
    expect(back.map((u) => u?.position)).toEqual([3, 4]);
  });
});
