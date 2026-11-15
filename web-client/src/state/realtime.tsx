// Realtime push over WebSocket. The socket authenticates with a single-use 60 s ticket fetched over HTTPS
// (no token in the URL that could land in logs). Reconnects with capped exponential backoff + jitter, and
// pings every 20 s so mobile Safari/NAT idle timeouts don't silently drop it. The server is still the source
// of truth: screens refetch on reconnect instead of trusting missed pushes.
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { API_BASE, api } from '../api/client';
import type { RealtimeMessage } from '../api/types';
import { useSession } from './session';

type Handler = (m: RealtimeMessage) => void;

class Bus {
  private handlers = new Map<string, Set<Handler>>();
  on(type: string, h: Handler) {
    if (!this.handlers.has(type)) this.handlers.set(type, new Set());
    this.handlers.get(type)!.add(h);
    return () => { this.handlers.get(type)?.delete(h); };
  }
  emit(m: RealtimeMessage) {
    this.handlers.get(m.type)?.forEach((h) => h(m));
    this.handlers.get('*')?.forEach((h) => h(m));
  }
}

export function wsUrl(base: string, ticket: string, loc: { protocol: string; host: string } = window.location): string {
  const origin = base || `${loc.protocol}//${loc.host}`;
  return origin.replace(/^http/, 'ws') + '/ws?ticket=' + encodeURIComponent(ticket);
}

export function backoff(attempt: number, rnd = Math.random): number {
  return Math.min(30_000, 1000 * 2 ** Math.min(attempt, 5)) * (0.75 + rnd() * 0.5);
}

interface Realtime { bus: Bus; connected: boolean; generation: number }
const Ctx = createContext<Realtime | null>(null);

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { status, player } = useSession();
  const busRef = useRef(new Bus());
  const [connected, setConnected] = useState(false);
  const [generation, setGeneration] = useState(0);

  useEffect(() => {
    if (status !== 'in_server' || !player) return;
    let ws: WebSocket | null = null;
    let stopped = false;
    let attempt = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let ping: ReturnType<typeof setInterval> | undefined;

    const connect = async () => {
      if (stopped) return;
      try {
        const { ticket } = await api.post<{ ticket: string }>('/api/realtime/ticket');
        if (stopped) return;
        ws = new WebSocket(wsUrl(API_BASE, ticket));
        ws.onopen = () => {
          attempt = 0;
          setConnected(true);
          setGeneration((g) => g + 1);
          ping = setInterval(() => ws?.readyState === WebSocket.OPEN && ws.send('ping'), 20_000);
        };
        ws.onmessage = (e) => {
          if (typeof e.data !== 'string' || e.data === 'pong') return;
          try { busRef.current.emit(JSON.parse(e.data)); } catch { /* ignore malformed */ }
        };
        ws.onclose = () => { setConnected(false); clearInterval(ping); schedule(); };
      } catch {
        schedule();
      }
    };
    const schedule = () => { if (!stopped) timer = setTimeout(connect, backoff(attempt++)); };
    const onVisible = () => { if (document.visibilityState === 'visible' && (!ws || ws.readyState > 1)) { clearTimeout(timer); attempt = 0; void connect(); } };
    document.addEventListener('visibilitychange', onVisible);
    void connect();
    return () => {
      stopped = true;
      clearTimeout(timer); clearInterval(ping);
      document.removeEventListener('visibilitychange', onVisible);
      ws?.close();
      setConnected(false);
    };
  }, [status, player?.serverId, player?.uid]); // eslint-disable-line react-hooks/exhaustive-deps

  return <Ctx.Provider value={{ bus: busRef.current, connected, generation }}>{children}</Ctx.Provider>;
}

export function useRealtime() {
  const c = useContext(Ctx);
  if (!c) throw new Error('no realtime');
  return c;
}

/** Subscribes to a realtime message type for the component's lifetime. */
export function useRealtimeEvent<T = unknown>(type: string, handler: (data: T, m: RealtimeMessage<T>) => void) {
  const { bus } = useRealtime();
  const ref = useRef(handler);
  ref.current = handler;
  useEffect(() => bus.on(type, (m) => ref.current(m.data as T, m as RealtimeMessage<T>)), [bus, type]);
}
