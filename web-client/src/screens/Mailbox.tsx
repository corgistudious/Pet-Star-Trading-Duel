import { api } from '../api/client';
import type { MailItem } from '../api/types';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { PageHead } from '../components/Layout';
import { RewardList } from '../components/Rewards';
import { Empty, Loading, timeAgo, useAction, useLoad } from '../components/ui';

export default function Mailbox() {
  const { t, lang } = useI18n();
  const { applyCurrencies, reloadPlayer } = useSession();
  const { data, error, reload } = useLoad(() => api.get<MailItem[]>('/api/mailbox'), []);
  const { busy, run } = useAction();
  const claim = async (m: MailItem) => {
    const r = await run(() => api.post<{ currencies: Record<string, number> }>(`/api/mailbox/${m.id}/claim`), t('mailbox.claimed'));
    if (r) { applyCurrencies(r.currencies); void reloadPlayer(); void reload(); }
  };
  return (
    <div className="page">
      <PageHead title={t('mailbox.title')} back="/" />
      {!data ? <Loading error={error} onRetry={reload} /> : data.length === 0 ? <Empty>{t('mailbox.empty')}</Empty> : (
        <ul className="list">
          {data.map((m) => (
            <li key={m.id} className="list-row mail">
              <div className="grow">
                <b>{t(m.titleKey)}</b>
                {m.body && <p className="pre">{m.body}</p>}
                <RewardList r={m.attachments} />
                <div className="muted small">{timeAgo(m.createdAt, lang)}</div>
              </div>
              {m.claimed ? <span className="tag">{t('mailbox.claimed')}</span> : <button className="btn primary" disabled={busy} onClick={() => claim(m)}>{t('mailbox.claim')}</button>}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
