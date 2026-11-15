// Web Admin Panel. Every action is authorized again on the server by role policy (Moderation < GameMaster <
// ServerAdmin < Owner) and written to the audit log; tabs are hidden here only for convenience.
import { useState, type ReactNode } from 'react';
import { api, qs } from '../api/client';
import type { RewardBundle } from '../api/types';
import { useT } from '../i18n';
import { useSession } from '../state/session';
import { PageHead } from '../components/Layout';
import { Empty, Loading, Tabs, useAction, useLoad } from '../components/ui';

type Tab = 'overview' | 'servers' | 'reports' | 'accounts' | 'giftcodes' | 'events' | 'fraud' | 'mail' | 'audit';
const MOD = ['owner', 'admin', 'gm', 'moderator'], GM = ['owner', 'admin', 'gm'], ADMIN = ['owner', 'admin'], OWNER = ['owner'];
const TABS: [Tab, string[]][] = [['overview', ADMIN], ['servers', ADMIN], ['reports', MOD], ['accounts', ADMIN], ['giftcodes', ADMIN], ['events', ADMIN], ['fraud', GM], ['mail', OWNER], ['audit', ADMIN]];

export default function Admin() {
  const t = useT();
  const { hasRole, player } = useSession();
  const allowed = TABS.filter(([, roles]) => hasRole(...roles));
  const [tab, setTab] = useState<Tab>(allowed[0]?.[0] ?? 'reports');
  const [serverId, setServerId] = useState(player?.serverId ?? 1);
  if (allowed.length === 0) return <div className="page"><PageHead title={t('web.admin')} back="/" /><Empty>{t('error.forbidden')}</Empty></div>;
  return (
    <div className="page admin">
      <PageHead title={t('web.admin')} back="/" right={<label className="inline small">Server <input type="number" min={1} value={serverId} onChange={(e) => setServerId(Number(e.target.value))} style={{ width: 64 }} /></label>} />
      <Tabs value={tab} onChange={setTab} items={allowed.map(([k]) => [k, t('web.admin_' + k)])} />
      {tab === 'overview' && <Overview serverId={serverId} />}
      {tab === 'servers' && <Servers />}
      {tab === 'reports' && <Reports />}
      {tab === 'accounts' && <Accounts owner={hasRole('owner')} />}
      {tab === 'giftcodes' && <Giftcodes owner={hasRole('owner')} />}
      {tab === 'events' && <Events />}
      {tab === 'fraud' && <Fraud serverId={serverId} />}
      {tab === 'mail' && <Mail serverId={serverId} />}
      {tab === 'audit' && <Audit />}
    </div>
  );
}

/** Generic read-only table for admin lists. */
function Table({ rows, actions }: { rows: Record<string, unknown>[]; actions?: (r: Record<string, unknown>) => ReactNode }) {
  if (rows.length === 0) return <Empty>—</Empty>;
  const cols = Object.keys(rows[0]).slice(0, 9);
  return (
    <div className="table-wrap">
      <table className="table">
        <thead><tr>{cols.map((c) => <th key={c}>{c}</th>)}{actions && <th />}</tr></thead>
        <tbody>{rows.map((r, i) => <tr key={i}>{cols.map((c) => <td key={c}>{cell(r[c])}</td>)}{actions && <td className="nowrap">{actions(r)}</td>}</tr>)}</tbody>
      </table>
    </div>
  );
}
const cell = (v: unknown) => (v == null ? '' : typeof v === 'object' ? JSON.stringify(v).slice(0, 80) : String(v).slice(0, 60));

function Overview({ serverId }: { serverId: number }) {
  const { data, error, reload } = useLoad(() => api.get<Record<string, unknown>>('/api/admin/economy/overview' + qs({ serverId })), [serverId]);
  if (!data) return <Loading error={error} onRetry={reload} />;
  return (
    <div className="admin-grid">
      {(['activeListings', 'activeAuctions', 'openFraudEvents'] as const).map((k) => <div key={k} className="stat-tile"><small>{k}</small><b>{String(data[k])}</b></div>)}
      {(['walletTotals', 'heldInAuctions', 'feesSunk', 'market24h', 'cardSupplyByRarity', 'ledgerFlows24h'] as const).map((k) => (
        <section key={k} className="panel"><h4>{k}</h4><Table rows={(data[k] as Record<string, unknown>[]) ?? []} /></section>
      ))}
    </div>
  );
}

