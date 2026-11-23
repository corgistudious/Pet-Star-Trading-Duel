// Social Hub (Preview): news feed, Pet Showcase, friends, guild, leaderboards and activity — on the shared Preview world.
import { useMemo, useState } from 'react';
import { petArt, useCatalog } from '../state/catalog';
import { Avatar, Bubble } from './Cosmetics';
import { GuildPanel } from './Guild';
import { ago, errText, type Hub } from './hub';
import { Leaderboards } from './Leaderboard';
import { stats, type SimCard } from './sim';
import {
  acc, addComment, areFriends, block, canView, commentCount, deleteComment, deletePost, editPost, feed, friendIds, friendRequest, friendRespond, guildOf,
  incoming, isBlocked, myNotifs, online, pinPost, rarityRank, readNotifs, report, share, snap, spotlight, toggleCommentLike, toggleLike, unblock, unfriend,
  createPost, RARE_RANK, type CardSnap, type FeedTab, type Vis, type WPost,
} from './world';

export type SocialTab = 'feed' | 'showcase' | 'friends' | 'guild' | 'leaders' | 'activity';

export function SocialHub({ h, tab, setTab, openChat }: { h: Hub; tab: SocialTab; setTab: (t: SocialTab) => void; openChat: (uid: number) => void }) {
  const { P, w, me } = h;
  const unreadN = myNotifs(w, me).filter((n) => !n.read).length;
  const tabs: [SocialTab, string][] = [['feed', P('Bảng Tin', 'Feed')], ['showcase', P('Khoe Pet', 'Showcase')], ['friends', P('Bạn Bè', 'Friends')], ['guild', 'Guild'], ['leaders', P('Xếp Hạng', 'Ranking')], ['activity', P('Hoạt Động', 'Activity')]];
  return (
    <div className="sh">
      <nav className="sh-tabs" role="tablist">{tabs.map(([k, label]) => (
        <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)} data-testid={`sh-tab-${k}`}>
          <span>{label}</span>{k === 'activity' && unreadN > 0 && <em className="lb-badge">{unreadN}</em>}{k === 'friends' && incoming(w, me).length > 0 && <em className="lb-badge">{incoming(w, me).length}</em>}
        </button>))}</nav>
      <div className="sh-body">
        {tab === 'feed' && <Feed h={h} />}
        {tab === 'showcase' && <Showcase h={h} />}
        {tab === 'friends' && <Friends h={h} openChat={openChat} />}
        {tab === 'guild' && <GuildPanel h={h} />}
        {tab === 'leaders' && <Leaderboards h={h} />}
        {tab === 'activity' && <Activity h={h} />}
      </div>
    </div>
  );
}

// ------------------------------------------------------------------ feed

const VIS_ICON: Record<Vis, string> = { public: '🌐', friends: '👥', guild: '🛡', private: '🔒' };
export const visLabel = (v: Vis, P: Hub['P']) => ({ public: P('Công khai', 'Public'), friends: P('Bạn bè', 'Friends'), guild: 'Guild', private: P('Chỉ mình tôi', 'Only me') })[v];

function Feed({ h }: { h: Hub }) {
  const { P, w, me } = h;
  const [tab, setTab] = useState<FeedTab>('explore');
  const list = feed(w, me, tab);
  const chips: [FeedTab, string][] = [['explore', P('Khám Phá', 'Explore')], ['latest', P('Mới Nhất', 'Latest')], ['friends', P('Bạn Bè', 'Friends')], ['guild', 'Guild'],
    ['showcase', P('Pet Nổi Bật', 'Showcase')], ['rare', P('Pet Hiếm', 'Rare')], ['popular', P('Được Yêu Thích', 'Popular')], ['mine', P('Của tôi', 'Mine')]];
  return (
    <div className="sh-feed">
      <Composer h={h} onHeld={() => setTab('mine')} />
      <div className="sh-chips">{chips.map(([k, l]) => <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)} data-testid={`feed-${k}`}>{l}</button>)}</div>
      {list.length === 0 && <div className="sh-empty">{tab === 'guild' && !guildOf(w, me) ? P('Bạn chưa tham gia Guild.', 'You are not in a guild yet.') : P('Chưa có bài đăng nào ở đây. Bảng tin chỉ hiển thị bài thật của các tài khoản Preview — hãy đăng bài đầu tiên!', 'Nothing here yet. The feed only shows real posts from Preview accounts — be the first to post!')}</div>}
      {list.slice(0, 40).map((p) => <PostCard key={p.id} h={h} p={p} />)}
    </div>
  );
}

