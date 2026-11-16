// Session state: profile (account), selected server, player snapshot (balances are server-authoritative and
// refreshed after every economic action), plus helpers to apply balances returned by mutation responses.
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api, refreshSession, tokens } from '../api/client';
import type { AuthResponse, PlayerDto, ProfileDto } from '../api/types';
import { disablePush, syncPush } from './push';

type Status = 'loading' | 'anonymous' | 'account' | 'in_server' | 'unreachable';

interface Session {
  status: Status;
  profile: ProfileDto | null;
  player: PlayerDto | null;
  login: (a: AuthResponse) => Promise<void>;
  retry: () => void;
  logout: () => Promise<void>;
  enterServer: (id: number, nickname?: string) => Promise<void>;
  leaveServer: () => void;
  reloadPlayer: () => Promise<void>;
  applyCurrencies: (c?: Record<string, number> | null) => void;
  reloadProfile: () => Promise<void>;
  hasRole: (...roles: string[]) => boolean;
}

const Ctx = createContext<Session | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>('loading');
  const [profile, setProfile] = useState<ProfileDto | null>(null);
  const [player, setPlayer] = useState<PlayerDto | null>(null);

  const reloadPlayer = useCallback(async () => { setPlayer(await api.get<PlayerDto>('/api/player')); }, []);
  const reloadProfile = useCallback(async () => { setProfile(await api.get<ProfileDto>('/api/me')); }, []);

  const bootstrap = useCallback(async () => {
    try {
      if (!tokens.access && !(await refreshSession())) {
        // a rejected token is cleared by refreshSession; if one is still stored the server was unreachable
        setStatus(tokens.refresh ? 'unreachable' : 'anonymous');
        return;
      }
      setProfile(await api.get<ProfileDto>('/api/me'));
      if (tokens.serverId != null) {
        try {
          setPlayer(await api.get<PlayerDto>('/api/player'));
          setStatus('in_server');
          return;
        } catch { /* token not bound to a server (or server offline): pick again */ }
      }
      setStatus('account');
    } catch {
      setStatus(tokens.refresh ? 'account' : 'anonymous');
    }
  }, []);

  useEffect(() => { void bootstrap(); }, [bootstrap]);
  useEffect(() => { if (status === 'account' || status === 'in_server') void syncPush(); }, [status]);
  useEffect(() => {
    if (status !== 'unreachable') return;
    const again = () => { setStatus('loading'); void bootstrap(); };
    window.addEventListener('online', again);
    return () => window.removeEventListener('online', again);
  }, [status, bootstrap]);
  const retry = useCallback(() => { setStatus('loading'); void bootstrap(); }, [bootstrap]);
  useEffect(() => tokens.subscribe(() => { if (!tokens.access && !tokens.refresh) { setStatus('anonymous'); setPlayer(null); setProfile(null); } }), []);

  const login = useCallback(async (a: AuthResponse) => {
    tokens.serverId = null;
    tokens.apply(a);
    setProfile(await api.get<ProfileDto>('/api/me'));
    setStatus('account');
  }, []);

  const logout = useCallback(async () => {
    await disablePush().catch(() => {}); // this device must not keep receiving the previous player's alerts
    try { await api.post('/api/auth/logout'); } catch { /* already gone */ }
    tokens.clear();
    setPlayer(null); setProfile(null); setStatus('anonymous');
  }, []);

  const enterServer = useCallback(async (id: number, nickname?: string) => {
    const a = await api.post<AuthResponse>(`/api/servers/${id}/enter`, nickname ? { nickname } : {});
    tokens.apply({ ...a, refreshToken: a.refreshToken || '' });
    tokens.serverId = id;
    setPlayer(await api.get<PlayerDto>('/api/player'));
    setStatus('in_server');
  }, []);

  const leaveServer = useCallback(() => { tokens.serverId = null; setPlayer(null); setStatus('account'); }, []);

  const applyCurrencies = useCallback((c?: Record<string, number> | null) => {
    if (c) setPlayer((p) => (p ? { ...p, currencies: { ...p.currencies, ...c } } : p));
  }, []);

  const hasRole = useCallback((...roles: string[]) => !!profile?.roles.some((r) => roles.includes(r)), [profile]);

  const value = useMemo<Session>(() => ({ status, profile, player, login, retry, logout, enterServer, leaveServer, reloadPlayer, applyCurrencies, reloadProfile, hasRole }),
    [status, profile, player, login, retry, logout, enterServer, leaveServer, reloadPlayer, applyCurrencies, reloadProfile, hasRole]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useSession(): Session {
  const s = useContext(Ctx);
  if (!s) throw new Error('no session');
  return s;
}
