import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, qs } from '../api/client';
import type { AuctionView } from '../api/types';
import { useT } from '../i18n';
import { PageHead } from '../components/Layout';
import { CardView } from '../components/CardView';
import { Countdown, Empty, Loading, Price, Tabs, useLoad, useServerClock } from '../components/ui';

export default function Auctions() {
  const t = useT();
  const nav = useNavigate();
  const [section, setSection] = useState<'active' | 'bidding' | 'mine'>('active');
  const { data, error, reload } = useLoad(() => api.get<AuctionView[]>('/api/auctions' + qs({ section: section === 'active' ? undefined : section })), [section]);
  const offset = useServerClock(data?.[0]?.serverTime);
  return (
    <div className="page">
      <PageHead title={t('auction.title')} back="/market" />
      <p className="notice">{t('market.in_game_only')}</p>
      <Tabs value={section} onChange={setSection} items={[['active', t('auction.active')], ['bidding', t('auction.bidding')], ['mine', t('auction.mine')]]} />
      {!data ? <Loading error={error} onRetry={reload} /> : data.length === 0 ? <Empty>{t('auction.empty')}</Empty> : (
        <div className="listing-grid">
          {data.map((a) => (
            <button key={a.auctionId} className="listing" onClick={() => nav(`/auctions/${a.auctionId}`)}>
              <CardView card={a.card} compact />
              <div className="listing-meta">
                <Price currency={a.currency} amount={a.highestAmount ?? a.startPrice} />
                <span className="muted small">{t('auction.bids', a.bidCount)} · {a.status === 'active' ? <Countdown until={a.endsAt} offset={offset} /> : a.status}</span>
                {a.leading && <span className="tag win">{t('auction.leading')}</span>}
              </div>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