function Composer({ h, onHeld }: { h: Hub; onHeld: () => void }) {
  const { P, s, me, w, say } = h;
  const cat = useCatalog();
  const [text, setText] = useState('');
  const [vis, setVis] = useState<Vis>(acc(w, me)?.defaultVis ?? 'public');
  const [pick, setPick] = useState<number[]>([]);
  const [open, setOpen] = useState(false);
  const [preview, setPreview] = useState(false);
  const cards = pick.map((id) => s.cards.find((c) => c.id === id)).filter(Boolean) as SimCard[];
  const post = () => {
    try {
      const p = createPost(me, { type: 'text', caption: text, visibility: vis, cards: cards.map(snap) }); setText(''); setPick([]); setPreview(false); setOpen(false);
      if (p.status === 'pending') { onHeld(); say(P('Bài đang chờ kiểm duyệt — chỉ bạn thấy cho đến khi được duyệt', 'Held for moderation — only you can see it until approved')); } else say(P('Đã đăng bài', 'Posted'));
    }
    catch (e) { say(errText(e, P)); }
  };
  return (
    <div className="sh-composer">
      <div className="sh-row"><Avatar a={acc(w, me)} d={h.d} size={44} />
        <textarea className="lb-input" rows={2} maxLength={500} value={text} onChange={(e) => setText(e.target.value)} placeholder={P('Chia sẻ điều gì đó về Pet của bạn…', 'Share something about your Pets…')} data-testid="post-text" /></div>
      <div className="sh-row wrap">
        <select className="lb-input small" value={vis} onChange={(e) => setVis(e.target.value as Vis)} data-testid="post-vis" aria-label={P('Quyền xem', 'Audience')}>
          {(['public', 'friends', 'guild', 'private'] as Vis[]).map((v) => <option key={v} value={v}>{VIS_ICON[v]} {visLabel(v, P)}</option>)}</select>
        <button className="btn small" onClick={() => setOpen(!open)} data-testid="post-attach">🐾 {P('Đính kèm Pet', 'Attach Pets')} ({pick.length}/5)</button>
        <button className="btn small ghost" disabled={!text.trim() && !pick.length} onClick={() => setPreview(!preview)}>{P('Xem trước', 'Preview')}</button>
        <small className="muted">{text.length}/500</small>
        <button className="btn small primary" disabled={!text.trim()} onClick={post} data-testid="post-go">{P('Đăng', 'Post')}</button>
      </div>
      {open && <div className="sh-pick">{[...s.cards].filter((c) => !c.listed).sort((a, b) => stats(cat, b).power - stats(cat, a).power).slice(0, 30).map((c) => (
        <button key={c.id} className={pick.includes(c.id) ? 'on' : ''} onClick={() => setPick((x) => (x.includes(c.id) ? x.filter((y) => y !== c.id) : x.length < 5 ? [...x, c.id] : x))} data-testid={`attach-${c.id}`}>
          <img src={petArt(c.formId, true)} alt="" /><small>{c.rarity}</small></button>))}</div>}
      {preview && <div className="sh-preview"><small className="muted">{P('Xem trước', 'Preview')} · {VIS_ICON[vis]} {visLabel(vis, P)}</small><p className="sh-text">{text}</p><PetStrip cards={cards.map(snap)} /></div>}
      <small className="muted">{P('Lưu ý: nội dung có số điện thoại, liên kết ngoài, mua bán tiền thật hoặc 18+ sẽ bị giữ lại để kiểm duyệt. Tải ảnh lên chưa mở trong bản này.', 'Note: posts with phone numbers, off-platform links, real-money trading or adult content are held for review. Image upload is not enabled in this build.')}</small>
    </div>
  );
}

