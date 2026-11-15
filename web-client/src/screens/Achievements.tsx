import { api } from '../api/client';
import { useI18n } from '../i18n';
import { PageHead } from '../components/Layout';
import { RewardList } from '../components/Rewards';
import { Loading, useLoad } from '../components/ui';

interface Ach { id: string; trigger: string; threshold: number; currencies: Record<string, number>; packs: Record<string, number>; unlocked: boolean; unlockedAt?: string | null }

export default function Achievements() {
  const { t, lang } = useI18n();
  const { data, error, reload } = useLoad(() => api.get<Ach[]>('/api/achievements'), []);
  if (!data) return <div className="page"><PageHead title={t('achievements.title')} back="/" /><Loading error={error} onRetry={reload} /></div>;
  const done = data.filter((a) => a.unlocked).length;
  return (
    <div className="page">
      <PageHead title={t('achievements.title')} back="/" right={<span className="muted">{done}/{data.length}</span>} />
      <ul className="list">
        {[...data].sort((a, b) => Number(b.unlocked) - Number(a.unlocked)).map((a) => (
          <li key={a.id} className={`list-row ${a.unlocked ? 'done' : 'todo'}`}>
            <span className="ach-icon">{a.unlocked ? '🏆' : '🔒'}</span>
            <div className="grow"><b>{t('ach.' + a.id)}</b><div><RewardList r={{ currencies: a.currencies, packs: a.packs }} /></div>
              {a.unlockedAt && <div className="muted small">{new Date(a.unlockedAt).toLocaleDateString(lang)}</div>}</div>
          </li>
        ))}
      </ul>
    </div>
  );
}
