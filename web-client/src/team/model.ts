// TypeScript mirror of shared-contracts/Runtime/Rules/Team/TeamModel.cs (camelCase JSON).
export interface TeamConfig {
  teamSize: number; startEn: number; maxEn: number; basicEnGain: number; roundEnGain: number; maxRounds: number; damageScalePct: number;
  critDamagePct: number; strongPct: number; weakPct: number; guardReductionPct: number; burnPct: number; poisonPct: number; poisonMaxStacks: number;
  regenPct: number; hastePct: number; weakenPct: number; counterPct: number; reducePassivePct: number; drainPassivePct: number; regenPassivePct: number;
  startEnPassive: number; hardCcTurns: number; ccImmunityTurns: number; shieldCapPct: number; planSeconds: number; timeoutsToForfeit: number;
}
export interface TeamEffect { op: string; power?: number; scale?: string | null; status?: string | null; chance?: number; turns?: number; on?: string | null; amount?: number }
export interface TeamSkill { id: string; name: Record<string, string>; kind: string; cost: number; cooldown: number; priority: number; target: string; dmg: string; effects: TeamEffect[] }
export interface TeamPassive { id: string; op: string; pct?: number; amount?: number; thresholdPct?: number }
export interface TeamKit { basic: string; skill: string | null; ultimate: string | null; passive: TeamPassive | null; element: string; role: string }
export interface TeamRole { magPct: number; resPct: number; dmg: string }
export interface TeamRules { version: string; config: TeamConfig; roles: Record<string, TeamRole>; strong: Record<string, string[]>; skills: Record<string, TeamSkill>; kits: Record<string, TeamKit> }

export interface TeamStatus { type: string; remaining: number; stacks: number }
export interface TeamUnit {
  uid: number; side: number; slot: number; formId: string; cardInstanceId: number; element: string; role: string; maxHp: number; hp: number;
  atk: number; def: number; mag: number; res: number; spd: number; critBp: number; en: number; shield: number; acted: boolean; ccImmune: number;
  basic: string; skill: string | null; ultimate: string | null; passive: TeamPassive | null; cooldowns: Record<string, number>; statuses: TeamStatus[];
}
export interface TeamOrder { unit: number; slot: string; target: number }
export interface TeamPlayerState { name: string; plan: TeamOrder[] | null; locked: boolean; timeouts: number }
export interface TeamEvent { type: string; round: number; a: number; b: number; n: number; s: string }
export interface TeamState {
  rng: bigint; defsVersion: string; units: TeamUnit[]; players: TeamPlayerState[]; round: number; phase: 'planning' | 'finished';
  winner: number | null; endReason: string | null; version: number; log: TeamEvent[];
}
export interface TeamAction { type: string; orders: TeamOrder[] }
export interface TeamSetupUnit { formId: string; cardInstanceId: number; hp: number; atk: number; def: number; mag: number; res: number; spd: number; critBp: number }
export interface TeamSetupPlayer { name: string; units: TeamSetupUnit[] }
export class TeamRuleError extends Error { constructor(public code: string) { super(code); } }
export const digest = (e: TeamEvent) => `${e.type}:${e.round}:${e.a}:${e.b}:${e.n}:${e.s}`;
export const alive = (u: TeamUnit) => u.hp > 0;
