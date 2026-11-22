import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { CardBack, CardView } from '../components/CardView';
import { useI18n } from '../i18n';
import { RARITY_COLORS, petArt, uiArt, useCatalog } from '../state/catalog';
import { Duel } from './Duel';
import {
  RARITY_ORDER, VIP_THRESHOLDS, applyUpgrade, blocked, cost, evolution, levelCap, openPack, packBlocked, packOdds, seed, toDto, vipLevel,
  type FullCatalog, type PackDef, type SimCard, type SimState,
} from './sim';

type Screen = { name: 'lobby' | 'shop' | 'collection' | 'divine' | 'market' | 'battle' | 'status' } | { name: 'open'; cards: SimCard[]; pack: PackDef } | { name: 'hub'; id: number };
const KEY = 'petstar.preview.state.v1';

function load(cat: FullCatalog): SimState {
  try { const j = sessionStorage.getItem(KEY); if (j) return JSON.parse(j) as SimState; } catch { /* storage blocked: fresh sandbox */ }
  return seed(cat);
}

export default function PreviewApp() {
  const cat = useCatalog() as FullCatalog;
  const { lang, setLang, t } = useI18n();
  const P = (vi: string, en: string) => (lang === 'vi' ? vi : en);
  const [s, setS] = useState<SimState>(() => load(cat));
  const [screen, setScreen] = useState<Screen>({ name: 'lobby' });
  const [toast, setToast] = useState<string | null>(null);
  useEffect(() => { try { sessionStorage.setItem(KEY, JSON.stringify(s)); } catch { /* ignore */ } }, [s]);
  useEffect(() => { if (!toast) return; const h = setTimeout(() => setToast(null), 2600); return () => clearTimeout(h); }, [toast]);
  const say = (m: string) => setToast(m);
  const err = (code: string) => say(t('error.' + code));
  const packs = useMemo(() => [...cat.packs.packTypes].sort((a, b) => a.order - b.order), [cat]);
  const go = (sc: Screen) => { setScreen(sc); window.scrollTo(0, 0); };
  const landscape = screen.name === 'battle';

  return (
    <div className={'pv-stage ' + (landscape ? 'pv-landscape' : 'pv-portrait')}>
      <div className="pv-frame">
        <div className="pv-banner" role="note">
          {P('BẢN XEM TRƯỚC · dữ liệu mô phỏng trong trình duyệt · không đăng nhập · không kết nối máy chủ · không ghi database', 'PREVIEW · simulated data in this browser · no login · no server · nothing is written to any database')}
        </div>
        {screen.name !== 'lobby' && screen.name !== 'battle' && (
          <div className="pv-top">
            <button className="btn ghost small" data-testid="back" onClick={() => go({ name: screen.name === 'hub' ? 'collection' : 'lobby' })}>‹ {P('Quay lại', 'Back')}</button>
            <Wallet s={s} />
          </div>
        )}
        <div className="pv-body">
          {screen.name === 'lobby' && <Lobby s={s} go={go} P={P} lang={lang} setLang={setLang} />}
          {screen.name === 'shop' && <Shop s={s} packs={packs} P={P} lang={lang} t={t}
            onBuy={(p) => {
              const b = packBlocked(p, s); if (b) return err(b);
              setS((x) => ({
                ...x, [p.price.currency === 'RAINBOW_STAR' ? 'stars' : 'beans']: (p.price.currency === 'RAINBOW_STAR' ? x.stars : x.beans) - p.price.amount,
                packs: { ...x.packs, [p.id]: (x.packs[p.id] ?? 0) + 1 },
                player: { ...x.player, vipPoints: x.player.vipPoints + (p.price.currency === 'RAINBOW_STAR' ? p.price.amount : 0) },
                event: p.requirements.requiresEvent ? { ...x.event, sold: x.event.sold + 1, mine: x.event.mine + 1 } : x.event,
                invitation: p.requirements.requiresInvitation && x.invitation ? { ...x.invitation, left: x.invitation.left - 1 } : x.invitation,
              }));
              say(P('Đã mua 1 gói (mô phỏng)', 'Bought 1 pack (simulated)'));
            }}
            onOpen={(p) => {
              if ((s.packs[p.id] ?? 0) < 1) return;
              const cards = openPack(cat, p, s.nextId);
              setS((x) => ({ ...x, packs: { ...x.packs, [p.id]: x.packs[p.id] - 1 }, cards: [...cards, ...x.cards], nextId: x.nextId + 5 }));
              go({ name: 'open', cards, pack: p });
            }} />}
          {screen.name === 'open' && <Opening cards={screen.cards} pack={screen.pack} P={P} t={t} done={() => go({ name: 'collection' })} />}
          {screen.name === 'collection' && <Collection s={s} P={P} open={(id) => go({ name: 'hub', id })} />}
          {screen.name === 'hub' && <Hub s={s} id={screen.id} P={P} t={t} setS={setS} say={say} err={err} />}
          {screen.name === 'divine' && <Divine s={s} P={P} />}
          {screen.name === 'market' && <Market s={s} setS={setS} P={P} say={say} err={err} />}
          {screen.name === 'battle' && <Duel s={s} P={P} exit={() => go({ name: 'lobby' })} />}
          {screen.name === 'status' && <Status P={P} />}
        </div>
        {toast && <div className="pv-toast" role="status">{toast}</div>}
      </div>
    </div>
  );
}