export function PetStrip({ cards, big }: { cards: CardSnap[]; big?: boolean }) {
  const cat = useCatalog();
  if (!cards.length) return null;
  return (
    <div className={'sh-pets' + (big ? ' big' : '') + (cards.length === 1 ? ' one' : '')}>
      {cards.map((c, i) => { const f = cat.forms[c.formId];
        return <div key={i} className={'sh-pet r-' + c.rarity + (c.finish !== 'Normal' ? ' holo' : '')}>
          <img src={petArt(c.formId, true)} alt="" loading="lazy" />
          <b title={f?.name}>{f?.name ?? c.formId}</b>
          <small>{c.rarity} · {f?.element ?? ''} · {f ? `St.${f.stage}` : ''}</small>
          <small>Lv {c.level} · {'★'.repeat(Math.min(5, c.stars))}{c.stars > 5 ? `+${c.stars - 5}` : ''}{c.finish !== 'Normal' ? ` · ${c.finish}` : ''}</small>
        </div>; })}
    </div>
  );
}

export function PostCard({ h, p, compact }: { h: Hub; p: WPost; compact?: boolean }) {
  const { P, w, me, say } = h;
  const a = acc(w, p.author);
  const g = guildOf(w, p.author);
  const [showC, setShowC] = useState(false);
  const [more, setMore] = useState(false);
  const [menu, setMenu] = useState(false);
  const [editing, setEditing] = useState<string | null>(null);
  const [pop, setPop] = useState(false);
  const liked = p.likes.includes(me);
  const src = p.sharedFrom != null ? w.posts.find((x) => x.id === p.sharedFrom) : null;
  const srcOk = src && canView(w, src, me);
  const act = (f: () => void, ok?: string) => { try { f(); if (ok) say(ok); } catch (e) { say(errText(e, P)); } };
  const long = p.caption.length > 220;
  return (
    <article className={'sh-post' + (p.status !== 'visible' ? ' held' : '')} data-testid={`post-${p.id}`}>
      <header>
        <Avatar a={a} d={h.d} size={46} onClick={() => h.openProfile(p.author)} />
        <div className="sh-who">
          <b className="ellipsis">{a?.name ?? '?'}{g?.tag && <span className="sh-tag">[{g.tag}]</span>}</b>
          <small>UID {a?.uid} · {ago(p.createdAt, P)} · {VIS_ICON[p.visibility]}{p.editedAt ? ` · ${P('đã sửa', 'edited')}` : ''}{p.pinned ? ' · 📌' : ''}</small>
        </div>
        <button className="sh-menu" onClick={() => setMenu(!menu)} aria-label={P('Tùy chọn', 'Options')} data-testid={`post-menu-${p.id}`}>⋯</button>
        {menu && <div className="sh-pop" onMouseLeave={() => setMenu(false)}>
          {p.author === me ? <>
            <button onClick={() => { setEditing(p.caption); setMenu(false); }}>✏️ {P('Sửa', 'Edit')}</button>
            <button onClick={() => act(() => pinPost(me, p.id), p.pinned ? P('Đã bỏ ghim', 'Unpinned') : P('Đã ghim lên hồ sơ', 'Pinned to profile'))}>📌 {p.pinned ? P('Bỏ ghim', 'Unpin') : P('Ghim', 'Pin')}</button>
            <button onClick={() => act(() => deletePost(me, p.id), P('Đã xóa bài', 'Post deleted'))} data-testid={`post-del-${p.id}`}>🗑 {P('Xóa', 'Delete')}</button>
          </> : <>
            <button onClick={() => act(() => report(me, 'post', p.id, 'spam'), P('Đã gửi báo cáo tới kiểm duyệt', 'Report sent to moderation'))} data-testid={`post-report-${p.id}`}>🚩 {P('Báo cáo', 'Report')}</button>
            <button onClick={() => act(() => block(me, p.author), P('Đã chặn người chơi', 'Player blocked'))}>⛔ {P('Chặn', 'Block')}</button>
            <button onClick={() => h.openProfile(p.author)}>👤 {P('Xem hồ sơ', 'View profile')}</button>
          </>}
        </div>}
      </header>
      {p.status === 'pending' && <div className="sh-held">⏳ {P('Đang chờ kiểm duyệt — chỉ bạn thấy bài này.', 'Waiting for moderation — only you can see this.')} <small>({p.flags.join(', ')})</small></div>}
      {p.status === 'hidden' && <div className="sh-held">🙈 {P('Bài đã bị ẩn sau nhiều báo cáo, đang chờ xem xét.', 'Hidden after several reports, awaiting review.')}</div>}
      {p.showcaseKind && <div className="sh-kind">✨ {showcaseLabel(p.showcaseKind, P)}{p.data.detail ? ` · ${p.data.detail}` : ''}</div>}
      {editing != null ? <div className="sh-row"><input className="lb-input" value={editing} maxLength={500} onChange={(e) => setEditing(e.target.value)} />
        <button className="btn small primary" onClick={() => act(() => { editPost(me, p.id, editing); setEditing(null); }, P('Đã cập nhật', 'Updated'))}>{P('Lưu', 'Save')}</button></div>
        : p.caption && <p className="sh-text">{long && !more ? p.caption.slice(0, 220) + '…' : p.caption}{long && <button className="sh-more" onClick={() => setMore(!more)}>{more ? P('Thu gọn', 'Less') : P('Xem thêm', 'More')}</button>}</p>}
      <PetStrip cards={p.cards} big={!!p.showcaseKind && !compact} />
      {p.type === 'share' && <div className="sh-shared">{srcOk ? <><small>🔁 {P('Chia sẻ từ', 'Shared from')} <b>{acc(w, src!.author)?.name}</b></small>{src!.caption && <p className="sh-text">{src!.caption}</p>}<PetStrip cards={src!.cards} /></> : <small>{P('Bài gốc không còn hoặc bạn không có quyền xem.', 'The original post is gone or not visible to you.')}</small>}</div>}
      <footer>
        <button className={'sh-like' + (liked ? ' on' : '') + (pop ? ' pop' : '')} disabled={p.status !== 'visible'} onClick={() => act(() => { toggleLike(me, p.id); if (!liked) { setPop(true); setTimeout(() => setPop(false), 600); } })} data-testid={`like-${p.id}`} aria-pressed={liked}>
          <span className="heart">{liked ? '❤' : '♡'}</span><b>{p.likes.length}</b></button>
        <button onClick={() => setShowC(!showC)} disabled={p.status !== 'visible'} data-testid={`comments-${p.id}`}>💬 <b>{commentCount(w, p.id)}</b></button>
        <ShareButton h={h} p={p} />
      </footer>
      {showC && <Comments h={h} p={p} />}
    </article>
  );
}
export const showcaseLabel = (k: string, P: Hub['P']) => ({ rare: P('Pet hiếm', 'Rare Pet'), new_pack: P('Mới mở từ gói', 'Fresh from a pack'), evolved: P('Vừa tiến hóa', 'Just evolved'),
  companion: P('Pet đồng hành', 'Companion'), team: P('Đội hình 3 Pet', '3-Pet team'), element: P('Bộ sưu tập theo hệ', 'Element collection'), theme: P('Bộ sưu tập chủ đề', 'Themed collection') } as Record<string, string>)[k] ?? k;

