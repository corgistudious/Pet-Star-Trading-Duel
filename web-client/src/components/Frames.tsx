import { useEffect, useRef, type ReactNode } from 'react';
import { useI18n } from '../i18n';
import { usePwa } from '../state/pwa';
import { overlayFor, tryLockOrientation, unlockOrientation, useViewport } from '../state/viewport';

/** Pure-black rotate prompt: no logo, header, footer, artwork or gradient behind it, and it swallows every input. */
export function RotateOverlay({ to }: { to: 'landscape' | 'portrait' }) {
  const { lang } = useI18n();
  const vi = to === 'landscape' ? 'Xoay ngang điện thoại để vào trận đấu' : 'Xoay dọc điện thoại để tiếp tục';
  const en = to === 'landscape' ? 'Rotate your phone to landscape to play the duel' : 'Rotate your phone to portrait to continue';
  const stop = (e: { preventDefault: () => void; stopPropagation: () => void }) => { e.stopPropagation(); };
  return (
    <div className={`rotate-overlay to-${to}`} role="alertdialog" aria-modal="true" aria-live="assertive" data-testid="rotate-overlay"
      onPointerDown={stop} onPointerUp={stop} onClick={stop} onTouchMove={(e) => e.preventDefault()}>
      <svg className="rotate-phone" viewBox="0 0 64 64" width="88" height="88" aria-hidden="true">
        <rect x="20" y="6" width="24" height="44" rx="5" fill="none" stroke="#ffffff" strokeWidth="3" />
        <circle cx="32" cy="44" r="2" fill="#ffffff" />
        <path d="M50 30 a18 18 0 0 1 -6 14" fill="none" stroke="#ffffff" strokeWidth="2.5" strokeLinecap="round" />
        <path d="M41 42 l3 3 l3 -4" fill="none" stroke="#ffffff" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
      <p className="rotate-primary">{lang === 'vi' ? vi : en}</p>
      <p className="rotate-secondary">{lang === 'vi' ? en : vi}</p>
    </div>
  );
}

/** `inert` (no focus, no clicks, hidden from assistive tech) on the content behind an overlay. */
function useInert(on: boolean) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (on) { el.setAttribute('inert', ''); el.setAttribute('aria-hidden', 'true'); }
    else { el.removeAttribute('inert'); el.removeAttribute('aria-hidden'); }
  }, [on]);
  return ref;
}

function Banners() {
  const { t } = useI18n();
  const { online, updateReady, applyUpdate } = usePwa();
  return (
    <>
      {!online && <div className="sys-banner offline" role="status">{t('web.offline_banner')}</div>}
      {updateReady && <div className="sys-banner update" role="status">{t('web.update_ready')} <button className="btn small" onClick={applyUpdate}>{t('web.reload')}</button></div>}
    </>
  );
}

/** Portrait 9:16 frame for every screen outside a duel. Phones: full width; wider screens: centered column on black. */
export function PortraitFrame({ children }: { children: ReactNode }) {
  const v = useViewport();
  const overlay = overlayFor('portrait', v);
  const ref = useInert(!!overlay);
  useEffect(() => { document.documentElement.dataset.mode = 'portrait'; }, []);
  return (
    <div className="frame-root portrait" data-testid="portrait-frame">
      <div className="portrait-app" ref={ref}>
        <Banners />
        {children}
      </div>
      {overlay && <RotateOverlay to="portrait" />}
    </div>
  );
}

/** Landscape 16:9 stage for PvE / PvP. Letterboxed on black, inside the safe area; rotate overlay on portrait phones. */
export function BattleFrame({ children }: { children: ReactNode }) {
  const v = useViewport();
  const overlay = overlayFor('landscape', v);
  const ref = useInert(!!overlay);
  useEffect(() => {
    document.documentElement.dataset.mode = 'battle';
    void tryLockOrientation('landscape');
    return () => {
      // leaving the duel: back to the portrait rules, release any lock (installed PWA falls back to its manifest: portrait)
      unlockOrientation();
      document.documentElement.dataset.mode = 'portrait';
    };
  }, []);
  return (
    <div className="frame-root battle-root" data-testid="battle-frame">
      <div className="battle-stage" data-testid="battle-stage" ref={ref}>
        {children}
      </div>
      {overlay && <RotateOverlay to="landscape" />}
    </div>
  );
}