type PFn = (vi: string, en: string) => string;

function Wallet({ s }: { s: SimState }) {
  return (
    <div className="pv-wallet">
      <span><img src={uiArt('ui_currency_mini_green_bean')} alt="" />{s.beans.toLocaleString('vi-VN')}</span>
      <span><img src={uiArt('ui_currency_rainbow_star')} alt="" />{s.stars.toLocaleString('vi-VN')}</span>
    </div>
  );
}

function Lobby({ s, go, P, lang, setLang }: { s: SimState; go: (sc: Screen) => void; P: PFn; lang: string; setLang: (l: 'vi' | 'en') => void }) {
  const vip = vipLevel(s.player.vipPoints);
  const items: [Screen['name'], string, string][] = [
    ['shop', P('Cửa hàng · 9 gói thẻ', 'Shop · 9 packs'), 'ui_pack_divine_premium'],
    ['collection', P('Bộ sưu tập · Pet Hub', 'Collection · Pet Hub'), 'ui_mat_pet_shard'],
    ['divine', P('15 Thần Pet Card', '15 Divine Pet Cards'), 'ui_crest_star'],
    ['market', P('Chợ giao dịch', 'Marketplace'), 'ui_currency_rainbow_star_bag'],
    ['battle', P('Đấu bài chiến thuật (16:9)', 'Tactical Duel (16:9)'), 'ui_mat_breakthrough_core'],
    ['status', P('Trạng thái bản xem trước', 'Preview status'), 'ui_mat_training_scroll'],
  ];
  return (
    <div className="pv-lobby" style={{ backgroundImage: `url(${uiArt('bg_lobby_portrait')})` }}>
      <div className="pv-lobby-head">
        <img className="pv-logo" src={`${import.meta.env.BASE_URL}brand/logo-512.webp`} alt="Pet Star: Trading Duel"
          onError={(e) => { (e.target as HTMLImageElement).src = `${import.meta.env.BASE_URL}icons/icon-512.png`; }} />
        <div className="pv-player">
          <b>{s.player.nickname}</b>
          <span>UID {s.player.uid} · Lv {s.player.level} · VIP {vip}</span>
          <small>{s.player.vipPoints.toLocaleString()} / {VIP_THRESHOLDS[Math.min(10, vip + 1)].toLocaleString()} {P('điểm VIP', 'VIP pts')}</small>
        </div>
        <button className="btn ghost small" onClick={() => setLang(lang === 'vi' ? 'en' : 'vi')}>{lang === 'vi' ? 'EN' : 'VI'}</button>
      </div>
      <Wallet s={s} />
      <div className="pv-menu">
        {items.map(([k, label, icon]) => (
          <button key={k} className="pv-tile" onClick={() => go({ name: k } as Screen)} data-testid={'go-' + k}>
            <img src={uiArt(icon)} alt="" /><span>{label}</span>
          </button>
        ))}
      </div>
      <button className="btn ghost small pv-reset" onClick={() => { try { sessionStorage.removeItem('petstar.preview.state.v1'); } catch { /* */ } location.reload(); }}>
        {P('Đặt lại dữ liệu thử nghiệm', 'Reset test data')}
      </button>
    </div>
  );
}