function ShareButton({ h, p }: { h: Hub; p: WPost }) {
  const { P, w, me, say } = h;
  const [open, setOpen] = useState(false);
  const shareable = p.status === 'visible' && (p.visibility === 'public' || p.visibility === 'guild');
  const go = (f: () => void, ok: string) => { try { f(); say(ok); setOpen(false); } catch (e) { say(errText(e, P)); } };
  return (
    <span className="sh-share">
      <button disabled={!shareable} onClick={() => setOpen(!open)} title={shareable ? '' : P('Bài riêng tư/bạn bè không thể chia sẻ', 'Private/friends-only posts cannot be shared')} data-testid={`share-${p.id}`}>🔁 <b>{p.shares}</b></button>
      {open && <div className="sh-pop">
        <button onClick={() => go(() => share(me, p.id, 'feed'), P('Đã chia sẻ lên bảng tin của bạn', 'Shared to your feed'))} data-testid={`share-feed-${p.id}`}>📰 {P('Lên bảng tin của tôi', 'To my feed')}</button>
        {friendIds(w, me).map((f) => <button key={f} onClick={() => go(() => share(me, p.id, 'friend', '', f), P('Đã gửi cho bạn bè', 'Sent to friend'))}>💬 {acc(w, f)?.name}</button>)}
        <button onClick={() => { try { navigator.clipboard?.writeText(`petstar://post/${p.id}`); } catch { /* clipboard blocked */ } say(P('Đã chép liên kết nội bộ', 'Internal link copied')); setOpen(false); }}>🔗 {P('Chép liên kết nội bộ', 'Copy internal link')}</button>
      </div>}
    </span>
  );
}

