// "Preview server": the shared world for every Preview account created in this browser (localStorage). It mirrors the
// real server rules (visibility, unique likes, one reply level, shares that keep the source, auto-moderation hold,
// guild roles, escrowed market listings/trades, beans-only market) so two accounts can really interact — by switching
// accounts, or live across two browser tabs (storage events). Nothing here is fake: the feed starts empty and only holds
// what Preview accounts post. The production game runs these rules on the API server + PostgreSQL.
import { useEffect, useState } from 'react';
import type { SimCard } from './sim';

export type Vis = 'public' | 'friends' | 'guild' | 'private';
export type Trainer = 'trainer_boy' | 'trainer_girl';
export interface Stats { pvpWins: number; pvpGames: number; collection: number; rare: number; evolutions: number; pve: number; achievements: number; minigames: number; bossDamage: number; power: number }
export interface WAccount {
  uid: number; name: string; trainer: Trainer | null; outfit: string | null; frame: string; bubble: string; effect: string | null; title: string | null;
  level: number; createdAt: string; lastSeen: number; profileVis: 'public' | 'friends' | 'private'; defaultVis: Vis; companion: string | null; stats: Stats;
  showcase: string[];
  /** The account's saved 3-Pet team (published so Guild War opponents can fight it). */
  team: CardSnap[];
  /** Snapshot of owned, unlisted cards (max 60) so trade partners can pick what to ask for; re-checked on accept. */
  cards: CardSnapId[];
}
export interface CardSnap { formId: string; rarity: string; level: number; stars: number; finish: string; quality: string }
export interface WPost {
  id: number; author: number; type: 'text' | 'showcase' | 'share' | 'battle' | 'achievement'; caption: string; visibility: Vis; guildId: number | null;
  showcaseKind: string | null; cards: CardSnap[]; data: Record<string, string | number>; sharedFrom: number | null; status: 'visible' | 'pending' | 'hidden' | 'deleted';
  flags: string[]; likes: number[]; shares: number; pinned: boolean; createdAt: number; editedAt: number | null; maxRarity: number;
}
export interface WComment { id: number; postId: number; author: number; text: string; parentId: number | null; likes: number[]; status: 'visible' | 'pending' | 'hidden' | 'deleted'; createdAt: number }
export interface WNotif { id: number; to: number; type: string; from: number; postId?: number; text?: string; at: number; read: boolean }
export interface Msg { id: number; from: number; text: string; at: number; status: 'visible' | 'pending' }
export type Role = 'leader' | 'vice' | 'officer' | 'elite' | 'member';
export interface WGuild {
  id: number; name: string; tag: string; emblem: string; banner: string; visibility: 'public' | 'private'; joinPolicy: 'open' | 'approval'; minLevel: number;
  desc: string; level: number; exp: number; members: { uid: number; role: Role; joinedAt: number }[]; requests: number[]; invites: number[];
  logs: { at: number; text: string }[]; announcements: { at: number; by: number; text: string }[];
  progress: Record<string, number>; claims: Record<string, number[]>; contrib: Record<number, number>;
  boss: { key: string; bossId: string; formId: string; hp: number; maxHp: number; hits: { uid: number; dmg: number; day: string }[] } | null;
  war: { key: string; opponent: number | null; points: number; battles: number; wins: number } | null; warHistory: { key: string; points: number; result: string }[];
}
export interface Listing { id: number; seller: number; card: SimCard; price: number; status: 'active' | 'sold' | 'cancelled'; buyer: number | null; at: number; closedAt: number | null }
export interface Trade { id: number; from: number; to: number; offer: SimCard[]; request: CardSnapId[]; note: string; status: 'pending' | 'accepted' | 'declined' | 'cancelled' | 'failed'; at: number; closedAt: number | null }
export interface CardSnapId extends CardSnap { id: number }
export interface Inbox { cards: SimCard[]; beans: number; log: string[] }
export interface World {
  v: 1; seq: number; accounts: WAccount[]; posts: WPost[]; comments: WComment[]; notifs: WNotif[];
  reports: { id: number; by: number; type: string; target: number; reason: string; at: number }[];
  modQueue: { id: number; type: 'post' | 'comment' | 'chat'; target: number; author: number; flags: string[]; status: 'pending' | 'approved' | 'rejected'; at: number }[];
  friendships: { a: number; b: number; status: 'pending' | 'accepted'; from: number }[]; blocks: { by: number; who: number }[];
  guilds: WGuild[]; chat: { world: Msg[]; guild: Record<number, Msg[]>; dm: Record<string, Msg[]> }; reads: Record<number, Record<string, number>>;
  listings: Listing[]; trades: Trade[]; inbox: Record<number, Inbox>;
}

const KEY = 'petstar.preview.world.v1';
const EVT = 'petstar-world';
const fresh = (): World => ({ v: 1, seq: 1000, accounts: [], posts: [], comments: [], notifs: [], reports: [], modQueue: [], friendships: [], blocks: [], guilds: [],
  chat: { world: [], guild: {}, dm: {} }, reads: {}, listings: [], trades: [], inbox: {} });
let memory: World | null = null;   // fallback when storage is blocked (private mode, previews)

export function loadWorld(): World {
  try { const j = localStorage.getItem(KEY); if (j) return { ...fresh(), ...(JSON.parse(j) as World) }; } catch { /* storage blocked */ }
  return memory ?? fresh();
}
function saveWorld(w: World) {
  memory = w;
  try { localStorage.setItem(KEY, JSON.stringify(w)); } catch { /* quota/blocked: keep in memory */ }
  try { dispatchEvent(new Event(EVT)); } catch { /* not in a browser */ }
}
/** Read-modify-write against the latest stored world (another tab may have changed it). Throws WorldError codes. */
export function mutate<T>(fn: (w: World) => T): T {
  const w = loadWorld();
  const r = fn(w);
  saveWorld(w);
  return r;
}
export class WorldError extends Error { constructor(public code: string) { super(code); } }
const fail = (code: string): never => { throw new WorldError(code); };
const next = (w: World) => ++w.seq;

export function useWorld(): World {
  const [w, setW] = useState<World>(() => loadWorld());
  useEffect(() => {
    const f = () => setW(loadWorld());
    addEventListener(EVT, f); addEventListener('storage', f);
    const h = setInterval(f, 2000);   // near-realtime between tabs even if a storage event is missed
    return () => { removeEventListener(EVT, f); removeEventListener('storage', f); clearInterval(h); };
  }, []);
  return w;
}
export function resetWorld() { try { localStorage.removeItem(KEY); } catch { /* ignore */ } memory = null; saveWorld(fresh()); }