function Shop({ s, packs, P, lang, t, onBuy, onOpen }: { s: SimState; packs: PackDef[]; P: PFn; lang: string; t: (k: string, ...a: (string | number)[]) => string; onBuy: (p: PackDef) => void; onOpen: (p: PackDef) => void }) {
  const [odds, setOdds] = useState<PackDef | null>(null);
  return (
    <div>
      <h2 className="pv-h">{P('Cửa hàng', 'Shop')} · VIP {vipLevel(s.player.vipPoints)} · Lv {s.player.level}</h2>
      <div className="pv-packs">
        {packs.map((p) => {
          const b = packBlocked(p, s); const r = p.requirements;
          return (
            <div key={p.id} className={'pv-pack theme-' + p.theme} data-testid={'pack-' + p.id}>
              <img src={uiArt(p.artAssetId)} alt="" />
              <b>{p.names[lang] ?? p.names.en}</b>
              <small className="muted">{p.descriptions[lang] ?? p.descriptions.en}</small>
              <span className="pv-price"><img src={uiArt(p.price.currency === 'RAINBOW_STAR' ? 'ui_currency_rainbow_star' : 'ui_currency_mini_green_bean')} alt="" />{p.price.amount}</span>
              <small>{[r.minLevel ? t('pack.req_level', r.minLevel) : '', r.minVip ? t('pack.req_vip', r.minVip) : '',
                r.requiresEvent ? `${P(s.event.nameVi, s.event.nameEn)} · ${t('pack.event_left', s.event.sold, s.event.total)}` : '',
                r.requiresInvitation ? (s.invitation ? t('pack.invite_left', s.invitation.left) : t('pack.req_invite')) : ''].filter(Boolean).join(' · ')}</small>
              {b && <small className="error-text">{t('error.' + b)}</small>}
              <div className="row gap">
                <button className="btn primary small" disabled={!!b} onClick={() => onBuy(p)}>{t('shop.buy')}</button>
                <button className="btn small" disabled={(s.packs[p.id] ?? 0) < 1} onClick={() => onOpen(p)}>{t('shop.open')} ({s.packs[p.id] ?? 0})</button>
                <button className="btn ghost small" onClick={() => setOdds(p)}>{t('pack.odds')}</button>
              </div>
            </div>
          );
        })}
      </div>
      {odds && (
        <div className="pv-modal" onClick={() => setOdds(null)}>
          <div className="pv-sheet" onClick={(e) => e.stopPropagation()}>
            <h3>{t('pack.odds_title')} · {odds.names[lang]}</h3>
            <p className="muted small">{t('pack.odds_same_table')}</p>
            <table className="odds-pack"><thead><tr><th>{t('rarity.title')}</th><th>{t('pack.odds_per_slot')}</th><th>{t('pack.odds_per_pack')}</th></tr></thead>
              <tbody>{packOdds(odds).map((r) => <tr key={r.rarity}><td style={{ color: RARITY_COLORS[r.rarity] }}>{r.rarity === 'DV' ? t('rarity.DV') : r.rarity}</td><td>{+r.perSlot.toFixed(4)}%</td><td>{+r.perPack.toFixed(4)}%</td></tr>)}</tbody></table>
            {odds.divinePool.length > 0 && <>
              <p><b>{t('pack.odds_divine', (odds.slotTable.DV ?? 0) / 1e4, (odds.slotTable.DV ?? 0) / 1e4 / odds.divinePool.length)}</b></p>
              <div className="pv-mini">{odds.divinePool.map((f) => <img key={f} src={petArt(f, true)} alt={t('pet.' + f)} title={t('pet.' + f)} />)}</div>
            </>}
            <button className="btn" onClick={() => setOdds(null)}>{t('common.close')}</button>
          </div>
        </div>
      )}
    </div>
  );
}

