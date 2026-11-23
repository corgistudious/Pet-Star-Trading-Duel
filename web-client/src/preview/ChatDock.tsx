// Three-channel chat (World / Guild / Friends) docked bottom-left in the lobby, plus a reusable chat panel.
import { useEffect, useRef, useState } from 'react';
import { Avatar, Bubble } from './Cosmetics';
import { ago, errText, type Hub } from './hub';
import { acc, areFriends, block, chanKey, friendIds, guildOf, markRead, messages, online, report, sendChat, unread, type Channel } from './world';

export function ChatPanel({ h, channel, compact }: { h: Hub; channel: Channel; compact?: boolean }) {
  const { P, w, me, say } = h;
  const [text, setText] = useState('');
  const list = messages(w, me, channel).slice(-60);
  const box = useRef<HTMLDivElement>(null);
  const key = chanKey(w, me, channel);
  const last = list.length ? list[list.length - 1].id : 0;
  useEffect(() => { box.current?.scrollTo({ top: 1e9 }); if (last) markRead(me, key, last); }, [last, key, me]);
  if (channel.kind === 'guild' && !guildOf(w, me)) return <small className="muted cd-empty">{P('Tham gia Guild để dùng Guild Chat.', 'Join a guild to use Guild Chat.')}</small>;
  if (channel.kind === 'dm' && !areFriends(w, me, channel.uid)) return <small className="muted cd-empty">{P('Chỉ nhắn riêng với bạn bè.', 'Direct messages are for friends only.')}</small>;
  const send = () => { try { const m = sendChat(me, channel, text); setText(''); if (m.status === 'pending') say(P('Tin nhắn đang chờ kiểm duyệt', 'Message held for review')); } catch (e) { say(errText(e, P)); } };
  return (
    <div className={'cd-panel' + (compact ? ' compact' : '')}>
      <div className="cd-msgs" ref={box}>
        {list.length === 0 && <small className="muted">{P('Chưa có tin nhắn.', 'No messages yet.')}</small>}
        {list.map((m) => { const a = acc(w, m.from); const post = /^\[post:(\d+)\]$/.exec(m.text);
          return <div key={m.id} className={'cd-msg' + (m.from === me ? ' mine' : '')} data-testid={`msg-${m.id}`}>
            {!compact && <Avatar a={a} d={h.d} size={26} onClick={() => h.openProfile(m.from)} />}
            <div><small className="cd-meta"><b>{a?.name}</b>{guildOf(w, m.from)?.tag ? ` [${guildOf(w, m.from)!.tag}]` : ''} · {ago(m.at, P)}{m.status === 'pending' ? ` · ⏳ ${P('chờ duyệt', 'pending')}` : ''}</small>
              <Bubble d={h.d} id={a?.bubble} mine={m.from === me}>{post ? `🔗 ${P('Bài đăng', 'Post')} #${post[1]}` : m.text}</Bubble>
              {m.from !== me && <span className="cd-act"><button onClick={() => { try { report(me, 'chat', m.id, 'spam'); say(P('Đã báo cáo', 'Reported')); } catch (e) { say(errText(e, P)); } }} aria-label={P('Báo cáo', 'Report')}>🚩</button><button onClick={() => { block(me, m.from); say(P('Đã chặn', 'Blocked')); }} aria-label={P('Chặn', 'Block')}>⛔</button></span>}
            </div></div>; })}
      </div>
      <div className="sh-row"><input className="lb-input" maxLength={200} value={text} onChange={(e) => setText(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && text.trim() && send()} placeholder={P('Nhắn tin…', 'Message…')} data-testid={`chat-input-${channel.kind}`} />
        <button className="btn small primary" disabled={!text.trim()} onClick={send} data-testid={`chat-send-${channel.kind}`}>{P('Gửi', 'Send')}</button></div>
    </div>
  );
}

export function ChatDock({ h, dm, setDm }: { h: Hub; dm: number | null; setDm: (uid: number | null) => void }) {
  const { P, w, me } = h;
  const [open, setOpen] = useState(false);
  const [tab, setTab] = useState<'world' | 'guild' | 'friends'>('world');
  useEffect(() => { if (dm != null) { setOpen(true); setTab('friends'); } }, [dm]);
  const fr = friendIds(w, me);
  const uW = unread(w, me, { kind: 'world' }), uG = guildOf(w, me) ? unread(w, me, { kind: 'guild' }) : 0, uF = fr.reduce((x, f) => x + unread(w, me, { kind: 'dm', uid: f }), 0);
  const total = uW + uG + uF;
  if (!open) return <button className="cd-toggle" onClick={() => setOpen(true)} data-testid="chat-open">💬 <span>Chat</span>{total > 0 && <em className="lb-badge">{total > 99 ? '99+' : total}</em>}</button>;
  return (
    <div className="cd" data-testid="chat-dock">
      <div className="cd-head">
        {([['world', P('Thế Giới', 'World'), uW], ['guild', 'Guild', uG], ['friends', P('Bạn Bè', 'Friends'), uF]] as const).map(([k, l, u]) => (
          <button key={k} className={tab === k ? 'on' : ''} onClick={() => { setTab(k); if (k !== 'friends') setDm(null); }} data-testid={`chat-tab-${k}`}>{l}{u > 0 && <em className="lb-badge">{u}</em>}</button>))}
        <button className="cd-x" onClick={() => setOpen(false)} aria-label={P('Thu gọn', 'Collapse')} data-testid="chat-close">▾</button>
      </div>
      {tab === 'world' && <ChatPanel h={h} channel={{ kind: 'world' }} compact />}
      {tab === 'guild' && <ChatPanel h={h} channel={{ kind: 'guild' }} compact />}
      {tab === 'friends' && (dm != null ? <><button className="cd-back" onClick={() => setDm(null)}>‹ {acc(w, dm)?.name}</button><ChatPanel h={h} channel={{ kind: 'dm', uid: dm }} compact /></>
        : <div className="cd-friends">{fr.length === 0 && <small className="muted">{P('Chưa có bạn bè.', 'No friends yet.')}</small>}
          {fr.map((f) => { const a = acc(w, f); const u = unread(w, me, { kind: 'dm', uid: f });
            return <button key={f} onClick={() => setDm(f)} data-testid={`chat-dm-${f}`}><Avatar a={a} d={h.d} size={26} /><span className="ellipsis">{a?.name}</span><small>{online(a) ? '🟢' : '⚪'}</small>{u > 0 && <em className="lb-badge">{u}</em>}</button>; })}</div>)}
    </div>
  );
}
