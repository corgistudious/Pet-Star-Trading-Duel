import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, qs } from '../api/client';
import { useT } from '../i18n';
import { PageHead } from '../components/Layout';
import { Empty, Loading, useAction, useLoad } from '../components/ui';

interface Friend { requestId: number; uid: number; nickname: string; level: number; status: 'friends' | 'incoming' | 'outgoing' }

export default function Friends() {
  const t = useT();
  const nav = useNavigate();
  const list = useLoad(() => api.get<Friend[]>('/api/friends'), []);
  const { busy, run } = useAction();
  const [q, setQ] = useState('');
  const [results, setResults] = useState<{ uid: number; nickname: string; level: number }[] | null>(null);
  const search = async () => { const r = await run(() => api.get<{ uid: number; nickname: string; level: number }[]>('/api/players/search' + qs({ q }))); if (r) setResults(r); };
  const groups: [Friend['status'], string][] = [['incoming', t('web.incoming')], ['friends', t('friends.title')], ['outgoing', t('web.outgoing')]];
  return (
    <div className="page">
      <PageHead title={t('friends.title')} back="/" />
      <form className="filters" onSubmit={(e) => { e.preventDefault(); if (q.trim()) void search(); }}>
        <input type="search" placeholder={t('friends.search_hint')} value={q} onChange={(e) => setQ(e.target.value)} />
        <button className="btn" disabled={busy}>{t('market.search')}</button>
      </form>
      {results && (
        <ul className="list">
          {results.length === 0 && <Empty>{t('market.empty')}</Empty>}
          {results.map((r) => <li key={r.uid} className="list-row" onClick={() => nav(`/player/${r.uid}`)}><div className="grow">{r.nickname} <span className="muted small">Lv.{r.level} · UID {r.uid}</span></div>›</li>)}
        </ul>
      )}
      {!list.data ? <Loading error={list.error} onRetry={list.reload} /> : list.data.length === 0 ? <Empty>{t('friends.empty')}</Empty> : groups.map(([st, label]) => {
        const rows = list.data!.filter((f) => f.status === st);
        if (rows.length === 0) return null;
        return (
          <section key={st}><h4>{label}</h4>
            <ul className="list">
              {rows.map((f) => (
                <li key={f.uid} className="list-row">
                  <div className="grow"><button className="link" onClick={() => nav(`/player/${f.uid}`)}>{f.nickname}</button> <span className="muted small">Lv.{f.level}</span></div>
                  {st === 'incoming' && <button className="btn primary" disabled={busy} onClick={() => run(() => api.post(`/api/friends/requests/${f.requestId}/accept`)).then(() => list.reload())}>{t('friends.accept')}</button>}
                  {st === 'friends' && <button className="btn ghost" onClick={() => nav(`/chat?c=dm:${f.uid}`)}>✉</button>}
                  <button className="btn ghost danger" disabled={busy} onClick={() => run(() => api.del(`/api/friends/${f.uid}`)).then(() => list.reload())}>✕</button>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
    </div>
  );
}
