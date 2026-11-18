// Interactive Web Preview: a browser-only simulation for the owner to review the UI before the official Unity build.
// Nothing here talks to the API or any database. Formulas and tables mirror the server
// (shared-contracts/Runtime/Rules/Progression.cs, database/seed/packs.json) so numbers look right, but the server
// stays the only authority in the real game.
import type { CardDto } from '../api/types';
import type { Catalog } from '../state/catalog';

export interface PackDef {
  id: string; order: number; theme: string; names: Record<string, string>; descriptions: Record<string, string>; artAssetId: string;
  price: { currency: 'MINI_GREEN_BEAN' | 'RAINBOW_STAR'; amount: number }; pool: { stages: number[]; includeStandalone: boolean };
  slotTable: Record<string, number>; qualityTable: Record<string, number>; finishTable: Record<string, number>; divinePool: string[];
  requirements: { minLevel: number; minVip: number; requiresEvent: boolean; requiresInvitation: boolean };
}
export type FullCatalog = Catalog & { packs: { packTypes: PackDef[] } };

export interface SimCard {
  id: number; serial: string; formId: string; rarity: string; quality: string; finish: string; level: number; stars: number;
  enhance: number; breakthrough: number; awakened: boolean; skillLevel: number; divineSet: string | null; origin: string; listed?: boolean;
}
export interface Listing { id: number; card: SimCard; seller: string; currency: 'RAINBOW_STAR' | 'MINI_GREEN_BEAN'; price: number; mine: boolean }
export interface SimState {
  player: { nickname: string; uid: number; level: number; vipPoints: number };
  beans: number; stars: number; materials: Record<string, number>; essences: Record<string, number>; packs: Record<string, number>;
  cards: SimCard[]; listings: Listing[]; nextId: number;
  event: { nameVi: string; nameEn: string; endsAt: string; total: number; sold: number; perPlayer: number; mine: number };
  invitation: { left: number; expiresAt: string } | null;
  log: string[];
}

export const RARITY_ORDER = ['N', 'R', 'SR', 'SSR', 'UR', 'LR', 'MR', 'GR', 'EX', 'Secret', 'DV'];
export const VIP_THRESHOLDS = [0, 100, 300, 800, 1500, 3000, 6000, 10000, 20000, 40000, 80000];
export const vipLevel = (pts: number) => VIP_THRESHOLDS.reduce((l, t, i) => (pts >= t ? i : l), 0);

