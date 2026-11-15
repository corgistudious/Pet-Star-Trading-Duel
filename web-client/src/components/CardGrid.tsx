import { useState } from 'react';
import { api, qs } from '../api/client';
import type { CardDto, CardPageDto } from '../api/types';
import { useT } from '../i18n';
import { RARITY_ORDER } from '../state/catalog';
import { CardView } from './CardView';
import { Empty, Loading, useLoad } from './ui';

const ELEMENTS = ['fire', 'water', 'nature', 'earth', 'wind', 'lightning', 'light', 'dark'];

/** Paged grid of the player's cards with server-side filters. Used by Pet Home and every card picker. */
export function CardGrid({ onPick, selectedIds, exclude, filterBusy, pageSize = 60 }: {
  onPick: (c: CardDto) => void; selectedIds?: number[]; exclude?: (c: CardDto) => boolean; filterBusy?: boolean; pageSize?: number;
}) {
  const t = useT();
  const [rarity, setRarity] = useState('');
  const [element, setElement] = useState('');
  const [sort, setSort] = useState('newest');
  const [fav, setFav] = useState(false);
  const [page, setPage] = useState(1);
  const { data, error, reload, loading } = useLoad(
    () => api.get<CardPageDto>('/api/cards' + qs({ rarity, element, sort, favorite: fav || undefined, page, pageSize })),
    [rarity, element, sort, fav, page],
  );
  const items = (data?.items ?? []).filter((c) => !(exclude?.(c)) && !(filterBusy && c.isBusy));
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  return (
    <div>
      <div className="filters">
        <select aria-label={t('filter.rarity')} value={rarity} onChange={(e) => { setRarity(e.target.value); setPage(1); }}>
          <option value="">{t('filter.rarity')}</option>
          {RARITY_ORDER.map((r) => <option key={r} value={r}>{r}</option>)}
        </select>
        <select aria-label={t('filter.element')} value={element} onChange={(e) => { setElement(e.target.value); setPage(1); }}>
          <option value="">{t('filter.element')}</option>
          {ELEMENTS.map((x) => <option key={x} value={x}>{t('element.' + x)}</option>)}
        </select>
        <select aria-label="sort" value={sort} onChange={(e) => setSort(e.target.value)}>
          {['newest', 'power', 'level', 'rarity', 'name'].map((x) => <option key={x} value={x}>{t('sort.' + x)}</option>)}
        </select>
        <label className="check"><input type="checkbox" checked={fav} onChange={(e) => { setFav(e.target.checked); setPage(1); }} />❤</label>
      </div>
      {!data ? <Loading error={error} onRetry={reload} /> : items.length === 0 ? <Empty>{t('home.empty')}</Empty> : (
        <>
          <div className={`card-grid ${loading ? 'dim' : ''}`}>
            {items.map((c) => <CardView key={c.id} card={c} compact selected={selectedIds?.includes(c.id)} onClick={() => onPick(c)}
              badge={(data.copiesByForm[c.formId] ?? 1) > 1 ? `×${data.copiesByForm[c.formId]}` : undefined} />)}
          </div>
          {pages > 1 && (
            <div className="pager">
              <button className="btn ghost" disabled={page <= 1} onClick={() => setPage(page - 1)}>‹</button>
              <span>{page}/{pages}</span>
              <button className="btn ghost" disabled={page >= pages} onClick={() => setPage(page + 1)}>›</button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
