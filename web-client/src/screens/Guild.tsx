import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, qs } from '../api/client';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { useRealtimeEvent } from '../state/realtime';
import { PageHead } from '../components/Layout';
import { Composer, FeedList, useFeed } from './Feed';
import { Confirm, Empty, Loading, Tabs, timeAgo, useAction, useLoad } from '../components/ui';

interface GuildRow { id: number; name: string; description: string; joinPolicy: string; maxMembers: number; members: number }
interface Member { uid: number; nickname: string; level: number; role: string; joinedAt: string }
interface Mine {
  guild: { id: number; name: string; description: string; joinPolicy: string; maxMembers: number } | null;
  myRole?: string; members?: Member[]; announcements?: { id: number; text: string; createdAt: string; author: string }[];
  requests?: { id: number; uid: number; nickname: string; level: number }[];
}
const rank = (r?: string) => (r === 'leader' ? 3 : r === 'officer' ? 2 : 1);

export default function Guild() {
  const { t } = useI18n();
  const mine = useLoad(() => api.get<Mine>('/api/guilds/mine'), []);
  useRealtimeEvent('notification', () => void mine.reload());
  if (!mine.data) return <div className="page"><PageHead title={t('web.guild')} back="/" /><Loading error={mine.error} onRetry={mine.reload} /></div>;
  return mine.data.guild ? <MyGuild data={mine.data} reload={mine.reload} /> : <FindGuild onJoined={mine.reload} />;
}

function FindGuild({ onJoined }: { onJoined: () => void }) {
  const { t } = useI18n();
  const [q, setQ] = useState('');
  const [query, setQuery] = useState('');
  const list = useLoad(() => api.get<GuildRow[]>('/api/guilds' + qs({ q: query })), [query]);
  const { busy, run } = useAction();
  const [name, setName] = useState('');
  const [desc, setDesc] = useState('');
  const [policy, setPolicy] = useState('open');
  const join = async (g: GuildRow) => {
    const r = await run(() => api.post<{ status: string }>(`/api/guilds/${g.id}/join`));
    if (r?.status === 'joined') onJoined(); else if (r) void list.reload();
  };
  const create = async () => { if (await run(() => api.post('/api/guilds', { name, description: desc, joinPolicy: policy }), t('web.guild_created'))) onJoined(); };
  return (
    <div className="page">
      <PageHead title={t('web.guild')} back="/" />
      <form className="filters" onSubmit={(e) => { e.preventDefault(); setQuery(q); }}>
        <input type="search" placeholder={t('web.search_guilds')} value={q} onChange={(e) => setQ(e.target.value)} />
        <button className="btn">{t('market.search')}</button>
      </form>
      {!list.data ? <Loading error={list.error} /> : list.data.length === 0 ? <Empty>{t('web.no_guilds')}</Empty> : (
        <ul className="list">
          {list.data.map((g) => (
            <li key={g.id} className="list-row">
              <div className="grow"><b>{g.name}</b> <span className="muted small">{g.members}/{g.maxMembers} · {t('web.policy_' + g.joinPolicy)}</span><div className="muted small">{g.description}</div></div>
              <button className="btn primary" disabled={busy || g.members >= g.maxMembers} onClick={() => join(g)}>{g.joinPolicy === 'open' ? t('web.join') : t('web.request_join')}</button>
            </li>
          ))}
        </ul>
      )}
      <h3>{t('web.create_guild')}</h3>
      <div className="form">
        <input placeholder={t('web.guild_name')} maxLength={24} value={name} onChange={(e) => setName(e.target.value)} />
        <textarea placeholder={t('web.guild_desc')} maxLength={200} rows={2} value={desc} onChange={(e) => setDesc(e.target.value)} />
        <select value={policy} onChange={(e) => setPolicy(e.target.value)}>
          <option value="open">{t('web.policy_open')}</option><option value="approval">{t('web.policy_approval')}</option>
        </select>
        <button className="btn primary" disabled={busy || name.trim().length < 3} onClick={create}>{t('web.create_guild')}</button>
      </div>
    </div>
  );
}