// ------------------------------------------------------------------ accounts, friends, blocks

const emptyStats = (): Stats => ({ pvpWins: 0, pvpGames: 0, collection: 0, rare: 0, evolutions: 0, pve: 0, achievements: 0, minigames: 0, bossDamage: 0, power: 0 });
export function loginOrRegister(name: string): WAccount {
  return mutate((w) => {
    const ex = w.accounts.find((a) => a.name.toLowerCase() === name.toLowerCase());
    if (ex) { ex.lastSeen = Date.now(); return ex; }
    const uid = 100100 + w.accounts.length * 7 + Math.floor(Math.random() * 7);
    const a: WAccount = { uid, name, trainer: null, outfit: null, frame: 'frame_default', bubble: 'bubble_default', effect: null, title: 'title_rookie', level: 1,
      createdAt: new Date().toISOString(), lastSeen: Date.now(), profileVis: 'public', defaultVis: 'public', companion: null, stats: emptyStats(), showcase: [], team: [], cards: [] };
    w.accounts.push(a);
    return a;
  });
}
export const acc = (w: World, uid: number) => w.accounts.find((a) => a.uid === uid);
export function patchAccount(uid: number, p: Partial<WAccount>) { mutate((w) => { const a = acc(w, uid); if (a) Object.assign(a, p, { lastSeen: Date.now() }); }); }
export const online = (a: WAccount | undefined) => !!a && Date.now() - a.lastSeen < 5 * 60_000;
export const isBlocked = (w: World, a: number, b: number) => w.blocks.some((x) => (x.by === a && x.who === b) || (x.by === b && x.who === a));
export const areFriends = (w: World, a: number, b: number) => a !== b && w.friendships.some((f) => f.status === 'accepted' && ((f.a === a && f.b === b) || (f.a === b && f.b === a)));
export const friendIds = (w: World, me: number) => w.friendships.filter((f) => f.status === 'accepted' && (f.a === me || f.b === me)).map((f) => (f.a === me ? f.b : f.a));
export const incoming = (w: World, me: number) => w.friendships.filter((f) => f.status === 'pending' && f.from !== me && (f.a === me || f.b === me)).map((f) => f.from);

function notify(w: World, to: number, type: string, from: number, extra: Partial<WNotif> = {}, dedupe?: string) {
  if (to === from || isBlocked(w, to, from)) return;
  if (dedupe && w.notifs.some((n) => n.to === to && `${n.type}:${n.from}:${n.postId ?? ''}:${n.text ?? ''}` === dedupe)) return;   // no repeat spam
  w.notifs.push({ id: next(w), to, type, from, at: Date.now(), read: false, ...extra });
  if (w.notifs.length > 600) w.notifs.splice(0, w.notifs.length - 600);
}

export function friendRequest(me: number, uid: number) {
  mutate((w) => {
    const other = acc(w, uid) ?? fail('player_not_found');
    if (other.uid === me) fail('cannot_friend_self');
    if (isBlocked(w, me, uid)) fail('user_blocked');
    const ex = w.friendships.find((f) => (f.a === me && f.b === uid) || (f.a === uid && f.b === me));
    if (ex?.status === 'accepted') fail('already_friends');
    if (ex && ex.from === uid) { ex.status = 'accepted'; notify(w, uid, 'friend_accepted', me); return; }
    if (ex) fail('request_exists');
    w.friendships.push({ a: me, b: uid, status: 'pending', from: me });
    notify(w, uid, 'friend_request', me, {}, `friend_request:${me}::`);
  });
}
export function friendRespond(me: number, from: number, accept: boolean) {
  mutate((w) => {
    const f = w.friendships.find((x) => x.status === 'pending' && x.from === from && (x.a === me || x.b === me)) ?? fail('request_not_found');
    if (accept) { f.status = 'accepted'; notify(w, from, 'friend_accepted', me); } else w.friendships.splice(w.friendships.indexOf(f), 1);
  });
}
export function unfriend(me: number, uid: number) { mutate((w) => { w.friendships = w.friendships.filter((f) => !((f.a === me && f.b === uid) || (f.a === uid && f.b === me))); }); }
export function block(me: number, uid: number) { mutate((w) => { if (!w.blocks.some((b) => b.by === me && b.who === uid)) w.blocks.push({ by: me, who: uid }); w.friendships = w.friendships.filter((f) => !((f.a === me && f.b === uid) || (f.a === uid && f.b === me))); }); }
export function unblock(me: number, uid: number) { mutate((w) => { w.blocks = w.blocks.filter((b) => !(b.by === me && b.who === uid)); }); }

// ------------------------------------------------------------------ moderation (mirrors ContentGuard on the server)

const BLOCKED = ['fuck', 'shit', 'bitch', 'nigger', 'faggot', 'đụ', 'địt', 'đéo', 'lồn', 'cặc', 'đĩ'];
const RISKY: [string, string[]][] = [
  ['payment', ['momo', 'chuyển khoản', 'chuyen khoan', 'stk', 'paypal', 'zalopay', 'banking', 'tiền thật', 'tien that', 'cash out', 'cashout', 'nạp hộ', 'nap ho', 'bán acc', 'ban acc', 'sell account', 'real money', 'venmo']],
  ['off_platform', ['zalo', 'telegram', 'discord.gg', 'whatsapp', 'facebook.com', 'inbox mình', 'ib mình', 'add zalo']],
  ['adult', ['18+', 'sex', 'nude', 'khỏa thân', 'khoa than', 'porn', 'sexy', 'nsfw', 'onlyfans']],
  ['harassment', ['ngu vãi', 'đồ ngu', 'óc chó', 'oc cho', 'kill yourself', 'kys', 'loser', 'thằng ngu', 'con ngu']],
];
export function clean(text: string, max: number, required = true): string {
  const t = (text ?? '').trim();
  if (required && !t) fail('text_required');
  if (t.length > max) fail('text_too_long');
  const lower = t.toLowerCase();
  if (BLOCKED.some((b) => lower.includes(b))) fail('text_inappropriate');
  if (/(https?:\/\/|www\.|\b[\w-]+\.(com|net|org|io|gg|vn|xyz|ru)\b)/i.test(t)) fail('text_links_not_allowed');
  if (/(\S)\1{9,}/.test(t)) fail('text_spam');
  if (/<\s*\/?\s*script|javascript:/i.test(t)) fail('text_invalid');   // rendered as text anyway (React escapes), rejected for clarity
  return t;
}
export function assess(text: string): string[] {
  const lower = text.toLowerCase();
  const flags = RISKY.filter(([, ws]) => ws.some((x) => lower.includes(x))).map(([f]) => f);
  if (/(\+?\d[\d .-]{8,}\d)/.test(text) || /[\w.+-]+@[\w-]+\.[\w.]+/.test(text)) flags.push('personal_info');
  return flags;
}
function hold(w: World, type: 'post' | 'comment' | 'chat', target: number, author: number, flags: string[]) {
  if (!w.modQueue.some((q) => q.type === type && q.target === target && q.status === 'pending')) w.modQueue.push({ id: next(w), type, target, author, flags, status: 'pending', at: Date.now() });
}

