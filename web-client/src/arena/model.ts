// TypeScript mirror of shared-contracts/Runtime/Rules/Arena/ArenaModel.cs (camelCase JSON, same field meaning).
export interface ArenaConfig {
  deckSize: number; maxCopies: number; minBasic: number; maxEnergyTypes: number; benchSize: number; handLimit: number; startHand: number;
  pointsToWin: number; exPoints: number; normalPoints: number; simultaneousWin: string; firstPlayerDraws: boolean; firstPlayerEnergy: boolean;
  attackBanFirstTurn: string; noEvolveFirstTurn: boolean; supporterFirstTurn: boolean; retreatsPerTurn: number; supportersPerTurn: number;
  energyCarryOver: boolean; weaknessBonus: number; poisonDamage: number; burnDamage: number; burnCureOnHeads: boolean; confusedSelfDamage: number;
  maxTurnsPerPlayer: number; pveMaxTurnsPerPlayer: number; turnSeconds: number; setupSeconds: number; timeoutsToForfeit: number;
  maxTriggerDepth: number; maxActionsPerTurn: number;
}
export interface ArenaEffect { op: string; target: string; amount?: number; status?: string | null; cond?: string | null; turns?: number; element?: string | null; count?: number; coins?: number }
export interface ArenaAttack { id: string; name: Record<string, string>; cost: string[]; damage: number; effects: ArenaEffect[]; text: Record<string, string> }
export interface ArenaAbility { id: string; name: Record<string, string>; trigger: string; effects: ArenaEffect[]; text: Record<string, string> }
export interface ArenaCardDef {
  id: string; kind: 'pet' | 'item' | 'tool' | 'supporter'; species?: string | null; name: Record<string, string>; element?: string; role?: string;
  stage?: number; evolvesFrom?: string | null; hp?: number; ex?: boolean; koPoints?: number; weakness?: string | null; retreat?: number;
  attacks?: ArenaAttack[]; ability?: ArenaAbility | null; target?: string | null; effects?: ArenaEffect[]; text?: Record<string, string>;
}
export interface ArenaRulesData { version: string; config: ArenaConfig; elements: string[]; cards: ArenaCardDef[]; starter: Record<string, number> }

export class ArenaRules {
  readonly byId = new Map<string, ArenaCardDef>();
  constructor(readonly data: ArenaRulesData) { for (const c of data.cards) this.byId.set(c.id, c); }
  get config() { return this.data.config; }
  get version() { return this.data.version; }
  def(id: string): ArenaCardDef { const d = this.byId.get(id); if (!d) throw new Error('unknown card ' + id); return d; }
  has(id: string) { return this.byId.has(id); }
}

export const isBasic = (d: ArenaCardDef) => d.kind === 'pet' && (d.stage ?? 0) === 0;
export const attacksOf = (d: ArenaCardDef) => d.attacks ?? [];
export const effectsOf = (d: ArenaCardDef) => d.effects ?? [];
export const amt = (e: ArenaEffect) => e.amount ?? 0;

export interface ArenaMod { kind: string; amount: number; fromTurn: number; untilTurn: number; source: string }
export interface ArenaPet {
  uid: number; owner: number; cards: number[]; defId: string; damage: number; energy: string[]; tool: number;
  poisoned: boolean; burned: boolean; special: string | null; specialTurn: number; playedTurn: number; evolvedTurn: number; abilityTurn: number; mods: ArenaMod[];
}
export interface ArenaCard { id: number; defId: string; owner: number; cardInstanceId: number }
export interface ArenaPlayerState {
  name: string; deck: number[]; hand: number[]; discard: number[]; active: ArenaPet | null; bench: ArenaPet[]; points: number; energyTypes: string[];
  zoneCurrent: string | null; zoneNext: string | null; energyAttached: boolean; supportersUsed: number; retreats: number; turnsTaken: number;
  setupDone: boolean; mustPromote: boolean; timeouts: number; turnMods: ArenaMod[];
}
export interface ArenaEvent { type: string; p: number; a: number; b: number; n: number; s: string }
export interface ArenaState {
  rng: bigint; defsVersion: string; cards: ArenaCard[]; players: ArenaPlayerState[]; turn: number; active: number; first: number;
  phase: 'setup' | 'main' | 'finished'; continuation: string | null; maxTurns: number; actionsThisTurn: number; winner: number | null;
  endReason: string | null; nextUid: number; version: number; log: ArenaEvent[];
}
export interface ArenaAction { type: string; card?: number; uid?: number; index?: number; targets?: number[]; bench?: number[] }
export interface ArenaDeckCard { defId: string; cardInstanceId: number }
export interface ArenaSetupPlayer { name: string; deck: ArenaDeckCard[]; energyTypes: string[] }
export class ArenaRuleError extends Error { constructor(public code: string) { super(code); } }

export const digest = (e: ArenaEvent) => `${e.type}:${e.p}:${e.a}:${e.b}:${e.n}:${e.s}`;
export const inPlay = (pl: ArenaPlayerState): ArenaPet[] => (pl.active ? [pl.active, ...pl.bench] : [...pl.bench]);
export const finished = (st: ArenaState) => st.phase === 'finished';
