import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { CardDto } from '../api/types';
import { useT } from '../i18n';
import { PageHead } from '../components/Layout';
import { CardView } from '../components/CardView';
import { CardGrid } from '../components/CardGrid';
import { ReportModal } from '../components/Post';
import { Loading, Modal, useAction, useLoad } from '../components/ui';

interface Profile {
  uid: number; nickname: string; level: number; codexDiscovered: number; codexTotal: number; achievements: number; followers: number; following: number;
  isFollowing: boolean; friendship: 'none' | 'friends' | 'request_sent' | 'request_received'; blockedByMe: boolean; showcase: CardDto[]; isMe: boolean;
}

export default function PlayerProfile() {
  const { uid } = useParams();
  const t = useT();
  const nav = useNavigate();
  const { data: p, error, reload } = useLoad(() => api.get<Profile>(`/api/players/${uid}`), [uid]);
  const { busy, run } = useAction();
  const [report, setReport] = useState(false);
  const [editing, setEditing] = useState(false);
  const [picked, setPicked] = useState<number[]>([]);
  if (!p) return <div className="page"><PageHead title={t('profile.title')} back /><Loading error={error} onRetry={reload} /></div>;
  const act = (fn: () => Promise<unknown>) => run(fn).then(() => reload());
  return (
    <div className="page">
      <PageHead title={p.nickname} back />
      <div className="panel">
        <p>Lv.{p.level} · UID {p.uid}</p>
        <p>{t('profile.codex', p.codexDiscovered, p.codexTotal)} · {t('profile.achievements', p.achievements)}</p>
        <p className="muted">{t('profile.follow_counts', p.followers, p.following)}</p>
        {!p.isMe && (
          <div className="actions">
            <button className="btn" disabled={busy} onClick={() => act(() => (p.isFollowing ? api.del(`/api/players/${p.uid}/follow`) : api.post(`/api/players/${p.uid}/follow`)))}>{p.isFollowing ? t('profile.unfollow') : t('profile.follow')}</button>
            {p.friendship === 'none' && <button className="btn primary" disabled={busy} onClick={() => act(() => api.post('/api/friends/requests', { uid: p.uid }))}>{t('profile.add_friend')}</button>}
            {p.friendship === 'request_sent' && <span className="tag">{t('web.outgoing')}</span>}
            {p.friendship === 'request_received' && <button className="btn primary" onClick={() => nav('/friends')}>{t('friends.accept')}</button>}
            {p.friendship === 'friends' && <>
              <button className="btn" onClick={() => nav(`/chat?c=dm:${p.uid}`)}>✉ {t('web.chat')}</button>
              <button className="btn" disabled={busy} onClick={() => run(() => api.post('/api/pvp/challenges', { uid: p.uid }), t('web.challenge_sent'))}>⚔️ {t('web.challenge')}</button>
            </>}
            <button className="btn ghost" disabled={busy} onClick={() => act(() => api.post(`/api/players/${p.uid}/mute`))}>{t('profile.mute')}</button>
            <button className="btn ghost danger" disabled={busy} onClick={() => act(() => (p.blockedByMe ? api.del(`/api/players/${p.uid}/block`) : api.post(`/api/players/${p.uid}/block`)))}>{p.blockedByMe ? t('profile.unblock') : t('profile.block')}</button>
            <button className="btn ghost" onClick={() => setReport(true)}>⚑ {t('report.button')}</button>
          </div>
        )}
      </div>
      <h3>{t('profile.showcase')} {p.isMe && <button className="btn ghost small" onClick={() => { setPicked([]); setEditing(true); }}>{t('profile.edit_showcase')}</button>}</h3>
      {p.showcase.length === 0 ? <p className="muted">{t('profile.showcase_empty')}</p> : <div className="card-grid">{p.showcase.map((c, i) => <CardView key={i} card={c} compact />)}</div>}
      {report && <ReportModal targetType="user" targetId={p.uid} onClose={() => setReport(false)} />}
      {editing && (
        <Modal title={t('profile.edit_showcase')} onClose={() => setEditing(false)} wide>
          <p className="muted small">{picked.length}/5</p>
          <CardGrid selectedIds={picked} pageSize={30} onPick={(c) => setPicked((x) => (x.includes(c.id) ? x.filter((y) => y !== c.id) : x.length < 5 ? [...x, c.id] : x))} />
          <button className="btn primary" disabled={busy} onClick={() => run(() => api.put('/api/profile/showcase', { cardIds: picked })).then(() => { setEditing(false); void reload(); })}>{t('web.save')}</button>
        </Modal>
      )}
    </div>
  );
}
