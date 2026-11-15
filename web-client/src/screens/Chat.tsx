import { useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api, qs } from '../api/client';
import type { ChatMessage } from '../api/types';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { useRealtime, useRealtimeEvent } from '../state/realtime';
import { PageHead } from '../components/Layout';
import { ReportModal } from '../components/Post';
import { Empty, Loading, useAction, useLoad } from '../components/ui';

interface Friend { uid: number; nickname: string; status: string }

/** Decides whether a realtime chat push belongs to the open channel. DM keys use internal ids, so DMs match by author. */
export function belongsTo(channel: string, pushKey: string, authorUid: number, myUid: number): boolean {
  if (channel === 'world') return pushKey === 'world';
  if (channel === 'guild') return pushKey.startsWith('guild:');
  if (channel.startsWith('dm:')) return pushKey.startsWith('dm:') && authorUid === Number(channel.slice(3)) && authorUid !== myUid;
  return false;
}

export default function Chat() {
  const { t } = useI18n();
  const nav = useNavigate();
  const [params, setParams] = useSearchParams();
  const channel = params.get('c') ?? 'world';
  const { player } = useSession();
  const { generation } = useRealtime();
  const friends = useLoad(() => api.get<Friend[]>('/api/friends'), []);
  const [msgs, setMsgs] = useState<ChatMessage[]>([]);
  const [err, setErr] = useState<unknown>();
  const [loaded, setLoaded] = useState(false);
  const [text, setText] = useState('');
  const [report, setReport] = useState<number | null>(null);
  const { busy, run } = useAction();
  const end = useRef<HTMLDivElement>(null);

  const load = async () => {
    try { setMsgs(await api.get<ChatMessage[]>(`/api/chat/${channel}`)); setErr(undefined); } catch (e) { setErr(e); } finally { setLoaded(true); }
  };
  useEffect(() => { setLoaded(false); setMsgs([]); void load(); }, [channel, generation]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { end.current?.scrollIntoView({ block: 'end' }); }, [msgs.length]);
  useRealtimeEvent<ChatMessage & { channel: string }>('chat', (m) => {
    if (!player || !belongsTo(channel, m.channel, m.authorUid, player.uid)) return;
    setMsgs((x) => (x.some((y) => y.id === m.id) ? x : [...x, { ...m, mine: m.authorUid === player.uid }].slice(-200)));
  });

  const older = async () => {
    if (!msgs[0]) return;
    const r = await api.get<ChatMessage[]>(`/api/chat/${channel}` + qs({ before: msgs[0].id }));
    setMsgs((x) => [...r, ...x]);
  };
  const send = async () => {
    const r = await run(() => api.post<{ id: number }>(`/api/chat/${channel}`, { text }));
    if (r) { setText(''); if (channel.startsWith('dm:')) void load(); }
  };
  const dmFriends = (friends.data ?? []).filter((f) => f.status === 'friends');
  return (
    <div className="page chat-page">
      <PageHead title={t('web.chat')} back="/" />
      <div className="chips">
        <button className={channel === 'world' ? 'chip on' : 'chip'} onClick={() => setParams({ c: 'world' })}>🌍 {t('web.chat_world')}</button>
        <button className={channel === 'guild' ? 'chip on' : 'chip'} onClick={() => setParams({ c: 'guild' })}>🏰 {t('web.guild')}</button>
        {dmFriends.map((f) => <button key={f.uid} className={channel === `dm:${f.uid}` ? 'chip on' : 'chip'} onClick={() => setParams({ c: `dm:${f.uid}` })}>✉ {f.nickname}</button>)}
      </div>
      <div className="chat-log" role="log" aria-live="polite">
        {!loaded ? <Loading /> : err ? <Loading error={err} onRetry={load} /> : msgs.length === 0 ? <Empty>{t('web.chat_empty')}</Empty> : (
          <>
            {msgs.length >= 50 && <button className="btn ghost small" onClick={older}>{t('web.load_more')}</button>}
            {msgs.map((m) => (
              <div key={m.id} className={`msg ${m.mine ? 'mine' : ''}`}>
                {!m.mine && <button className="link small" onClick={() => nav(`/player/${m.authorUid}`)}>{m.authorNickname}</button>}
                <div className="bubble">{m.text}</div>
                {!m.mine && <button className="link tiny" aria-label={t('report.button')} onClick={() => setReport(m.id)}>⚑</button>}
              </div>
            ))}
          </>
        )}
        <div ref={end} />
      </div>
      <form className="chat-input" onSubmit={(e) => { e.preventDefault(); if (text.trim()) void send(); }}>
        <input maxLength={200} value={text} onChange={(e) => setText(e.target.value)} placeholder={t('web.chat_hint')} disabled={!!err} aria-label={t('web.chat_hint')} />
        <button className="btn primary" disabled={busy || !text.trim()}>{t('feed.send')}</button>
      </form>
      {report && <ReportModal targetType="chat" targetId={report} onClose={() => setReport(null)} />}
    </div>
  );
}