function Servers() {
  const t = useT();
  const { data, error, reload } = useLoad(() => api.get<Record<string, unknown>[]>('/api/admin/servers'), []);
  const { busy, run } = useAction();
  if (!data) return <Loading error={error} onRetry={reload} />;
  const patch = (id: unknown, body: object) => run(() => api.patch(`/api/admin/servers/${id}`, body), t('web.saved')).then(() => reload());
  return <Table rows={data} actions={(r) => (
    <>
      <select disabled={busy} value={String(r.status)} onChange={(e) => patch(r.id, { status: e.target.value })}>
        {['online', 'maintenance', 'coming_soon'].map((s) => <option key={s}>{s}</option>)}
      </select>
      <button className="btn ghost small" disabled={busy} onClick={() => patch(r.id, { registrationOpen: !r.registrationOpen })}>{r.registrationOpen ? 'close reg.' : 'open reg.'}</button>
    </>
  )} />;
}

function Reports() {
  const t = useT();
  const { data, error, reload } = useLoad(() => api.get<Record<string, unknown>[]>('/api/admin/reports?status=open'), []);
  const { busy, run } = useAction();
  if (!data) return <Loading error={error} onRetry={reload} />;
  if (data.length === 0) return <Empty>{t('moderation.empty')}</Empty>;
  const resolve = (id: unknown, action: string) => run(() => api.post(`/api/admin/reports/${id}/resolve`, { action, reason: 'web_admin', restrictHours: action === 'restrict' ? 24 : undefined })).then(() => reload());
  return <Table rows={data} actions={(r) => (
    <>
      <button className="btn ghost small" disabled={busy} onClick={() => resolve(r.id, 'dismiss')}>{t('moderation.dismiss')}</button>
      <button className="btn ghost small" disabled={busy} onClick={() => resolve(r.id, 'hide')}>{t('moderation.hide')}</button>
      <button className="btn ghost small danger" disabled={busy} onClick={() => resolve(r.id, 'restrict')}>{t('moderation.restrict')}</button>
      {r.targetType === 'chat' && <button className="btn ghost small" disabled={busy} onClick={() => run(() => api.post(`/api/admin/chat/${r.targetId}/hide`)).then(() => reload())}>hide msg</button>}
    </>
  )} />;
}

function Accounts({ owner }: { owner: boolean }) {
  const t = useT();
  const [q, setQ] = useState('');
  const [query, setQuery] = useState('');
  const { data, error, reload } = useLoad(() => api.get<Record<string, unknown>[]>('/api/admin/accounts' + qs({ q: query })), [query]);
  const { busy, run } = useAction();
  const [role, setRole] = useState('moderator');
  return (
    <div>
      <form className="filters" onSubmit={(e) => { e.preventDefault(); setQuery(q); }}>
        <input type="search" placeholder="email / name / uid" value={q} onChange={(e) => setQ(e.target.value)} />
        <button className="btn">{t('market.search')}</button>
        {owner && <select value={role} onChange={(e) => setRole(e.target.value)}>{['moderator', 'gm', 'admin'].map((r) => <option key={r}>{r}</option>)}</select>}
      </form>
      {!data ? <Loading error={error} onRetry={reload} /> : <Table rows={data} actions={(r) => (
        <>
          {r.status === 'banned'
            ? <button className="btn ghost small" disabled={busy} onClick={() => run(() => api.post(`/api/admin/accounts/${r.uid}/unban`)).then(() => reload())}>unban</button>
            : <button className="btn ghost small danger" disabled={busy} onClick={() => { const reason = prompt('Ban reason'); if (reason) void run(() => api.post(`/api/admin/accounts/${r.uid}/ban`, { reason, hours: null })).then(() => reload()); }}>ban</button>}
          {owner && <button className="btn ghost small" disabled={busy} onClick={() => run(() => api.post(`/api/admin/accounts/${r.uid}/roles`, { role })).then(() => reload())}>+{role}</button>}
          {owner && (r.roles as string[]).filter((x) => x !== 'owner').map((x) => <button key={x} className="btn ghost small" disabled={busy} onClick={() => run(() => api.del(`/api/admin/accounts/${r.uid}/roles/${x}`)).then(() => reload())}>−{x}</button>)}
        </>
      )} />}
    </div>
  );
}

