// Cosmetics shared by every screen (lobby, profile, feed, chat, guild, leaderboard, market): avatar frames and chat
// bubbles are original procedural vector designs rendered from database/seed/cosmetics.json; Trainer skins use the
// original skin art listed in trainers.json (only skins whose idle/blink/happy frames all exist are sold).
import { useEffect, useState, type ReactNode } from 'react';
import { uiArt } from '../state/catalog';
import type { WAccount } from './world';

export interface CosItem {
  id: string; type: 'frame' | 'bubble' | 'effect' | 'title' | 'item'; rarity: string; name: Record<string, string>; desc: Record<string, string>;
  price: { currency: string; amount: number } | null; style: { ring?: string[]; gem?: string | null; animated?: boolean; bg?: string; fg?: string; border?: string; deco?: string; category?: string; particle?: string; color?: string };
  unlock?: string; category?: string; grant?: Record<string, number>; dailyLimit?: number;
}
export interface Outfit { id: string; characterId: string; name: Record<string, string>; source: string; assetsComplete: boolean; rarity?: string; price?: { currency: string; amount: number }; art: { idle: string; blink: string; happy: string } }
export interface CosData { items: CosItem[]; outfits: Outfit[]; defaults: string[]; missions: unknown[] }

let cache: CosData | null = null;
const listeners: ((d: CosData) => void)[] = [];
async function load(): Promise<CosData> {
  const base = import.meta.env.BASE_URL;
  const [c, t, g] = await Promise.all([fetch(`${base}data/cosmetics.json`).then((r) => r.json()), fetch(`${base}data/trainers.json`).then((r) => r.json()), fetch(`${base}data/guild.json`).then((r) => r.json())]);
  return { items: c.items, defaults: c.defaults, outfits: t.outfits, missions: g.missions };
}
export function useCosmetics(): CosData | null {
  const [d, setD] = useState<CosData | null>(cache);
  useEffect(() => {
    if (cache) return;
    listeners.push(setD);
    if (listeners.length === 1) load().then((x) => { cache = x; listeners.splice(0).forEach((f) => f(x)); }).catch(() => { /* offline: render defaults */ });
  }, []);
  return d;
}
export const findItem = (d: CosData | null, id: string | null | undefined) => d?.items.find((i) => i.id === id) ?? null;
export const outfitOf = (d: CosData | null, id: string | null | undefined) => d?.outfits.find((o) => o.id === id) ?? null;
/** Trainer art for the equipped outfit (skin art when owned + equipped, otherwise the character's default frames). */
export function skinArt(d: CosData | null, trainer: string, outfit: string | null | undefined, frame: 'idle' | 'blink' | 'happy' = 'idle') {
  const o = outfitOf(d, outfit);
  const key = o && o.characterId === trainer && o.assetsComplete ? o.art[frame] : frame === 'idle' ? trainer : `${trainer}_${frame}`;
  return uiArt(key);
}

const RARITY_COLOR: Record<string, string> = { default: '#9AA6C2', common: '#6FBF73', rare: '#4A90E2', epic: '#9B5DE5', legendary: '#F2A93B', event: '#FF7A59', limited: '#E8467C' };
export const rarityColor = (r: string) => RARITY_COLOR[r] ?? '#9AA6C2';

