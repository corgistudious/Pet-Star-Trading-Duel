// Types mirroring shared-contracts/Runtime/Rules/Duel/DuelModel.cs (camelCase JSON).
export interface DuelTributes { stage1: number; stage2: number; stage3: number; divine: number }
export interface DuelConfig {
  deckSize: number; petMin: number; petMax: number; maxCopies: number; divineMaxCopies: number; startLp: number; deathPenalty: number;
  startHand: number; maxHand: number; petZones: number; frontZones: number; spellTrapZones: number; maxEnergy: number; startEnergy: number;
  energyPerTurn: number; maxTurnsPerPlayer: number; turnSeconds: number; reactionSeconds: number; chainMaxDepth: number; damageScale: number;
  critPct: number; strongPct: number; weakPct: number; guardPct: number; weakenPct: number; burnPct: number; poisonPct: number;
  evolveCost: number[]; tributes: DuelTributes; rankedGrowthCap: number; timeoutsToForfeit: number;
}
export interface TacticEffect { op: string; target?: string | null; pct: number; stat?: string | null; turns: number; status?: string | null; chance: number; element?: string | null; n: number; maxStage: number; what?: string | null }
export interface TacticDef {
  id: string; type: 'spell' | 'trap'; speed: 'normal' | 'quick' | 'trap'; category: string; cost: number; targeting: string; trigger?: string | null;
  effects: TacticEffect[]; oncePerTurn: boolean; oncePerDuel: boolean; name: Record<string, string>; text: Record<string, string>;
}
export interface PetFormStats { formId: string; stage: number; divine: boolean; element: string; role: string; hp: number; atk: number; def: number; spd: number; critBp: number; skill?: string | null; ultimate?: string | null; passive?: string | null }
export interface DuelCard { id: number; kind: 'pet' | 'tactic'; tacticId?: string | null; cardInstanceId: number; forms: PetFormStats[] }
export interface DuelSkillEffect { type: string; powerPct: number; scale?: string | null; status?: string | null; chance: number; duration: number }
export interface DuelSkill { id: string; cost: number; target: string; effects: DuelSkillEffect[] }
export interface DuelPassive { type: string; stat?: string | null; pct: number; amount: number }
export interface DuelRules { config: DuelConfig; tactics: Record<string, TacticDef>; skills: Record<string, DuelSkill>; passives: Record<string, DuelPassive>; strong: Record<string, string[]> }

export interface DuelStatus { type: string; remaining: number }
export interface DuelBuff { stat: string; pct: number; untilTurn: number }
export interface DuelUnit {
  uid: number; cardId: number; owner: number; zone: number; formIndex: number; formId: string; stage: number; divine: boolean; element: string; role: string;
  maxHp: number; hp: number; atk: number; def: number; spd: number; critBp: number; shield: number; skill?: string | null; ultimate?: string | null; passive?: string | null;
  summonedTurn: number; acted: boolean; moved: boolean; evolvedTurn: number; statuses: DuelStatus[]; buffs: DuelBuff[];
}
export interface SetCard { cardId: number; tacticId: string; setTurn: number }
export interface DuelPlayerState {
  name: string; lp: number; deck: number[]; hand: number[]; grave: number[]; units: DuelUnit[]; zones: (SetCard | null)[];
  energy: number; maxEnergy: number; normalSummonUsed: boolean; turnsTaken: number; usedThisTurn: string[]; usedThisDuel: string[]; timeouts: number;
}
export interface ChainLink { player: number; kind: string; cardId: number; tacticId?: string | null; targets: number[]; attacker: number; target: number; negated: boolean }
export interface DuelEvent { type: string; p: number; a: number; b: number; n: number; s: string }
export interface DuelState {
  rng: bigint; cards: DuelCard[]; players: DuelPlayerState[]; turn: number; active: number; first: number; phase: 'main' | 'battle' | 'finished';
  chain: ChainLink[]; priority: number; lastPassed: boolean; event: string | null; eventPlayer: number; eventUnit: number;
  winner: number | null; endReason: string | null; nextUid: number; version: number; log: DuelEvent[];
}
export interface DuelAction { type: string; card?: number; zone?: number; unit?: number; target?: number; targets?: number[]; tributes?: number[] }
export class DuelRuleError extends Error { constructor(public code: string) { super(code); } }

export const digest = (e: DuelEvent) => `${e.type}:${e.p}:${e.a}:${e.b}:${e.n}:${e.s}`;
export const toAct = (st: DuelState) => (st.priority >= 0 ? st.priority : st.active);
export const finished = (st: DuelState) => st.phase === 'finished';
