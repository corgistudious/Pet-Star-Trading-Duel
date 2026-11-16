import { useEffect, useState } from 'react';
import { api } from '../api/client';
import { useT } from '../i18n';
import { currentState, disablePush, enablePush, pushConfig, setCategories, type PushState } from '../state/push';
import { useAction, useToast } from './ui';

export function PushSettings() {
  const t = useT();
  const toast = useToast();
  const { busy, run } = useAction();
  const [state, setState] = useState<PushState | 'loading'>('loading');
  const [cats, setCats] = useState<string[]>([]);
  const [all, setAll] = useState<string[]>([]);
  const refresh = async () => {
    const s = await currentState().catch(() => ({ state: 'unsupported' as PushState, categories: [] }));
    setState(s.state); setCats(s.categories);
    pushConfig().then((c) => setAll(c.categories)).catch(() => {});
  };
  useEffect(() => { void refresh(); }, []);

  const toggle = async () => {
    if (state === 'on') { await run(disablePush); setState('off'); return; }
    const r = await run(() => enablePush());
    if (r) { setState(r); if (r === 'on') { toast(t('web.push_enabled'), 'ok'); void refresh(); } }
  };
  const flip = async (c: string) => {
    const next = cats.includes(c) ? cats.filter((x) => x !== c) : [...cats, c];
    setCats(next);
    await run(() => setCategories(next));
  };

  return (
    <div role="group" aria-labelledby="push-label" className="push-settings" data-testid="push-settings">
      <span id="push-label" className="muted small">{t('web.push_title')}</span>
      {state === 'loading' ? null
        : state === 'ios-install-first' ? <p className="small muted">{t('web.push_ios_install')}</p>
        : state === 'unsupported' ? <p className="small muted">{t('web.push_unsupported')}</p>
        : state === 'not-configured' ? <p className="small muted">{t('web.push_not_configured')}</p>
        : state === 'denied' ? <p className="small muted">{t('web.push_denied')}</p>
        : (
          <>
            <label className="check"><input type="checkbox" role="switch" checked={state === 'on'} disabled={busy} onChange={toggle} data-testid="push-toggle" /> {t('web.push_toggle')}</label>
            {state === 'on' && (
              <>
                <div className="chips">
                  {all.map((c) => (
                    <button key={c} className={cats.includes(c) ? 'chip on' : 'chip'} aria-pressed={cats.includes(c)} disabled={busy} onClick={() => flip(c)}>{t('web.push_cat_' + c)}</button>
                  ))}
                </div>
                <button className="btn ghost small" disabled={busy} onClick={() => run(() => api.post('/api/push/test'), t('web.push_test_sent'))}>{t('web.push_test')}</button>
              </>
            )}
            <p className="small muted">{t('web.push_note')}</p>
          </>
        )}
    </div>
  );
}
