// Pet Market (Preview): explore, buy, list (escrowed), card-for-card trades, my listings, history. Mini Green Beans only.
import { useState } from 'react';
import { petArt, useCatalog } from '../state/catalog';
import { Avatar } from './Cosmetics';
import { ago, errText, type Hub } from './hub';
import { PetStrip } from './SocialHub';
import { stats, type SimCard } from './sim';
import { acc, acceptTrade, buyListing, cancelListing, closeTrade, FEE_BPS, friendIds, isBlocked, listCard, offerTrade, snap, type CardSnapId } from './world';

type MTab = 'explore' | 'buy' | 'sell' | 'trade' | 'mine' | 'history';

export function MarketHub({ h }: { h: Hub }) {
  const { P } = h;
  const [tab, setTab] = useState<MTab>('explore');
  const tabs: [MTab, string][] = [['explore', P('Khám Phá', 'Explore')], ['buy', P('Mua Pet', 'Buy')], ['sell', P('Đăng Bán', 'Sell')], ['trade', P('Trao Đổi', 'Trade')], ['mine', P('Tin Của Tôi', 'My Listings')], ['history', P('Lịch Sử', 'History')]];
  return (
    <div className="mk">
      <nav className="sh-tabs">{tabs.map(([k, l]) => <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)} data-testid={`mk-${k}`}><span>{l}</span></button>)}</nav>
      <div className="mk-note">🫘 {P('Chợ chỉ dùng Đậu Xanh Mini — tiền ảo trong game. Không quy đổi tiền thật, không rút tiền, không thanh toán ngoài game.', 'The market uses Mini Green Beans only — in-game currency. No real-money exchange, no cash-out, no outside payments.')}</div>
      {tab === 'explore' && <Explore h={h} go={setTab} />}
      {tab === 'buy' && <Buy h={h} />}
      {tab === 'sell' && <Sell h={h} />}
      {tab === 'trade' && <TradeTab h={h} />}
      {tab === 'mine' && <Mine h={h} />}
      {tab === 'history' && <History h={h} />}
    </div>
  );
}

function Explore({ h, go }: { h: Hub; go: (t: MTab) => void }) {
  const { P, w, me } = h;
  const active = w.listings.filter((l) => l.status === 'active');
  const sold = w.listings.filter((l) => l.status === 'sold');
  return (
    <div className="lp-col">
      <div className="mk-stats"><div><b>{active.length}</b><small>{P('Tin đang bán', 'Active listings')}</small></div><div><b>{sold.length}</b><small>{P('Đã bán', 'Sold')}</small></div>
        <div><b>{w.trades.filter((t) => t.status === 'pending' && t.to === me).length}</b><small>{P('Đề nghị đến', 'Offers to me')}</small></div><div><b>{FEE_BPS / 100}%</b><small>{P('Phí bán', 'Sale fee')}</small></div></div>
      <b>🆕 {P('Mới đăng', 'Newest')}</b>
      {active.length === 0 ? <small className="muted">{P('Chưa có tin đăng nào. Chợ chỉ hiển thị thẻ do các tài khoản Preview đăng bán.', 'No listings yet. The market only shows cards listed by Preview accounts.')}</small>
        : <div className="mk-grid">{active.slice(-8).reverse().map((l) => <ListingCard key={l.id} h={h} id={l.id} />)}</div>}
      <div className="lp-row"><button className="btn small primary" onClick={() => go('sell')}>{P('Đăng bán thẻ', 'List a card')}</button><button className="btn small" onClick={() => go('trade')}>{P('Đề nghị trao đổi', 'Offer a trade')}</button></div>
    </div>
  );
}

