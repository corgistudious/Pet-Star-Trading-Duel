import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { ApiError } from '../api/client';
import { useT } from '../i18n';
import { uiArt } from '../state/catalog';

// ---------- toasts ----------
interface Toast { id: number; text: string; kind: 'ok' | 'err' | 'info' }
const ToastCtx = createContext<(text: string, kind?: Toast['kind']) => void>(() => {});
export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<Toast[]>([]);
  const push = useCallback((text: string, kind: Toast['kind'] = 'info') => {
    const id = Date.now() + Math.random();
    setItems((x) => [...x.slice(-3), { id, text, kind }]);
    setTimeout(() => setItems((x) => x.filter((t) => t.id !== id)), 3800);
  }, []);
  return (
    <ToastCtx.Provider value={push}>
      {children}
      <div className="toasts" role="status" aria-live="polite">
        {items.map((t) => <div key={t.id} className={`toast toast-${t.kind}`}>{t.text}</div>)}
      </div>
    </ToastCtx.Provider>
  );
}
export const useToast = () => useContext(ToastCtx);

/** Maps an API error code to a localized message (error.<code>), falling back to a generic text. */
export function useErrorText() {
  const t = useT();
  return useCallback((e: unknown) => {
    if (e instanceof ApiError) {
      const k = 'error.' + e.code;
      const s = t(k);
      return s === k ? `${t('error.generic')} (${e.code})` : s;
    }
    return t('error.generic');
  }, [t]);
}

/** Runs an async action with a busy flag and toast on failure. Returns the result or undefined. */
export function useAction() {
  const toast = useToast();
  const errText = useErrorText();
  const [busy, setBusy] = useState(false);
  const run = useCallback(async <T,>(fn: () => Promise<T>, okText?: string): Promise<T | undefined> => {
    setBusy(true);
    try {
      const r = await fn();
      if (okText) toast(okText, 'ok');
      return r;
    } catch (e) {
      toast(errText(e), 'err');
      return undefined;
    } finally {
      setBusy(false);
    }
  }, [toast, errText]);
  return { busy, run };
}

/** Fetches data on mount / dependency change. */
export function useLoad<T>(fn: () => Promise<T>, deps: unknown[]) {
  const [data, setData] = useState<T | undefined>();
  const [error, setError] = useState<unknown>();
  const [loading, setLoading] = useState(true);
  const fnRef = useRef(fn);
  fnRef.current = fn;
  const reload = useCallback(async () => {
    setLoading(true);
    try { setData(await fnRef.current()); setError(undefined); } catch (e) { setError(e); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void reload(); }, deps); // eslint-disable-line react-hooks/exhaustive-deps
  return { data, setData, error, loading, reload };
}

export function Loading({ error, onRetry }: { error?: unknown; onRetry?: () => void }) {
  const errText = useErrorText();
  const t = useT();
  if (error) return <div className="empty"><p>{errText(error)}</p>{onRetry && <button className="btn" onClick={onRetry}>{t('web.retry')}</button>}</div>;
  return <div className="empty"><div className="spinner" /></div>;
}

export function Empty({ children }: { children: ReactNode }) { return <div className="empty">{children}</div>; }

export function Modal({ title, onClose, children, wide }: { title?: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  useEffect(() => {
    const k = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', k);
    return () => window.removeEventListener('keydown', k);
  }, [onClose]);
  return (
    <div className="modal-back" onClick={onClose}>
      <div className={`modal ${wide ? 'modal-wide' : ''}`} role="dialog" aria-modal="true" aria-label={title} onClick={(e) => e.stopPropagation()}>
        <div className="modal-head"><h3>{title}</h3><button className="icon-btn" aria-label="close" onClick={onClose}>✕</button></div>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  );
}

export function Tabs<T extends string>({ value, onChange, items }: { value: T; onChange: (v: T) => void; items: [T, string][] }) {
  return (
    <div className="tabs" role="tablist">
      {items.map(([v, label]) => (
        <button key={v} role="tab" aria-selected={v === value} className={v === value ? 'tab on' : 'tab'} onClick={() => onChange(v)}>{label}</button>
      ))}
    </div>
  );
}

const CURRENCY_ICON: Record<string, string> = { RAINBOW_STAR: 'ui_currency_rainbow_star', MINI_GREEN_BEAN: 'ui_currency_mini_green_bean' };
export function CurrencyIcon({ currency, size = 20 }: { currency: string; size?: number }) {
  const t = useT();
  return <img className="cur-icon" src={uiArt(CURRENCY_ICON[currency] ?? 'ui_currency_mini_green_bean')} width={size} height={size} alt={t('currency.' + currency.toLowerCase())} />;
}
export function Price({ currency, amount }: { currency: string; amount: number | null | undefined }) {
  return <span className="price"><CurrencyIcon currency={currency} size={18} />{amount == null ? '—' : fmt(amount)}</span>;
}
export const fmt = (n: number) => n.toLocaleString('en-US');

/** Countdown driven by server time: offset = serverTime - client clock at receipt, so wrong device clocks don't matter. */
export function useServerClock(serverTime?: string) {
  const [offset, setOffset] = useState(0);
  useEffect(() => { if (serverTime) setOffset(Date.parse(serverTime) - Date.now()); }, [serverTime]);
  return offset;
}
export function Countdown({ until, offset = 0, onEnd }: { until: string; offset?: number; onEnd?: () => void }) {
  const [now, setNow] = useState(() => Date.now() + offset);
  const ended = useRef(false);
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now() + offset), 500);
    return () => clearInterval(id);
  }, [offset]);
  const ms = Math.max(0, Date.parse(until) - now);
  useEffect(() => { if (ms === 0 && !ended.current) { ended.current = true; onEnd?.(); } }, [ms, onEnd]);
  return <span className={`countdown ${ms < 60_000 ? 'urgent' : ''}`}>{formatDuration(ms)}</span>;
}
export function formatDuration(ms: number): string {
  const s = Math.floor(ms / 1000);
  const d = Math.floor(s / 86400), h = Math.floor((s % 86400) / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${String(m).padStart(2, '0')}m`;
  return `${m}:${String(sec).padStart(2, '0')}`;
}

export function timeAgo(iso: string, lang: string): string {
  const s = Math.max(0, (Date.now() - Date.parse(iso)) / 1000);
  const rtf = new Intl.RelativeTimeFormat(lang, { numeric: 'auto' });
  if (s < 60) return rtf.format(-Math.floor(s), 'second');
  if (s < 3600) return rtf.format(-Math.floor(s / 60), 'minute');
  if (s < 86400) return rtf.format(-Math.floor(s / 3600), 'hour');
  return rtf.format(-Math.floor(s / 86400), 'day');
}

export function Confirm({ text, onYes, onNo, busy }: { text: ReactNode; onYes: () => void; onNo: () => void; busy?: boolean }) {
  const t = useT();
  return (
    <Modal title={t('web.confirm')} onClose={onNo}>
      <div className="confirm-text">{text}</div>
      <div className="row end gap">
        <button className="btn ghost" onClick={onNo}>{t('web.cancel')}</button>
        <button className="btn primary" disabled={busy} onClick={onYes}>{t('web.ok')}</button>
      </div>
    </Modal>
  );
}
