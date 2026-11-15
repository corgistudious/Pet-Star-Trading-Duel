import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { PackOpeningDto } from '../api/types';
import { useT } from '../i18n';
import { useSession } from '../state/session';
import { useLoad } from '../components/ui';
import { claimable, type MissionsDto } from './Missions';
import { uiArt } from '../state/catalog';

export default function LobbyScreen() {
  const t = useT();
  const nav = useNavigate();
  const { player, hasRole } = useSession();
  const pending = useLoad(() => api.get<PackOpeningDto[]>('/api/packs/openings?unacknowledged=true&limit=1'), []);
  const missions = useLoad(() => api.get<MissionsDto>('/api/missions'), []);
  const login = useLoad(() => api.get<{ claimedToday: boolean; streakDay: number }>('/api/rewards/login'), []);
  const packs = Object.values(player?.packs ?? {}).reduce((a, b) => a + b, 0);
  const staff = hasRole('owner', 'admin', 'gm', 'moderator');

  const tiles: [string, string, string, string?][] = [
    ['/shop', '🛍️', t('lobby.shop')],
    ['/home', '🐾', t('lobby.pet_home'), `${player?.cardCount ?? 0}`],
    ['/codex', '📖', t('lobby.codex'), `${player?.codexDiscovered ?? 0}/${player?.codexTotal ?? 0}`],
    ['/decks', '🃏', t('web.decks')],
    ['/duel', '⚔️', t('lobby.duel')],
    ['/market', '🏪', t('hub.market')],
    ['/auctions', '🔨', t('web.auctions')],
    ['/feed', '📰', t('web.feed')],
    ['/guild', '🏰', t('web.guild')],
    ['/chat', '💬', t('web.chat')],
    ['/friends', '👥', t('web.friends')],
    ['/missions', '🎯', t('web.missions'), claimable(missions.data) > 0 ? String(claimable(missions.data)) : undefined],
    ['/rewards', '🎁', t('web.rewards'), login.data && !login.data.claimedToday ? '!' : undefined],
    ['/mailbox', '✉️', t('lobby.mailbox')],
    ['/albums', '🗂️', t('web.albums')],
    ['/achievements', '🏆', t('achievements.title')],
    ['/settings', '⚙️', t('lobby.settings')],
  ];
  if (staff) tiles.push(['/admin', '🛡️', t('web.admin')]);

  return (
    <div className="lobby" style={{ backgroundImage: `url(${uiArt('bg_lobby_portrait')})` }}>
      {pending.data && pending.data.length > 0 && (
        <button className="banner" onClick={() => nav(`/open/${pending.data![0].openingId}`)}>{t('web.resume_opening')}</button>
      )}
      <div className="lobby-hero">
        <div>
          <div className="muted small">{player?.serverCode}</div>
          <h2>{player?.nickname}</h2>
          <div className="muted">{t('lobby.level', player?.level ?? 1)}</div>
        </div>
        <button className="btn primary big" onClick={() => nav(packs > 0 ? '/open' : '/shop')}>
          {packs > 0 ? t('lobby.open_pack', packs) : t('lobby.go_shop')}
        </button>
      </div>
      <div className="tile-grid">
        {tiles.map(([to, icon, label, badge]) => (
          <button key={to} className="tile" onClick={() => nav(to)}>
            <span className="ti">{icon}</span><span className="tl">{label}</span>{badge && <span className="tb">{badge}</span>}
          </button>
        ))}
      </div>
    </div>
  );
}
