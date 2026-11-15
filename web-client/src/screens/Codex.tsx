import { useMemo, useState } from 'react';
import { api } from '../api/client';
import type { CodexDto } from '../api/types';
import { useI18n } from '../i18n';
import { PageHead } from '../components/Layout';
import { Loading, Modal, useLoad } from '../components/ui';
import { ELEMENT_COLORS, petArt, useCatalog } from '../state/catalog';
import { ELEMENT_GLYPH } from '../components/CardView';

export default function Codex() {
  const { t, lang } = useI18n();
  const cat = useCatalog();
  const { data, error, reload } = useLoad(() => api.get<CodexDto>('/api/codex'), []);
  const [element, setElement] = useState('');
  const [open, setOpen] = useState<string | null>(null);
  const entries = useMemo(() => (data?.entries ?? []).filter((e) => !element || cat.forms[e.formId]?.element === element), [data, element, cat]);
  if (!data) return <div className="page"><PageHead title={t('lobby.codex')} back="/" /><Loading error={error} onRetry={reload} /></div>;
  const f = open ? cat.forms[open] : null;
  return (
    <div className="page">
      <PageHead title={t('lobby.codex')} back="/" right={<span className="muted">{data.discovered}/{data.total} · {Math.round(data.completion)}%</span>} />
      <div className="progress"><div style={{ width: `${Math.min(100, data.completion)}%` }} /></div>
      <div className="chips">
        <button className={!element ? 'chip on' : 'chip'} onClick={() => setElement('')}>{t('filter.all')}</button>
        {Object.keys(ELEMENT_COLORS).map((e) => <button key={e} className={element === e ? 'chip on' : 'chip'} onClick={() => setElement(e)}>{ELEMENT_GLYPH[e]} {t('element.' + e)}</button>)}
      </div>
      <div className="codex-grid">
        {entries.map((e) => (
          <button key={e.formId} className={`codex-cell ${e.discovered ? '' : 'unknown'}`} onClick={() => e.discovered && setOpen(e.formId)} aria-label={e.discovered ? cat.forms[e.formId]?.name : '???'}>
            <img src={petArt(e.formId, true)} alt="" loading="lazy" width={96} height={96} />
            <span>{e.discovered ? cat.forms[e.formId]?.name : '???'}</span>
            {e.owned > 0 && <small>×{e.owned}</small>}
          </button>
        ))}
      </div>
      {f && open && (
        <Modal title={f.name} onClose={() => setOpen(null)}>
          <img className="codex-art" src={petArt(open)} alt="" width={256} height={256} />
          <p>{ELEMENT_GLYPH[f.element]} {t('element.' + f.element)} · {t('role.' + f.role)} · {t('card.stage', f.stage)}</p>
          <p className="lore">{f.lore[lang] ?? f.lore.en}</p>
          <p className="muted small">HP {f.stats.hp} · ATK {f.stats.atk} · DEF {f.stats.def} · SPD {f.stats.spd}</p>
        </Modal>
      )}
    </div>
  );
}
