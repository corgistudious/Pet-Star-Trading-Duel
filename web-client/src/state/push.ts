// Web Push on the client. Permission is only ever requested from a user tap (required by iOS, polite elsewhere).
// iPhone/iPad: push works only for a web app added to the Home Screen (iOS/iPadOS 16.4+), never in a Safari tab.
import { api } from '../api/client';
import { isIos, isStandalone } from './pwa';

export type PushState = 'unsupported' | 'ios-install-first' | 'not-configured' | 'denied' | 'off' | 'on';
export interface PushConfig { enabled: boolean; publicKey: string | null; categories: string[] }

export function pushSupported(w: Window = window): boolean {
  return 'serviceWorker' in w.navigator && 'PushManager' in w && 'Notification' in w;
}

/** Decides what the settings screen can offer, before touching any permission. */
export function pushAvailability(opts: { supported: boolean; ios: boolean; standalone: boolean; configured: boolean; permission: NotificationPermission | 'n/a' }): PushState | 'ask' {
  if (opts.ios && !opts.standalone) return 'ios-install-first';
  if (!opts.supported) return 'unsupported';
  if (!opts.configured) return 'not-configured';
  if (opts.permission === 'denied') return 'denied';
  return 'ask';
}

export function urlB64ToUint8Array(b64: string): Uint8Array {
  const pad = '='.repeat((4 - (b64.length % 4)) % 4);
  const raw = atob((b64 + pad).replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from(raw, (c) => c.charCodeAt(0));
}

let configCache: Promise<PushConfig> | null = null;
export const pushConfig = () => (configCache ??= api.get<PushConfig>('/api/push/config', { auth: false }).catch((e) => { configCache = null; throw e; }));

async function registration(): Promise<ServiceWorkerRegistration | null> {
  if (!('serviceWorker' in navigator)) return null;
  return (await navigator.serviceWorker.getRegistration()) ?? null;
}

export async function currentState(): Promise<{ state: PushState; categories: string[] }> {
  const cfg = await pushConfig().catch(() => null);
  const a = pushAvailability({
    supported: pushSupported(), ios: isIos(), standalone: isStandalone(), configured: !!cfg?.enabled,
    permission: 'Notification' in window ? Notification.permission : 'n/a',
  });
  if (a !== 'ask') return { state: a, categories: [] };
  const sub = await (await registration())?.pushManager.getSubscription();
  if (!sub || Notification.permission !== 'granted') return { state: 'off', categories: [] };
  const st = await api.post<{ subscribed: boolean; categories: string[] }>('/api/push/subscriptions/status', { endpoint: sub.endpoint }).catch(() => null);
  return st?.subscribed ? { state: 'on', categories: st.categories } : { state: 'off', categories: [] };
}

/** Must be called from a click handler. */
export async function enablePush(categories?: string[]): Promise<PushState> {
  const cfg = await pushConfig();
  if (!cfg.enabled || !cfg.publicKey) return 'not-configured';
  const perm = await Notification.requestPermission();
  if (perm !== 'granted') return perm === 'denied' ? 'denied' : 'off';
  const reg = (await registration()) ?? (await navigator.serviceWorker.register('/sw.js'));
  await navigator.serviceWorker.ready;
  let sub = await reg.pushManager.getSubscription();
  // a subscription made with an older VAPID key cannot be reused
  if (sub && sub.options.applicationServerKey && btoa(String.fromCharCode(...new Uint8Array(sub.options.applicationServerKey))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '') !== cfg.publicKey) {
    await sub.unsubscribe(); sub = null;
  }
  sub ??= await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlB64ToUint8Array(cfg.publicKey) as BufferSource });
  await api.post('/api/push/subscriptions', { ...sub.toJSON(), categories });
  return 'on';
}

export async function disablePush(): Promise<void> {
  const sub = await (await registration())?.pushManager.getSubscription();
  if (!sub) return;
  await api.post('/api/push/subscriptions/delete', { endpoint: sub.endpoint }).catch(() => {});
  await sub.unsubscribe().catch(() => {});
}

export async function setCategories(categories: string[]): Promise<void> {
  const sub = await (await registration())?.pushManager.getSubscription();
  if (sub) await api.put('/api/push/subscriptions/categories', { endpoint: sub.endpoint, categories });
}

/** On app start: if this device already allowed push, re-register its (possibly rotated) subscription. */
export async function syncPush(): Promise<void> {
  if (!pushSupported() || Notification.permission !== 'granted') return;
  const cfg = await pushConfig().catch(() => null);
  if (!cfg?.enabled) return;
  const sub = await (await registration())?.pushManager.getSubscription();
  if (sub) await api.post('/api/push/subscriptions', sub.toJSON()).catch(() => {});
}