function Comments({ h, p }: { h: Hub; p: WPost }) {
  const { P, w, me, say } = h;
  const [text, setText] = useState('');
  const [reply, setReply] = useState<number | null>(null);
  const list = w.comments.filter((c) => c.postId === p.id && (c.status === 'visible' || (c.status === 'pending' && c.author === me)) && !isBlocked(w, me, c.author));
  const top = list.filter((c) => c.parentId == null);
  const act = (f: () => void, ok?: string) => { try { f(); if (ok) say(ok); } catch (e) { say(errText(e, P)); } };
  const row = (c: typeof list[number], child = false) => (
    <div key={c.id} className={'sh-c' + (child ? ' child' : '')} data-testid={`comment-${c.id}`}>
      <Avatar a={acc(w, c.author)} d={h.d} size={30} onClick={() => h.openProfile(c.author)} />
      <div className="sh-c-main">
        <Bubble d={h.d} id={acc(w, c.author)?.bubble} mine={c.author === me}><b>{acc(w, c.author)?.name}</b> {c.text}</Bubble>
        <small>{ago(c.createdAt, P)}{c.status === 'pending' ? ` · ⏳ ${P('chờ duyệt', 'pending')}` : ''}
          <button onClick={() => act(() => toggleCommentLike(me, c.id))} className={c.likes.includes(me) ? 'on' : ''} data-testid={`clike-${c.id}`}>♥ {c.likes.length}</button>
          {!child && <button onClick={() => setReply(c.id)} data-testid={`reply-${c.id}`}>{P('Trả lời', 'Reply')}</button>}
          {c.author === me ? <button onClick={() => act(() => deleteComment(me, c.id), P('Đã xóa', 'Deleted'))}>{P('Xóa', 'Delete')}</button>
            : <><button onClick={() => act(() => report(me, 'comment', c.id, 'harassment'), P('Đã báo cáo', 'Reported'))}>{P('Báo cáo', 'Report')}</button><button onClick={() => act(() => block(me, c.author), P('Đã chặn', 'Blocked'))}>{P('Chặn', 'Block')}</button></>}
        </small>
      </div>
    </div>
  );
  return (
    <div className="sh-comments">
      {top.map((c) => <div key={c.id}>{row(c)}{list.filter((r) => r.parentId === c.id).map((r) => row(r, true))}</div>)}
      {reply != null && <small className="muted">↪ {P('Đang trả lời', 'Replying to')} {acc(w, list.find((c) => c.id === reply)?.author ?? 0)?.name} <button className="sh-more" onClick={() => setReply(null)}>✕</button></small>}
      <div className="sh-row"><input className="lb-input" maxLength={300} value={text} onChange={(e) => setText(e.target.value)} placeholder={P('Viết bình luận…', 'Write a comment…')}
        onKeyDown={(e) => { if (e.key === 'Enter' && text.trim()) act(() => { addComment(me, p.id, text, reply); setText(''); setReply(null); }); }} data-testid={`ctext-${p.id}`} />
        <button className="btn small primary" disabled={!text.trim()} onClick={() => act(() => { addComment(me, p.id, text, reply); setText(''); setReply(null); })} data-testid={`csend-${p.id}`}>{P('Gửi', 'Send')}</button></div>
    </div>
  );
}

// ------------------------------------------------------------------ Pet Showcase