function Opening({ cards, pack, P, t, done }: { cards: SimCard[]; pack: PackDef; P: PFn; t: (k: string) => string; done: () => void }) {
  const cat = useCatalog();
  const [shown, setShown] = useState(0);
  const order = RARITY_ORDER;
  const best = cards.reduce((m, c) => Math.max(m, order.indexOf(c.rarity)), 0);
  return (
    <div className="pv-open" style={{ backgroundImage: `url(${uiArt('bg_pack_opening')})` }}>
      <h2 className="pv-h">{pack.names.vi} · {P('chạm để lật thẻ', 'tap to flip')}</h2>
      <div className="pv-open-grid">
        {cards.map((c, i) => (
          <div key={c.id} className={'pv-flip' + (i < shown ? ' up' : '') + (c.divineSet ? ' divine' : order.indexOf(c.rarity) >= 3 ? ' rare' : '')}
            style={{ ['--glow' as string]: RARITY_COLORS[c.rarity] }} onClick={() => i === shown && setShown(shown + 1)}>
            {i < shown ? <CardView card={toDto(cat, c)} compact /> : <CardBack />}
          </div>
        ))}
      </div>
      {shown < 5 ? <button className="btn primary" data-testid="reveal-all" onClick={() => setShown(5)}>{P('Lật tất cả', 'Reveal all')}</button> : (
        <>
          {cards.some((c) => c.divineSet) && <p className="pv-divine-msg">★ {t('divine.obtained')} ★</p>}
          <p className="muted">{P('Thẻ hiếm nhất', 'Best card')}: {order[best]}</p>
          <button className="btn primary" data-testid="to-collection" onClick={done}>{P('Xem bộ sưu tập', 'Go to collection')}</button>
        </>
      )}
    </div>
  );
}

function Collection({ s, P, open }: { s: SimState; P: PFn; open: (id: number) => void }) {
  const cat = useCatalog();
  const [filter, setFilter] = useState<'all' | 'divine' | 'upgraded'>('all');
  const list = s.cards.filter((c) => filter === 'all' || (filter === 'divine' ? !!c.divineSet : c.stars > 1 || c.enhance > 0))
    .map((c) => toDto(cat, c)).sort((a, b) => b.power - a.power);
  return (
    <div>
      <h2 className="pv-h">{P('Bộ sưu tập', 'Collection')} · {s.cards.length} {P('thẻ', 'cards')}</h2>
      <div className="row gap">{(['all', 'divine', 'upgraded'] as const).map((f) => (
        <button key={f} className={'btn small ' + (filter === f ? 'primary' : 'ghost')} onClick={() => setFilter(f)}>{{ all: P('Tất cả', 'All'), divine: P('Thần Pet', 'Divine'), upgraded: P('Đã nâng cấp', 'Upgraded') }[f]}</button>
      ))}</div>
      <div className="pv-grid">{list.map((c) => <CardView key={c.id} card={c} compact onClick={() => open(c.id)} />)}</div>
    </div>
  );
}

const KINDS = ['star', 'enhance', 'evolve', 'breakthrough', 'awaken', 'skill'] as const;

