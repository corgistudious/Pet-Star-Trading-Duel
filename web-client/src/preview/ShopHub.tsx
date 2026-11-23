// Shop Hub + wardrobe (Preview): booster packs, Trainer skins (try-on), avatar frames, chat bubbles, effects, evolution
// items, event items, profile decorations. Prices in in-game currencies only; each purchase is single-flight and logged.
import { useRef, useState } from 'react';
import { uiArt } from '../state/catalog';
import { Avatar, Bubble, Effect, findItem, rarityColor, skinArt, type CosItem, type Outfit } from './Cosmetics';
import { errText, type Hub } from './hub';
import { acc, guildOf, patchAccount, WorldError } from './world';

export type ShopCat = 'packs' | 'skins' | 'frames' | 'bubbles' | 'accessories' | 'evolution' | 'event' | 'profile';
const RARITIES = ['all', 'default', 'common', 'rare', 'epic', 'legendary', 'event', 'limited'];

export function ShopHub({ h, openPacks, initial = 'skins' }: { h: Hub; openPacks: () => void; initial?: ShopCat }) {
  const { P } = h;
  const [cat, setCat] = useState<ShopCat>(initial);
  const cats: [ShopCat, string, string][] = [['packs', P('Gói Thẻ', 'Booster Packs'), 'ui_pack_basic'], ['skins', P('Skin Trainer', 'Trainer Skins'), 'ui_icon_shop'], ['frames', P('Khung Avatar', 'Avatar Frames'), 'ui_crest_star'],
    ['bubbles', P('Bong Bóng Chat', 'Chat Bubbles'), 'ui_icon_mail'], ['accessories', P('Phụ Kiện', 'Accessories'), 'ui_icon_events'], ['evolution', P('Vật Phẩm Tiến Hóa', 'Evolution Items'), 'ui_icon_gifts'],
    ['event', P('Vật Phẩm Sự Kiện', 'Event Items'), 'ui_icon_season'], ['profile', P('Trang Trí Hồ Sơ', 'Profile Decor'), 'ui_icon_center']];
  return (
    <div className="sp">
      <nav className="sp-cats">{cats.map(([k, l, icon]) => <button key={k} className={cat === k ? 'on' : ''} onClick={() => (k === 'packs' ? openPacks() : setCat(k))} data-testid={`shop-${k}`}><img src={uiArt(icon)} alt="" /><span>{l}</span></button>)}</nav>
      <div className="sp-body">
        <Wallet h={h} />
        {cat === 'skins' && <Skins h={h} />}
        {(cat === 'frames' || cat === 'bubbles' || cat === 'accessories' || cat === 'profile' || cat === 'event') && <Items h={h} cat={cat} />}
        {cat === 'evolution' && <Items h={h} cat="evolution" />}
      </div>
    </div>
  );
}

function Wallet({ h }: { h: Hub }) {
  return <div className="sp-wallet"><span><img src={uiArt('ui_currency_mini_green_bean')} alt="" />{h.s.beans.toLocaleString()}</span><span><img src={uiArt('ui_currency_rainbow_star')} alt="" />{h.s.stars.toLocaleString()}</span><small className="muted">{h.P('Không có thanh toán tiền thật.', 'No real-money payments.')}</small></div>;
}

/** Single-flight purchase: the button is locked while a purchase runs, and each purchase id is recorded once. */
function usePurchase(h: Hub) {
  const busy = useRef(false);
  return (id: string, price: { currency: string; amount: number }, qty: number, grant: () => void) => {
    if (busy.current) return;
    busy.current = true;
    try {
      const total = price.amount * qty;
      const key = price.currency === 'RAINBOW_STAR' ? 'stars' : 'beans';
      if (h.s[key] < total) throw new WorldError(price.currency === 'RAINBOW_STAR' ? 'insufficient_rainbow_star' : 'insufficient_mini_green_bean');
      h.setS((x) => ({ ...x, [key]: x[key] - total }));
      grant();
      h.upd((l) => ({ ...l, purchases: [{ id: `${Date.now()}-${id}`, item: id, qty, currency: price.currency, price: total, at: Date.now() }, ...(l.purchases ?? [])].slice(0, 50) }));
      h.say(h.P('Mua thành công!', 'Purchased!'));
    } catch (e) { h.say(errText(e, h.P)); } finally { setTimeout(() => { busy.current = false; }, 400); }
  };
}

