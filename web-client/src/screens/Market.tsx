import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, qs } from '../api/client';
import type { ListingView } from '../api/types';
import { useI18n } from '../i18n';
import { PageHead } from '../components/Layout';
import { CardView } from '../components/CardView';
import { Empty, Loading, Price, Tabs, fmt, timeAgo, useAction, useLoad, useToast } from '../components/ui';
import { RARITY_ORDER, useCatalog } from '../state/catalog';

type Section = 'all' | 'rare' | 'recommended' | 'wishlist' | 'mine' | 'history' | 'wishlist_edit';

export default function Market() {
  const { t } = useI18n();
  const nav = useNavigate();
  const [section, setSection] = useState<Section>('all');
  return (
    <div className="page">
      <PageHead title={t('market.title')} right={<button className="btn ghost" onClick={() => nav('/auctions')}>{t('market.auctions')}</button>} />
      <p className="notice">{t('market.in_game_only')}</p>
      <Tabs value={section} onChange={setSection} items={[['all', t('filter.all')], ['rare', t('market.rare')], ['recommended', t('market.recommended')],
        ['wishlist', t('market.wishlist_matches')], ['mine', t('market.mine')], ['history', t('market.history')], ['wishlist_edit', t('wishlist.entries')]]} />
      {section === 'history' ? <History /> : section === 'wishlist_edit' ? <WishlistEditor /> : <Browse section={section} />}
    </div>
  );
}

function Browse({ section }: { section: Section }) {
  const { t } = useI18n();
  const nav = useNavigate();
  const [q, setQ] = useState('');
  const [query, setQuery] = useState('');
  const [rarity, setRarity] = useState('');
  const [currency, setCurrency] = useState('');
  const [sort, setSort] = useState('newest');
  const [page, setPage] = useState(1);
  const isSerial = /^[A-Z0-9]{12}$/i.test(query.trim());
  const { data, error, reload } = useLoad(() => api.get<{ total: number; pageSize: number; items: ListingView[] }>('/api/market/listings' + qs({
    section: section === 'all' ? undefined : section, q: isSerial ? undefined : query, serial: isSerial ? query.trim() : undefined, rarity, currency, sort, page,
  })), [section, query, rarity, currency, sort, page]);
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  return (
    <div>
      <form className="filters" onSubmit={(e) => { e.preventDefault(); setQuery(q); setPage(1); }}>
        <input type="search" placeholder={t('market.search_hint')} value={q} onChange={(e) => setQ(e.target.value)} aria-label={t('market.search')} />
        <select value={rarity} onChange={(e) => { setRarity(e.target.value); setPage(1); }} aria-label={t('filter.rarity')}>
          <option value="">{t('filter.rarity')}</option>{RARITY_ORDER.map((r) => <option key={r}>{r}</option>)}
        </select>
        <select value={currency} onChange={(e) => { setCurrency(e.target.value); setPage(1); }} aria-label="currency">
          <option value="">{t('market.any_currency')}</option>
          <option value="MINI_GREEN_BEAN">{t('currency.mini_green_bean')}</option>
          <option value="RAINBOW_STAR">{t('currency.rainbow_star')}</option>
        </select>
        <select value={sort} onChange={(e) => setSort(e.target.value)} aria-label={t('filter.sort')}>
          {['newest', 'price_asc', 'price_desc', 'popular', 'rarity'].map((s) => <option key={s} value={s}>{t('web.sort_' + s)}</option>)}
        </select>
      </form>
      {!data ? <Loading error={error} onRetry={reload} /> : data.items.length === 0 ? <Empty>{t('market.empty')}</Empty> : (
        <div className="listing-grid">
          {data.items.map((l) => (
            <button key={l.listingId} className="listing" onClick={() => nav(`/market/${l.listingId}`)}>
              <CardView card={l.card} compact />
              <div className="listing-meta">
                <Price currency={l.currency} amount={l.price} />
                <span className="muted small">{l.isMine ? t('market.yours') : l.sellerNickname}{l.status !== 'active' ? ` · ${l.status}` : ''}</span>
              </div>
            </button>
          ))}
        </div>
      )}
      {pages > 1 && <div className="pager"><button className="btn ghost" disabled={page <= 1} onClick={() => setPage(page - 1)}>‹</button><span>{page}/{pages}</span><button className="btn ghost" disabled={page >= pages} onClick={() => setPage(page + 1)}>›</button></div>}
    </div>
  );
}