// ------------------------------------------------------------------ feed

export const guildOf = (w: World, uid: number) => w.guilds.find((g) => g.members.some((m) => m.uid === uid)) ?? null;
export function canView(w: World, p: WPost, viewer: number): boolean {
  if (p.status === 'deleted') return false;
  if (p.author === viewer) return true;
  if (p.status !== 'visible' || isBlocked(w, viewer, p.author)) return false;
  if (p.guildId != null && guildOf(w, viewer)?.id !== p.guildId) return false;
  return p.visibility === 'public' || (p.visibility === 'guild' && p.guildId != null) || (p.visibility === 'friends' && areFriends(w, viewer, p.author));
}
const RANK = ['N', 'R', 'SR', 'SSR', 'UR', 'LR', 'MR', 'GR', 'EX', 'Secret', 'DV'];
export const RARE_RANK = 3;
export const rarityRank = (r: string) => Math.max(0, RANK.indexOf(r));
export const snap = (c: SimCard): CardSnap => ({ formId: c.formId, rarity: c.rarity, level: c.level, stars: c.stars, finish: c.finish, quality: c.quality });

export interface NewPost { type: WPost['type']; caption: string; visibility: Vis; showcaseKind?: string | null; cards?: CardSnap[]; data?: Record<string, string | number>; sharedFrom?: number | null }
/** Cards passed here come from the poster's own collection (the caller reads them from the logged-in account's state). */
export function createPost(me: number, n: NewPost): WPost {
  return mutate((w) => {
    const now = Date.now();
    const caption = clean(n.caption, 500, n.type === 'text');
    if (w.posts.filter((p) => p.author === me && now - p.createdAt < 3600_000).length >= 10) fail('posting_too_fast');
    if (caption && w.posts.some((p) => p.author === me && p.caption === caption && p.status !== 'deleted' && now - p.createdAt < 600_000)) fail('duplicate_post');
    const g = guildOf(w, me);
    if (n.visibility === 'guild' && !g) fail('not_in_guild');
    if ((n.cards?.length ?? 0) > 5) fail('post_cards_1_to_5');
    const flags = assess(caption);
    const p: WPost = { id: next(w), author: me, type: n.type, caption, visibility: n.visibility, guildId: n.visibility === 'guild' ? g!.id : null, showcaseKind: n.showcaseKind ?? null,
      cards: n.cards ?? [], data: n.data ?? {}, sharedFrom: n.sharedFrom ?? null, status: flags.length ? 'pending' : 'visible', flags, likes: [], shares: 0, pinned: false,
      createdAt: now, editedAt: null, maxRarity: Math.max(0, ...(n.cards ?? []).map((c) => rarityRank(c.rarity))) };
    w.posts.push(p);
    if (flags.length) hold(w, 'post', p.id, me, flags);
    else if (g) { guildProgress(w, g, me, 'post', 1); if (p.showcaseKind) guildProgress(w, g, me, 'showcase', 1); }
    return p;
  });
}
export function editPost(me: number, id: number, caption: string) {
  mutate((w) => {
    const p = w.posts.find((x) => x.id === id && x.author === me && x.status !== 'deleted') ?? fail('post_not_found');
    if (Date.now() - p.createdAt > 24 * 3600_000) fail('edit_window_closed');
    if (p.status === 'hidden') fail('post_under_review');
    p.caption = clean(caption, 500, p.type === 'text'); p.editedAt = Date.now();
    const flags = assess(p.caption);
    if (flags.length) { p.status = 'pending'; p.flags = flags; hold(w, 'post', p.id, me, flags); }
  });
}
export function deletePost(me: number, id: number) { mutate((w) => { const p = w.posts.find((x) => x.id === id && x.author === me) ?? fail('post_not_found'); p.status = 'deleted'; p.pinned = false; }); }
export function pinPost(me: number, id: number) { mutate((w) => { const p = w.posts.find((x) => x.id === id && x.author === me && x.status === 'visible') ?? fail('post_not_found'); const was = p.pinned; w.posts.forEach((x) => { if (x.author === me) x.pinned = false; }); p.pinned = !was; }); }
function visiblePost(w: World, id: number, me: number) { const p = w.posts.find((x) => x.id === id && x.status === 'visible'); if (!p || !canView(w, p, me)) fail('post_not_found'); return p!; }
export function toggleLike(me: number, id: number) {
  mutate((w) => {
    const p = visiblePost(w, id, me);
    const i = p.likes.indexOf(me);
    if (i >= 0) p.likes.splice(i, 1);
    else { p.likes.push(me); notify(w, p.author, 'post_liked', me, { postId: id }, `post_liked:${me}:${id}:`); }
  });
}
export function addComment(me: number, postId: number, text: string, parentId: number | null) {
  return mutate((w) => {
    const p = visiblePost(w, postId, me);
    const t = clean(text, 300);
    const now = Date.now();
    if (w.comments.filter((c) => c.author === me && now - c.createdAt < 3600_000).length >= 30) fail('commenting_too_fast');
    if (w.comments.some((c) => c.author === me && c.postId === postId && c.text === t && now - c.createdAt < 300_000)) fail('duplicate_comment');
    let parent: WComment | undefined;
    if (parentId != null) {
      parent = w.comments.find((c) => c.id === parentId && c.postId === postId && c.status === 'visible') ?? fail('comment_not_found');
      if (parent.parentId != null) fail('reply_depth_limit');
      if (isBlocked(w, me, parent.author)) fail('user_blocked');
    }
    const flags = assess(t);
    const c: WComment = { id: next(w), postId, author: me, text: t, parentId: parent?.id ?? null, likes: [], status: flags.length ? 'pending' : 'visible', createdAt: now };
    w.comments.push(c);
    if (flags.length) hold(w, 'comment', c.id, me, flags);
    else {
      notify(w, p.author, 'post_commented', me, { postId, text: t.slice(0, 40) });
      if (parent && parent.author !== p.author) notify(w, parent.author, 'comment_replied', me, { postId, text: t.slice(0, 40) });
    }
    return c;
  });
}
export function toggleCommentLike(me: number, id: number) {
  mutate((w) => {
    const c = w.comments.find((x) => x.id === id && x.status === 'visible') ?? fail('comment_not_found');
    visiblePost(w, c.postId, me);
    const i = c.likes.indexOf(me);
    if (i >= 0) c.likes.splice(i, 1); else { c.likes.push(me); notify(w, c.author, 'comment_liked', me, { postId: c.postId }, `comment_liked:${me}:${c.postId}:`); }
  });
}
export function deleteComment(me: number, id: number) { mutate((w) => { const c = w.comments.find((x) => x.id === id && x.author === me) ?? fail('comment_not_found'); c.status = 'deleted'; }); }
/** Share to own feed (keeps the source) or to a friend by DM. Friends-only / private posts are never re-shared. */
export function share(me: number, id: number, target: 'feed' | 'friend', caption = '', friend?: number) {
  return mutate((w) => {
    let orig = visiblePost(w, id, me);
    if (orig.type === 'share' && orig.sharedFrom != null) orig = visiblePost(w, orig.sharedFrom, me);
    if (orig.visibility === 'private' || orig.visibility === 'friends') fail('post_not_shareable');
    if (target === 'friend') {
      if (friend == null || !areFriends(w, me, friend)) fail('dm_friends_only');
      if (orig.guildId != null && guildOf(w, friend!)?.id !== orig.guildId) fail('post_not_shareable');
      const k = dmKey(me, friend!); (w.chat.dm[k] ??= []).push({ id: next(w), from: me, text: `[post:${orig.id}]`, at: Date.now(), status: 'visible' });
    } else {
      const t = clean(caption, 500, false);
      const flags = assess(t);
      const p: WPost = { id: next(w), author: me, type: 'share', caption: t, visibility: orig.guildId != null ? 'guild' : 'public', guildId: orig.guildId, showcaseKind: null, cards: [], data: {},
        sharedFrom: orig.id, status: flags.length ? 'pending' : 'visible', flags, likes: [], shares: 0, pinned: false, createdAt: Date.now(), editedAt: null, maxRarity: 0 };
      w.posts.push(p);
      if (flags.length) hold(w, 'post', p.id, me, flags);
    }
    orig.shares++;
    notify(w, orig.author, 'post_shared', me, { postId: orig.id }, `post_shared:${me}:${orig.id}:${target}`);
  });
}
export function report(me: number, type: 'post' | 'comment' | 'chat' | 'user', target: number, reason: string) {
  mutate((w) => {
    if (w.reports.some((r) => r.by === me && r.type === type && r.target === target)) fail('already_reported');
    if (w.reports.filter((r) => r.by === me && Date.now() - r.at < 3600_000).length >= 20) fail('too_many_reports');
    w.reports.push({ id: next(w), by: me, type, target, reason, at: Date.now() });
    // 3 distinct reports hide the content and queue it for review (same rule as the server)
    if (type === 'user' || w.reports.filter((r) => r.type === type && r.target === target).length < 3) return;
    if (type === 'post') { const p = w.posts.find((x) => x.id === target); if (p && p.status === 'visible') { p.status = 'hidden'; hold(w, 'post', p.id, p.author, ['report']); } }
    if (type === 'comment') { const c = w.comments.find((x) => x.id === target); if (c && c.status === 'visible') { c.status = 'hidden'; hold(w, 'comment', c.id, c.author, ['report']); } }
  });
}
export type FeedTab = 'explore' | 'latest' | 'friends' | 'guild' | 'showcase' | 'rare' | 'popular' | 'mine';
export function feed(w: World, me: number, tab: FeedTab): WPost[] {
  const now = Date.now();
  const pub = (p: WPost) => p.status === 'visible' && p.visibility === 'public' && p.guildId == null && !isBlocked(w, me, p.author);
  let list: WPost[];
  switch (tab) {
    case 'mine': list = w.posts.filter((p) => p.author === me && p.status !== 'deleted'); break;
    case 'friends': { const fr = new Set(friendIds(w, me)); list = w.posts.filter((p) => fr.has(p.author) && p.guildId == null && canView(w, p, me)); break; }
    case 'guild': { const g = guildOf(w, me); if (!g) return []; const mates = new Set(g.members.map((m) => m.uid)); list = w.posts.filter((p) => (p.guildId === g.id || (mates.has(p.author) && pub(p))) && canView(w, p, me)); break; }
    case 'showcase': list = w.posts.filter((p) => pub(p) && p.showcaseKind); break;
    case 'rare': list = w.posts.filter((p) => pub(p) && p.maxRarity >= RARE_RANK); break;
    case 'popular': list = w.posts.filter((p) => pub(p) && p.likes.length > 0 && now - p.createdAt < 7 * 86400_000).sort((a, b) => b.likes.length - a.likes.length || b.id - a.id); return list;
    case 'explore': {
      const score = (p: WPost) => p.likes.length * 3 + commentCount(w, p.id) * 2 + p.shares * 4 - (now - p.createdAt) / 3600_000 / 12;
      return w.posts.filter((p) => pub(p) && now - p.createdAt < 14 * 86400_000).sort((a, b) => score(b) - score(a) || b.id - a.id);
    }
    default: list = w.posts.filter(pub);
  }
  return list.sort((a, b) => b.id - a.id);
}
export const commentCount = (w: World, postId: number) => w.comments.filter((c) => c.postId === postId && c.status === 'visible').length;
/** Weekly showcase spotlight: distinct likers (author excluded) ×3 + distinct commenters (author excluded) ×2. */
export function spotlight(w: World, me: number) {
  const week = Date.now() - 7 * 86400_000;
  return w.posts.filter((p) => p.status === 'visible' && p.visibility === 'public' && p.guildId == null && p.showcaseKind && p.createdAt > week && !isBlocked(w, me, p.author))
    .map((p) => ({ p, score: p.likes.filter((u) => u !== p.author).length * 3 + new Set(w.comments.filter((c) => c.postId === p.id && c.status === 'visible' && c.author !== p.author).map((c) => c.author)).size * 2 }))
    .filter((x) => x.score > 0).sort((a, b) => b.score - a.score || a.p.id - b.p.id).slice(0, 5);
}

