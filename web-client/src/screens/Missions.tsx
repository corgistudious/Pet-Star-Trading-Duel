import { useState } from 'react';
import { api } from '../api/client';
import type { RewardBundle } from '../api/types';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { PageHead } from '../components/Layout';
import { RewardList } from '../components/Rewards';
import { Countdown, Loading, Tabs, useAction, useLoad, useServerClock } from '../components/ui';

export interface MissionView { id: string; trigger: string; target: number; progress: number; completed: boolean; claimed: boolean; rewards: RewardBundle }
export interface MissionPeriod { periodKey: string; resetsAt: string; items: MissionView[] }
export interface MissionsDto { serverTime: string; daily: MissionPeriod; weekly: MissionPeriod }

/** Missions ready to claim (used for the lobby badge). */
export const claimable = (m?: MissionsDto) => (m ? [...m.daily.items, ...m.weekly.items].filter((x) => x.completed && !x.claimed).length : 0);

export default function Missions() {
  const { t } = useI18n();
  const { applyCurrencies, reloadPlayer } = useSession();
  const [tab, setTab] = useState<'daily' | 'weekly'>('daily');
  const { data, error, reload } = useLoad(() => api.get<MissionsDto>('/api/missions'), []);
  const offset = useServerClock(data?.serverTime);
  const { busy, run } = useAction();
  if (!data) return <div className="page"><PageHead title={t('web.missions')} back="/" /><Loading error={error} onRetry={reload} /></div>;
  const period = data[tab];
  const claim = async (m: MissionView) => {
    const r = await run(() => api.post<{ currencies: Record<string, number> }>(`/api/missions/${m.id}/claim`), t('web.claimed'));
    if (r) { applyCurrencies(r.currencies); void reloadPlayer(); void reload(); }
  };
  return (
    <div className="page">
      <PageHead title={t('web.missions')} back="/" />
      <Tabs value={tab} onChange={setTab} items={[['daily', `${t('web.daily')} (${data.daily.items.filter((x) => x.completed && !x.claimed).length})`], ['weekly', `${t('web.weekly')} (${data.weekly.items.filter((x) => x.completed && !x.claimed).length})`]]} />
      <p className="muted small">{t('web.resets_in')} <Countdown until={period.resetsAt} offset={offset} onEnd={reload} /> · UTC</p>
      <ul className="list">
        {period.items.map((m) => (
          <li key={m.id} className={`list-row mission ${m.claimed ? 'done' : ''}`} data-testid={`mission-${m.id}`}>
            <div className="grow">
              <b>{t('mission.' + m.id, m.target)}</b>
              <div className="progress"><div style={{ width: `${(100 * m.progress) / m.target}%` }} /></div>
              <div className="row gap small"><span className="muted">{m.progress}/{m.target}</span><RewardList r={m.rewards} /></div>
            </div>
            {m.claimed ? <span className="tag">{t('mailbox.claimed')}</span>
              : <button className="btn primary" disabled={busy || !m.completed} onClick={() => claim(m)}>{t('mailbox.claim')}</button>}
          </li>
        ))}
      </ul>
      <p className="muted small">{t('web.missions_note')}</p>
    </div>
  );
}