function Hub({ s, id, P, t, setS, say, err }: { s: SimState; id: number; P: PFn; t: (k: string, ...a: (string | number)[]) => string; setS: (f: (x: SimState) => SimState) => void; say: (m: string) => void; err: (c: string) => void }) {
  const cat = useCatalog();
  const [kind, setKind] = useState<(typeof KINDS)[number]>('star');
  const [burst, setBurst] = useState(0);
  const card = s.cards.find((c) => c.id === id);
  if (!card) return <p>{P('Không tìm thấy thẻ', 'Card not found')}</p>;
  const dto = toDto(cat, card);
  const replace = (n: SimCard, spend: { beans: number; items: Record<string, number> }) => setS((x) => ({
    ...x, beans: x.beans - spend.beans, cards: x.cards.map((c) => (c.id === n.id ? n : c)),
    materials: Object.fromEntries(Object.entries(x.materials).map(([k, v]) => [k, v - (spend.items[k] ?? 0)])),
    essences: Object.fromEntries(Object.entries(x.essences).map(([k, v]) => [k, v - (spend.items[k] ?? 0)])),
  }));
  let body: ReactNode;
  if (kind === 'evolve') {
    const ev = evolution(cat, card);
    if (!ev) body = <p>{card.divineSet ? P('Thần Pet là hình thái duy nhất, không tiến hóa.', 'Divine Pets are single-form: no evolution.') : t('upgrade.max')}</p>;
    else {
      const next = toDto(cat, { ...card, formId: ev.next });
      const why = card.listed ? 'card_busy' : card.level < ev.minLevel ? 'evolution_leveltoolow' : (s.essences[ev.essenceId] ?? 0) < ev.essence ? 'evolution_notenoughessence' : s.beans < ev.beans ? 'not_enough_beans' : null;
      body = (
        <div>
          <p>{P('Tiến hóa giữ nguyên serial, sao, cường hóa và lịch sử.', 'Evolution keeps serial, stars, enhancement and history.')}</p>
          <p>{t('upgrade.cost')}: {ev.beans} {t('currency.mini_green_bean')} · {ev.essence} × {t('item.' + ev.essenceId)} ({s.essences[ev.essenceId] ?? 0}) · {t('upgrade.needs_level', ev.minLevel)}</p>
          <div className="pv-evo"><CardView card={dto} compact /><span>→</span><CardView card={next} compact /></div>
          {why && <p className="error-text">{t('error.' + why)}</p>}
          <button className="btn primary big" disabled={!!why} onClick={() => { replace({ ...card, formId: ev.next }, { beans: ev.beans, items: { [ev.essenceId]: ev.essence } }); setBurst((b) => b + 1); say(P('Tiến hóa thành công (mô phỏng)', 'Evolved (simulated)')); }}>{t('card.evolve')}</button>
        </div>
      );
    }
  } else {
    const k = cost(kind, card); const b = blocked(kind, card, s);
    const after = k ? toDto(cat, applyUpgrade(kind, card)) : null;
    const cur = { star: card.stars, enhance: card.enhance, breakthrough: card.breakthrough, awaken: card.awakened ? 1 : 0, skill: card.skillLevel }[kind];
    const max = { star: 10, enhance: 20, breakthrough: 4, awaken: 1, skill: 10 }[kind];
    body = (
      <div>
        <h3>{t('upgrade.' + kind)}: {cur} / {max}</h3>
        {kind === 'enhance' && <p className="muted small">{t('upgrade.always_succeeds')}</p>}
        {!k ? <p>{t('upgrade.max')}</p> : (
          <>
            <div className="pv-cost">
              <span><img src={uiArt('ui_currency_mini_green_bean')} alt="" />{k.beans} ({s.beans})</span>
              {Object.entries(k.items).map(([it, n]) => <span key={it}><img src={uiArt('ui_mat_' + it)} alt="" />{n} × {t('item.' + it)} ({s.materials[it] ?? 0})</span>)}
            </div>
            {after && <p>{t('upgrade.preview')}: {t('card.power')} {dto.power} → <b>{after.power}</b> · HP {dto.hp} → {after.hp} · ATK {dto.atk} → {after.atk}</p>}
            {b && b !== 'max_reached' && <p className="error-text">{t('error.' + b)}</p>}
            <button className="btn primary big" disabled={!!b} data-testid={'up-' + kind}
              onClick={() => { const bb = blocked(kind, card, s); if (bb) return err(bb); replace(applyUpgrade(kind, card), k); setBurst((x) => x + 1); say(t('upgrade.done')); }}>{t('upgrade.confirm')}</button>
          </>
        )}
      </div>
    );
  }
  return (
    <div className="pv-hub">
      <div className="pv-hub-card">
        <div key={burst} className={burst ? 'pv-burst' : ''}><CardView card={dto} /></div>
        <div>
          <b>{t('pet.' + card.formId) !== 'pet.' + card.formId ? t('pet.' + card.formId) : dto.name}</b>
          {card.divineSet && <div className="gold">{t('divine.' + card.divineSet)}</div>}
          <div className="pv-stars">{'★'.repeat(card.stars)}<span>{'★'.repeat(10 - card.stars)}</span>{card.enhance > 0 && <em> +{card.enhance}</em>}</div>
          <small>Lv {card.level}/{levelCap(card.breakthrough)} · {t('card.power')} {dto.power}{card.awakened ? ' · ' + t('upgrade.awakened') : ''}</small>
          <small>HP {dto.hp} · ATK {dto.atk} · DEF {dto.def} · SPD {dto.spd}</small>
          <button className="btn ghost small" onClick={() => setS((x) => ({ ...x, cards: x.cards.map((c) => (c.id === id ? { ...c, level: Math.min(levelCap(c.breakthrough), c.level + 10) } : c)) }))}>
            +10 Lv ({P('nút thử nghiệm', 'test button')})
          </button>
        </div>
      </div>
      <div className="pv-tabs">{KINDS.map((x) => <button key={x} className={'btn small ' + (kind === x ? 'primary' : 'ghost')} onClick={() => setKind(x)}>{x === 'evolve' ? t('card.evolve') : t('upgrade.' + x)}</button>)}</div>
      {body}
      <p className="muted small">{P('Khắc ấn và Tái thiết lập có trong game thật (Unity/Web) — bản xem trước chưa mô phỏng.', 'Engravings and Reset exist in the real game (Unity/Web) — not simulated in this preview.')}</p>
    </div>
  );
}