// ------------------------------------------------------------------ chat

export const dmKey = (a: number, b: number) => `${Math.min(a, b)}:${Math.max(a, b)}`;
export type Channel = { kind: 'world' } | { kind: 'guild' } | { kind: 'dm'; uid: number };
export const chanKey = (w: World, me: number, c: Channel) => (c.kind === 'world' ? 'world' : c.kind === 'guild' ? `guild:${guildOf(w, me)?.id ?? 0}` : `dm:${dmKey(me, c.uid)}`);
export function messages(w: World, me: number, c: Channel): Msg[] {
  let list: Msg[] = [];
  if (c.kind === 'world') list = w.chat.world;
  else if (c.kind === 'guild') { const g = guildOf(w, me); if (!g) return []; list = w.chat.guild[g.id] ?? []; }
  else { if (!areFriends(w, me, c.uid)) return []; list = w.chat.dm[dmKey(me, c.uid)] ?? []; }
  return list.filter((m) => (m.status === 'visible' || m.from === me) && !isBlocked(w, me, m.from));
}
export function sendChat(me: number, c: Channel, text: string) {
  return mutate((w) => {
    const t = clean(text, 200);
    if (/^\[(system|post:)/i.test(t)) fail('text_invalid');   // no fake system messages / forged links
    const now = Date.now();
    const all = [...w.chat.world, ...Object.values(w.chat.guild).flat(), ...Object.values(w.chat.dm).flat()].filter((m) => m.from === me && now - m.at < 30_000);
    if (all.length >= 8 || all.some((m) => now - m.at < 1000)) fail('chat_too_fast');
    let list: Msg[];
    if (c.kind === 'world') list = w.chat.world;
    else if (c.kind === 'guild') { const g = guildOf(w, me) ?? fail('not_in_guild'); list = (w.chat.guild[g.id] ??= []); }
    else { if (!areFriends(w, me, c.uid)) fail('dm_friends_only'); if (isBlocked(w, me, c.uid)) fail('user_blocked'); list = (w.chat.dm[dmKey(me, c.uid)] ??= []); }
    if (list.some((m) => m.from === me && m.text === t && now - m.at < 30_000)) fail('duplicate_message');
    const flags = assess(t);
    const m: Msg = { id: next(w), from: me, text: t, at: now, status: flags.length ? 'pending' : 'visible' };
    list.push(m);
    if (list.length > 200) list.splice(0, list.length - 200);
    if (flags.length) hold(w, 'chat', m.id, me, flags);
    return m;
  });
}
export function markRead(me: number, key: string, lastId: number) { mutate((w) => { (w.reads[me] ??= {}); if ((w.reads[me][key] ?? 0) < lastId) w.reads[me][key] = lastId; }); }
export function unread(w: World, me: number, c: Channel) { const k = chanKey(w, me, c); const last = w.reads[me]?.[k] ?? 0; return messages(w, me, c).filter((m) => m.id > last && m.from !== me).length; }

// ------------------------------------------------------------------ guilds (same role ladder as the server)

export const ROLES: Role[] = ['leader', 'vice', 'officer', 'elite', 'member'];
export const roleRank = (r: Role) => ({ leader: 5, vice: 4, officer: 3, elite: 2, member: 1 })[r];
export const GUILD_LEVELS = [0, 600, 1800, 3600, 6000, 9500, 14000, 20000, 28000, 38000];
export const maxMembers = (lvl: number) => 30 + 3 * (lvl - 1);
export const weekKey = (d = new Date()) => { const t = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate())); const day = (t.getUTCDay() + 6) % 7; t.setUTCDate(t.getUTCDate() - day + 3); const y = t.getUTCFullYear(); const w1 = new Date(Date.UTC(y, 0, 4)); return `${y}-W${String(1 + Math.round(((t.getTime() - w1.getTime()) / 86400000 - 3 + ((w1.getUTCDay() + 6) % 7)) / 7)).padStart(2, '0')}`; };
export const dayKey = () => new Date().toISOString().slice(0, 10);
export interface GuildMission { id: string; period: 'daily' | 'weekly'; category: string; kind: string; target: number; guildExp: number; contribution: number; reward: Record<string, number> }

