import { beforeEach, describe, expect, it } from 'vitest';
import type { SimCard } from './sim';
import {
  acceptTrade, addComment, block, buyListing, canView, closeTrade, createGuild, createPost, drainInbox, feed, friendRequest, friendRespond, joinGuild, listCard,
  loadWorld, loginOrRegister, offerTrade, resetWorld, setRole, share, snap, spotlight, toggleCommentLike, toggleLike, WorldError, leaderboard, guildInvite, report,
} from './world';

const card = (id: number, formId = 'pet_emberkit', rarity = 'SSR'): SimCard => ({ id, serial: 'S' + id, formId, rarity, quality: 'A', finish: 'Normal', level: 10, stars: 2, enhance: 0, breakthrough: 0, awakened: false, skillLevel: 1, divineSet: null, origin: 'test' });
const code = (f: () => unknown) => { try { f(); return 'ok'; } catch (e) { return e instanceof WorldError ? e.code : String(e); } };

describe('Preview world (mirrors the server rules)', () => {
  let a: number, b: number, c: number;
  beforeEach(() => { localStorage.clear(); resetWorld(); a = loginOrRegister('An').uid; b = loginOrRegister('Bình').uid; c = loginOrRegister('Chi').uid; });

  it('logs into the same account by name and keeps uids distinct', () => {
    expect(loginOrRegister('an').uid).toBe(a);
    expect(new Set([a, b, c]).size).toBe(3);
  });

  it('enforces post visibility (public / friends / private / guild)', () => {
    const pub = createPost(a, { type: 'text', caption: 'xin chào', visibility: 'public' });
    const fr = createPost(a, { type: 'text', caption: 'bạn bè thôi', visibility: 'friends' });
    const pr = createPost(a, { type: 'text', caption: 'riêng tư', visibility: 'private' });
    friendRequest(a, b); friendRespond(b, a, true);
    const w = loadWorld();
    expect(canView(w, pub, c)).toBe(true);
    expect(canView(w, fr, c)).toBe(false);
    expect(canView(w, fr, b)).toBe(true);
    expect(canView(w, pr, b)).toBe(false);
    expect(feed(w, c, 'latest').map((p) => p.id)).toEqual([pub.id]);
    expect(code(() => createPost(a, { type: 'text', caption: 'g', visibility: 'guild' }))).toBe('not_in_guild');
  });

  it('keeps one like per account and counts only valid likes for the spotlight', () => {
    const p = createPost(a, { type: 'showcase', showcaseKind: 'rare', caption: 'SSR!', visibility: 'public', cards: [snap(card(1))] });
    toggleLike(a, p.id);   // self-like
    expect(spotlight(loadWorld(), c)).toHaveLength(0);
    toggleLike(b, p.id); toggleLike(b, p.id); toggleLike(b, p.id);
    expect(loadWorld().posts.find((x) => x.id === p.id)!.likes.filter((u) => u === b)).toHaveLength(1);
    expect(spotlight(loadWorld(), c)[0].score).toBe(3);
    expect(loadWorld().notifs.filter((n) => n.to === a && n.type === 'post_liked')).toHaveLength(1);   // no repeated notifications
  });

  it('supports one reply level and comment likes', () => {
    const p = createPost(a, { type: 'text', caption: 'hỏi', visibility: 'public' });
    const top = addComment(b, p.id, 'trả lời', null);
    const rep = addComment(c, p.id, 'đồng ý', top.id);
    expect(code(() => addComment(a, p.id, 'sâu hơn', rep.id))).toBe('reply_depth_limit');
    toggleCommentLike(a, top.id);
    expect(loadWorld().comments.find((x) => x.id === top.id)!.likes).toEqual([a]);
    expect(loadWorld().notifs.some((n) => n.to === b && n.type === 'comment_replied')).toBe(true);
  });

  it('holds risky text for review and rejects links', () => {
    const p = createPost(a, { type: 'text', caption: 'bán acc, add zalo 0912 345 678', visibility: 'public' });
    expect(p.status).toBe('pending');
    expect(canView(loadWorld(), p, b)).toBe(false);
    expect(loadWorld().modQueue).toHaveLength(1);
    expect(code(() => createPost(a, { type: 'text', caption: 'xem www.example.com', visibility: 'public' }))).toBe('text_links_not_allowed');
    const q = createPost(b, { type: 'text', caption: 'bình thường', visibility: 'public' });
    report(a, 'post', q.id, 'spam'); report(c, 'post', q.id, 'spam');
    expect(loadWorld().posts.find((x) => x.id === q.id)!.status).toBe('visible');
    const d = loginOrRegister('Dũng').uid; report(d, 'post', q.id, 'spam');
    expect(loadWorld().posts.find((x) => x.id === q.id)!.status).toBe('hidden');
  });

  it('shares keep the source; friends-only posts cannot be shared', () => {
    const pub = createPost(a, { type: 'text', caption: 'chia sẻ đi', visibility: 'public' });
    const fr = createPost(a, { type: 'text', caption: 'kín', visibility: 'friends' });
    friendRequest(a, b); friendRespond(b, a, true);
    expect(code(() => share(b, fr.id, 'feed'))).toBe('post_not_shareable');
    share(b, pub.id, 'feed');
    const s = loadWorld().posts.find((x) => x.author === b && x.type === 'share')!;
    expect(s.sharedFrom).toBe(pub.id);
    expect(loadWorld().posts.find((x) => x.id === pub.id)!.shares).toBe(1);
  });

  it('applies the guild role ladder and keeps guild posts private', () => {
    createGuild(a, 'Sao Băng', 'SAO', 'emblem_star', 'banner_sky', 'private', 'open', 1, '');
    const g = loadWorld().guilds[0];
    expect(code(() => joinGuild(b, g.id))).toBe('guild_invite_only');
    guildInvite(a, b); joinGuild(b, g.id); guildInvite(a, c); joinGuild(c, g.id);
    setRole(a, b, 'vice');
    expect(code(() => setRole(c, b, 'member'))).toBe('guild_rank_too_low');
    expect(code(() => setRole(b, a, 'member'))).toBe('cannot_change_leader');
    const gp = createPost(c, { type: 'text', caption: 'nội bộ', visibility: 'guild' });
    const outsider = loginOrRegister('Ngoài').uid;
    expect(canView(loadWorld(), gp, outsider)).toBe(false);
    expect(feed(loadWorld(), outsider, 'latest')).toHaveLength(0);
  });

  it('market sells for beans only to one buyer; trades swap or fail safely', () => {
    const l = listCard(a, card(5), 300);
    const bought = buyListing(b, l.id, 1000);
    expect(bought.card.id).not.toBe(5);   // re-keyed for the new owner
    expect(code(() => buyListing(c, l.id, 1000))).toBe('listing_closed');
    expect(drainInbox(a).beans).toBe(291);   // 3 % fee
    expect(code(() => buyListing(c, listCard(a, card(6), 900).id, 100))).toBe('insufficient_mini_green_bean');

    const t = offerTrade(a, b, [card(7)], [{ ...snap(card(8, 'pet_puddlotl', 'R')), id: 8 }], '');
    expect(code(() => acceptTrade(b, t.id, [card(9)]))).toBe('trade_cards_changed');   // requested card no longer owned
    expect(drainInbox(a).cards.map((x) => x.formId)).toEqual(['pet_emberkit']);   // escrow returned
    const t2 = offerTrade(a, b, [card(10)], [{ ...snap(card(11, 'pet_puddlotl', 'R')), id: 11 }], '');
    const r = acceptTrade(b, t2.id, [card(11, 'pet_puddlotl', 'R')]);
    expect(r.given).toEqual([11]);
    expect(drainInbox(a).cards.map((x) => x.formId)).toEqual(['pet_puddlotl']);
    expect(code(() => closeTrade(a, t2.id, 'cancelled'))).toBe('trade_closed');
  });

  it('blocks hide content both ways', () => {
    const p = createPost(a, { type: 'text', caption: 'hi', visibility: 'public' });
    block(b, a);
    expect(canView(loadWorld(), p, b)).toBe(false);
    expect(code(() => friendRequest(a, b))).toBe('user_blocked');
  });

  it('leaderboards rank real accounts with a deterministic tie-break', () => {
    const w = loadWorld();
    w.accounts.find((x) => x.uid === b)!.stats.collection = 5;
    w.accounts.find((x) => x.uid === c)!.stats.collection = 5;
    localStorage.setItem('petstar.preview.world.v1', JSON.stringify(w));
    const rows = leaderboard(loadWorld(), 'collection', 'all');
    expect(rows.map((r) => r.id)).toEqual([Math.min(b, c), Math.max(b, c)]);
  });
});
