import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { LedgerEntryDto, SessionDto } from '../api/types';
import { useI18n, type Lang } from '../i18n';
import { useSession } from '../state/session';
import { PageHead } from '../components/Layout';
import { Loading, Price, Tabs, timeAgo, useAction, useLoad } from '../components/ui';

export default function Settings() {
  const { t, lang, setLang } = useI18n();
  const nav = useNavigate();
  const { profile, player, logout, leaveServer, reloadProfile } = useSession();
  const { busy, run } = useAction();
  const [tab, setTab] = useState<'general' | 'ledger' | 'sessions'>('general');
  const [name, setName] = useState(profile?.displayName ?? '');
  const changeLang = (l: Lang) => { setLang(l); void api.patch('/api/me', { language: l }).catch(() => {}); };
  return (
    <div className="page">
      <PageHead title={t('lobby.settings')} back="/" />
      <Tabs value={tab} onChange={setTab} items={[['general', t('web.general')], ['ledger', t('web.ledger')], ['sessions', t('web.sessions')]]} />
      {tab === 'general' && (
        <div className="form">
          <div role="group" aria-labelledby="lang-label">
            <span id="lang-label" className="muted small">{t('settings.language')}</span>
            <div className="seg"><button aria-pressed={lang === 'en'} className={lang === 'en' ? 'on' : ''} onClick={() => changeLang('en')}>English</button><button aria-pressed={lang === 'vi'} className={lang === 'vi' ? 'on' : ''} onClick={() => changeLang('vi')}>Tiếng Việt</button></div>
          </div>
          <label>{t('auth.display_name')}
            <div className="row gap"><input className="grow" value={name} maxLength={20} onChange={(e) => setName(e.target.value)} />
              <button className="btn" disabled={busy || name === profile?.displayName} onClick={() => run(() => api.patch('/api/me', { displayName: name }), t('web.saved')).then(() => reloadProfile())}>{t('web.save')}</button></div>
          </label>
          <p className="muted small">{profile?.email} · {profile?.emailVerified ? t('profile.email_verified') : t('profile.email_unverified')}</p>
          {!profile?.emailVerified && <button className="btn ghost" disabled={busy} onClick={() => run(() => api.post('/api/auth/resend-verification'), t('auth.check_email'))}>{t('profile.resend')}</button>}
          <p className="muted small">UID {profile?.uid} · {player?.serverCode} · {player?.nickname}</p>
          <button className="btn" onClick={() => nav(`/player/${profile?.uid}`)}>{t('profile.title')}</button>
          <button className="btn" onClick={() => { leaveServer(); nav('/'); }}>{t('settings.switch_server')}</button>
          <button className="btn danger" onClick={() => void logout()}>{t('settings.logout')}</button>
          <p className="muted small">{t('web.no_real_money')}</p>
        </div>
      )}
      {tab === 'ledger' && <Ledger />}
      {tab === 'sessions' && <Sessions />}
    </div>
  );
}

function Ledger() {
  const { t, lang } = useI18n();
  const { data, error } = useLoad(() => api.get<LedgerEntryDto[]>('/api/economy/ledger?limit=100'), []);
  if (!data) return <Loading error={error} />;
  return (
    <ul className="list ledger">
      {data.map((l) => (
        <li key={l.id} className="list-row">
          <div className="grow">{t('web.reason_' + l.reason) !== 'web.reason_' + l.reason ? t('web.reason_' + l.reason) : l.reason}<div className="muted small">{timeAgo(l.at, lang)}</div></div>
          <div className={`right ${l.delta < 0 ? 'neg' : 'pos'}`}>{l.delta > 0 ? '+' : ''}<Price currency={l.currency} amount={l.delta} /><div className="muted small">= {l.balanceAfter.toLocaleString()}</div></div>
        </li>
      ))}
    </ul>
  );
}

function Sessions() {
  const { t, lang } = useI18n();
  const { data, error, reload } = useLoad(() => api.get<SessionDto[]>('/api/auth/sessions'), []);
  const { busy, run } = useAction();
  if (!data) return <Loading error={error} />;
  return (
    <ul className="list">
      {data.map((s) => (
        <li key={s.id} className="list-row">
          <div className="grow">{s.platform ?? '?'} · {s.deviceName ?? ''} {s.current && <span className="tag">{t('web.this_device')}</span>}<div className="muted small">{timeAgo(s.lastUsedAt, lang)}</div></div>
          {!s.current && <button className="btn ghost danger" disabled={busy} onClick={() => run(() => api.del(`/api/auth/sessions/${s.id}`)).then(() => reload())}>{t('web.revoke')}</button>}
        </li>
      ))}
    </ul>
  );
}