let missionDefs: GuildMission[] = [];
export function setGuildMissions(m: GuildMission[]) { missionDefs = m; }
export const guildMissions = () => missionDefs;
const periodKey = (m: GuildMission) => `${m.id}:${m.period === 'daily' ? dayKey() : weekKey()}`;
function log(g: WGuild, text: string) { g.logs.unshift({ at: Date.now(), text }); g.logs = g.logs.slice(0, 40); }
function addExp(g: WGuild, n: number) { g.exp += n; let l = 1; GUILD_LEVELS.forEach((t, i) => { if (g.exp >= t) l = i + 1; }); if (l > g.level) { g.level = l; log(g, `⬆ Lv ${l}`); } }
function guildProgress(_w: World, g: WGuild, uid: number, kind: string, n: number) {
  let c = 0;
  for (const m of missionDefs.filter((x) => x.kind === kind)) { const k = periodKey(m); g.progress[k] = Math.min(m.target * 10, (g.progress[k] ?? 0) + n); c = Math.max(c, m.contribution * n); }
  g.contrib[uid] = (g.contrib[uid] ?? 0) + c;
}
/** Hook for game actions done by the logged-in account (pack opened, PvE/PvP win, evolution...). */
export function guildEvent(uid: number, kind: string, n = 1) { mutate((w) => { const g = guildOf(w, uid); if (g) guildProgress(w, g, uid, kind, n); }); }
const myRole = (g: WGuild, uid: number) => g.members.find((m) => m.uid === uid)?.role;