function Showcase({ h }: { h: Hub }) {
  const { P, s, l, w, me, say } = h;
  const cat = useCatalog();
  const [kind, setKind] = useState('rare');
  const [cardId, setCardId] = useState<number | null>(null);
  const [element, setElement] = useState('fire');
  const [family, setFamily] = useState('');
  const [caption, setCaption] = useState('');
  const owned = s.cards.filter((c) => !c.listed);
  const rare = owned.filter((c) => rarityRank(c.rarity) >= RARE_RANK);
  const evolved = owned.filter((c) => l.evolved?.includes(c.id));
  const fresh = owned.filter((c) => l.lastPack?.includes(c.id));
  const fams = useMemo(() => [...new Set(owned.map((c) => cat.forms[c.formId]?.family).filter(Boolean))] as string[], [owned, cat]);
  // every card in a showcase is read from this account's own collection (never typed in by the user)
  const cards = (): { cards: SimCard[]; detail: string } => {
    switch (kind) {
      case 'rare': case 'evolved': { const c = owned.find((x) => x.id === cardId); return { cards: c ? [c] : [], detail: c ? c.rarity : '' }; }
      case 'new_pack': return { cards: fresh.slice(0, 5), detail: P('Gói mới nhất', 'Latest pack') };
      case 'companion': { const c = owned.find((x) => x.id === l.companionId); return { cards: c ? [c] : [], detail: '' }; }
      case 'team': return { cards: l.team.map((id) => owned.find((c) => c.id === id)).filter(Boolean) as SimCard[], detail: '' };
      case 'element': { const list = owned.filter((c) => cat.forms[c.formId]?.element === element); return { cards: [...list].sort((a, b) => stats(cat, b).power - stats(cat, a).power).slice(0, 5), detail: `${element} · ${new Set(list.map((c) => c.formId)).size} ${P('dạng', 'forms')}` }; }
      default: { const list = owned.filter((c) => cat.forms[c.formId]?.family === family); return { cards: list.slice(0, 5), detail: family }; }
    }
  };
  const pickList = kind === 'rare' ? rare : kind === 'evolved' ? evolved : [];
  const post = () => {
    const { cards: cs, detail } = cards();
    if (!cs.length) return say(P('Chưa có Pet phù hợp cho mục này', 'No matching Pet for this showcase'));
    try { createPost(me, { type: 'showcase', showcaseKind: kind, caption, visibility: 'public', cards: cs.map(snap), data: { detail } }); setCaption(''); say(P('Đã khoe Pet!', 'Showcase posted!')); }
    catch (e) { say(errText(e, P)); }
  };
  const spot = spotlight(w, me);
  return (
    <div className="sh-show">
      <section className="sh-spot">
        <b>🌟 {P('Pet nổi bật tuần', 'Showcase of the week')}</b>
        {spot.length === 0 ? <small className="muted">{P('Chưa có bài nổi bật tuần này. Điểm chỉ tính từ lượt thích/bình luận hợp lệ của người khác (không tính tự thích).', 'No featured showcase yet this week. Only valid likes/comments from other players count (self-likes never do).')}</small>
          : <div className="sh-spot-row">{spot.map(({ p, score }, i) => <div key={p.id} className="sh-spot-card"><em>#{i + 1}</em><PetStrip cards={p.cards.slice(0, 1)} big /><small>{acc(w, p.author)?.name} · ⭐ {score}</small></div>)}</div>}
      </section>
      <section className="sh-composer">
        <b>✨ {P('Khoe Pet mới', 'New showcase')}</b>
        <div className="sh-chips">{['rare', 'new_pack', 'evolved', 'companion', 'team', 'element', 'theme'].map((k) => <button key={k} className={kind === k ? 'on' : ''} onClick={() => { setKind(k); setCardId(null); }} data-testid={`sc-${k}`}>{showcaseLabel(k, P)}</button>)}</div>
        {(kind === 'rare' || kind === 'evolved') && (pickList.length ? <div className="sh-pick">{pickList.slice(0, 24).map((c) => <button key={c.id} className={cardId === c.id ? 'on' : ''} onClick={() => setCardId(c.id)} data-testid={`sc-card-${c.id}`}><img src={petArt(c.formId, true)} alt="" /><small>{c.rarity}</small></button>)}</div>
          : <small className="muted">{kind === 'rare' ? P('Bạn chưa có Pet từ SSR trở lên.', 'You have no SSR-or-better Pet yet.') : P('Chưa có Pet vừa tiến hóa (tiến hóa ở Album → Pet Hub).', 'No recently evolved Pet (evolve in Album → Pet Hub).')}</small>)}
        {kind === 'new_pack' && <small className="muted">{fresh.length ? `${fresh.length} ${P('thẻ từ gói vừa mở', 'cards from your latest pack')}` : P('Hãy mở một gói thẻ trước.', 'Open a booster pack first.')}</small>}
        {kind === 'element' && <select className="lb-input small" value={element} onChange={(e) => setElement(e.target.value)}>{['fire', 'water', 'nature', 'earth', 'lightning', 'wind', 'light', 'dark'].map((e) => <option key={e}>{e}</option>)}</select>}
        {kind === 'theme' && <select className="lb-input small" value={family} onChange={(e) => setFamily(e.target.value)}><option value="">—</option>{fams.map((f) => <option key={f}>{f}</option>)}</select>}
        <PetStrip cards={cards().cards.map(snap)} big />
        <div className="sh-row"><input className="lb-input" maxLength={500} value={caption} onChange={(e) => setCaption(e.target.value)} placeholder={P('Lời khoe…', 'Caption…')} />
          <button className="btn small primary" onClick={post} data-testid="sc-post">{P('Khoe ngay', 'Show off')}</button></div>
        <small className="muted">{P('Độ hiếm, hệ, tiến hóa, biến thể đều lấy từ thẻ bạn sở hữu — không thể tự khai báo.', 'Rarity, element, evolution and variant come from the cards you own — they cannot be typed in.')}</small>
      </section>
      <div className="sh-grid">{feed(w, me, 'showcase').slice(0, 24).map((p) => <PostCard key={p.id} h={h} p={p} compact />)}</div>
    </div>
  );
}

// ------------------------------------------------------------------ friends + activity

function Friends({ h, openChat }: { h: Hub; openChat: (uid: number) => void }) {
  const { P, w, me, say } = h;
  const [q, setQ] = useState('');
  const act = (f: () => void, ok: string) => { try { f(); say(ok); } catch (e) { say(errText(e, P)); } };
  const found = q.trim().length >= 2 ? w.accounts.filter((a) => a.uid !== me && (String(a.uid) === q.trim() || a.name.toLowerCase().includes(q.trim().toLowerCase()))).slice(0, 8) : [];
  const row = (uid: number, actions: React.ReactNode) => { const a = acc(w, uid); if (!a) return null;
    return <div key={uid} className="lp-friend"><Avatar a={a} d={h.d} size={40} onClick={() => h.openProfile(uid)} /><span className="grow"><b className="ellipsis">{a.name}</b><small>UID {a.uid} · Lv {a.level} · {online(a) ? '🟢 online' : '⚪ offline'}</small></span>{actions}</div>; };
  const blockedList = w.blocks.filter((b) => b.by === me);
  return (
    <div className="lp-col">
      <div className="sh-row"><input className="lb-input" value={q} onChange={(e) => setQ(e.target.value)} placeholder={P('Tìm theo tên hoặc UID', 'Search by name or UID')} data-testid="friend-search" /></div>
      {found.map((a) => row(a.uid, areFriends(w, me, a.uid) ? <small>✅</small> : <button className="btn small primary" onClick={() => act(() => friendRequest(me, a.uid), P('Đã gửi lời mời kết bạn', 'Friend request sent'))} data-testid={`add-${a.uid}`}>{P('Kết bạn', 'Add')}</button>))}
      {q.trim().length >= 2 && !found.length && <small className="muted">{P('Không thấy. Bản Preview chỉ có các tài khoản đã tạo trong trình duyệt này.', 'No match. The Preview only has accounts created in this browser.')}</small>}
      {incoming(w, me).length > 0 && <b>{P('Lời mời kết bạn', 'Friend requests')}</b>}
      {incoming(w, me).map((uid) => row(uid, <><button className="btn small primary" onClick={() => act(() => friendRespond(me, uid, true), P('Đã chấp nhận', 'Accepted'))} data-testid={`accept-${uid}`}>{P('Chấp nhận', 'Accept')}</button><button className="btn small ghost" onClick={() => act(() => friendRespond(me, uid, false), P('Đã từ chối', 'Declined'))}>{P('Từ chối', 'Decline')}</button></>))}
      <b>{P('Bạn bè', 'Friends')} ({friendIds(w, me).length})</b>
      {friendIds(w, me).map((uid) => row(uid, <><button className="btn small" onClick={() => openChat(uid)} data-testid={`dm-${uid}`}>💬</button><button className="btn small ghost" onClick={() => act(() => unfriend(me, uid), P('Đã xóa bạn', 'Removed'))}>{P('Xóa', 'Remove')}</button><button className="btn small ghost" onClick={() => act(() => block(me, uid), P('Đã chặn', 'Blocked'))}>⛔</button></>))}
      {friendIds(w, me).length === 0 && <small className="muted">{P('Chưa có bạn bè. Tạo thêm một tài khoản Preview (Cài đặt → Đăng xuất → đăng ký tên khác) rồi kết bạn để thử tương tác thật.', 'No friends yet. Create a second Preview account (Settings → Log out → sign up with another name) and add each other to try real interactions.')}</small>}
      {blockedList.length > 0 && <><b>{P('Đã chặn', 'Blocked')}</b>{blockedList.map((b) => row(b.who, <button className="btn small ghost" onClick={() => act(() => unblock(me, b.who), P('Đã bỏ chặn', 'Unblocked'))}>{P('Bỏ chặn', 'Unblock')}</button>))}</>}
    </div>
  );
}

const NOTIF_TEXT: Record<string, [string, string]> = {
  post_liked: ['đã thích bài của bạn', 'liked your post'], post_commented: ['đã bình luận', 'commented'], comment_replied: ['đã trả lời bình luận của bạn', 'replied to your comment'],
  comment_liked: ['đã thích bình luận của bạn', 'liked your comment'], post_shared: ['đã chia sẻ bài của bạn', 'shared your post'], friend_request: ['muốn kết bạn', 'sent a friend request'],
  friend_accepted: ['đã chấp nhận kết bạn', 'accepted your request'], guild_invite: ['mời bạn vào Guild', 'invited you to a guild'], guild_join_request: ['xin vào Guild', 'asked to join the guild'],
  guild_request_accepted: ['đã duyệt bạn vào Guild', 'accepted you into the guild'], guild_request_rejected: ['đã từ chối yêu cầu Guild', 'declined your guild request'],
  guild_role_changed: ['đã đổi vai trò của bạn', 'changed your role'], guild_kicked: ['đã mời bạn rời Guild', 'removed you from the guild'], guild_announcement: ['đăng thông báo Guild', 'posted a guild notice'],
  market_sold: ['đã mua thẻ bạn đăng bán', 'bought your listed card'], trade_offer: ['gửi đề nghị trao đổi', 'sent a trade offer'], trade_accepted: ['đã chấp nhận trao đổi', 'accepted your trade'],
};
function Activity({ h }: { h: Hub }) {
  const { P, w, me } = h;
  const list = myNotifs(w, me);
  return (
    <div className="lp-col">
      <div className="sh-row"><b className="grow">{P('Thông báo', 'Notifications')}</b><button className="btn small" onClick={() => readNotifs(me)} data-testid="notif-read">{P('Đánh dấu đã đọc tất cả', 'Mark all read')}</button></div>
      {list.length === 0 && <small className="muted">{P('Chưa có thông báo.', 'No notifications yet.')}</small>}
      {list.slice(0, 60).map((n) => { const t = NOTIF_TEXT[n.type];
        return <div key={n.id} className={'sh-notif' + (n.read ? '' : ' unread')} data-testid={`notif-${n.type}`}><Avatar a={acc(w, n.from)} d={h.d} size={34} /><span className="grow"><b>{acc(w, n.from)?.name}</b> {t ? P(t[0], t[1]) : n.type}{n.text ? ` · “${n.text}”` : ''}<small> · {ago(n.at, P)}</small></span>{!n.read && <em className="lb-dot static" />}</div>; })}
    </div>
  );
}