/** Avatar with its equipped frame. The face area is inset so the frame never covers eyes or hair (circle crop on the head). */
export function Avatar({ a, size = 56, d, onClick }: { a: Pick<WAccount, 'trainer' | 'outfit' | 'frame'> | null | undefined; size?: number; d: CosData | null; onClick?: () => void }) {
  const f = findItem(d, a?.frame ?? 'frame_default');
  const ring = f?.style.ring ?? ['#FFFFFF', '#C9D8FF'];
  const id = `g${(a?.frame ?? 'fd').replace(/\W/g, '')}${size}`;
  const art = skinArt(d, a?.trainer ?? 'trainer_boy', a?.outfit ?? null);
  const body = (
    <span className={'cs-avatar' + (f?.style.animated ? ' anim' : '')} style={{ width: size, height: size }}>
      <span className="cs-face" style={{ backgroundImage: `url(${art})` }} />
      <svg viewBox="0 0 100 100" className="cs-ring" aria-hidden="true">
        <defs><linearGradient id={id} x1="0" y1="0" x2="1" y2="1">{ring.map((c, i) => <stop key={i} offset={`${(i / Math.max(1, ring.length - 1)) * 100}%`} stopColor={c} />)}</linearGradient></defs>
        <circle cx="50" cy="50" r="45" fill="none" stroke={`url(#${id})`} strokeWidth="8" />
        <circle cx="50" cy="50" r="40.5" fill="none" stroke="rgba(255,255,255,.55)" strokeWidth="1.2" />
        {f?.style.gem && <Gem kind={f.style.gem} color={ring[ring.length - 1]} />}
      </svg>
    </span>
  );
  return onClick ? <button className="cs-avatar-btn" onClick={onClick} aria-label="profile">{body}</button> : body;
}
function Gem({ kind, color }: { kind: string; color: string }) {
  // small original ornaments on the frame's bottom edge
  switch (kind) {
    case 'leaf': return <path d="M50 99c-8-3-11-9-9-15 6 0 10 5 9 15zm0 0c8-3 11-9 9-15-6 0-10 5-9 15z" fill={color} />;
    case 'bubble': return <g fill="#fff" fillOpacity=".85"><circle cx="18" cy="80" r="5" /><circle cx="82" cy="22" r="4" /><circle cx="86" cy="76" r="3" /></g>;
    case 'crystal': return <path d="M50 86l7 7-7 7-7-7z" fill={color} stroke="#fff" strokeWidth="1.5" />;
    case 'sun': return <circle cx="50" cy="94" r="6" fill={color} stroke="#fff" strokeWidth="1.5" />;
    case 'crown': return <path d="M36 12l6 7 8-9 8 9 6-7-3 12H39z" fill={color} stroke="#fff" strokeWidth="1.2" />;
    case 'lantern': return <rect x="45" y="86" width="10" height="12" rx="4" fill={color} stroke="#fff" strokeWidth="1.2" />;
    case 'laurel': return <path d="M30 92c4-6 10-8 16-8M70 92c-4-6-10-8-16-8" stroke={color} strokeWidth="4" fill="none" strokeLinecap="round" />;
    default: return <path d="M50 85l3.5 7 7.5 1-5.5 5 1.5 7.5-7-4-7 4 1.5-7.5-5.5-5 7.5-1z" fill={color} stroke="#fff" strokeWidth="1" />;
  }
}

/** Chat/comment bubble in the sender's equipped style. Text is always rendered as text (never HTML). */
export function Bubble({ d, id, children, mine }: { d: CosData | null; id: string | null | undefined; children: ReactNode; mine?: boolean }) {
  const s = findItem(d, id ?? 'bubble_default')?.style ?? {};
  return <span className={'cs-bubble' + (mine ? ' mine' : '') + (s.deco ? ' deco-' + s.deco : '')} style={{ background: s.bg, color: s.fg, borderColor: s.border }}>{children}</span>;
}

export function Effect({ d, id }: { d: CosData | null; id: string | null | undefined }) {
  const e = findItem(d, id);
  if (!e) return null;
  const glyph = e.style.particle === 'heart' ? '♥' : e.style.particle === 'mist' ? '●' : '✦';
  return <span className={'cs-effect p-' + e.style.particle} aria-hidden="true">{Array.from({ length: 8 }, (_, i) => <i key={i} style={{ left: `${10 + i * 11}%`, animationDelay: `${i * 0.45}s`, color: e.style.color }}>{glyph}</i>)}</span>;
}