function RewardEditor({ value, onChange }: { value: RewardBundle; onChange: (r: RewardBundle) => void }) {
  const set = (k: 'currencies' | 'packs' | 'items', id: string, v: string) => {
    const m = { ...(value[k] ?? {}) } as Record<string, number>;
    if (Number(v) > 0) m[id] = Number(v); else delete m[id];
    onChange({ ...value, [k]: m });
  };
  return (
    <div className="row gap wrap">
      <label className="inline small">MINI_GREEN_BEAN<input inputMode="numeric" value={value.currencies?.MINI_GREEN_BEAN ?? ''} onChange={(e) => set('currencies', 'MINI_GREEN_BEAN', e.target.value)} /></label>
      <label className="inline small">RAINBOW_STAR<input inputMode="numeric" value={value.currencies?.RAINBOW_STAR ?? ''} onChange={(e) => set('currencies', 'RAINBOW_STAR', e.target.value)} /></label>
      <label className="inline small">basic_pack<input inputMode="numeric" value={value.packs?.basic_pack ?? ''} onChange={(e) => set('packs', 'basic_pack', e.target.value)} /></label>
      <label className="inline small">training_scroll<input inputMode="numeric" value={value.items?.training_scroll ?? ''} onChange={(e) => set('items', 'training_scroll', e.target.value)} /></label>
    </div>
  );
}

function Giftcodes({ owner }: { owner: boolean }) {
  const t = useT();
  const { data, error, reload } = useLoad(() => api.get<Record<string, unknown>[]>('/api/admin/giftcodes'), []);
  const { busy, run } = useAction();
  const [code, setCode] = useState('');
  const [maxUses, setMaxUses] = useState('100');
  const [days, setDays] = useState('7');
  const [rewards, setRewards] = useState<RewardBundle>({ currencies: { MINI_GREEN_BEAN: 100 } });
  const create = () => run(() => api.post('/api/admin/giftcodes', {
    code: code.trim().toUpperCase() || null, rewards, maxUses: Number(maxUses), endsAt: new Date(Date.now() + Number(days) * 86400_000).toISOString(),
  }), t('web.saved')).then(() => reload());
  return (
    <div>
      <div className="panel form">
        <h4>{t('web.create')}</h4>
        <input placeholder="CODE (optional, A-Z 0-9 -)" value={code} onChange={(e) => setCode(e.target.value)} />
        <RewardEditor value={rewards} onChange={setRewards} />
        <div className="row gap"><label className="inline small">max uses<input inputMode="numeric" value={maxUses} onChange={(e) => setMaxUses(e.target.value)} /></label>
          <label className="inline small">days<input inputMode="numeric" value={days} onChange={(e) => setDays(e.target.value)} /></label></div>
        {!owner && <p className="muted small">{t('web.cards_owner_only')}</p>}
        <button className="btn primary" disabled={busy} onClick={create}>{t('web.create')}</button>
      </div>
      {!data ? <Loading error={error} onRetry={reload} /> : <Table rows={data} actions={(r) => r.active ? <button className="btn ghost small" disabled={busy} onClick={() => run(() => api.post(`/api/admin/giftcodes/${r.id}/deactivate`)).then(() => reload())}>deactivate</button> : null} />}
    </div>
  );
}

