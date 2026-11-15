// Thin fetch wrapper around the PetStar REST API.
// - Access token in memory, refresh token in localStorage (rotated on every refresh; the server revokes the
//   whole session family if a rotated token is ever replayed).
// - Refresh is single-flight within a tab and serialized across tabs with the Web Locks API when available,
//   so two tabs never present the same refresh token.
// - Mutations that the server runs exactly-once take an Idempotency-Key; callers keep the same key when retrying.
import type { AuthResponse } from './types';

export const API_BASE: string = (import.meta.env.VITE_API_BASE as string | undefined)?.replace(/\/$/, '') ?? '';

const LS_REFRESH = 'petstar.refresh';
const LS_SERVER = 'petstar.server';

export class ApiError extends Error {
  constructor(public status: number, public code: string, public detail?: string | null) {
    super(code);
  }
}

type Listener = () => void;

class TokenStore {
  access: string | null = null;
  accessExp = 0;
  uid = 0;
  private listeners = new Set<Listener>();

  get refresh(): string | null { return safeGet(LS_REFRESH); }
  get serverId(): number | null { const v = safeGet(LS_SERVER); return v ? Number(v) : null; }
  set serverId(v: number | null) { v == null ? safeDel(LS_SERVER) : safeSet(LS_SERVER, String(v)); }

  apply(a: AuthResponse) {
    this.access = a.accessToken;
    this.accessExp = Date.parse(a.accessTokenExpiresAt);
    this.uid = a.uid;
    if (a.refreshToken) safeSet(LS_REFRESH, a.refreshToken);
    if (a.serverId != null) this.serverId = a.serverId;
    this.emit();
  }

  clear() {
    this.access = null; this.accessExp = 0; this.uid = 0;
    safeDel(LS_REFRESH); safeDel(LS_SERVER);
    this.emit();
  }

  subscribe(l: Listener) { this.listeners.add(l); return () => { this.listeners.delete(l); }; }
  private emit() { this.listeners.forEach((l) => l()); }
}

function safeGet(k: string) { try { return localStorage.getItem(k); } catch { return null; } }
function safeSet(k: string, v: string) { try { localStorage.setItem(k, v); } catch { /* private mode */ } }
function safeDel(k: string) { try { localStorage.removeItem(k); } catch { /* ignore */ } }

export const tokens = new TokenStore();

let refreshing: Promise<boolean> | null = null;

/** Rotates the refresh token. Resolves false when the session is gone (caller should show login). */
export function refreshSession(): Promise<boolean> {
  if (!refreshing) {
    refreshing = withLock(async () => {
      const rt = tokens.refresh;
      if (!rt) return false;
      try {
        const headers: Record<string, string> = { 'Content-Type': 'application/json' };
        if (tokens.serverId != null) headers['X-Server-Id'] = String(tokens.serverId);
        const r = await fetch(API_BASE + '/api/auth/refresh', { method: 'POST', headers, body: JSON.stringify({ refreshToken: rt }) });
        if (r.status === 401 || r.status === 403) {
          // another tab may have rotated it a moment ago: accept its fresh token instead of logging out
          if (tokens.refresh && tokens.refresh !== rt) return refreshSessionInner();
          tokens.clear();
          return false;
        }
        if (!r.ok) return false;
        tokens.apply(await r.json());
        return true;
      } catch {
        return false;
      }
    }).finally(() => { refreshing = null; });
  }
  return refreshing;
}

async function refreshSessionInner(): Promise<boolean> {
  const r = await fetch(API_BASE + '/api/auth/refresh', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(tokens.serverId != null ? { 'X-Server-Id': String(tokens.serverId) } : {}) },
    body: JSON.stringify({ refreshToken: tokens.refresh }),
  });
  if (!r.ok) { tokens.clear(); return false; }
  tokens.apply(await r.json());
  return true;
}

async function withLock<T>(fn: () => Promise<T>): Promise<T> {
  const locks = (navigator as Navigator & { locks?: { request: (n: string, cb: () => Promise<T>) => Promise<T> } }).locks;
  return locks ? locks.request('petstar-refresh', fn) : fn();
}

export function newIdempotencyKey(): string {
  const b = new Uint8Array(16);
  crypto.getRandomValues(b);
  return 'w' + Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('');
}

export interface RequestOpts { idem?: string; auth?: boolean; signal?: AbortSignal }

export async function request<T>(method: string, path: string, body?: unknown, opts: RequestOpts = {}): Promise<T> {
  const auth = opts.auth !== false;
  if (auth && tokens.access && tokens.accessExp - Date.now() < 30_000) await refreshSession();
  const send = () => {
    const headers: Record<string, string> = {};
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (auth && tokens.access) headers['Authorization'] = 'Bearer ' + tokens.access;
    if (opts.idem) headers['Idempotency-Key'] = opts.idem;
    return fetch(API_BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: opts.signal });
  };
  let r: Response;
  try {
    r = await send();
    if (r.status === 401 && auth && tokens.refresh) {
      if (await refreshSession()) r = await send();
    }
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw e;
    throw new ApiError(0, 'network');
  }
  if (!r.ok) {
    let code = r.status === 429 ? 'rate_limited' : 'generic';
    let detail: string | null = null;
    try { const j = await r.json(); code = j.code ?? code; detail = j.detail ?? null; } catch { /* non-json */ }
    throw new ApiError(r.status, code, detail);
  }
  if (r.status === 204) return undefined as T;
  const text = await r.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  get: <T>(p: string, o?: RequestOpts) => request<T>('GET', p, undefined, o),
  post: <T>(p: string, b?: unknown, o?: RequestOpts) => request<T>('POST', p, b ?? {}, o),
  put: <T>(p: string, b?: unknown, o?: RequestOpts) => request<T>('PUT', p, b ?? {}, o),
  patch: <T>(p: string, b?: unknown, o?: RequestOpts) => request<T>('PATCH', p, b ?? {}, o),
  del: <T>(p: string, o?: RequestOpts) => request<T>('DELETE', p, undefined, o),
};

/** Builds a query string, skipping empty values. */
export function qs(params: Record<string, string | number | boolean | null | undefined>): string {
  const u = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') u.set(k, String(v));
  const s = u.toString();
  return s ? '?' + s : '';
}
