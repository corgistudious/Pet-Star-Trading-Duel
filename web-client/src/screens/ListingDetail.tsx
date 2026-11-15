import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api, newIdempotencyKey } from '../api/client';
import type { CardDto, ListingView } from '../api/types';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { PageHead } from '../components/Layout';
import { CardView } from '../components/CardView';
import { Confirm, Countdown, Loading, Price, fmt, timeAgo, useAction, useLoad } from '../components/ui';

interface Detail { listing: ListingView; cardTradeHistory: { at: string; currency: string; price: number; kind: string }[] }

export default function ListingDetail() {
  const { id } = useParams();
  const { t, lang } = useI18n();
  const nav = useNavigate();
  const { player, applyCurrencies, reloadPlayer } = useSession();
  const { data, error, reload } = useLoad(() => api.get<Detail>(`/api/market/listings/${id}`), [id]);
  const { busy, run } = useAction();
  const [confirm, setConfirm] = useState(false);
  const [buyKey] = useState(newIdempotencyKey); // stable for this screen: a double tap / retry buys at most once
  if (!data) return <div className="page"><PageHead title={t('market.listing')} back /><Loading error={error} onRetry={reload} /></div>;
  const l = data.listing;
  const afford = (player?.currencies[l.currency] ?? 0) >= l.price;

  const buy = async () => {
    setConfirm(false);
    const r = await run(() => api.post<{ card: CardDto; currencies: Record<string, number> }>('/api/market/buy', { listingId: l.listingId, expectedPrice: l.price }, { idem: buyKey }), t('market.bought'));
    if (r) { applyCurrencies(r.currencies); void reloadPlayer(); nav(`/card/${r.card.id}`); }
  };
  const cancel = async () => { if (await run(() => api.post(`/api/market/listings/${l.listingId}/cancel`), t('market.cancelled'))) void reload(); };

  return (
    <div className="page">
      <PageHead title={t('market.listing')} back />
      <div className="detail">
        <div className="detail-card"><CardView card={l.card} /></div>
        <div className="detail-side">
          <div className="big-price"><Price currency={l.currency} amount={l.price} /></div>
          <p>{t('market.seller', l.sellerNickname)}</p>
          {l.status === 'active' && <p className="muted small">{t('market.expires', '')} <Countdown until={l.expiresAt} /></p>}
          <p className="muted small">{t('market.fee_buyer_info', l.feeBps / 100)}</p>
          {l.estimatedValue != null && <p className="muted small">{t('market.reference_value', fmt(l.estimatedValue))}</p>}
          <p className="serial">#{l.card.serial}</p>
          <div className="actions">
            {l.isMine ? (l.status === 'active' && <button className="btn" disabled={busy} onClick={cancel}>{t('market.cancel_listing')}</button>)
              : l.status === 'active' && <button className="btn primary big" disabled={busy || !afford} onClick={() => setConfirm(true)} data-testid="buy">{t('market.buy')}</button>}
            <button className="btn ghost" onClick={() => nav(`/passport/${l.card.serial}`)}>{t('passport.open')}</button>
          </div>
          {!afford && !l.isMine && <p className="warn">{t('error.insufficient_' + l.currency.toLowerCase())}</p>}
          <h4>{t('market.trade_history')}</h4>
          {data.cardTradeHistory.length === 0 ? <p className="muted small">{t('market.no_trades')}</p> : (
            <ul className="mini-list">{data.cardTradeHistory.map((x, i) => <li key={i}><Price currency={x.currency} amount={x.price} /> <span className="muted small">{x.kind} · {timeAgo(x.at, lang)}</span></li>)}</ul>
          )}
        </div>
      </div>
      {confirm && <Confirm busy={busy} onNo={() => setConfirm(false)} onYes={buy}
        text={<p className="pre">{t('market.confirm_buy_body', l.card.name, l.card.rarity, fmt(l.price), t('currency.' + l.currency.toLowerCase()))}</p>} />}
    </div>
  );
}