export function createGuild(me: number, name: string, tag: string, emblem: string, banner: string, visibility: 'public' | 'private', joinPolicy: 'open' | 'approval', minLevel: number, desc: string) {
  return mutate((w) => {
    const n = clean(name, 24); if (n.length < 3) fail('guild_name_too_short'); if (assess(n).length) fail('text_inappropriate');
    const t = tag.trim().toUpperCase(); if (t && (t.length < 2 || t.length > 5 || !/^[A-Z0-9]+$/.test(t))) fail('invalid_guild_tag');
    if (guildOf(w, me)) fail('already_in_guild');
    if (w.guilds.some((g) => g.name.toUpperCase() === n.toUpperCase())) fail('guild_name_taken');
    if (t && w.guilds.some((g) => g.tag === t)) fail('guild_tag_taken');
    const g: WGuild = { id: next(w), name: n, tag: t, emblem, banner, visibility, joinPolicy, minLevel: Math.max(1, Math.min(100, minLevel)), desc: clean(desc, 300, false),
      level: 1, exp: 0, members: [{ uid: me, role: 'leader', joinedAt: Date.now() }], requests: [], invites: [], logs: [], announcements: [], progress: {}, claims: {}, contrib: {}, boss: null, war: null, warHistory: [] };
    log(g, `${acc(w, me)?.name} ✦`);
    w.guilds.push(g);
    return g;
  });
}
export function joinGuild(me: number, id: number) {
  return mutate((w) => {
    const g = w.guilds.find((x) => x.id === id) ?? fail('guild_not_found');
    if (guildOf(w, me)) fail('already_in_guild');
    const invited = g.invites.includes(me);
    if (!invited && g.visibility === 'private') fail('guild_invite_only');
    if (!invited && (acc(w, me)?.level ?? 1) < g.minLevel) fail('guild_level_too_low');
    if (!invited && g.joinPolicy === 'approval') { if (g.requests.includes(me)) fail('join_request_exists'); g.requests.push(me); g.members.filter((m) => roleRank(m.role) >= 3).forEach((m) => notify(w, m.uid, 'guild_join_request', me)); return 'requested'; }
    if (g.members.length >= maxMembers(g.level)) fail('guild_full');
    g.invites = g.invites.filter((u) => u !== me); g.members.push({ uid: me, role: 'member', joinedAt: Date.now() }); log(g, `+ ${acc(w, me)?.name}`);
    return 'joined';
  });
}
export function guildInvite(me: number, uid: number) {
  mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    if (roleRank(myRole(g, me)!) < 3) fail('guild_officer_required');
    const t = acc(w, uid) ?? fail('player_not_found');
    if (isBlocked(w, me, uid)) fail('user_blocked');
    if (guildOf(w, uid)) fail('already_in_guild');
    if (g.invites.includes(uid)) fail('invite_exists');
    g.invites.push(uid); notify(w, uid, 'guild_invite', me, { text: g.name }); log(g, `✉ ${t.name}`);
  });
}
export function decideRequest(me: number, uid: number, accept: boolean) {
  mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    if (roleRank(myRole(g, me)!) < 3) fail('guild_officer_required');
    if (!g.requests.includes(uid)) fail('request_not_found');
    g.requests = g.requests.filter((u) => u !== uid);
    if (accept) { if (guildOf(w, uid)) fail('already_in_guild'); if (g.members.length >= maxMembers(g.level)) fail('guild_full'); g.members.push({ uid, role: 'member', joinedAt: Date.now() }); log(g, `+ ${acc(w, uid)?.name}`); }
    notify(w, uid, accept ? 'guild_request_accepted' : 'guild_request_rejected', me, { text: g.name });
  });
}
export function setRole(me: number, uid: number, role: Role) {
  mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    const mine = myRole(g, me)!, target = g.members.find((m) => m.uid === uid) ?? fail('member_not_found');
    if (target.role === 'leader') fail('cannot_change_leader');
    if (role === 'leader') { if (mine !== 'leader') fail('guild_leader_required'); target.role = 'leader'; g.members.find((m) => m.uid === me)!.role = 'vice'; }
    else { if (roleRank(mine) < 3 || roleRank(target.role) >= roleRank(mine) || roleRank(role) >= roleRank(mine)) fail('guild_rank_too_low'); target.role = role; }
    log(g, `${acc(w, uid)?.name} → ${role}`); notify(w, uid, 'guild_role_changed', me, { text: role });
  });
}
export function kick(me: number, uid: number) {
  mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    const t = g.members.find((m) => m.uid === uid) ?? fail('member_not_found');
    if (roleRank(myRole(g, me)!) < 3 || roleRank(myRole(g, me)!) <= roleRank(t.role)) fail('guild_rank_too_low');
    g.members = g.members.filter((m) => m.uid !== uid); log(g, `− ${acc(w, uid)?.name}`); notify(w, uid, 'guild_kicked', me, { text: g.name });
  });
}
export function leaveGuild(me: number) {
  mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    const was = myRole(g, me);
    g.members = g.members.filter((m) => m.uid !== me);
    if (!g.members.length) { dissolveNow(w, g); return; }
    if (was === 'leader') { const nx = [...g.members].sort((a, b) => roleRank(b.role) - roleRank(a.role) || a.joinedAt - b.joinedAt)[0]; nx.role = 'leader'; log(g, `👑 ${acc(w, nx.uid)?.name}`); }
    else log(g, `← ${acc(w, me)?.name}`);
  });
}
function dissolveNow(w: World, g: WGuild) { w.posts.filter((p) => p.guildId === g.id).forEach((p) => { p.status = 'deleted'; }); delete w.chat.guild[g.id]; w.guilds = w.guilds.filter((x) => x.id !== g.id); }
export function dissolve(me: number, confirm: string) {
  mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    if (myRole(g, me) !== 'leader') fail('guild_leader_required');
    if (confirm.trim().toLowerCase() !== g.name.toLowerCase()) fail('confirm_name_mismatch');
    if (g.members.length > 1) fail('guild_has_members');
    dissolveNow(w, g);
  });
}
export function announce(me: number, text: string) {
  mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    if (roleRank(myRole(g, me)!) < 3) fail('guild_officer_required');
    g.announcements.unshift({ at: Date.now(), by: me, text: clean(text, 300) }); g.announcements = g.announcements.slice(0, 20);
    g.members.forEach((m) => notify(w, m.uid, 'guild_announcement', me, { text: g.name }));
  });
}
export function guildMissionView(g: WGuild, me: number) {
  return missionDefs.map((m) => { const k = periodKey(m); const p = g.progress[k] ?? 0; return { ...m, progress: Math.min(p, m.target), completed: p >= m.target, claimed: (g.claims[k] ?? []).includes(me) }; });
}
/** Returns beans granted (the caller credits its own wallet). */
export function claimGuildMission(me: number, id: string): number {
  return mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    const m = missionDefs.find((x) => x.id === id) ?? fail('mission_not_found');
    const k = periodKey(m);
    if ((g.progress[k] ?? 0) < m.target) fail('mission_not_completed');
    const c = (g.claims[k] ??= []);
    if (c.includes(me)) fail('already_claimed');
    c.push(me);
    if (c.length === 1) addExp(g, m.guildExp);
    return m.reward.MINI_GREEN_BEAN ?? 0;
  });
}
export const BOSSES = [{ id: 'gboss_ember_titan', formId: 'pet_calderon', hpBase: 400000, hpPerLevel: 80000 }, { id: 'gboss_tide_colossus', formId: 'pet_tidecrown', hpBase: 420000, hpPerLevel: 85000 }];
export function currentBoss(g: WGuild) {
  const key = weekKey();
  if (g.boss?.key === key) return g.boss;
  const def = BOSSES[[...key].reduce((a, c) => a + c.charCodeAt(0), 0) % BOSSES.length];
  const hp = def.hpBase + def.hpPerLevel * (g.level - 1);
  return { key, bossId: def.id, formId: def.formId, hp, maxHp: hp, hits: [] as { uid: number; dmg: number; day: string }[] };
}
/** Applies a boss hit computed by the caller from the shared 3v3 engine (event-log damage). Max 3 attacks a day. */
export function bossHit(me: number, dmg: number): { hp: number; defeated: boolean } {
  return mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    const b = (g.boss = currentBoss(g));
    if (b.hp <= 0) fail('boss_defeated');
    if (b.hits.filter((h) => h.uid === me && h.day === dayKey()).length >= 3) fail('boss_attacks_used');
    const applied = Math.min(b.hp, Math.max(0, Math.round(dmg)));
    b.hp -= applied; b.hits.push({ uid: me, dmg: applied, day: dayKey() });
    g.contrib[me] = (g.contrib[me] ?? 0) + 5 + Math.floor(applied / 2000);
    guildProgress(w, g, me, 'boss_attack', 1);
    const a = acc(w, me); if (a) a.stats.bossDamage += applied;
    if (b.hp === 0) { addExp(g, 1500); log(g, `🏆 ${b.bossId}`); }
    return { hp: b.hp, defeated: b.hp === 0 };
  });
}
export function warRegister(me: number) {
  mutate((w) => {
    const g = guildOf(w, me) ?? fail('not_in_guild');
    if (roleRank(myRole(g, me)!) < 3) fail('guild_officer_required');
    const key = weekKey();
    if (g.war?.key === key) fail('already_registered');
    if (g.war && g.war.key !== key) g.warHistory.unshift({ key: g.war.key, points: g.war.points, result: '—' });
    g.war = { key, opponent: null, points: 0, battles: 0, wins: 0 };
    const open = w.guilds.filter((x) => x.id !== g.id && x.war?.key === key && x.war.opponent == null).sort((a, b) => Math.abs(a.level - g.level) - Math.abs(b.level - g.level));
    if (open[0]) { g.war.opponent = open[0].id; open[0].war!.opponent = g.id; log(g, `⚔ ${open[0].name}`); log(open[0], `⚔ ${g.name}`); }
    log(g, '⚔ war');
  });
}
export function warResult(me: number, won: boolean | null) {
  mutate((w) => {
    const g = guildOf(w, me); if (!g?.war) return;
    g.war.battles++; if (won) { g.war.wins++; g.war.points += 3; } else if (won === null) g.war.points += 1;
    g.contrib[me] = (g.contrib[me] ?? 0) + (won ? 30 : won === null ? 10 : 0);
  });
}

