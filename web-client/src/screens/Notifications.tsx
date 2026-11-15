import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { NotificationItem } from '../api/types';
import { useI18n } from '../i18n';
import { PageHead } from '../components/Layout';
import { Empty, Loading, timeAgo, useAction, useLoad } from '../components/ui';
import { useRealtimeEvent } from '../state/realtime';

export function notificationLink(n: NotificationItem): string | null {
  const d = n.data as Record<string, string | number | undefined>;
  if (d.auctionId) return `/auctions/${d.auctionId}`;
  if (d.listingId) return n.type === 'market_bought' ? '/home' : `/market/${d.listingId}`;
  if (d.postId) return `/feed/${d.postId}`;
  if (n.type === 'pvp_challenge') return '/duel';
  if (d.battleId) return `/battle/${d.battleId}`;
  if (n.type.startsWith('friend') || n.type === 'new_follower') return d.uid ? `/player/${d.uid}` : '/friends';
  if (n.type.startsWith('guild')) return '/guild';
  if (n.type === 'wishlist_match') return '/market';
  if (n.type === 'achievement_unlocked') return '/achievements';
  if (n.type === 'admin_notice') return '/mailbox';
  return null;
}

export default function Notifications() {
  const { t, lang } = useI18n();
  const nav = useNavigate();
  const { data, error, reload } = useLoad(() => api.get<{ items: NotificationItem[]; unreadCount: number }>('/api/notifications'), []);
  const { busy, run } = useAction();
  useRealtimeEvent('notification', () => void reload());
  const open = async (n: NotificationItem) => {
    if (!n.read) void api.post('/api/notifications/read', [n.id]).catch(() => {});
    const to = notificationLink(n);
    if (to) nav(to);
  };
  return (
    <div className="page">
      <PageHead title={t('notifications.title')} back="/" right={<button className="btn ghost" disabled={busy} onClick={() => run(() => api.post('/api/notifications/read', [])).then(() => reload())}>{t('notifications.mark_all')}</button>} />
      {!data ? <Loading error={error} onRetry={reload} /> : data.items.length === 0 ? <Empty>{t('notifications.empty')}</Empty> : (
        <ul className="list">
          {data.items.map((n) => (
            <li key={n.id} className={`list-row ${n.read ? '' : 'unread'}`} onClick={() => open(n)}>
              <div className="grow">{t('notif.' + n.type)}<div className="muted small">{timeAgo(n.createdAt, lang)}</div></div>
              {!n.read && <span className="dot" />}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