interface Tx { id: number; kind: string; at: string; formId: string; rarity: string; serial?: string; currency: string; price: number; fee: number; sellerNet?: number | null }

function History() {
  const { t, lang } = useI18n();
  const cat = useCatalog();
  const [type, setType] = useState<'purchases' | 'sales'>('purchases');
  const { data, error } = useLoad(() => api.get<Tx[]>('/api/market/history' + qs({ type })), [type]);
  return (
    <div>
      <Tabs value={type} onChange={setType} items={[['purchases', t('market.purchases')], ['sales', t('market.sales')]]} />
      {!data ? <Loading error={error} /> : data.length === 0 ? <Empty>{t('market.no_history')}</Empty> : (
        <ul className="list">
          {data.map((x) => (
            <li key={x.id} className="list-row">
              <div className="grow"><b>{cat.forms[x.formId]?.name ?? x.formId}</b> <span className="tag">{x.rarity}</span> <span className="muted small">#{x.serial} · {x.kind} · {timeAgo(x.at, lang)}</span></div>
              <div className="right"><Price currency={x.currency} amount={x.price} />{x.sellerNet != null && <div className="muted small">{t('web.net')} {fmt(x.sellerNet)}</div>}</div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

interface Wish { id: number; formId?: string | null; familyId?: string | null; minRarity?: string | null; currency?: string | null; maxPrice?: number | null }

function WishlistEditor() {
  const { t } = useI18n();
  const cat = useCatalog();
  const list = useLoad(() => api.get<Wish[]>('/api/market/wishlist'), []);
  const toast = useToast();
  const { busy, run } = useAction();
  const [name, setName] = useState('');
  const [minRarity, setMinRarity] = useState('');
  const [maxPrice, setMaxPrice] = useState('');
  const [currency, setCurrency] = useState('');
  const add = async () => {
    const formId = Object.entries(cat.forms).find(([, f]) => f.name.toLowerCase() === name.trim().toLowerCase())?.[0];
    if (!formId) { toast(t('wishlist.unknown_pet'), 'err'); return; }
    const r = await run(() => api.post('/api/market/wishlist', { formId, minRarity: minRarity || null, currency: currency || null, maxPrice: maxPrice ? Number(maxPrice) : null }), t('wishlist.add'));
    if (r) { setName(''); void list.reload(); }
  };
  return (
    <div>
      <div className="form inline-form">
        <input list="pet-names" placeholder={t('wishlist.pet_name')} value={name} onChange={(e) => setName(e.target.value)} />
        <datalist id="pet-names">{Object.values(cat.forms).map((f) => <option key={f.name} value={f.name} />)}</datalist>
        <select value={minRarity} onChange={(e) => setMinRarity(e.target.value)}><option value="">{t('wishlist.any_rarity')}</option>{RARITY_ORDER.map((r) => <option key={r}>{r}</option>)}</select>
        <select value={currency} onChange={(e) => setCurrency(e.target.value)}><option value="">{t('market.any_currency')}</option><option value="MINI_GREEN_BEAN">{t('currency.mini_green_bean')}</option><option value="RAINBOW_STAR">{t('currency.rainbow_star')}</option></select>
        <input inputMode="numeric" placeholder={t('wishlist.max_price')} value={maxPrice} onChange={(e) => setMaxPrice(e.target.value.replace(/\D/g, ''))} />
        <button className="btn primary" disabled={busy || !name} onClick={add}>{t('wishlist.save')}</button>
      </div>
      {!list.data ? <Loading error={list.error} /> : list.data.length === 0 ? <Empty>{t('market.no_history')}</Empty> : (
        <ul className="list">
          {list.data.map((w) => (
            <li key={w.id} className="list-row">
              <div className="grow">{w.formId ? cat.forms[w.formId]?.name : w.familyId} {w.minRarity && <span className="tag">{w.minRarity}+</span>} {w.maxPrice != null && w.currency && <Price currency={w.currency} amount={w.maxPrice} />}</div>
              <button className="btn ghost danger" onClick={() => run(() => api.del(`/api/market/wishlist/${w.id}`)).then(() => list.reload())}>✕</button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