function ListingCard({ h, id }: { h: Hub; id: number }) {
  const { P, w, me, s, setS, say } = h;
  const l = w.listings.find((x) => x.id === id)!;
  const seller = acc(w, l.seller);
  const buy = () => {
    try {
      const got = buyListing(me, l.id, s.beans);
      setS((x) => ({ ...x, beans: x.beans - got.price, cards: [...x.cards, got.card] }));
      say(P('Đã mua! Thẻ đã vào bộ sưu tập.', 'Bought! The card is in your collection.'));
    } catch (e) { say(errText(e, P)); }
  };
  return (
    <div className="mk-card" data-testid={`listing-${l.id}`}>
      <PetStrip cards={[snap(l.card)]} />
      <div className="sh-row"><Avatar a={seller} d={h.d} size={24} onClick={() => h.openProfile(l.seller)} /><small className="ellipsis grow">{seller?.name}</small></div>
      <b className="mk-price">🫘 {l.price.toLocaleString()}</b>
      {l.status === 'active' && (l.seller === me ? <button className="btn small ghost" onClick={() => { try { cancelListing(me, l.id); say(P('Đã hủy tin, thẻ được trả lại', 'Listing cancelled, card returned')); } catch (e) { say(errText(e, P)); } }} data-testid={`mk-cancel-${l.id}`}>{P('Hủy', 'Cancel')}</button>
        : <button className="btn small primary" disabled={isBlocked(w, me, l.seller)} onClick={buy} data-testid={`mk-buy-${l.id}`}>{P('Mua', 'Buy')}</button>)}
    </div>
  );
}

function Buy({ h }: { h: Hub }) {
  const { P, w, me } = h;
  const cat = useCatalog();
  const [q, setQ] = useState('');
  const [sort, setSort] = useState<'new' | 'low' | 'high'>('new');
  let list = w.listings.filter((l) => l.status === 'active' && l.seller !== me && !isBlocked(w, me, l.seller) && (!q || (cat.forms[l.card.formId]?.name ?? '').toLowerCase().includes(q.toLowerCase()) || l.card.rarity === q.toUpperCase()));
  list = sort === 'low' ? [...list].sort((a, b) => a.price - b.price) : sort === 'high' ? [...list].sort((a, b) => b.price - a.price) : [...list].reverse();
  return (
    <div className="lp-col">
      <div className="sh-row"><input className="lb-input" value={q} onChange={(e) => setQ(e.target.value)} placeholder={P('Tên Pet hoặc độ hiếm (SSR…)', 'Pet name or rarity (SSR…)')} />
        <select className="lb-input small" value={sort} onChange={(e) => setSort(e.target.value as 'new' | 'low' | 'high')}><option value="new">{P('Mới nhất', 'Newest')}</option><option value="low">{P('Giá thấp', 'Lowest')}</option><option value="high">{P('Giá cao', 'Highest')}</option></select></div>
      <small>{P('Số dư', 'Balance')}: 🫘 {h.s.beans.toLocaleString()}</small>
      {list.length === 0 ? <small className="muted">{P('Không có tin phù hợp.', 'No matching listings.')}</small> : <div className="mk-grid">{list.map((l) => <ListingCard key={l.id} h={h} id={l.id} />)}</div>}
    </div>
  );
}

function Sell({ h }: { h: Hub }) {
  const { P, s, setS, me, say, l } = h;
  const cat = useCatalog();
  const [pick, setPick] = useState<number | null>(null);
  const [price, setPrice] = useState('200');
  const busy = new Set([...l.team, l.companionId ?? -1]);
  const cards = [...s.cards].filter((c) => !c.listed).sort((a, b) => stats(cat, b).power - stats(cat, a).power);
  const sell = () => {
    const c = s.cards.find((x) => x.id === pick);
    if (!c) return;
    try {
      listCard(me, c, Number(price));
      // escrow: the card leaves the collection while listed (cannot be used, evolved, traded or listed twice)
      setS((x) => ({ ...x, cards: x.cards.filter((y) => y.id !== c.id) }));
      setPick(null); say(P('Đã đăng bán (thẻ được khóa trong chợ)', 'Listed (the card is locked in the market)'));
    } catch (e) { say(errText(e, P)); }
  };
  const fee = Math.floor(Number(price || 0) * FEE_BPS / 10000);
  return (
    <div className="lp-col">
      <small className="muted">{P('Chọn thẻ, đặt giá bằng Đậu Xanh Mini. Thẻ trong đội hình / Pet đồng hành nên đổi trước khi bán.', 'Pick a card and a price in Mini Green Beans. Swap cards out of your team / companion before selling.')}</small>
      <div className="sh-pick big">{cards.slice(0, 40).map((c) => <button key={c.id} className={pick === c.id ? 'on' : ''} onClick={() => setPick(c.id)} data-testid={`sell-${c.id}`}><img src={petArt(c.formId, true)} alt="" /><small>{c.rarity} · Lv{c.level}</small>{busy.has(c.id) && <em>🛡</em>}</button>)}</div>
      <div className="sh-row"><label className="grow">🫘 <input className="lb-input small" inputMode="numeric" value={price} onChange={(e) => setPrice(e.target.value.replace(/\D/g, ''))} data-testid="sell-price" /></label>
        <small>{P('Phí', 'Fee')} {fee} · {P('Bạn nhận', 'You get')} {Math.max(0, Number(price || 0) - fee)}</small>
        <button className="btn primary" disabled={pick == null || !Number(price)} onClick={sell} data-testid="sell-go">{P('Đăng bán', 'List')}</button></div>
    </div>
  );
}

