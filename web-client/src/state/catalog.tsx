// Display catalog (forms, skills, rarities...) generated from database/seed by tools/build_web_assets.py.
// Purely cosmetic: every number that matters (stats, prices, odds, balances) comes from the server.
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

export interface FormInfo {
  name: string; family: string; stage: number; element: string; role: string;
  stats: { hp: number; atk: number; def: number; spd: number; crit: number };
  skill?: string | null; ultimate?: string | null; passive?: string | null; lore: Record<string, string>;
}
export interface SkillInfo { kind: string; cost: number; target: string; name: Record<string, string>; effects: { type: string; [k: string]: unknown }[] }
export interface RarityInfo { id: string; order: number; color: string }
export interface Catalog {
  forms: Record<string, FormInfo>;
  families: Record<string, { element: string; role: string; forms: string[] }>;
  skills: Record<string, SkillInfo>;
  passives: Record<string, Record<string, unknown>>;
  rarities: RarityInfo[];
  qualities: { id: string; order: number }[];
  finishes: string[] | { id: string }[];
  elements: { elements: { id: string; color: string; strongAgainst: string[] }[]; strongMultiplier: number; weakMultiplier: number };
}

const Ctx = createContext<Catalog | null>(null);

export function CatalogProvider({ children }: { children: ReactNode }) {
  const [cat, setCat] = useState<Catalog | null>(null);
  const [err, setErr] = useState(false);
  useEffect(() => {
    fetch('/data/catalog.json').then((r) => r.json()).then(setCat).catch(() => setErr(true));
  }, []);
  if (err) return <div className="boot">Could not load game data. Check your connection and reload.</div>;
  if (!cat) return <div className="boot"><div className="spinner" /></div>;
  return <Ctx.Provider value={cat}>{children}</Ctx.Provider>;
}

export function useCatalog(): Catalog {
  const c = useContext(Ctx);
  if (!c) throw new Error('catalog not loaded');
  return c;
}

export const RARITY_ORDER = ['N', 'R', 'SR', 'SSR', 'UR', 'LR', 'MR', 'GR', 'EX', 'Secret'];
export const RARITY_COLORS: Record<string, string> = {
  N: '#9CA3AF', R: '#43BD78', SR: '#4099F4', SSR: '#AA70E8', UR: '#F3C04F', LR: '#F36D79', MR: '#DE6DC9', GR: '#48D0C4', EX: '#E6B95E', Secret: '#9673EC',
};
export const ELEMENT_COLORS: Record<string, string> = {
  fire: '#F2643D', water: '#3D9BF2', nature: '#55C26A', earth: '#B98A52', lightning: '#F5C83B', wind: '#7FD6C9', light: '#FFE7A3', dark: '#6B4FA8',
};
export const rarityRank = (r: string) => Math.max(0, RARITY_ORDER.indexOf(r));
export const petArt = (formId: string, thumb = false) => `/art/pets/${thumb ? 'thumb/' : ''}${formId}.webp`;
export const uiArt = (id: string) => `/art/ui/${id}.webp`;
