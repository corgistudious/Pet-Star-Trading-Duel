// Advanced profile (Preview): overview, trainer, companion, collection, posts, showcase, achievements, ranking, guild,
// fashion. Other players' profiles respect their privacy setting and blocks.
import { useState } from 'react';
import { petArt, useCatalog } from '../state/catalog';
import { Avatar, Bubble, Effect, findItem, skinArt } from './Cosmetics';
import { GuildBadge, roleName } from './Guild';
import { errText, type Hub } from './hub';
import { boardName } from './Leaderboard';
import { PetStrip, PostCard } from './SocialHub';
import { acc, areFriends, block, BOARDS, friendRequest, guildOf, isBlocked, leaderboard, online, patchAccount, report, unblock, type Vis } from './world';

type PTab = 'overview' | 'trainer' | 'companion' | 'collection' | 'posts' | 'showcase' | 'achievements' | 'ranking' | 'guild' | 'fashion';

export function Profile({ h, uid, openFashion, openChat }: { h: Hub; uid: number; openFashion: () => void; openChat: (uid: number) => void }) {
  const { P, w, me, d, lang, say } = h;
  const cat = useCatalog();
  const a = acc(w, uid);
  const [tab, setTab] = useState<PTab>('overview');
  if (!a) return <small>{P('Không tìm thấy người chơi', 'Player not found')}</small>;
  const self = uid === me;
  const friends = areFriends(w, me, uid);
  const restricted = !self && (isBlocked(w, me, uid) || a.profileVis === 'private' || (a.profileVis === 'friends' && !friends));
  const g = guildOf(w, uid);
  const posts = w.posts.filter((p) => p.author === uid && (self ? p.status !== 'deleted' : p.status === 'visible' && (p.visibility === 'public' || (p.visibility === 'friends' && friends) || (p.visibility === 'guild' && guildOf(w, me)?.id === p.guildId))))
    .sort((x, y) => Number(y.pinned) - Number(x.pinned) || y.id - x.id);
  const likes = w.posts.filter((p) => p.author === uid && p.status === 'visible').reduce((x, p) => x + p.likes.filter((u) => u !== uid).length, 0);
  const tabs: [PTab, string][] = [['overview', P('Tổng Quan', 'Overview')], ['trainer', 'Trainer'], ['companion', P('Pet Đồng Hành', 'Companion')], ['collection', P('Bộ Sưu Tập', 'Collection')],
    ['posts', P('Bài Đăng', 'Posts')], ['showcase', 'Showcase'], ['achievements', P('Thành Tích', 'Achievements')], ['ranking', P('Xếp Hạng', 'Ranking')], ['guild', 'Guild'], ['fashion', P('Thời Trang', 'Fashion')]];
  const act = (f: () => void, ok: string) => { try { f(); say(ok); } catch (e) { say(errText(e, P)); } };
  const badges = [a.stats.pve >= 1 && ['🥉', P('Chiến thắng đầu tiên', 'First victory')], a.stats.pve >= 5 && ['🥈', P('5 màn PvE', '5 PvE stages')], a.stats.collection >= 20 && ['📚', P('20 dạng Pet', '20 Pet forms')],
    a.stats.rare >= 3 && ['💎', P('3 Pet hiếm', '3 rare Pets')], a.stats.bossDamage > 0 && ['🐉', P('Đánh Boss Guild', 'Guild Boss fighter')], likes >= 5 && ['❤', P('5 lượt thích', '5 likes')]].filter(Boolean) as [string, string][];
  return (
    <div className="pf">
      <div className="pf-head">
        <span className="pf-trainer"><img src={skinArt(d, a.trainer ?? 'trainer_boy', a.outfit, 'idle')} alt="" /><Effect d={d} id={a.effect} /></span>
        <Avatar a={a} d={d} size={84} />
        <div className="pf-id">
          <b className="ellipsis" title={a.name}>{a.name}</b>
          <small>UID {a.uid} · Lv {a.level} · {online(a) ? '🟢 online' : '⚪ offline'}</small>
          {findItem(d, a.title) && <span className="pf-title">🏷 {findItem(d, a.title)!.name[lang]}</span>}
          {g && <small>🛡 {g.tag && `[${g.tag}] `}{g.name}</small>}
          <small>❤ {likes} {P('lượt thích nhận được', 'likes received')}</small>
        </div>
        {!self && <div className="pf-actions">
          {!friends && <button className="btn small primary" onClick={() => act(() => friendRequest(me, uid), P('Đã gửi lời mời', 'Request sent'))} data-testid="pf-add">{P('Kết bạn', 'Add friend')}</button>}
          {friends && <button className="btn small" onClick={() => openChat(uid)}>💬 {P('Nhắn tin', 'Message')}</button>}
          {isBlocked(w, me, uid) ? <button className="btn small ghost" onClick={() => act(() => unblock(me, uid), P('Đã bỏ chặn', 'Unblocked'))}>{P('Bỏ chặn', 'Unblock')}</button>
            : <button className="btn small ghost" onClick={() => act(() => block(me, uid), P('Đã chặn', 'Blocked'))} data-testid="pf-block">⛔ {P('Chặn', 'Block')}</button>}
          <button className="btn small ghost" onClick={() => act(() => report(me, 'user', uid, 'other'), P('Đã báo cáo', 'Reported'))}>🚩</button>
        </div>}
      </div>
      {restricted ? <div className="sh-empty">🔒 {P('Hồ sơ này ở chế độ riêng tư.', 'This profile is private.')}</div> : <>
        <nav className="sh-chips">{tabs.map(([k, l]) => <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)} data-testid={`pf-${k}`}>{l}</button>)}</nav>
        {tab === 'overview' && <div className="pf-grid">
          <div className="lp-card"><b>{P('Thống kê', 'Stats')}</b><small>{P('Bộ sưu tập', 'Collection')}: {a.stats.collection} {P('dạng', 'forms')} · {P('Hiếm', 'Rare')}: {a.stats.rare}</small><small>PvE: {a.stats.pve} · PvP: {a.stats.pvpWins}/{a.stats.pvpGames}</small><small>{P('Tiến hóa', 'Evolutions')}: {a.stats.evolutions} · Boss: {a.stats.bossDamage.toLocaleString()}</small><small>{P('Lực chiến đội', 'Team power')}: {a.stats.power.toLocaleString()}</small></div>
          <div className="lp-card"><b>{P('Huy hiệu', 'Badges')}</b><div className="pf-badges">{badges.length ? badges.map(([i, t]) => <span key={t} title={t}>{i}<small>{t}</small></span>) : <small className="muted">—</small>}</div></div>
          <div className="lp-card"><b>{P('Pet yêu thích', 'Favourite Pets')}</b><PetStrip cards={a.team} /></div>
          <div className="lp-card"><b>{P('Bong bóng chat', 'Chat bubble')}</b><Bubble d={d} id={a.bubble}>{P('Xin chào!', 'Hello!')}</Bubble></div>
        </div>}
        {tab === 'trainer' && <div className="pf-grid"><img className="pf-big" src={skinArt(d, a.trainer ?? 'trainer_boy', a.outfit, 'happy')} alt="" />
          <div className="lp-card"><b>{a.trainer === 'trainer_girl' ? P('Trainer Nữ', 'Female Trainer') : P('Trainer Nam', 'Male Trainer')}</b><small>{P('Trang phục', 'Outfit')}: {d?.outfits.find((o) => o.id === a.outfit)?.name[lang] ?? P('Mặc định', 'Default')}</small><small>{P('Trang phục không cộng chỉ số.', 'Outfits give no stats.')}</small></div></div>}
        {tab === 'companion' && (a.companion ? <div className="pf-grid"><img className="pf-big pet" src={petArt(a.companion, true)} alt="" /><div className="lp-card"><b>{cat.forms[a.companion]?.name}</b><small>{cat.forms[a.companion]?.element} · {cat.forms[a.companion]?.role}</small></div></div> : <small className="muted">—</small>)}
        {tab === 'collection' && <><small>{a.stats.collection} {P('dạng Pet', 'Pet forms')} · {a.cards.length} {P('thẻ hiển thị', 'cards shown')}</small><div className="pf-cards">{a.cards.slice(0, 60).map((c) => <img key={c.id} src={petArt(c.formId, true)} alt="" title={`${cat.forms[c.formId]?.name} · ${c.rarity}`} />)}</div></>}
        {tab === 'posts' && <div className="lp-col">{posts.length === 0 ? <small className="muted">{P('Chưa có bài đăng.', 'No posts yet.')}</small> : posts.map((p) => <PostCard key={p.id} h={h} p={p} />)}</div>}
        {tab === 'showcase' && <div className="sh-grid">{posts.filter((p) => p.showcaseKind).map((p) => <PostCard key={p.id} h={h} p={p} compact />)}{!posts.some((p) => p.showcaseKind) && <small className="muted">—</small>}</div>}
        {tab === 'achievements' && <div className="lp-col">{badges.map(([i, t]) => <div key={t} className="lp-card row"><span className="pf-ach">{i}</span><b>{t}</b></div>)}{!badges.length && <small className="muted">—</small>}</div>}
        {tab === 'ranking' && <div className="lp-col">{BOARDS.filter((b) => !b.startsWith('guild_') || b === 'guild_contribution').map((b) => { const rows = leaderboard(w, b, 'all'); const i = rows.findIndex((r) => r.id === uid);
          return <div key={b} className="lp-rank"><span className="grow">{boardName(b, P)}</span><b>{i >= 0 ? `#${i + 1}` : '—'}</b></div>; })}</div>}
        {tab === 'guild' && (g ? <div className="lp-card row"><GuildBadge g={g} size={56} /><span className="grow"><b>{g.tag && `[${g.tag}] `}{g.name}</b><small>Lv {g.level} · {roleName(g.members.find((m) => m.uid === uid)!.role, P)} · {P('Đóng góp', 'Contribution')} {g.contrib[uid] ?? 0}</small></span></div> : <small className="muted">{P('Chưa tham gia Guild.', 'Not in a guild.')}</small>)}
        {tab === 'fashion' && <div className="lp-col">
          <div className="pf-fashion"><Avatar a={a} d={d} size={70} /><Bubble d={d} id={a.bubble}>Aa</Bubble><small>{findItem(d, a.frame)?.name[lang]} · {findItem(d, a.bubble)?.name[lang]}{a.effect ? ` · ${findItem(d, a.effect)?.name[lang]}` : ''}</small></div>
          {self && <button className="btn small primary" onClick={openFashion}>{P('Mở tủ đồ / Tiệm', 'Open wardrobe / Shop')}</button>}
        </div>}
        {self && <div className="lp-card"><b>🔒 {P('Quyền riêng tư', 'Privacy')}</b>
          <label className="small">{P('Ai xem được hồ sơ', 'Who can see my profile')} <select className="lb-input small" value={a.profileVis} onChange={(e) => patchAccount(me, { profileVis: e.target.value as 'public' | 'friends' | 'private' })} data-testid="pf-privacy">
            <option value="public">{P('Mọi người', 'Everyone')}</option><option value="friends">{P('Bạn bè', 'Friends')}</option><option value="private">{P('Chỉ mình tôi', 'Only me')}</option></select></label>
          <label className="small">{P('Quyền xem mặc định của bài mới', 'Default audience for new posts')} <select className="lb-input small" value={a.defaultVis} onChange={(e) => patchAccount(me, { defaultVis: e.target.value as Vis })}>
            <option value="public">{P('Công khai', 'Public')}</option><option value="friends">{P('Bạn bè', 'Friends')}</option><option value="guild">Guild</option><option value="private">{P('Chỉ mình tôi', 'Only me')}</option></select></label></div>}
      </>}
    </div>
  );
}
