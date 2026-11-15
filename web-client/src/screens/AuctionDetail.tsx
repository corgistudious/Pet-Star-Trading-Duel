import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, newIdempotencyKey } from '../api/client';
import type { AuctionView } from '../api/types';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { PageHead } from '../components/Layout';
import { CardView } from '../components/CardView';
import { Countdown, Loading, Price, fmt, timeAgo, useAction, useLoad, useServerClock } from '../components/ui';

interface Detail { auction: AuctionView; serverTime: string; bids: { amount: number; createdAt: string; mine: boolean; status: string }[] }

export default function AuctionDetail() {
  const { id } = useParams();
  const { t, lang } = useI18n();
  const { player, applyCurrencies } = useSession();
  const { data, error, reload } = useLoad(() => api.get<Detail>(`/api/auctions/${id}`), [id]);
  const offset = useServerClock(data?.serverTime);
  const { busy, run } = useAction();
  const [amount, setAmount] = useState('');
  useEffect(() => { if (data?.auction.nextMinBid) setAmount(String(data.auction.nextMinBid)); }, [data?.auction.nextMinBid]);
  // auctions settle on the server clock; poll gently while live so anti-snipe extensions show up
  useEffect(() => { if (data?.auction.status !== 'active') return; const h = setInterval(() => void reload(), 10_000); return () => clearInterval(h); }, [data?.auction.status, reload]);
  if (!data) return <div className="page"><PageHead title={t('auction.detail')} back /><Loading error={error} onRetry={reload} /></div>;
  const a = data.auction;
  const bid = async (value: number) => {
    const r = await run(() => api.post<{ currencies: Record<string, number> }>('/api/auctions/bid', { auctionId: a.auctionId, amount: value }, { idem: newIdempotencyKey() }), t('auction.bid_placed'));
    if (r) { applyCurrencies(r.currencies); void reload(); }
  };
  const cancel = async () => { if (await run(() => api.post(`/api/auctions/${a.auctionId}/cancel`))) void reload(); };
  const n = Number(amount);
  return (
    <div className="page">
      <PageHead title={t('auction.detail')} back />
      <div className="detail">
        <div className="detail-card"><CardView card={a.card} /></div>
        <div className="detail-side">
          <p>{t('market.seller', a.sellerNickname)}</p>
          <p className="big-price">{a.highestAmount != null ? <>{t('auction.highest', '')}<Price currency={a.currency} amount={a.highestAmount} /></> : t('auction.no_bids_start', fmt(a.startPrice))}</p>
          {a.status === 'active' && <p>{t('auction.time_left', '')}<Countdown until={a.endsAt} offset={offset} onEnd={reload} /></p>}
          {a.status !== 'active' && <p className="tag">{a.status}</p>}
          {a.leading && <p className="tag win">{t('auction.leading')}</p>}
          {a.status === 'active' && !a.isMine && (
            <div className="bid-box">
              {a.nextMinBid != null && <p className="muted small">{t('auction.min_bid', fmt(a.nextMinBid))}</p>}
              <div className="row gap">
                <input inputMode="numeric" value={amount} onChange={(e) => setAmount(e.target.value.replace(/\D/g, ''))} aria-label={t('auction.place_bid')} />
                <button className="btn primary" disabled={busy || !n || n < (a.nextMinBid ?? 0) || (player?.currencies[a.currency] ?? 0) < n} onClick={() => bid(n)}>{t('auction.place_bid')}</button>
              </div>
              {a.buyoutPrice != null && <button className="btn" disabled={busy || (player?.currencies[a.currency] ?? 0) < a.buyoutPrice} onClick={() => bid(a.buyoutPrice!)}>{t('auction.buyout', fmt(a.buyoutPrice))}</button>}
              <p className="muted small">{t('auction.hold_info')}</p>
            </div>
          )}
          {a.isMine && a.status === 'active' && a.bidCount === 0 && <button className="btn" disabled={busy} onClick={cancel}>{t('auction.cancel')}</button>}
          <h4>{t('auction.bid_history')}</h4>
          <ul className="mini-list">{data.bids.map((b, i) => <li key={i}><Price currency={a.currency} amount={b.amount} /> {b.mine && <b>({t('auction.you')})</b>} <span className="muted small">{timeAgo(b.createdAt, lang)} · {b.status}</span></li>)}</ul>
        </div>
      </div>
    </div>
  );
}
