import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, newIdempotencyKey } from '../api/client';
import type { PackOddsDto, PackShopItemDto, PurchasePackResponse } from '../api/types';
import { useT } from '../i18n';
import { useSession } from '../state/session';
import { PageHead } from '../components/Layout';
import { Loading, Modal, Price, Tabs, useAction, useLoad } from '../components/ui';
import { RARITY_COLORS, petArt, uiArt, useCatalog } from '../state/catalog';

export default function Shop() {
  const t = useT();
  const nav = useNavigate();
  const { player, applyCurrencies, reloadPlayer } = useSession();
  const [tab, setTab] = useState<'packs' | 'stars'>('packs');
  const { data, error, reload, setData } = useLoad(() => api.get<PackShopItemDto[]>('/api/packs'), []);
  const { busy, run } = useAction();
  const [odds, setOdds] = useState<PackOddsDto | null>(null);
  const [qty, setQty] = useState<Record<string, number>>({});

  const buy = async (p: PackShopItemDto) => {
    const q = qty[p.id] ?? 1;
    const key = newIdempotencyKey(); // one key per tap: a network retry of this request can never charge twice
    const r = await run(() => api.post<PurchasePackResponse>('/api/packs/purchase', { packTypeId: p.id, quantity: q }, { idem: key }), t('web.purchased', q));
    if (r) {
      applyCurrencies(r.currencies);
      setData((d) => d?.map((x) => (x.id === p.id ? { ...x, owned: r.owned } : x)));
      void reloadPlayer();
    }
  };

  return (
    <div className="page">
      <PageHead title={t('shop.title')} back="/" />
      <Tabs value={tab} onChange={setTab} items={[['packs', t('shop.tab_packs')], ['stars', t('shop.tab_rainbow_star')]]} />
      {tab === 'stars' ? (
        <div className="panel center">
          <img src={uiArt('ui_currency_rainbow_star_chest')} width={120} height={120} alt="" />
          <h3>{t('shop.iap_unavailable')}</h3>
          <p className="muted">{t('shop.iap_unavailable_detail')}</p>
          <p className="muted small">{t('web.no_real_money')}</p>
        </div>
      ) : !data ? <Loading error={error} onRetry={reload} /> : (
        <div className="pack-list">
          {data.map((p) => {
            const q = qty[p.id] ?? 1;
            const afford = (player?.currencies[p.priceCurrency] ?? 0) >= p.priceAmount * q;
            return (
              <div key={p.id} className="pack-item">
                <img className="pack-art" src={uiArt(p.artAssetId)} alt="" onError={(e) => { (e.target as HTMLImageElement).src = uiArt('ui_pack_basic'); }} />
                <div className="pack-info">
                  <h3>{t(p.nameKey)}</h3>
                  <Price currency={p.priceCurrency} amount={p.priceAmount} />
                  <div className="muted small">{t('shop.owned', p.owned)}</div>
                  <div className="row gap wrap">
                    <select aria-label="quantity" value={q} onChange={(e) => setQty({ ...qty, [p.id]: Number(e.target.value) })}>
                      {[1, 2, 3, 5, 10].map((n) => <option key={n} value={n}>×{n}</option>)}
                    </select>
                    <button className="btn primary" disabled={busy || !afford} onClick={() => buy(p)} data-testid={`buy-${p.id}`}>{t('shop.buy')}</button>
                    <button className="btn" disabled={p.owned < 1} onClick={() => nav(`/open?pack=${p.id}`)}>{t('shop.open')}</button>
                    <button className="btn ghost" onClick={() => api.get<PackOddsDto>(`/api/packs/${p.id}/odds`).then(setOdds)}>{t('shop.odds')}</button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
      {odds && <OddsModal odds={odds} onClose={() => setOdds(null)} />}
    </div>
  );
}

function OddsTable({ title, rows }: { title: string; rows: Record<string, number> }) {
  return (
    <div className="odds-table">
      <h4>{title}</h4>
      {Object.entries(rows).map(([k, v]) => (
        <div key={k} className="odds-row"><span style={{ color: RARITY_COLORS[k] }}>{k}</span><span>{v < 0.01 ? v.toFixed(4) : v.toFixed(2)}%</span></div>
      ))}
    </div>
  );
}

function OddsModal({ odds, onClose }: { odds: PackOddsDto; onClose: () => void }) {
  const t = useT();
  const cat = useCatalog();
  return (
    <Modal title={t('shop.odds_title')} onClose={onClose} wide>
      <div className="odds-grid">
        <OddsTable title={t('shop.odds_slots')} rows={odds.slots1To4} />
        <OddsTable title={t('shop.odds_final')} rows={odds.slot5} />
        <OddsTable title={t('shop.odds_quality')} rows={odds.quality} />
        <OddsTable title={t('shop.odds_finish')} rows={odds.finish} />
      </div>
      <h4>{t('web.possible_pets', odds.possiblePets.length)}</h4>
      <div className="mini-pets">
        {odds.possiblePets.map((f) => <img key={f} src={petArt(f, true)} alt={cat.forms[f]?.name ?? f} title={cat.forms[f]?.name} loading="lazy" width={48} height={48} />)}
      </div>
      <p className="muted small">v{odds.version}</p>
    </Modal>
  );
}