function Skins({ h }: { h: Hub }) {
  const { P, d, l, w, me, upd } = h;
  const a = acc(w, me);
  const trainer = a?.trainer ?? 'trainer_boy';
  const [tryOn, setTryOn] = useState<string | null>(null);
  const buy = usePurchase(h);
  const outfits = (d?.outfits ?? []).filter((o) => o.assetsComplete);
  const owned = (o: Outfit) => o.source === 'default' || (l.outfits ?? []).includes(o.id);
  const shown = tryOn ?? a?.outfit ?? null;
  return (
    <div className="sp-skins">
      <div className="sp-stage"><img src={skinArt(d, trainer, shown, 'happy')} alt="" /><Effect d={d} id={a?.effect} /><small>{tryOn ? P('Đang thử', 'Trying on') : P('Đang mặc', 'Wearing')}: {d?.outfits.find((o) => o.id === shown)?.name[h.lang] ?? '—'}</small></div>
      <div className="sp-list">
        {outfits.map((o) => { const mineChar = o.characterId === trainer || o.characterId === '*'; const has = owned(o); const on = (a?.outfit ?? `outfit_${trainer === 'trainer_boy' ? 'boy' : 'girl'}_default`) === o.id;
          return <div key={o.id} className="sp-item" style={{ borderColor: rarityColor(o.rarity ?? 'default') }} data-testid={`skin-${o.id}`}>
            <img src={uiArt(o.art.idle)} alt="" className="sp-skin-thumb" />
            <b className="ellipsis">{o.name[h.lang] ?? o.id}</b>
            <small>{o.characterId === 'trainer_boy' ? P('Nam', 'Male') : o.characterId === 'trainer_girl' ? P('Nữ', 'Female') : P('Dùng chung', 'Shared')} · {o.rarity ?? 'default'}</small>
            <small className="muted">{P('Không cộng sức mạnh PvP', 'No PvP stats')}</small>
            {mineChar && <button className="btn small ghost" onClick={() => setTryOn(tryOn === o.id ? null : o.id)} data-testid={`try-${o.id}`}>{tryOn === o.id ? P('Bỏ thử', 'Stop') : P('Thử', 'Try on')}</button>}
            {!mineChar ? <small>{P('Dành cho Trainer khác', 'For the other Trainer')}</small>
              : has ? (on ? <small>✅ {P('Đang dùng', 'Equipped')}</small> : <button className="btn small primary" onClick={() => { patchAccount(me, { outfit: o.id }); upd((x) => ({ ...x, outfit: o.id })); h.say(P('Đã trang bị', 'Equipped')); }} data-testid={`equip-${o.id}`}>{P('Trang bị', 'Equip')}</button>)
                : o.price && <button className="btn small primary" onClick={() => buy(o.id, o.price!, 1, () => upd((x) => ({ ...x, outfits: [...(x.outfits ?? []), o.id] })))} data-testid={`buy-${o.id}`}>{o.price.amount.toLocaleString()} {o.price.currency === 'RAINBOW_STAR' ? '⭐' : '🫘'}</button>}
          </div>; })}
        {wearingSkin(a?.outfit) && <button className="btn small ghost" onClick={() => { patchAccount(me, { outfit: null }); upd((x) => ({ ...x, outfit: null })); }} data-testid="skin-unequip">{P('Tháo skin (về mặc định)', 'Remove skin (default)')}</button>}
      </div>
      <small className="muted lp-wide">{P('Kiểu tóc và phụ kiện riêng chưa có asset hoàn chỉnh nên chưa được bán.', 'Separate hairstyles and accessories have no finished art yet, so they are not sold.')}</small>
    </div>
  );
  function wearingSkin(o: string | null | undefined) { return !!o && !o.endsWith('_default'); }
}