function MyGuild({ data, reload }: { data: Mine; reload: () => void }) {
  const { t, lang } = useI18n();
  const nav = useNavigate();
  const { player } = useSession();
  const { busy, run } = useAction();
  const [tab, setTab] = useState<'members' | 'feed' | 'news'>('members');
  const [leave, setLeave] = useState(false);
  const [ann, setAnn] = useState('');
  const [composing, setComposing] = useState(false);
  const feed = useFeed((before) => '/api/guilds/feed' + qs({ before }), [data.guild!.id]);
  const my = rank(data.myRole);
  const g = data.guild!;
  return (
    <div className="page">
      <PageHead title={g.name} back="/" right={<button className="btn ghost" onClick={() => nav('/chat?c=guild')}>💬 {t('web.chat')}</button>} />
      <p className="muted">{g.description}</p>
      <p className="muted small">{(data.members ?? []).length}/{g.maxMembers} · {t('web.role_' + data.myRole)}</p>
      <Tabs value={tab} onChange={setTab} items={[['members', t('web.members')], ['feed', t('web.guild_feed')], ['news', t('web.announcements')]]} />
      {tab === 'members' && (
        <>
          {my >= 2 && (data.requests ?? []).length > 0 && (
            <section><h4>{t('web.join_requests')}</h4>
              <ul className="list">{data.requests!.map((r) => (
                <li key={r.id} className="list-row"><div className="grow">{r.nickname} <span className="muted small">Lv.{r.level}</span></div>
                  <button className="btn primary" disabled={busy} onClick={() => run(() => api.post(`/api/guilds/requests/${r.id}/accept`)).then(reload)}>{t('web.accept')}</button>
                  <button className="btn ghost" disabled={busy} onClick={() => run(() => api.post(`/api/guilds/requests/${r.id}/reject`)).then(reload)}>{t('web.decline')}</button></li>))}
              </ul></section>
          )}
          <ul className="list">
            {(data.members ?? []).map((m) => (
              <li key={m.uid} className="list-row">
                <div className="grow"><button className="link" onClick={() => nav(`/player/${m.uid}`)}>{m.nickname}</button> <span className="tag">{t('web.role_' + m.role)}</span> <span className="muted small">Lv.{m.level}</span></div>
                {m.uid !== player?.uid && my === 3 && (
                  <button className="btn ghost small" disabled={busy} onClick={() => run(() => api.post(`/api/guilds/members/${m.uid}/role`, { role: m.role === 'officer' ? 'member' : 'officer' })).then(reload)}>
                    {m.role === 'officer' ? t('web.demote') : t('web.promote')}</button>
                )}
                {m.uid !== player?.uid && my > rank(m.role) && my >= 2 && (
                  <button className="btn ghost small danger" disabled={busy} onClick={() => run(() => api.post(`/api/guilds/members/${m.uid}/kick`)).then(reload)}>{t('web.kick')}</button>
                )}
              </li>
            ))}
          </ul>
          <button className="btn ghost danger" onClick={() => setLeave(true)}>{t('web.leave_guild')}</button>
        </>
      )}
      {tab === 'feed' && (
        <>
          <button className="btn primary" onClick={() => setComposing(true)}>{t('feed.create')}</button>
          <FeedList feed={feed} />
          {composing && <Composer endpoint="/api/guilds/feed" onClose={() => setComposing(false)} onDone={() => { setComposing(false); void feed.first.reload(); }} />}
        </>
      )}
      {tab === 'news' && (
        <>
          {my >= 2 && (
            <form className="row gap" onSubmit={(e) => { e.preventDefault(); void run(() => api.post('/api/guilds/announcements', { text: ann })).then((r) => { if (r) { setAnn(''); reload(); } }); }}>
              <input className="grow" maxLength={300} value={ann} onChange={(e) => setAnn(e.target.value)} placeholder={t('web.announcement_hint')} />
              <button className="btn primary" disabled={busy || !ann.trim()}>{t('feed.publish')}</button>
            </form>
          )}
          {(data.announcements ?? []).length === 0 ? <Empty>{t('feed.empty')}</Empty> : (
            <ul className="list">{data.announcements!.map((a) => <li key={a.id} className="list-row"><div className="grow">{a.text}<div className="muted small">{a.author} · {timeAgo(a.createdAt, lang)}</div></div></li>)}</ul>
          )}
        </>
      )}
      {leave && <Confirm text={t('web.leave_guild') + '?'} busy={busy} onNo={() => setLeave(false)} onYes={() => run(() => api.post('/api/guilds/leave')).then(() => { setLeave(false); reload(); })} />}
    </div>
  );
}