// ------------------------------------------------------------------ RNG (browser CSPRNG; preview only)
const rnd = (n: number) => { const a = new Uint32Array(1); crypto.getRandomValues(a); return a[0] % n; };
function roll(table: Record<string, number>, order: string[]): string {
  const sum = Object.values(table).reduce((a, b) => a + b, 0);
  let r = rnd(sum);
  for (const k of order) { const w = table[k] ?? 0; if (w <= 0) continue; if (r < w) return k; r -= w; }
  return order[0];
}
const serial = () => Array.from({ length: 12 }, () => 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'[rnd(36)]).join('');

// ------------------------------------------------------------------ progression (same tables as Progression.cs)
const STAR_BEANS = [0, 0, 100, 200, 350, 500, 800, 1200, 1800, 2600, 4000];
const STAR_SHARDS = [0, 0, 5, 10, 15, 25, 40, 60, 90, 130, 200];
const ENH_BEANS = [0, 100, 150, 200, 250, 300, 450, 500, 550, 600, 700, 900, 1000, 1100, 1200, 1400, 1800, 2000, 2200, 2500, 3000];
const ENH_STONES = [0, 1, 1, 2, 2, 3, 4, 4, 5, 5, 6, 8, 8, 10, 10, 12, 15, 15, 18, 18, 20];
export interface Cost { beans: number; items: Record<string, number>; minLevel?: number; minStars?: number; minBt?: number }
export const enhanceTier = (l: number) => (l <= 0 ? 0 : Math.floor((Math.min(l, 20) - 1) / 5) + 1);
export const enhanceBonus = (e: number) => { let b = 0; for (let l = 1; l <= Math.min(e, 20); l++) b += 0.005 * (enhanceTier(l) + 1); return b; };
export const levelCap = (bt: number) => 50 + 5 * Math.min(4, Math.max(0, bt));

export function cost(kind: string, c: SimCard): Cost | null {
  switch (kind) {
    case 'star': return c.stars >= 10 ? null : { beans: STAR_BEANS[c.stars + 1], items: { pet_shard: STAR_SHARDS[c.stars + 1] } };
    case 'enhance': {
      if (c.enhance >= 20) return null;
      const to = c.enhance + 1;
      return { beans: ENH_BEANS[to], items: { enhance_stone: ENH_STONES[to] }, minLevel: [1, 1, 10, 25, 40][enhanceTier(to)] };
    }
    case 'breakthrough': {
      if (c.breakthrough >= 4) return null;
      const to = c.breakthrough + 1;
      return { beans: [0, 2000, 5000, 10000, 20000][to], items: { breakthrough_core: [0, 5, 10, 20, 40][to] }, minLevel: levelCap(c.breakthrough), minStars: [0, 3, 5, 7, 9][to] };
    }
    case 'awaken': return c.awakened ? null : { beans: 30000, items: { awakening_crystal: 10 }, minLevel: 60, minStars: 10, minBt: 2 };
    case 'skill': {
      if (c.skillLevel >= 10) return null;
      const train = [null, [10, 1, 200], [20, 2, 500], [30, 4, 1000], [40, 8, 2000]][c.skillLevel] as number[] | null | undefined;
      if (train) return { beans: train[2], items: { training_scroll: train[1] }, minLevel: train[0] };
      const i = c.skillLevel - 5;
      return { beans: [3000, 4000, 6000, 8000, 12000][i], items: { training_scroll: [12, 16, 20, 25, 30][i] }, minLevel: 45, minBt: Math.min(4, i + 1) };
    }
    default: return null;
  }
}

export function blocked(kind: string, c: SimCard, s: SimState): string | null {
  const k = cost(kind, c);
  if (!k) return 'max_reached';
  if (c.listed) return 'card_busy';
  if (k.minLevel && c.level < k.minLevel) return kind === 'breakthrough' ? 'breakthrough_level_too_low' : kind === 'awaken' ? 'awaken_level_too_low' : kind === 'skill' ? 'training_level_too_low' : 'enhance_level_too_low';
  if (k.minStars && c.stars < k.minStars) return kind === 'awaken' ? 'awaken_stars_too_low' : 'breakthrough_stars_too_low';
  if (k.minBt && c.breakthrough < k.minBt) return kind === 'awaken' ? 'awaken_breakthrough_too_low' : 'training_breakthrough_too_low';
  if (s.beans < k.beans) return 'not_enough_beans';
  for (const [it, n] of Object.entries(k.items)) if ((s.materials[it] ?? 0) < n) return 'insufficient_items';
  return null;
}

export function applyUpgrade(kind: string, c: SimCard): SimCard {
  const n = { ...c };
  if (kind === 'star') n.stars++;
  if (kind === 'enhance') n.enhance++;
  if (kind === 'breakthrough') n.breakthrough++;
  if (kind === 'awaken') n.awakened = true;
  if (kind === 'skill') n.skillLevel++;
  return n;
}

// ------------------------------------------------------------------ evolution (database/seed/pets.json evolutionRules)
export function evolution(cat: Catalog, c: SimCard) {
  const fam = cat.families[cat.forms[c.formId]?.family];
  const idx = fam ? fam.forms.indexOf(c.formId) : -1;
  const next = fam && idx >= 0 ? fam.forms[idx + 1] : undefined;
  if (!next) return null;
  const stage = cat.forms[c.formId].stage;
  const rule = stage === 1 ? { minLevel: 15, essence: 5, beans: 500 } : { minLevel: 30, essence: 15, beans: 2000 };
  return { next, essenceId: 'essence_' + cat.forms[c.formId].element, ...rule };
}

// ------------------------------------------------------------------ stats + DTO
export function stats(cat: Catalog, c: SimCard) {
  const f = cat.forms[c.formId];
  const rar = (cat.rarities as { id: string; statMultiplier?: number }[]).find((r) => r.id === c.rarity)?.statMultiplier ?? 1;
  const q = (cat.qualities as { id: string; statMultiplier?: number }[]).find((x) => x.id === c.quality)?.statMultiplier ?? 1;
  const lvl = Math.max(1, Math.min(levelCap(c.breakthrough), c.level));
  const base = (1 + 0.04 * (lvl - 1)) * rar * q;
  const g = (1 + 0.04 * (Math.min(10, c.stars) - 1)) * (1 + enhanceBonus(c.enhance)) * (c.awakened ? 1.06 : 1);
  const hp = Math.round(f.stats.hp * base * g), atk = Math.round(f.stats.atk * base * g), def = Math.round(f.stats.def * base * g);
  const spd = Math.round(f.stats.spd * (1 + 0.01 * (lvl - 1)));
  return { hp, atk, def, spd, crit: f.stats.crit, power: Math.round(hp * 0.25 + atk * 2 + def * 1.5 + spd) };
}

export function toDto(cat: Catalog, c: SimCard): CardDto {
  const f = cat.forms[c.formId]; const s = stats(cat, c);
  return {
    id: c.id, serial: c.serial, skillLevel: c.skillLevel, formId: c.formId, name: f.name, familyId: f.family, stage: f.stage, element: f.element,
    role: f.role, rarity: c.rarity, quality: c.quality, finish: c.finish, level: c.level, exp: 0, expToNext: 0, ...s,
    skillId: f.skill, ultimateId: f.ultimate, passiveId: f.passive, isFavorite: false, isLocked: false, isBusy: !!c.listed, slot: 0,
    stars: c.stars, enhanceLevel: c.enhance, breakthrough: c.breakthrough, levelCap: levelCap(c.breakthrough), awakened: c.awakened, divineSet: c.divineSet, engravings: [],
  };
}

// ------------------------------------------------------------------ packs
export function divineSets(cat: FullCatalog): Record<string, string> {
  const m: Record<string, string> = {};
  for (const p of cat.packs.packTypes) for (const id of p.divinePool) m[id] = p.theme;
  return m;
}
export function regularPool(cat: Catalog, p: PackDef): string[] {
  return Object.entries(cat.forms).filter(([id, f]) => !id.startsWith('pet_dv_') && (cat.families[f.family]?.forms.length === 1 ? p.pool.includeStandalone : p.pool.stages.includes(f.stage))).map(([id]) => id).sort();
}
export function openPack(cat: FullCatalog, p: PackDef, nextId: number): SimCard[] {
  const pool = regularPool(cat, p); const sets = divineSets(cat);
  const qOrder = cat.qualities.map((x) => x.id); const fOrder = (cat.finishes as { id: string }[]).map((x) => x.id);
  return Array.from({ length: 5 }, (_, i) => {
    const rarity = roll(p.slotTable, RARITY_ORDER);
    const divine = rarity === 'DV';
    const formId = divine ? p.divinePool[rnd(p.divinePool.length)] : pool[rnd(pool.length)];
    return { id: nextId + i, serial: serial(), formId, rarity, quality: roll(p.qualityTable, qOrder), finish: roll(p.finishTable, fOrder), level: 1,
      stars: divine ? 10 : 1, enhance: 0, breakthrough: 0, awakened: false, skillLevel: 1, divineSet: divine ? sets[formId] : null, origin: p.id };
  });
}
export function packOdds(p: PackDef) {
  return RARITY_ORDER.filter((r) => (p.slotTable[r] ?? 0) > 0).map((r) => {
    const ps = p.slotTable[r] / 1e6;
    return { rarity: r, perSlot: ps * 100, perPack: (1 - Math.pow(1 - ps, 5)) * 100 };
  });
}
export function packBlocked(p: PackDef, s: SimState): string | null {
  const r = p.requirements;
  if (s.player.level < r.minLevel) return 'pack_level_too_low';
  if (vipLevel(s.player.vipPoints) < r.minVip) return 'pack_vip_too_low';
  if (r.requiresEvent && (s.event.sold >= s.event.total)) return 'pack_event_sold_out';
  if (r.requiresEvent && s.event.mine >= s.event.perPlayer) return 'pack_event_player_limit';
  if (r.requiresInvitation && (!s.invitation || s.invitation.left < 1)) return 'pack_invitation_required';
  const bal = p.price.currency === 'RAINBOW_STAR' ? s.stars : s.beans;
  if (bal < p.price.amount) return p.price.currency === 'RAINBOW_STAR' ? 'insufficient_rainbow_star' : 'insufficient_mini_green_bean';
  return null;
}

// ------------------------------------------------------------------ test data (a sandbox player; never saved anywhere but this browser tab)
export function seed(cat: FullCatalog): SimState {
  const sets = divineSets(cat);
  const pick = (stage: number) => Object.entries(cat.forms).filter(([id, f]) => !id.startsWith('pet_dv_') && f.stage === stage).map(([id]) => id).sort();
  const s1 = pick(1), s2 = pick(2), s3 = pick(3);
  let id = 1;
  const mk = (formId: string, rarity: string, quality: string, level: number, extra: Partial<SimCard> = {}): SimCard => ({
    id: id++, serial: serial(), formId, rarity, quality, finish: 'Normal', level, stars: 1, enhance: 0, breakthrough: 0, awakened: false, skillLevel: 1,
    divineSet: sets[formId] ?? null, origin: 'preview_seed', ...extra,
  });
  const cards: SimCard[] = [
    mk(s1[3], 'R', 'B', 16), mk(s1[11], 'SR', 'A', 31, { finish: 'Foil' }), mk(s1[20], 'N', 'C', 8), mk(s1[33], 'SSR', 'S', 22),
    mk(s2[4], 'SR', 'B', 35, { stars: 4, enhance: 6 }), mk(s2[18], 'UR', 'A', 40, { stars: 6, enhance: 11, finish: 'Holographic' }),
    mk(s3[2], 'SSR', 'SS', 50, { stars: 9, enhance: 15, breakthrough: 0 }), mk(s3[9], 'EX', 'S', 60, { stars: 10, enhance: 18, breakthrough: 2 }),
    mk(s3[25], 'UR', 'B', 55, { stars: 8, enhance: 12, breakthrough: 1, skillLevel: 6 }),
    mk(s3[40], 'Secret', 'SSS', 70, { stars: 10, enhance: 20, breakthrough: 4, awakened: true, skillLevel: 10, finish: 'Prismatic' }),
  ];
  // one owned Divine Pet per set (10 stars at creation)
  for (const set of ['premium', 'limited', 'star']) {
    const f = Object.keys(sets).sort().find((k) => sets[k] === set)!;
    cards.push(mk(f, 'DV', 'A', set === 'star' ? 1 : 20, { stars: 10, origin: 'divine_' + set + '_pack' }));
  }
  const sellers = ['Mèo Lửa', 'Gấu Trúc Xanh', 'StarHunter', 'Lan Anh', 'DuelKing'];
  const listings: Listing[] = [s3[5], s2[30], s1[44], s3[17], s2[9]].map((f, i) => ({
    id: 900 + i, card: mk(f, ['SSR', 'SR', 'R', 'UR', 'EX'][i], 'A', 20 + i * 8, { stars: 1 + i * 2, enhance: i * 3 }),
    seller: sellers[i], currency: i % 2 ? 'MINI_GREEN_BEAN' : 'RAINBOW_STAR', price: [180, 2400, 600, 420, 1500][i], mine: false,
  }));
  const dv = Object.keys(sets).sort().find((k) => sets[k] === 'star')!;
  listings.push({ id: 950, card: mk(dv, 'DV', 'S', 30, { stars: 10, enhance: 5 }), seller: 'Collector', currency: 'RAINBOW_STAR', price: 9800, mine: false });
  return {
    player: { nickname: 'Owner Preview', uid: 100001, level: 65, vipPoints: 12000 },
    beans: 80000, stars: 6000,
    materials: { pet_shard: 900, enhance_stone: 400, breakthrough_core: 120, awakening_crystal: 30, engraving_dust: 100, training_scroll: 150 },
    essences: Object.fromEntries(['fire', 'water', 'nature', 'earth', 'lightning', 'wind', 'light', 'dark'].map((e) => ['essence_' + e, 40])),
    packs: { basic_pack: 3, premium_pack: 1 }, cards, listings, nextId: 1000,
    event: { nameVi: 'Lễ Hội Đèn Lồng (thử nghiệm)', nameEn: 'Lantern Festival (test)', endsAt: new Date(Date.now() + 5 * 86400_000).toISOString(), total: 500, sold: 137, perPlayer: 5, mine: 0 },
    invitation: { left: 2, expiresAt: new Date(Date.now() + 7 * 86400_000).toISOString() },
    log: [],
  };
}
