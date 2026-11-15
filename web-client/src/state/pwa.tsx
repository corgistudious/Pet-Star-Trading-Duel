// PWA runtime: service-worker registration with an explicit "new version" prompt (never reloads mid-duel on
// its own), install prompt (Chromium `beforeinstallprompt`; iOS Safari needs Share → Add to Home Screen),
// standalone detection and online/offline state. The game always needs the server; offline only shows the shell.
import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';

interface BeforeInstallPromptEvent extends Event { prompt: () => Promise<void>; userChoice: Promise<{ outcome: string }> }

export function isStandalone(w: Window = window): boolean {
  return w.matchMedia?.('(display-mode: standalone)').matches || (w.navigator as Navigator & { standalone?: boolean }).standalone === true;
}
export function isIos(ua = navigator.userAgent, maxTouch = navigator.maxTouchPoints): boolean {
  return /iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && maxTouch > 1); // iPadOS reports as Mac
}

interface Pwa {
  online: boolean; standalone: boolean; ios: boolean;
  updateReady: boolean; applyUpdate: () => void;
  canInstall: boolean; install: () => Promise<void>;
}
const Ctx = createContext<Pwa>({ online: true, standalone: false, ios: false, updateReady: false, applyUpdate: () => {}, canInstall: false, install: async () => {} });

export function PwaProvider({ children }: { children: ReactNode }) {
  const [online, setOnline] = useState(() => navigator.onLine);
  const [waiting, setWaiting] = useState<ServiceWorker | null>(null);
  const [installEvt, setInstallEvt] = useState<BeforeInstallPromptEvent | null>(null);
  const [standalone, setStandalone] = useState(() => isStandalone());
  const userAskedReload = useRef(false);

  useEffect(() => {
    const on = () => setOnline(true), off = () => setOnline(false);
    window.addEventListener('online', on); window.addEventListener('offline', off);
    const bip = (e: Event) => { e.preventDefault(); setInstallEvt(e as BeforeInstallPromptEvent); };
    window.addEventListener('beforeinstallprompt', bip);
    const installed = () => { setInstallEvt(null); setStandalone(true); };
    window.addEventListener('appinstalled', installed);
    return () => {
      window.removeEventListener('online', on); window.removeEventListener('offline', off);
      window.removeEventListener('beforeinstallprompt', bip); window.removeEventListener('appinstalled', installed);
    };
  }, []);

  useEffect(() => {
    if (!import.meta.env.PROD || !('serviceWorker' in navigator)) return;
    // Reload ONLY after the player tapped "Reload" on the update banner. The first install also fires
    // controllerchange (clients.claim) and must never wipe a form or interrupt a duel.
    let reloading = false;
    navigator.serviceWorker.addEventListener('controllerchange', () => {
      if (userAskedReload.current && !reloading) { reloading = true; location.reload(); }
    });
    navigator.serviceWorker.register('/sw.js').then((reg) => {
      if (reg.waiting && navigator.serviceWorker.controller) setWaiting(reg.waiting);
      reg.addEventListener('updatefound', () => {
        const sw = reg.installing;
        sw?.addEventListener('statechange', () => { if (sw.state === 'installed' && navigator.serviceWorker.controller) setWaiting(sw); });
      });
      const check = () => document.visibilityState === 'visible' && reg.update().catch(() => {});
      document.addEventListener('visibilitychange', check);
    }).catch(() => { /* SW unsupported or blocked (private mode): the site still works */ });
  }, []);

  const applyUpdate = useCallback(() => { userAskedReload.current = true; waiting?.postMessage({ type: 'SKIP_WAITING' }); }, [waiting]);
  const install = useCallback(async () => {
    if (!installEvt) return;
    await installEvt.prompt();
    await installEvt.userChoice.catch(() => null);
    setInstallEvt(null);
  }, [installEvt]);

  return <Ctx.Provider value={{ online, standalone, ios: isIos(), updateReady: !!waiting, applyUpdate, canInstall: !!installEvt, install }}>{children}</Ctx.Provider>;
}

export const usePwa = () => useContext(Ctx);
