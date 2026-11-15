import { useEffect, useState } from 'react';
import { api, ApiError, newIdempotencyKey, qs } from '../api/client';
import type { CardDto, Currency } from '../api/types';
import { CURRENCIES } from '../api/types';
import { useT } from '../i18n';
import { Modal, Price, fmt, useAction, useErrorText, useToast } from './ui';

interface Quote { price: number; currency: string; feeBps: number; fee: number; sellerNet: number; reference: { estimated: number; initial: number; samples: number; confidence: string }; warning?: string | null }

function CurrencyPicker({ value, onChange }: { value: Currency; onChange: (c: Currency) => void }) {
  const t = useT();
  return (
    <div className="seg">
      {CURRENCIES.map((c) => <button key={c} type="button" className={c === value ? 'on' : ''} onClick={() => onChange(c)}>{t('currency.' + c.toLowerCase())}</button>)}
    </div>
  );
}

/** Fixed-price listing. Shows the exact fee and net from the server quote before listing. */
export function SellModal({ card, onClose, onDone }: { card: CardDto; onClose: () => void; onDone: () => void }) {
  const t = useT();
  const toast = useToast();
  const errText = useErrorText();
  const [currency, setCurrency] = useState<Currency>('MINI_GREEN_BEAN');
  const [price, setPrice] = useState('');
  const [days, setDays] = useState(7);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [needConfirm, setNeedConfirm] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const p = Number(price);

  useEffect(() => {
    setQuote(null); setNeedConfirm(null);
    if (!p || p < 1) return;
    const id = setTimeout(() => api.get<Quote>('/api/market/sell-quote' + qs({ cardId: card.id, currency, price: p })).then(setQuote).catch(() => {}), 300);
    return () => clearTimeout(id);
  }, [p, currency, card.id]);

  const submit = async (confirm: boolean) => {
    setBusy(true);
    try {
      await api.post('/api/market/listings', { cardId: card.id, currency, price: p, days, confirmPriceWarning: confirm }, { idem: newIdempotencyKey() });
      toast(t('market.listed'), 'ok');
      onDone();
    } catch (e) {
      if (e instanceof ApiError && e.code === 'price_confirmation_required') setNeedConfirm(e.detail ?? 'price');
      else toast(errText(e), 'err');
    } finally { setBusy(false); }
  };

  return (
    <Modal title={t('market.sell')} onClose={onClose}>
      <p className="muted small">{t('market.in_game_only')}</p>
      <CurrencyPicker value={currency} onChange={setCurrency} />
      <label>{t('market.price')}<input inputMode="numeric" pattern="[0-9]*" value={price} onChange={(e) => setPrice(e.target.value.replace(/\D/g, ''))} /></label>
      <label>{t('web.duration_days')}<select value={days} onChange={(e) => setDays(Number(e.target.value))}>{[1, 3, 7, 14].map((d) => <option key={d} value={d}>{d}</option>)}</select></label>
      {quote ? (
        <div className="quote">
          <div>{t('market.fee_seller', fmt(quote.fee), fmt(quote.sellerNet))} ({quote.feeBps / 100}%)</div>
          <div className="muted small">{t('market.reference_value', fmt(quote.reference.estimated))} · {t('confidence.' + quote.reference.confidence)}</div>
          <div className="muted small">{t('market.reference_disclaimer')}</div>
        </div>
      ) : <p className="muted small">{t('market.enter_price')}</p>}
      {needConfirm && <div className="warn">{t('error.price_confirmation_required')} ({needConfirm})</div>}
      <div className="row end gap">
        <button className="btn ghost" onClick={onClose}>{t('web.cancel')}</button>
        <button className="btn primary" disabled={busy || !p} onClick={() => submit(!!needConfirm)}>{needConfirm ? t('web.confirm_anyway') : t('market.list_now')}</button>
      </div>
    </Modal>
  );
}

export function AuctionModal({ card, onClose, onDone }: { card: CardDto; onClose: () => void; onDone: () => void }) {
  const t = useT();
  const { busy, run } = useAction();
  const [currency, setCurrency] = useState<Currency>('MINI_GREEN_BEAN');
  const [start, setStart] = useState('');
  const [buyout, setBuyout] = useState('');
  const [hours, setHours] = useState(24);
  const submit = async () => {
    const r = await run(() => api.post('/api/auctions', { cardId: card.id, currency, startPrice: Number(start), buyoutPrice: buyout ? Number(buyout) : null, hours }, { idem: newIdempotencyKey() }), t('market.auction_started'));
    if (r) onDone();
  };
  return (
    <Modal title={t('market.start_auction')} onClose={onClose}>
      <p className="muted small">{t('market.in_game_only')}</p>
      <CurrencyPicker value={currency} onChange={setCurrency} />
      <label>{t('market.start_price')}<input inputMode="numeric" value={start} onChange={(e) => setStart(e.target.value.replace(/\D/g, ''))} /></label>
      <label>{t('market.buyout_optional')}<input inputMode="numeric" value={buyout} onChange={(e) => setBuyout(e.target.value.replace(/\D/g, ''))} /></label>
      <label>{t('web.duration_hours')}<select value={hours} onChange={(e) => setHours(Number(e.target.value))}>{[1, 6, 12, 24, 48, 72].map((h) => <option key={h} value={h}>{h}</option>)}</select></label>
      <p className="muted small">{t('market.fee_buyer_info', 3)}</p>
      {Number(start) > 0 && <p><Price currency={currency} amount={Number(start)} /></p>}
      <div className="row end gap">
        <button className="btn ghost" onClick={onClose}>{t('web.cancel')}</button>
        <button className="btn primary" disabled={busy || !Number(start)} onClick={submit}>{t('market.start_auction')}</button>
      </div>
    </Modal>
  );
}
