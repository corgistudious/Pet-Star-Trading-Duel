import { useState } from 'react';
import { api } from '../api/client';
import type { ServerDto } from '../api/types';
import { useT } from '../i18n';
import { useSession } from '../state/session';
import { Loading, useAction, useLoad } from '../components/ui';
import { uiArt } from '../state/catalog';

export default function ServersScreen() {
  const t = useT();
  const { enterServer, logout, profile } = useSession();
  const { data, error, reload } = useLoad(() => api.get<ServerDto[]>('/api/servers'), []);
  const { busy, run } = useAction();
  const [nick, setNick] = useState('');
  const [joining, setJoining] = useState<ServerDto | null>(null);

  return (
    <div className="auth" style={{ backgroundImage: `url(${uiArt('bg_lobby_portrait')})` }}>
      <div className="auth-card wide">
        <h2>{t('server.title')}</h2>
        <p className="muted">{profile?.displayName} · {profile?.email}</p>
        {!data ? <Loading error={error} onRetry={reload} /> : data.length === 0 ? <p>{t('server.none')}</p> : (
          <ul className="server-list">
            {data.map((s) => (
              <li key={s.id} className={`server s-${s.status}`}>
                <div>
                  <b>{s.code}</b> · {s.name}
                  <div className="muted small">
                    {s.hasProgress ? t('server.your_progress', s.nickname ?? '', s.playerLevel ?? 1) : t('server.new_start')}
                    {' · '}{t('web.server_status_' + s.status)}
                  </div>
                </div>
                {s.hasProgress
                  ? <button className="btn primary" disabled={busy} onClick={() => run(() => enterServer(s.id))}>{t('server.continue')}</button>
                  : <button className="btn" disabled={busy || (!s.registrationOpen)} onClick={() => { setJoining(s); setNick(profile?.displayName ?? ''); }}>
                      {s.registrationOpen ? t('server.join') : t('server.registration_closed')}
                    </button>}
              </li>
            ))}
          </ul>
        )}
        {joining && (
          <form className="form" onSubmit={(e) => { e.preventDefault(); void run(() => enterServer(joining.id, nick)); }}>
            <label>{t('web.nickname_on', joining.code)}<input required minLength={3} maxLength={20} value={nick} onChange={(e) => setNick(e.target.value)} /></label>
            <button className="btn primary" disabled={busy}>{t('server.join')}</button>
          </form>
        )}
        <button className="link" onClick={() => void logout()}>{t('web.logout')}</button>
      </div>
    </div>
  );
}