function TradeTab({ h }: { h: Hub }) {
  const { P, w, me, s, setS, say } = h;
  const [to, setTo] = useState<number | null>(null);
  const [give, setGive] = useState<number[]>([]);
  const [want, setWant] = useState<number[]>([]);
  const [note, setNote] = useState('');
  const partner = to != null ? acc(w, to) : null;
  const incoming = w.trades.filter((t) => t.to === me && t.status === 'pending');
  const partners = [...new Set([...friendIds(w, me), ...w.accounts.map((a) => a.uid)])].filter((u) => u !== me && !isBlocked(w, me, u));
  const offer = () => {
    const cards = give.map((id) => s.cards.find((c) => c.id === id)!).filter(Boolean);
    const req = want.map((id) => partner!.cards.find((c) => c.id === id)!).filter(Boolean) as CardSnapId[];
    try {
      offerTrade(me, to!, cards, req, note);
      setS((x) => ({ ...x, cards: x.cards.filter((c) => !give.includes(c.id)) }));   // offered cards are escrowed until accepted/declined
      setGive([]); setWant([]); setNote(''); say(P('Đã gửi đề nghị trao đổi', 'Trade offer sent'));
    } catch (e) { say(errText(e, P)); }
  };
  const accept = (id: number) => {
    try {
      const r = acceptTrade(me, id, s.cards);
      setS((x) => ({ ...x, cards: [...x.cards.filter((c) => !r.given.includes(c.id)), ...r.received] }));
      say(P('Trao đổi thành công!', 'Trade complete!'));
    } catch (e) { say(errText(e, P)); }
  };
  return (
    <div className="lp-col">
      {incoming.length > 0 && <><b>📥 {P('Đề nghị gửi đến bạn', 'Offers to you')}</b>{incoming.map((t) => <div key={t.id} className="lp-card" data-testid={`trade-${t.id}`}>
        <small><b>{acc(w, t.from)?.name}</b> · {ago(t.at, P)}{t.note ? ` · “${t.note}”` : ''}</small>
        <div className="mk-swap"><div><small>{P('Bạn nhận', 'You get')}</small><PetStrip cards={t.offer.map(snap)} /></div><span>⇄</span><div><small>{P('Bạn đưa', 'You give')}</small><PetStrip cards={t.request} /></div></div>
        <div className="lp-row"><button className="btn small primary" onClick={() => accept(t.id)} data-testid={`trade-accept-${t.id}`}>{P('Chấp nhận', 'Accept')}</button><button className="btn small ghost" onClick={() => { try { closeTrade(me, t.id, 'declined'); say(P('Đã từ chối', 'Declined')); } catch (e) { say(errText(e, P)); } }}>{P('Từ chối', 'Decline')}</button></div></div>)}</>}
      <b>🔄 {P('Tạo đề nghị trao đổi', 'New trade offer')}</b>
      <select className="lb-input small" value={to ?? ''} onChange={(e) => { setTo(e.target.value ? Number(e.target.value) : null); setWant([]); }} data-testid="trade-to">
        <option value="">{P('Chọn người chơi', 'Choose a player')}</option>{partners.map((u) => <option key={u} value={u}>{acc(w, u)?.name} · UID {u}</option>)}</select>
      {partners.length === 0 && <small className="muted">{P('Cần ít nhất một tài khoản Preview khác để trao đổi.', 'You need another Preview account to trade with.')}</small>}
      {partner && <>
        <small>{P('Thẻ của bạn (1–5)', 'Your cards (1–5)')}</small>
        <div className="sh-pick">{s.cards.filter((c) => !c.listed).slice(0, 40).map((c: SimCard) => <button key={c.id} className={give.includes(c.id) ? 'on' : ''} onClick={() => setGive((x) => (x.includes(c.id) ? x.filter((y) => y !== c.id) : x.length < 5 ? [...x, c.id] : x))} data-testid={`give-${c.id}`}><img src={petArt(c.formId, true)} alt="" /><small>{c.rarity}</small></button>)}</div>
        <small>{P('Thẻ của', 'Cards of')} {partner.name} (1–5)</small>
        {partner.cards.length === 0 ? <small className="muted">{P('Người này chưa có thẻ công khai để trao đổi.', 'This player has no tradeable cards published yet.')}</small>
          : <div className="sh-pick">{partner.cards.map((c) => <button key={c.id} className={want.includes(c.id) ? 'on' : ''} onClick={() => setWant((x) => (x.includes(c.id) ? x.filter((y) => y !== c.id) : x.length < 5 ? [...x, c.id] : x))} data-testid={`want-${c.id}`}><img src={petArt(c.formId, true)} alt="" /><small>{c.rarity}</small></button>)}</div>}
        <input className="lb-input" maxLength={120} value={note} onChange={(e) => setNote(e.target.value)} placeholder={P('Lời nhắn (tùy chọn)', 'Note (optional)')} />
        <button className="btn primary" disabled={!give.length || !want.length} onClick={offer} data-testid="trade-go">{P('Gửi đề nghị', 'Send offer')} ({give.length} ⇄ {want.length})</button>
        <small className="muted">{P('Thẻ bạn đưa ra được khóa đến khi đối phương trả lời. Khi chấp nhận, hệ thống kiểm tra lại thẻ của cả hai bên — nếu thay đổi, đề nghị bị hủy và không thẻ nào bị chuyển.', 'Offered cards are locked until the other player answers. On accept both sides are re-checked — if anything changed the offer fails and nothing moves.')}</small>
      </>}
    </div>
  );
}

