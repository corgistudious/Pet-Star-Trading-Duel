import { useState } from 'react';
import { api } from '../api/client';
import type { RewardBundle } from '../api/types';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { PageHead } from '../components/Layout';
import { RewardList } from '../components/Rewards';
import { Countdown, Empty, Loading, useAction, useLoad } from '../components/ui';

interface LoginInfo { claimedToday: boolean; streakDay: number; cycle: RewardBundle[] }
interface GameEvent { id: number; key: string; startsAt: string; endsAt: string; started: boolean; rewards: RewardBundle; claimed: boolean }

export default function Rewards() {
  const { t } = useI18n();
  const { applyCurrencies, reloadPlayer } = useSession();
  const login = useLoad(() => api.get<LoginInfo>('/api/rewards/login'), []);
  const events = useLoad(() => api.get<GameEvent[]>('/api/events'), []);
  const { busy, run } = useAction();
  const [code, setCode] = useState('');
  const after = (r?: { currencies?: Record<string, number> }) => { if (r) { applyCurrencies(r.currencies); void reloadPlayer(); } };

  return (
    <div className="page">
      <PageHead title={t('web.rewards')} back="/" />
      <section className="panel">
        <h3>{t('web.daily_login')}</h3>
        {!login.data ? <Loading error={login.error} /> : (
          <>
            <div className="login-cycle">
              {login.data.cycle.map((r, i) => {
                const day = i + 1;
                const done = day < login.data!.streakDay || (day === login.data!.streakDay && login.data!.claimedToday);
                const today = day === login.data!.streakDay && !login.data!.claimedToday;
                return <div key={i} className={`day ${done ? 'done' : ''} ${today ? 'today' : ''}`}><b>{t('web.day', day)}</b><RewardList r={r} /></div>;
              })}
            </div>
            <button className="btn primary big" disabled={busy || login.data.claimedToday} data-testid="claim-login"
              onClick={() => run(() => api.post<{ currencies: Record<string, number> }>('/api/rewards/login/claim'), t('web.claimed')).then((r) => { after(r); void login.reload(); })}>
              {login.data.claimedToday ? t('web.come_back_tomorrow') : t('mailbox.claim')}
            </button>
          </>
        )}
      </section>
      <section className="panel">
        <h3>{t('web.giftcode')}</h3>
        <form className="row gap" onSubmit={(e) => { e.preventDefault(); void run(() => api.post<{ currencies: Record<string, number> }>('/api/giftcodes/redeem', { code: code.trim().toUpperCase() }), t('web.redeemed')).then((r) => { if (r) { setCode(''); after(r); } }); }}>
          <input className="grow" value={code} maxLength={32} autoCapitalize="characters" placeholder="PETSTAR-2026" onChange={(e) => setCode(e.target.value)} aria-label={t('web.giftcode')} />
          <button className="btn primary" disabled={busy || code.trim().length < 4}>{t('web.redeem')}</button>
        </form>
      </section>
      <section>
        <h3>{t('web.events')}</h3>
        {!events.data ? <Loading error={events.error} /> : events.data.length === 0 ? <Empty>{t('web.no_events')}</Empty> : (
          <ul className="list">
            {events.data.map((e) => (
              <li key={e.id} className="list-row">
                <div className="grow"><b>{t('event.' + e.key) !== 'event.' + e.key ? t('event.' + e.key) : e.key}</b><div><RewardList r={e.rewards} /></div>
                  <div className="muted small">{e.started ? <>{t('web.ends_in')} <Countdown until={e.endsAt} /></> : <>{t('web.starts_in')} <Countdown until={e.startsAt} /></>}</div></div>
                <button className="btn primary" disabled={busy || e.claimed || !e.started}
                  onClick={() => run(() => api.post<{ currencies: Record<string, number> }>(`/api/events/${e.id}/claim`), t('web.claimed')).then((r) => { after(r); void events.reload(); })}>
                  {e.claimed ? t('mailbox.claimed') : t('mailbox.claim')}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