function Divine({ s, P }: { s: SimState; P: PFn }) {
  const cat = useCatalog() as FullCatalog;
  const { t } = useI18n();
  const owned = new Set(s.cards.filter((c) => c.divineSet).map((c) => c.formId));
  return (
    <div>
      <h2 className="pv-h">{P('15 Thần Pet Card · 10 sao khi nhận', '15 Divine Pet Cards · 10 stars on arrival')}</h2>
      {(['premium', 'limited', 'star'] as const).map((set) => {
        const pack = cat.packs.packTypes.find((p) => p.theme === set)!;
        return (
          <section key={set}>
            <h3 className="pv-set"><img src={uiArt('ui_crest_' + set)} alt="" />{t('divine.' + set)} · {(pack.slotTable.DV ?? 0) / 1e4}% / {P('thẻ', 'card')}</h3>
            <div className="pv-grid">{pack.divinePool.map((f) => (
              <CardView key={f} card={toDto(cat, { id: 0, serial: owned.has(f) ? P('ĐÃ SỞ HỮU', 'OWNED') : '', formId: f, rarity: 'DV', quality: 'A', finish: 'Prismatic', level: 1, stars: 10, enhance: 0, breakthrough: 0, awakened: false, skillLevel: 1, divineSet: set, origin: '' })} compact />
            ))}</div>
          </section>
        );
      })}
    </div>
  );
}

function Market({ s, setS, P, say, err }: { s: SimState; setS: (f: (x: SimState) => SimState) => void; P: PFn; say: (m: string) => void; err: (c: string) => void }) {
  const cat = useCatalog();
  const [sell, setSell] = useState(false);
  return (
    <div>
      <h2 className="pv-h">{P('Chợ giao dịch', 'Marketplace')} · {P('phí 3%', '3% fee')}</h2>
      <p className="muted small">{P('Sao Ngũ Sắc chỉ chuyển giữa người chơi, không bao giờ đổi ra tiền thật.', 'Rainbow Stars only move between players and can never be exchanged for real money.')}</p>
      <button className="btn small" onClick={() => setSell(!sell)}>{sell ? P('Xem tin đăng', 'Show listings') : P('Đăng bán thẻ của tôi', 'List one of my cards')}</button>
      {!sell ? (
        <div className="pv-list">{s.listings.map((l) => (
          <div key={l.id} className="pv-listing">
            <CardView card={toDto(cat, l.card)} compact />
            <div>
              <b>{l.price.toLocaleString()} <img src={uiArt(l.currency === 'RAINBOW_STAR' ? 'ui_currency_rainbow_star' : 'ui_currency_mini_green_bean')} alt="" /></b>
              <small>{l.mine ? P('Tin của bạn', 'Your listing') : `${P('Người bán', 'Seller')}: ${l.seller}`}</small>
              <small>★{l.card.stars} · +{l.card.enhance} · {l.card.serial}</small>
              {l.mine ? <button className="btn ghost small" onClick={() => setS((x) => ({ ...x, listings: x.listings.filter((y) => y.id !== l.id), cards: x.cards.map((c) => (c.id === l.card.id ? { ...c, listed: false } : c)) }))}>{P('Hủy', 'Cancel')}</button> :
                <button className="btn primary small" onClick={() => {
                  const bal = l.currency === 'RAINBOW_STAR' ? s.stars : s.beans;
                  if (bal < l.price) return err(l.currency === 'RAINBOW_STAR' ? 'insufficient_rainbow_star' : 'insufficient_mini_green_bean');
                  setS((x) => ({ ...x, [l.currency === 'RAINBOW_STAR' ? 'stars' : 'beans']: bal - l.price, listings: x.listings.filter((y) => y.id !== l.id),
                    cards: [{ ...l.card, id: x.nextId, listed: false }, ...x.cards], nextId: x.nextId + 1 }));
                  say(P('Đã mua — thẻ giữ nguyên sao, cường hóa, serial (mô phỏng)', 'Bought — stars, enhancement and serial kept (simulated)'));
                }}>{P('Mua', 'Buy')}</button>}
            </div>
          </div>
        ))}</div>
      ) : (
        <div className="pv-grid">{s.cards.filter((c) => !c.listed).map((c) => (
          <CardView key={c.id} card={toDto(cat, c)} compact onClick={() => {
            const price = Math.max(10, Math.round(toDto(cat, c).power / 10));
            setS((x) => ({ ...x, cards: x.cards.map((y) => (y.id === c.id ? { ...y, listed: true } : y)),
              listings: [{ id: x.nextId, card: { ...c, listed: true }, seller: x.player.nickname, currency: 'RAINBOW_STAR', price, mine: true }, ...x.listings], nextId: x.nextId + 1 }));
            setSell(false); say(P(`Đã đăng bán ${price} Sao Ngũ Sắc; thẻ bị khóa khi đang bán (mô phỏng)`, `Listed for ${price} Rainbow Stars; the card is locked while listed (simulated)`));
          }} />
        ))}</div>
      )}
    </div>
  );
}