function Items({ h, cat }: { h: Hub; cat: ShopCat }) {
  const { P, d, l, w, me, upd, setS } = h;
  const a = acc(w, me);
  const [rar, setRar] = useState('all');
  const buy = usePurchase(h);
  const type = { frames: 'frame', bubbles: 'bubble', accessories: 'effect', profile: 'title', evolution: 'item', event: 'event' }[cat as string];
  const items = (d?.items ?? []).filter((i) => (type === 'event' ? i.unlock === 'event' : i.type === type) && (rar === 'all' || i.rarity === rar));
  const owned = (i: CosItem) => (d?.defaults ?? []).includes(i.id) || (l.owned ?? []).includes(i.id);
  const slot = (i: CosItem) => ({ frame: 'frame', bubble: 'bubble', effect: 'effect', title: 'title' } as Record<string, 'frame' | 'bubble' | 'effect' | 'title'>)[i.type];
  const equipped = (i: CosItem) => slot(i) && a?.[slot(i)] === i.id;
  const equip = (i: CosItem | null, s: 'frame' | 'bubble' | 'effect' | 'title') => {
    const def = { frame: 'frame_default', bubble: 'bubble_default', effect: null, title: 'title_rookie' }[s];
    patchAccount(me, { [s]: i?.id ?? def } as Partial<NonNullable<typeof a>>); h.say(i ? P('Đã trang bị', 'Equipped') : P('Đã tháo', 'Removed'));
  };
  const today = new Date().toISOString().slice(0, 10);
  const boughtToday = (id: string) => (l.purchases ?? []).filter((p) => p.item === id && new Date(p.at).toISOString().slice(0, 10) === today).reduce((x, p) => x + p.qty, 0);
  return (
    <div className="sp-items">
      {type !== 'item' && <div className="sp-preview">
        {type === 'frame' && <Avatar a={a} d={d} size={96} />}
        {type === 'bubble' && <Bubble d={d} id={a?.bubble}>{P('Xin chào! Đây là bong bóng của bạn.', 'Hi! This is your bubble.')}</Bubble>}
        {type === 'effect' && <span className="sp-fx"><img src={skinArt(d, a?.trainer ?? 'trainer_boy', a?.outfit)} alt="" /><Effect d={d} id={a?.effect} /></span>}
        {type === 'title' && <b className="sp-title">🏷 {findItem(d, a?.title)?.name[h.lang] ?? '—'}</b>}
      </div>}
      <div className="sh-chips">{RARITIES.map((r) => <button key={r} className={rar === r ? 'on' : ''} onClick={() => setRar(r)}>{r === 'all' ? P('Tất cả', 'All') : r}</button>)}</div>
      {items.length === 0 && <small className="muted">{cat === 'event' ? P('Chưa có vật phẩm sự kiện đang mở. Vật phẩm sự kiện là phần thưởng, không bán.', 'No event items right now. Event items are rewards, not sold.') : P('Không có vật phẩm.', 'No items.')}</small>}
      <div className="sp-grid">{items.map((i) => { const has = owned(i); const s = slot(i); const left = i.dailyLimit ? i.dailyLimit - boughtToday(i.id) : null;
        const locked = i.unlock === 'guild_member' && !guildOf(w, me);
        return <div key={i.id} className="sp-item" style={{ borderColor: rarityColor(i.rarity) }} data-testid={`item-${i.id}`}>
          <div className="sp-thumb">{i.type === 'frame' ? <Avatar a={{ trainer: a?.trainer ?? 'trainer_boy', outfit: a?.outfit ?? null, frame: i.id }} d={d} size={58} />
            : i.type === 'bubble' ? <Bubble d={d} id={i.id}>Aa</Bubble> : i.type === 'effect' ? <span className="sp-fx small"><Effect d={d} id={i.id} />✦</span> : i.type === 'title' ? <b>🏷</b> : <img src={uiArt('ui_icon_gifts')} alt="" />}</div>
          <b className="ellipsis" title={i.name[h.lang]}>{i.name[h.lang]}</b>
          <small style={{ color: rarityColor(i.rarity) }}>{i.rarity}</small>
          <small className="muted sp-desc">{i.desc[h.lang]}</small>
          {i.type === 'item' ? <>{left != null && <small>{P('Còn hôm nay', 'Left today')}: {left}</small>}
            <button className="btn small primary" disabled={left === 0} onClick={() => buy(i.id, i.price!, 1, () => setS((x) => ({ ...x, materials: Object.entries(i.grant ?? {}).reduce((m, [k, n]) => ({ ...m, [k]: (m[k] ?? 0) + n }), x.materials) })))} data-testid={`buy-${i.id}`}>{i.price!.amount} {i.price!.currency === 'RAINBOW_STAR' ? '⭐' : '🫘'}</button></>
            : has ? (equipped(i) ? <><small>✅ {P('Đang dùng', 'Equipped')}</small>{!(d?.defaults ?? []).includes(i.id) && <button className="btn small ghost" onClick={() => equip(null, s!)} data-testid={`unequip-${i.id}`}>{P('Tháo', 'Remove')}</button>}</>
              : <button className="btn small primary" onClick={() => equip(i, s!)} data-testid={`equip-${i.id}`}>{P('Trang bị', 'Equip')}</button>)
              : i.price ? <button className="btn small primary" disabled={locked} title={locked ? P('Cần tham gia Guild', 'Requires a guild') : ''} onClick={() => buy(i.id, i.price!, 1, () => upd((x) => ({ ...x, owned: [...(x.owned ?? []), i.id] })))} data-testid={`buy-${i.id}`}>{i.price.amount.toLocaleString()} {i.price.currency === 'RAINBOW_STAR' ? '⭐' : '🫘'}</button>
                : <small className="muted">🔒 {i.unlock === 'ranking' ? P('Phần thưởng xếp hạng', 'Ranking reward') : i.unlock === 'event' ? P('Phần thưởng sự kiện', 'Event reward') : i.unlock === 'codex_50' ? P('Mở khi có 50 Pet', 'Unlocks at 50 Pets') : P('Chưa sở hữu', 'Not owned')}</small>}
        </div>; })}</div>
    </div>
  );
}