function Events() {
  const t = useT();
  const { data, error, reload } = useLoad(() => api.get<Record<string, unknown>[]>('/api/admin/events'), []);
  const { busy, run } = useAction();
  const [key, setKey] = useState('');
  const [days, setDays] = useState('3');
  const [rewards, setRewards] = useState<RewardBundle>({ currencies: { MINI_GREEN_BEAN: 200 } });
  const create = () => run(() => api.post('/api/admin/events', { key, rewards, startsAt: new Date().toISOString(), endsAt: new Date(Date.now() + Number(days) * 86400_000).toISOString() }), t('web.saved')).then(() => reload());
  return (
    <div>
      <div className="panel form">
        <h4>{t('web.create')}</h4>
        <input placeholder="event key (e.g. launch_week)" value={key} onChange={(e) => setKey(e.target.value)} />
        <RewardEditor value={rewards} onChange={setRewards} />
        <label className="inline small">days<input inputMode="numeric" value={days} onChange={(e) => setDays(e.target.value)} /></label>
        <button className="btn primary" disabled={busy || !key} onClick={create}>{t('web.create')}</button>
      </div>
      {!data ? <Loading error={error} onRetry={reload} /> : <Table rows={data} actions={(r) => r.active ? <button className="btn ghost small" disabled={busy} onClick={() => run(() => api.post(`/api/admin/events/${r.id}/deactivate`)).then(() => reload())}>deactivate</button> : null} />}
    </div>
  );
}

function Fraud({ serverId }: { serverId: number }) {
  const { data, error, reload } = useLoad(() => api.get<Record<string, unknown>[]>('/api/admin/economy/suspicious' + qs({ serverId, status: 'open' })), [serverId]);
  const { busy, run } = useAction();
  const [serial, setSerial] = useState('');
  const [found, setFound] = useState<unknown>(null);
  return (
    <div>
      <form className="filters" onSubmit={(e) => { e.preventDefault(); void run(() => api.get(`/api/admin/cards/serial/${serial.trim().toUpperCase()}`)).then((r) => r && setFound(r)); }}>
        <input placeholder="12-char serial" value={serial} onChange={(e) => setSerial(e.target.value)} />
        <button className="btn">lookup</button>
      </form>
      {found != null && <pre className="json">{JSON.stringify(found, null, 2)}</pre>}
      {!data ? <Loading error={error} onRetry={reload} /> : <Table rows={data} actions={(r) => (
        <>
          <button className="btn ghost small" disabled={busy} onClick={() => run(() => api.post(`/api/admin/economy/suspicious/${r.id}/review`, { lock: false, reason: 'reviewed_ok' })).then(() => reload())}>ok</button>
          <button className="btn ghost small danger" disabled={busy} onClick={() => run(() => api.post(`/api/admin/economy/suspicious/${r.id}/review`, { lock: true, reason: 'confirmed' })).then(() => reload())}>confirm</button>
        </>
      )} />}
    </div>
  );
}

function Mail({ serverId }: { serverId: number }) {
  const t = useT();
  const { busy, run } = useAction();
  const [uid, setUid] = useState('');
  const [body, setBody] = useState('');
  const [rewards, setRewards] = useState<RewardBundle>({});
  return (
    <div className="panel form">
      <p className="muted small">{t('web.mail_owner_note')}</p>
      <input inputMode="numeric" placeholder="UID" value={uid} onChange={(e) => setUid(e.target.value)} />
      <textarea rows={3} maxLength={500} value={body} onChange={(e) => setBody(e.target.value)} />
      <RewardEditor value={rewards} onChange={setRewards} />
      <button className="btn primary" disabled={busy || !uid || !body} onClick={() => run(() => api.post('/api/admin/mail', { uid: Number(uid), serverId, body, currencies: rewards.currencies ?? {}, items: rewards.items ?? {}, packs: rewards.packs ?? {} }), t('web.saved'))}>{t('feed.send')}</button>
    </div>
  );
}

function Audit() {
  const { data, error, reload } = useLoad(() => api.get<Record<string, unknown>[]>('/api/admin/audit'), []);
  if (!data) return <Loading error={error} onRetry={reload} />;
  return <Table rows={data} />;
}