// ------------------------------------------------------------------ market (escrow + inbox; beans only)

export const inboxOf = (w: World, uid: number): Inbox => (w.inbox[uid] ??= { cards: [], beans: 0, log: [] });
/** Takes and clears everything waiting for this account (sale proceeds, bought/traded cards, returned escrow). */
export function drainInbox(uid: number): Inbox {
  return mutate((w) => { const i = w.inbox[uid] ?? { cards: [], beans: 0, log: [] }; w.inbox[uid] = { cards: [], beans: 0, log: [] }; return i; });
}
const reId = (w: World, c: SimCard): SimCard => ({ ...c, id: 1_000_000 + next(w), listed: false });   // ids are per account: re-key on transfer
export const FEE_BPS = 300;
export function listCard(me: number, card: SimCard, price: number) {
  return mutate((w) => {
    if (!Number.isInteger(price) || price < 1 || price > 10_000_000) fail('price_out_of_range');
    if (w.listings.filter((l) => l.seller === me && l.status === 'active').length >= 20) fail('too_many_listings');
    const l: Listing = { id: next(w), seller: me, card: { ...card, listed: true }, price, status: 'active', buyer: null, at: Date.now(), closedAt: null };
    w.listings.push(l); return l;
  });
}
export function cancelListing(me: number, id: number) {
  mutate((w) => {
    const l = w.listings.find((x) => x.id === id && x.seller === me && x.status === 'active') ?? fail('listing_not_found');
    l.status = 'cancelled'; l.closedAt = Date.now(); inboxOf(w, me).cards.push({ ...l.card, listed: false });
  });
}
/** Buyer side: the caller has checked and debits its own beans only if this succeeds. Exactly one buyer wins. */
export function buyListing(me: number, id: number, balance: number): Listing {
  return mutate((w) => {
    const l = w.listings.find((x) => x.id === id) ?? fail('listing_not_found');
    if (l.status !== 'active') fail('listing_closed');
    if (l.seller === me) fail('cannot_buy_own_listing');
    if (isBlocked(w, me, l.seller)) fail('user_blocked');
    if (balance < l.price) fail('insufficient_mini_green_bean');
    l.status = 'sold'; l.buyer = me; l.closedAt = Date.now();
    const fee = Math.floor(l.price * FEE_BPS / 10000);
    const ib = inboxOf(w, l.seller); ib.beans += l.price - fee; ib.log.push(`sold:${l.card.formId}:${l.price - fee}`);
    notify(w, l.seller, 'market_sold', me, { text: `${l.price}` });
    return { ...l, card: reId(w, l.card) };
  });
}
export function offerTrade(me: number, to: number, offer: SimCard[], request: CardSnapId[], note: string) {
  return mutate((w) => {
    if (offer.length < 1 || offer.length > 5 || request.length < 1 || request.length > 5) fail('trade_cards_1_to_5');
    if (to === me) fail('cannot_trade_self');
    if (!acc(w, to)) fail('player_not_found');
    if (isBlocked(w, me, to)) fail('user_blocked');
    if (w.trades.filter((t) => t.from === me && t.status === 'pending').length >= 10) fail('too_many_open_trades');
    const t: Trade = { id: next(w), from: me, to, offer: offer.map((c) => ({ ...c, listed: true })), request, note: clean(note, 120, false), status: 'pending', at: Date.now(), closedAt: null };
    w.trades.push(t); notify(w, to, 'trade_offer', me); return t;
  });
}
/** Receiver side: `mine` = the receiver's current cards. Requested cards must still exist unchanged; offered cards are in escrow. */
export function acceptTrade(me: number, id: number, mine: SimCard[]): { received: SimCard[]; given: number[] } {
  // the failure path must be saved too (offer marked failed, escrow returned), so the error is raised after mutate()
  const r = mutate((w) => {
    const t = w.trades.find((x) => x.id === id && x.to === me) ?? fail('trade_not_found');
    if (t.status !== 'pending') fail('trade_closed');
    const given = t.request.map((q) => mine.find((c) => c.id === q.id && !c.listed && c.formId === q.formId && c.level === q.level && c.stars === q.stars));
    if (given.some((g) => !g)) { t.status = 'failed'; t.closedAt = Date.now(); inboxOf(w, t.from).cards.push(...t.offer.map((c) => ({ ...c, listed: false }))); return null; }
    t.status = 'accepted'; t.closedAt = Date.now();
    inboxOf(w, t.from).cards.push(...given.map((c) => reId(w, c!)));
    notify(w, t.from, 'trade_accepted', me);
    return { received: t.offer.map((c) => reId(w, c)), given: given.map((c) => c!.id) };
  });
  return r ?? fail('trade_cards_changed');
}
export function closeTrade(me: number, id: number, how: 'declined' | 'cancelled') {
  mutate((w) => {
    const t = w.trades.find((x) => x.id === id && (how === 'declined' ? x.to === me : x.from === me)) ?? fail('trade_not_found');
    if (t.status !== 'pending') fail('trade_closed');
    t.status = how; t.closedAt = Date.now(); inboxOf(w, t.from).cards.push(...t.offer.map((c) => ({ ...c, listed: false })));
  });
}