function Status({ P }: { P: PFn }) {
  const done = [
    P('Lobby dọc 9:16, chiến đấu ngang 16:9, iPhone và PC', 'Portrait 9:16 lobby, landscape 16:9 battle, iPhone and PC'),
    P('Cửa hàng 9 gói với giá, điều kiện Level/VIP/sự kiện/lời mời, bảng tỷ lệ mỗi thẻ và mỗi gói', 'Shop with 9 packs: prices, Level/VIP/event/invitation conditions, odds per card and per pack'),
    P('Mở gói 5 thẻ dùng đúng bảng tỷ lệ và nhóm Thần Pet thật', 'Opening 5 cards with the real odds tables and divine pools'),
    P('Bộ sưu tập, Pet Hub: nâng sao, cường hóa, tiến hóa, đột phá, thức tỉnh, kỹ năng (công thức như server)', 'Collection, Pet Hub: stars, enhancement, evolution, breakthrough, awakening, skill (server formulas)'),
    P('15 Thần Pet Card 10 sao với artwork, khung và huy hiệu thật', '15 Divine Pet Cards at 10 stars with real art, frames and crests'),
    P('Chợ giao dịch: mua, đăng bán, hủy, khóa thẻ khi đang bán', 'Marketplace: buy, list, cancel, card locked while listed'),
    P('Đấu bài 40 lá: 5 ô Pet + 3 ô Spell/Trap, 2.500 LP, chuỗi phản ứng, tiến hóa trong trận (engine đối chiếu server)', '40-card duel: 5 Pet + 3 Spell/Trap zones, 2,500 LP, reaction chain, in-duel evolution (server-verified engine)'),
    P('Asset thật: 215 Pet, 9 gói, nguyên liệu, huy hiệu, nền, logo chính thức', 'Real assets: 215 Pets, 9 packs, materials, crests, backgrounds, official logo'),
  ];
  const missing = [
    P('Chiến đấu trong bản xem trước là mô phỏng đơn giản hóa; trận thật do server tính (hiệu ứng trạng thái, khiên, đặc tính chưa hiển thị đầy đủ ở đây)', 'Preview battle is simplified; real battles are server-resolved (statuses, shields, passives not fully shown here)'),
    P('Khắc ấn và Tái thiết lập: có trong game thật, chưa mô phỏng ở bản xem trước', 'Engravings and Reset: in the real game, not simulated in the preview'),
    P('Âm thanh riêng từng gói và hiệu ứng mở gói riêng từng gói: chưa có (dùng hiệu ứng chung)', 'Per-pack sounds and per-pack opening animations: not made yet (shared effect)'),
    P('Đăng nhập, máy chủ, PvP realtime, Ranked, bang hội, chat: chỉ có trong game thật', 'Login, server, realtime PvP, Ranked, guilds, chat: real game only'),
    P('Bản Unity WebGL chưa được build bằng Unity Editor thật', 'The Unity WebGL build has not been built in a real Unity Editor yet'),
  ];
  return (
    <div className="pv-status">
      <h2 className="pv-h">{P('Trạng thái bản xem trước', 'Preview status')}</h2>
      <h3>✅ {P('Đã có', 'Done')}</h3><ul>{done.map((x) => <li key={x}>{x}</li>)}</ul>
      <h3>⏳ {P('Còn thiếu / chỉ mô phỏng', 'Missing / simulated only')}</h3><ul>{missing.map((x) => <li key={x}>{x}</li>)}</ul>
      <p className="muted small">{P('Dữ liệu chỉ lưu trong tab trình duyệt này (sessionStorage) và mất khi đóng tab. Không có request nào tới API hay database.', 'Data lives only in this browser tab (sessionStorage) and disappears when it closes. No request goes to any API or database.')}</p>
    </div>
  );
}
