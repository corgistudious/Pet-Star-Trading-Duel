import { useEffect, useState, type ReactNode } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useT } from '../i18n';
import { useRealtime, useRealtimeEvent } from '../state/realtime';
import { useSession } from '../state/session';
import { CurrencyIcon, fmt, useToast } from './ui';
import { CURRENCIES } from '../api/types';

export function CurrencyBar() {
  const { player } = useSession();
  const t = useT();
  if (!player) return null;
  return (
    <div className="currency-bar" aria-label={t('web.balances')}>
      {CURRENCIES.map((c) => (
        <span key={c} className="cur" data-testid={`bal-${c}`} title={t('currency.' + c.toLowerCase())}>
          <CurrencyIcon currency={c} size={22} />
          <b>{fmt(player.currencies[c] ?? 0)}</b>
        </span>
      ))}
    </div>
  );
}

const NAV: [string, string, string][] = [
  ['/', '🏠', 'hub.home'],
  ['/home', '🐾', 'home.title'],
  ['/duel', '⚔️', 'hub.battle'],
  ['/market', '🏪', 'hub.market'],
  ['/feed', '💬', 'hub.social'],
];

export function Layout({ children }: { children: ReactNode }) {
  const t = useT();
  const nav = useNavigate();
  const loc = useLocation();
  const { player, reloadPlayer } = useSession();
  const { connected, generation } = useRealtime();
  const toast = useToast();
  const [unread, setUnread] = useState(0);

  const loadUnread = () => api.get<{ items: unknown[]; unreadCount?: number }>('/api/notifications?unread=true')
    .then((r) => setUnread(r.unreadCount ?? r.items.length)).catch(() => {});
  useEffect(() => { void loadUnread(); }, [generation, loc.pathname === '/notifications']); // eslint-disable-line react-hooks/exhaustive-deps

  useRealtimeEvent<{ type: string }>('notification', (n) => {
    setUnread((u) => u + 1);
    toast(t('notif.' + n.type) !== 'notif.' + n.type ? t('notif.' + n.type) : t('web.new_notification'), 'info');
    if (/^(market_sold|auction_|mail|achievement)/.test(n.type)) void reloadPlayer();
  });
  useRealtimeEvent<{ battleId: string }>('pvp_start', (d) => nav(`/pvp/${d.battleId}`));

  const full = loc.pathname.startsWith('/open') || loc.pathname.startsWith('/battle/') || loc.pathname.startsWith('/pvp/');
  return (
    <div className={`app ${full ? 'fullscreen' : ''}`}>
      {!full && (
        <header className="topbar">
          <button className="avatar-btn" onClick={() => nav('/settings')} aria-label={t('lobby.settings')}>
            <span className="lv">{player ? t('lobby.level', player.level) : ''}</span>
            <span className="nick">{player?.nickname}</span>
          </button>
          <CurrencyBar />
          <button className="icon-btn bell" onClick={() => nav('/notifications')} aria-label={t('lobby.notifications')}>
            🔔{unread > 0 && <span className="dot">{unread > 99 ? '99+' : unread}</span>}
          </button>
          <span className={`live-dot ${connected ? 'on' : ''}`} title={connected ? t('web.online') : t('web.offline')} />
        </header>
      )}
      <main className="content">{children}</main>
      {!full && (
        <nav className="bottomnav">
          {NAV.map(([to, icon, key]) => (
            <NavLink key={to} to={to} end={to === '/'} className={({ isActive }) => (isActive ? 'on' : '')}>
              <span className="ni">{icon}</span><span className="nl">{t(key)}</span>
            </NavLink>
          ))}
        </nav>
      )}
    </div>
  );
}

export function PageHead({ title, back, right }: { title: string; back?: string | true; right?: ReactNode }) {
  const nav = useNavigate();
  return (
    <div className="pagehead">
      {back && <button className="icon-btn" aria-label="back" onClick={() => (back === true ? nav(-1) : nav(back))}>‹</button>}
      <h2>{title}</h2>
      <div className="ph-right">{right}</div>
    </div>
  );
}