// ------------------------------------------------------------------ leaderboards (from published account stats)

export type Board = 'pvp_ranked' | 'pvp_wins' | 'pvp_winrate' | 'collection' | 'rare_collector' | 'evolution' | 'pve_progress' | 'boss_damage' | 'trainer_level' | 'achievements'
  | 'minigames' | 'guild_contribution' | 'showcase_liked' | 'creators' | 'guild_ranking' | 'guild_boss' | 'guild_war';
export const BOARDS: Board[] = ['pvp_ranked', 'pvp_wins', 'pvp_winrate', 'collection', 'rare_collector', 'evolution', 'pve_progress', 'boss_damage', 'trainer_level', 'achievements',
  'minigames', 'guild_ranking', 'guild_contribution', 'guild_boss', 'guild_war', 'showcase_liked', 'creators'];
export const GUILD_BOARDS: Board[] = ['guild_ranking', 'guild_boss', 'guild_war'];
export interface Row { id: number; name: string; score: number; extra?: string; acc?: WAccount; guild?: WGuild }
export function leaderboard(w: World, board: Board, period: 'week' | 'month' | 'season' | 'all'): Row[] {
  const since = period === 'week' ? Date.now() - 7 * 86400_000 : period === 'month' ? Date.now() - 30 * 86400_000 : period === 'season' ? Date.now() - 90 * 86400_000 : 0;
  const P = (a: WAccount, score: number, extra?: string): Row => ({ id: a.uid, name: a.name, score, extra, acc: a });
  const G = (g: WGuild, score: number, extra?: string): Row => ({ id: g.id, name: (g.tag ? `[${g.tag}] ` : '') + g.name, score, extra, guild: g });
  let rows: Row[];
  switch (board) {
    case 'pvp_ranked': rows = w.accounts.filter((a) => a.stats.pvpGames > 0).map((a) => P(a, 1000 + 16 * (2 * a.stats.pvpWins - a.stats.pvpGames))); break;
    case 'pvp_wins': rows = w.accounts.map((a) => P(a, a.stats.pvpWins, `${a.stats.pvpGames}`)); break;
    case 'pvp_winrate': rows = w.accounts.filter((a) => a.stats.pvpGames >= 10).map((a) => P(a, Math.round(10000 * a.stats.pvpWins / a.stats.pvpGames), `${a.stats.pvpWins}/${a.stats.pvpGames}`)); break;
    case 'collection': rows = w.accounts.map((a) => P(a, a.stats.collection)); break;
    case 'rare_collector': rows = w.accounts.map((a) => P(a, a.stats.rare)); break;
    case 'evolution': rows = w.accounts.map((a) => P(a, a.stats.evolutions)); break;
    case 'pve_progress': rows = w.accounts.map((a) => P(a, a.stats.pve)); break;
    case 'boss_damage': rows = w.accounts.map((a) => P(a, w.guilds.flatMap((g) => g.boss?.hits ?? []).filter((h) => h.uid === a.uid && Date.parse(h.day) >= since - 86400_000).reduce((x, h) => x + h.dmg, 0))); break;
    case 'trainer_level': rows = w.accounts.map((a) => P(a, a.level, `Lv.${a.level}`)); break;
    case 'achievements': rows = w.accounts.map((a) => P(a, a.stats.achievements * 10)); break;
    case 'minigames': rows = w.accounts.map((a) => P(a, a.stats.minigames)); break;
    case 'guild_contribution': rows = w.accounts.map((a) => P(a, guildOf(w, a.uid)?.contrib[a.uid] ?? 0)); break;
    case 'showcase_liked': case 'creators': {
      // valid likes: distinct accounts, never the author (the server additionally ignores accounts younger than 24 h;
      // every Preview account is brand new, so that rule is not applied here)
      rows = w.accounts.map((a) => {
        const posts = w.posts.filter((p) => p.author === a.uid && p.status === 'visible' && p.visibility === 'public' && p.createdAt >= since && (board === 'creators' || p.showcaseKind));
        const valid = (p: WPost) => p.likes.filter((u) => u !== a.uid);
        return board === 'showcase_liked' ? P(a, Math.max(0, ...posts.map((p) => valid(p).length))) : P(a, new Set(posts.flatMap(valid)).size, `${posts.length}`);
      });
      break;
    }
    case 'guild_ranking': rows = w.guilds.map((g) => G(g, g.exp, `Lv.${g.level}`)); break;
    case 'guild_boss': rows = w.guilds.map((g) => G(g, (g.boss?.hits ?? []).filter((h) => Date.parse(h.day) >= since - 86400_000).reduce((x, h) => x + h.dmg, 0))); break;
    default: rows = w.guilds.map((g) => G(g, (g.war?.points ?? 0) + (period === 'all' ? g.warHistory.reduce((x, h) => x + h.points, 0) : 0), `${g.war?.wins ?? 0}W`));
  }
  return rows.filter((r) => r.score > 0).sort((a, b) => b.score - a.score || a.id - b.id);   // tie-break: earliest id
}

// ------------------------------------------------------------------ notifications

export const myNotifs = (w: World, me: number) => w.notifs.filter((n) => n.to === me).sort((a, b) => b.id - a.id);
export function readNotifs(me: number) { mutate((w) => { w.notifs.forEach((n) => { if (n.to === me) n.read = true; }); }); }