function Mine({ h }: { h: Hub }) {
  const { P, w, me, say } = h;
  const mine = w.listings.filter((l) => l.seller === me && l.status === 'active');
  const out = w.trades.filter((t) => t.from === me && t.status === 'pending');
  return (
    <div className="lp-col">
      <b>{P('Tin đang bán', 'Active listings')} ({mine.length})</b>
      {mine.length === 0 ? <small className="muted">—</small> : <div className="mk-grid">{mine.map((l) => <ListingCard key={l.id} h={h} id={l.id} />)}</div>}
      <b>{P('Đề nghị đã gửi', 'Sent offers')} ({out.length})</b>
      {out.map((t) => <div key={t.id} className="lp-card row"><span className="grow"><small>→ {acc(w, t.to)?.name} · {t.offer.length} ⇄ {t.request.length} · {ago(t.at, P)}</small></span>
        <button className="btn small ghost" onClick={() => { try { closeTrade(me, t.id, 'cancelled'); say(P('Đã hủy, thẻ được trả lại', 'Cancelled, cards returned')); } catch (e) { say(errText(e, P)); } }} data-testid={`trade-cancel-${t.id}`}>{P('Hủy', 'Cancel')}</button></div>)}
    </div>
  );
}

function History({ h }: { h: Hub }) {
  const { P, w, me } = h;
  const cat = useCatalog();
  const rows = [
    ...w.listings.filter((l) => l.status !== 'active' && (l.seller === me || l.buyer === me)).map((l) => ({ at: l.closedAt ?? l.at, text: `${l.status === 'sold' ? (l.seller === me ? P('Đã bán', 'Sold') : P('Đã mua', 'Bought')) : P('Đã hủy', 'Cancelled')} · ${cat.forms[l.card.formId]?.name ?? l.card.formId} · 🫘 ${l.price}` })),
    ...w.trades.filter((t) => t.status !== 'pending' && (t.from === me || t.to === me)).map((t) => ({ at: t.closedAt ?? t.at, text: `${P('Trao đổi', 'Trade')} ${t.from === me ? '→' : '←'} ${acc(w, t.from === me ? t.to : t.from)?.name} · ${t.status}` })),
  ].sort((a, b) => b.at - a.at);
  return <div className="lp-col">{rows.length === 0 ? <small className="muted">{P('Chưa có giao dịch.', 'No transactions yet.')}</small> : rows.map((r, i) => <small key={i}>{ago(r.at, P)} · {r.text}</small>)}</div>;
}
