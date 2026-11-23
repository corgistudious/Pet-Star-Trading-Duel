// New main lobby for the Interactive Web Preview: landscape 16:9 layout inspired by the owner's reference (layout and
// interaction only; every character, Pet, background and icon is original Pet Star art). Header with avatar/name/UID/
// level, both currencies, settings/notifications/help; Messages and Friends on the left; the chosen chibi Trainer on a
// podium with the companion Pet; four hexagon hubs; utility bar with NEW badges. All panels work on preview data.
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { petArt, uiArt, useCatalog } from '../state/catalog';
import { GAME_LIMITS, MiniGames, rewardFor, today, type GameId } from './MiniGames';
import { ChatDock } from './ChatDock';
import { Avatar, Effect, skinArt, useCosmetics } from './Cosmetics';
import type { Hub } from './hub';
import { Leaderboards } from './Leaderboard';
import { EVENT, EXP_PER_LEVEL, GIFTCODES, MISSIONS, SEASON_TIERS, type LobbyState } from './lobbyState';
import { MarketHub } from './MarketHub';
import { Profile } from './Profile';
import { ShopHub, type ShopCat } from './ShopHub';
import { SocialHub, type SocialTab } from './SocialHub';
import { acc, guildOf, incoming, myNotifs, useWorld, type CardSnap } from './world';
import { stats, type SimCard, type SimState } from './sim';
import type { ReplayData } from './TeamBattle';

type PFn = (vi: string, en: string) => string;
type Upd = (f: (l: LobbyState) => LobbyState) => void;
export type Panel = 'profile' | 'settings' | 'notices' | 'help' | 'mail' | 'friends' | 'starter' | 'center' | 'gifts' | 'season' | 'events'
  | 'social' | 'market' | 'shop' | 'viewProfile' | 'games' | 'world' | 'battle' | 'team' | 'missions' | 'leaders' | 'history' | 'companion' | 'pve' | 'admin';
export type Legacy = 'shop' | 'collection' | 'divine' | 'market' | 'status';
export interface BattleStart { mode: 'pve' | 'boss' | 'practice' | 'hotseat' | 'war'; stageId?: string; replay?: ReplayData; war?: { team: CardSnap[]; name: string } }

const BEANS = 'MINI_GREEN_BEAN', STARS = 'RAINBOW_STAR';
export const trainerArt = (id: string, frame: 'idle' | 'blink' | 'happy' = 'idle') => uiArt(frame === 'idle' ? id : `${id}_${frame}`);

// ------------------------------------------------------------------ login + trainer choice

export function LoginScreen({ P, known, onLogin, accounts = [] }: { P: PFn; known: string | null; onLogin: (name: string) => void; accounts?: string[] }) {
  const [name, setName] = useState(known ?? '');
  return (
    <div className="lb-gate" style={{ backgroundImage: `url(${uiArt('bg_lobby_main')})` }}>
      <div className="lb-gate-card">
        <img src={`${import.meta.env.BASE_URL}brand/logo-512.webp`} alt="Pet Star: Trading Duel" className="lb-logo" onError={(e) => ((e.target as HTMLImageElement).style.display = 'none')} />
        <b>{known ? P('Đăng nhập lại', 'Sign in again') : P('Tạo tài khoản Preview', 'Create a Preview account')}</b>
        <small>{P('Tài khoản Preview chỉ lưu trong trình duyệt này (không phải tài khoản Production). Đăng ký/đăng nhập thật dùng máy chủ.', 'Preview accounts live only in this browser (not a Production account). Real sign-up/login uses the server.')}</small>
        <input className="lb-input" value={name} maxLength={24} placeholder={P('Tên Trainer', 'Trainer name')} onChange={(e) => setName(e.target.value)} data-testid="login-name" />
        <button className="btn primary" disabled={name.trim().length < 2} onClick={() => onLogin(name.trim())} data-testid="login-go">{accounts.some((a) => a.toLowerCase() === name.trim().toLowerCase()) ? P('Đăng nhập', 'Sign in') : P('Đăng ký & vào game', 'Sign up & play')}</button>
        {accounts.length > 0 && <div className="lb-accounts"><small>{P('Tài khoản Preview trên trình duyệt này:', 'Preview accounts in this browser:')}</small>{accounts.map((a) => <button key={a} className="btn small" onClick={() => onLogin(a)} data-testid={`login-as-${a}`}>{a}</button>)}</div>}
      </div>
    </div>
  );
}

export function TrainerSelect({ P, onPick }: { P: PFn; onPick: (id: 'trainer_boy' | 'trainer_girl') => void }) {
  const [pick, setPick] = useState<'trainer_boy' | 'trainer_girl'>('trainer_boy');
  return (
    <div className="lb-gate" style={{ backgroundImage: `url(${uiArt('bg_lobby_main')})` }}>
      <div className="lb-select">
        <b className="lb-select-title">{P('Chọn Trainer của bạn', 'Choose your Trainer')}</b>
        <div className="lb-select-row">
          {(['trainer_boy', 'trainer_girl'] as const).map((id) => (
            <button key={id} className={'lb-choice' + (pick === id ? ' on' : '')} onClick={() => setPick(id)} data-testid={`pick-${id}`}>
              <img src={trainerArt(id, pick === id ? 'happy' : 'idle')} alt="" />
              <span>{id === 'trainer_boy' ? P('Trainer Nam', 'Male Trainer') : P('Trainer Nữ', 'Female Trainer')}</span>
              <small>{id === 'trainer_boy' ? P('Trang phục: Nhà Thám Hiểm Bầu Trời', 'Outfit: Sky Explorer') : P('Trang phục: Nhà Thám Hiểm Bạc Hà', 'Outfit: Mint Explorer')}</small>
            </button>
          ))}
        </div>
        <small>{P('Lựa chọn được lưu vào hồ sơ. Chưa hỗ trợ đổi Nam/Nữ (chờ chính sách).', 'Your choice is saved to your profile. Switching is not offered yet (policy pending).')}</small>
        <button className="btn primary big" onClick={() => onPick(pick)} data-testid="pick-go">{P('Xác nhận', 'Confirm')}</button>
      </div>
    </div>
  );
}

// ------------------------------------------------------------------ lobby

export function LobbyView({ s, setS, l, upd, P, lang, setLang, say, openLegacy, startBattle, logout, bossFight, startWar }: {
  s: SimState; setS: (f: (x: SimState) => SimState) => void; l: LobbyState; upd: Upd; P: PFn; lang: string; setLang: (x: 'vi' | 'en') => void;
  say: (m: string) => void; openLegacy: (x: Legacy) => void; startBattle: (b: BattleStart) => void; logout: () => void;
  bossFight: (formId: string, guildLevel: number) => number | null; startWar: (team: CardSnap[], name: string) => void;
}) {
  const cat = useCatalog();
  const w = useWorld();
  const d = useCosmetics();
  const me = l.account?.uid ?? 0;
  const meAcc = acc(w, me);
  const [panel, setPanel] = useState<Panel | null>(null);
  const [socialTab, setSocialTab] = useState<SocialTab>('feed');
  const [shopCat, setShopCat] = useState<ShopCat>('skins');
  const [viewUid, setViewUid] = useState<number>(me);
  const [dm, setDm] = useState<number | null>(null);
  const openProfile = (uid: number) => { setViewUid(uid); setPanel('viewProfile'); };
  const h: Hub = { me, w, s, setS, l, upd, P, lang, say, d, openProfile, bossFight, startWar: (t, n) => { setPanel(null); startWar(t, n); } };
  const openChat = (uid: number) => { setPanel(null); setDm(uid); };
  const [frame, setFrame] = useState<'idle' | 'blink' | 'happy'>('idle');
  const [petHop, setPetHop] = useState(false);
  const [par, setPar] = useState({ x: 0, y: 0 });
  const root = useRef<HTMLDivElement>(null);
  const trainer = l.trainer ?? 'trainer_boy';
  const companion = s.cards.find((c) => c.id === l.companionId) ?? [...s.cards].sort((a, b) => stats(cat, b).power - stats(cat, a).power)[0];

  // idle blink every few seconds; tap → happy
  useEffect(() => {
    let alive = true;
    const loop = () => { if (!alive) return; setTimeout(() => { if (!alive) return; setFrame((f) => (f === 'happy' ? f : 'blink')); setTimeout(() => setFrame((f) => (f === 'blink' ? 'idle' : f)), 160); loop(); }, 2600 + Math.random() * 2400); };
    loop(); return () => { alive = false; };
  }, []);
  const tapTrainer = () => { setFrame('happy'); setTimeout(() => setFrame('idle'), 1300); };
  const tapPet = () => { setPetHop(true); setTimeout(() => setPetHop(false), 700); };
  const move = (e: React.PointerEvent) => {
    if (l.settings.effects === 'low' || !root.current) return;
    const r = root.current.getBoundingClientRect();
    setPar({ x: (e.clientX - r.left) / r.width - 0.5, y: (e.clientY - r.top) / r.height - 0.5 });
  };
  const open = (p: Panel) => { setPanel(p); upd((x) => ({ ...x, seen: { ...x.seen, [p]: true } })); };

  // NEW badges come from real state (cleared when the content is used or seen)
  const unreadMail = l.mail.filter((m) => !m.read && !m.hidden).length;
  const unreadSocial = myNotifs(w, me).filter((n) => !n.read).length;
  const friendReqs = incoming(w, me).length;
  const badges: Record<string, boolean | number> = {
    starter: !l.starterClaimed, gifts: l.checkin.last !== today(), season: SEASON_TIERS.some((t, i) => l.season.xp >= t.xp && !l.season.claimed.includes(i)),
    events: !l.seen.events, shop: !l.seen.shop, center: !l.seen.center,
  };
  const expNeed = EXP_PER_LEVEL(l.level);

  const bottom: [Panel | 'shop', string, string][] = [
    ['starter', P('Quà Tân Thủ', 'Starter Gift'), 'ui_icon_starter_gift'], ['center', P('Trung Tâm', 'Center'), 'ui_icon_center'],
    ['gifts', P('Quà Tặng', 'Gifts'), 'ui_icon_gifts'], ['season', P('Thẻ Mùa', 'Season Pass'), 'ui_icon_season'],
    ['events', P('Hoạt Động', 'Events'), 'ui_icon_events'], ['shop', P('Tiệm', 'Shop'), 'ui_icon_shop'],
  ];

  return (
    <div className={'lb' + (l.settings.effects === 'low' ? ' low' : '')} ref={root} onPointerMove={move}>
      <div className="lb-bg" style={{ backgroundImage: `url(${uiArt('bg_lobby_main')})`, transform: `translate(${-par.x * 14}px, ${-par.y * 8}px) scale(1.05)` }} />
      {l.settings.effects === 'high' && <div className="lb-sparkles">{Array.from({ length: 14 }, (_, i) => <i key={i} style={{ left: `${(i * 37) % 100}%`, animationDelay: `${(i * 0.7) % 6}s` }} />)}</div>}

      {/* header */}
      <button className="lb-profile" onClick={() => open('profile')} data-testid="open-profile">
        <Avatar a={meAcc ?? { trainer, outfit: l.outfit, frame: 'frame_default' }} d={d} size={58} />
        <span className="lb-pinfo"><b className="ellipsis" title={l.account?.name}>{l.account?.name}</b><small>UID {l.account?.uid}{guildOf(w, me)?.tag ? ` · [${guildOf(w, me)!.tag}]` : ''}</small>
          <span className="lb-lv">Lv {l.level}<i><em style={{ width: `${Math.min(100, (100 * l.exp) / expNeed)}%` }} /></i></span></span>
      </button>
      <div className="lb-wallet">
        {[[BEANS, 'ui_currency_mini_green_bean', s.beans], [STARS, 'ui_currency_rainbow_star', s.stars]].map(([id, icon, v]) => (
          <span key={id as string} className="lb-coin"><img src={uiArt(icon as string)} alt="" /><b data-testid={`bal-${id}`}>{(v as number).toLocaleString(lang)}</b><button onClick={() => openLegacy('shop')} aria-label="+">+</button></span>
        ))}
      </div>
      <div className="lb-tools">
        <button onClick={() => open('notices')} aria-label={P('Thông báo', 'Notifications')}>🔔{(badges.season || unreadMail > 0 || unreadSocial > 0) && <em className="lb-dot" />}</button>
        <button onClick={() => open('help')} aria-label={P('Trợ giúp', 'Help')}>❔</button>
        <button onClick={() => open('settings')} aria-label={P('Cài đặt', 'Settings')} data-testid="open-settings">⚙️</button>
      </div>

      {/* left menu */}
      <div className="lb-left">
        <button onClick={() => open('mail')} data-testid="open-mail"><img src={uiArt('ui_icon_mail')} alt="" /><span>{P('Tin Nhắn', 'Messages')}</span>{unreadMail > 0 && <em className="lb-badge">{unreadMail}</em>}</button>
        <button onClick={() => { setSocialTab('friends'); open('social'); }} data-testid="open-friends"><img src={uiArt('ui_icon_friends')} alt="" /><span>{P('Bạn Bè', 'Friends')}</span>{friendReqs > 0 && <em className="lb-badge">{friendReqs}</em>}</button>
      </div>

      {/* stage: podium + trainer + companion */}
      <div className="lb-stage" style={{ transform: `translate(${par.x * 8}px, ${par.y * 4}px)` }}>
        <img className="lb-podium" src={uiArt('ui_podium')} alt="" />
        <button className={'lb-trainer ' + frame} onClick={tapTrainer} aria-label={P('Trainer', 'Trainer')} data-testid="trainer">
          <span className="lb-shadow" />
          <img src={skinArt(d, trainer, meAcc?.outfit ?? l.outfit, 'idle')} alt="" className={frame === 'idle' ? 'on' : ''} />
          <img src={skinArt(d, trainer, meAcc?.outfit ?? l.outfit, 'blink')} alt="" className={frame === 'blink' ? 'on' : ''} />
          <img src={skinArt(d, trainer, meAcc?.outfit ?? l.outfit, 'happy')} alt="" className={frame === 'happy' ? 'on' : ''} />
          <Effect d={d} id={meAcc?.effect} />
          {frame === 'happy' && <span className="lb-heart">✨</span>}
        </button>
        {companion && (
          <button className={'lb-pet' + (petHop ? ' hop' : '')} onClick={tapPet} onDoubleClick={() => open('companion')} data-testid="companion">
            <span className="lb-shadow small" />
            <img src={petArt(companion.formId, true)} alt="" />
            {petHop && <span className="lb-heart">💖</span>}
          </button>
        )}
        {companion && <button className="lb-pet-tag" onClick={() => open('companion')} data-testid="open-companion">🐾 {cat.forms[companion.formId]?.name ?? companion.formId} ›</button>}
      </div>

      {/* five hexagon hubs (honeycomb: 2 on top, 3 below; Start is the largest) */}
      <div className="lb-hexes five">
        <Hex cls="market" label={P('Chợ Giao Dịch', 'Pet Market')} icon="ui_hex_market" pets={['pet_bloomhare']} onClick={() => open('market')} id="hex-market" />
        <Hex cls="games" label={P('Trò Chơi Khác', 'Mini Games')} icon="ui_hex_games" pets={['pet_floatlet']} onClick={() => open('games')} id="hex-games" />
        <Hex cls="world" label={P('Sảnh Liên Thông', 'Pet World')} icon="ui_hex_world" pets={['pet_dandefluff']} onClick={() => open('world')} id="hex-world" />
        <Hex cls="battle" label={P('Bắt Đầu', 'Start')} icon="ui_hex_battle" pets={['pet_pyrelynx', 'pet_tidecrown']} onClick={() => open('battle')} id="hex-battle" />
        <Hex cls="social" label="Social Hub" icon="ui_hex_social" pets={['pet_boltcub']} badge={unreadSocial + friendReqs} onClick={() => { setSocialTab('feed'); open('social'); }} id="hex-social" />
      </div>

      {/* utility bar */}
      <div className="lb-bottom">
        {bottom.map(([p, label, icon]) => (
          <button key={p} onClick={() => { if (p === 'shop') setShopCat('skins'); open(p as Panel); }} data-testid={`bar-${p}`}>
            <img src={uiArt(icon)} alt="" /><span>{label}</span>{badges[p] ? <em className="lb-new">NEW</em> : null}
          </button>
        ))}
      </div>
      <div className="lb-preview-tag">{P('PREVIEW · dữ liệu cục bộ', 'PREVIEW · local data')}</div>
      <ChatDock h={h} dm={dm} setDm={setDm} />

      {panel && (
        <Sheet title={panelTitle(panel, P)} onClose={() => setPanel(null)} wide={['social', 'market', 'shop', 'games', 'profile', 'viewProfile', 'leaders'].includes(panel)}>
          {panel === 'social' ? <SocialHub h={h} tab={socialTab} setTab={setSocialTab} openChat={openChat} />
            : panel === 'market' ? <MarketHub h={h} />
            : panel === 'shop' ? <ShopHub h={h} initial={shopCat} openPacks={() => { setPanel(null); upd((x) => ({ ...x, seen: { ...x.seen, shop: true } })); openLegacy('shop'); }} />
            : panel === 'viewProfile' || panel === 'profile' ? <Profile h={h} uid={panel === 'profile' ? me : viewUid} openChat={openChat} openFashion={() => { setShopCat('frames'); setPanel('shop'); }} />
            : panel === 'leaders' ? <Leaderboards h={h} />
            : <Panels panel={panel} setPanel={setPanel} s={s} setS={setS} l={l} upd={upd} P={P} lang={lang} setLang={setLang} say={say} h={h}
              openLegacy={(x) => { setPanel(null); openLegacy(x); }} startBattle={(b) => { setPanel(null); startBattle(b); }} logout={logout} />}
        </Sheet>
      )}
    </div>
  );
}

function Hex({ cls, label, pets, icon, onClick, id, badge }: { cls: string; label: string; pets: string[]; icon: string; onClick: () => void; id: string; badge?: number }) {
  // the label sits in its own band inside the hexagon's safe area (never clipped, never scaled)
  return (
    <button className={'lb-hex ' + cls} onClick={onClick} data-testid={id} aria-label={label}>
      <span className="lb-hex-rim" />
      <span className="lb-hex-body">
        <span className="lb-hex-shine" />
        <span className="lb-hex-art"><img src={uiArt(icon)} alt="" className="lb-hex-emblem" />{pets.map((p, i) => <img key={p} src={petArt(p, true)} alt="" className={'p' + i} onError={(e) => ((e.target as HTMLImageElement).style.visibility = 'hidden')} />)}</span>
        <span className="lb-hex-label">{label}</span>
      </span>
      {!!badge && <em className="lb-badge lb-hex-badge">{badge > 99 ? '99+' : badge}</em>}
    </button>
  );
}

export function Sheet({ title, onClose, children, wide }: { title: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  return (
    <div className="lb-modal" onClick={onClose}>
      <div className={'lb-sheet' + (wide ? ' wide' : '')} onClick={(e) => e.stopPropagation()}>
        <div className="lb-sheet-head"><b>{title}</b><button className="lb-x" onClick={onClose} aria-label="×" data-testid="sheet-close">✕</button></div>
        <div className="lb-sheet-body">{children}</div>
      </div>
    </div>
  );
}

function panelTitle(p: Panel, P: PFn): string {
  return ({
    profile: P('Hồ sơ', 'Profile'), settings: P('Cài đặt', 'Settings'), notices: P('Thông báo', 'Notifications'), help: P('Trợ giúp', 'Help'),
    mail: P('Tin Nhắn', 'Messages'), friends: P('Bạn Bè', 'Friends'), starter: P('Quà Tân Thủ', 'Starter Gift'), center: P('Trung Tâm', 'Center'),
    gifts: P('Quà Tặng', 'Gifts'), season: P('Thẻ Mùa', 'Season Pass'), events: P('Hoạt Động', 'Events'), social: 'Social Hub', market: P('Chợ Giao Dịch Pet', 'Pet Market'), shop: P('Tiệm — Shop Hub', 'Shop Hub'), viewProfile: P('Hồ sơ', 'Profile'),
    games: P('Trò Chơi Khác — Mini Games', 'Mini Games'), world: P('Sảnh Liên Thông — Pet World', 'Pet World'), battle: P('Battle 3v3', 'Battle 3v3'),
    team: P('Quản lý đội hình', 'Team management'), missions: P('Nhiệm vụ', 'Missions'), leaders: P('Bảng xếp hạng', 'Leaderboard'),
    history: P('Lịch sử trận & Replay', 'Match history & Replay'), companion: P('Pet đồng hành', 'Companion Pet'), pve: P('PvE — Chương thử thách', 'PvE chapters'),
    admin: P('Admin Dashboard', 'Admin Dashboard'),
  } as Record<Panel, string>)[p];
}

// ------------------------------------------------------------------ PvE chapters (preview data from the real Pet catalog)

export interface Stage { id: string; chapter: number; index: number; boss: boolean; level: number; forms: string[]; reward: { beans: number; stars: number } }
export function previewStages(forms: string[]): Stage[] {
  const out: Stage[] = [];
  for (let c = 1; c <= 3; c++) for (let i = 1; i <= 5; i++) {
    const k = (c - 1) * 5 + i;
    out.push({ id: `pv_c${c}_s${i}`, chapter: c, index: i, boss: i === 5, level: 4 + (c - 1) * 14 + i * 2, forms: [0, 1, 2].map((j) => forms[(k * 37 + j * 71) % forms.length]),
      reward: { beans: 60 + 30 * k, stars: i === 5 ? 2 : 0 } });
  }
  return out;
}

// ------------------------------------------------------------------ panels

function Panels({ panel, setPanel, s, setS, l, upd, P, lang, setLang, say, openLegacy, startBattle, logout, h }: {
  panel: Panel; setPanel: (p: Panel | null) => void; s: SimState; setS: (f: (x: SimState) => SimState) => void; l: LobbyState; upd: Upd; P: PFn; lang: string;
  setLang: (x: 'vi' | 'en') => void; say: (m: string) => void; openLegacy: (x: Legacy) => void; startBattle: (b: BattleStart) => void; logout: () => void; h: Hub;
}) {
  const cat = useCatalog();
  const L = (o: { vi: string; en: string }) => (lang === 'vi' ? o.vi : o.en);
  const pay = (r: { beans?: number; stars?: number }) => setS((x) => ({ ...x, beans: x.beans + (r.beans ?? 0), stars: x.stars + (r.stars ?? 0) }));
  const rewardText = (r: { beans?: number; stars?: number }) => [r.beans ? `+${r.beans} ${P('Đậu Xanh Mini', 'Mini Green Beans')}` : '', r.stars ? `+${r.stars} ${P('Sao Ngũ Sắc', 'Rainbow Stars')}` : ''].filter(Boolean).join(' · ');
  const best = [...s.cards].sort((a, b) => stats(cat, b).power - stats(cat, a).power);
  const team = (l.team.length === 3 && l.team.every((id) => s.cards.some((c) => c.id === id)) ? l.team : best.slice(0, 3).map((c) => c.id));
  const mission = (id: string) => l.missions.counters[id] ?? 0;

  switch (panel) {
    case 'companion': return (
      <div>
        <small>{P('Chạm một Pet trong bộ sưu tập để làm Pet đồng hành. Hình thái hiện tại theo thẻ bạn sở hữu.', 'Tap a Pet from your collection to make it your companion. The form shown is the one you own.')}</small>
        <div className="lp-pets">{best.map((c) => (
          <button key={c.id} className={'lp-pet' + (l.companionId === c.id ? ' on' : '')} onClick={() => { upd((x) => ({ ...x, companionId: c.id })); say(P('Đã đổi Pet đồng hành', 'Companion changed')); }} data-testid={`comp-${c.id}`}>
            <img src={petArt(c.formId, true)} alt="" /><b>{cat.forms[c.formId]?.name}</b><small>{c.rarity} · Lv {c.level} · ⚔ {stats(cat, c).power}</small>
          </button>))}</div>
      </div>
    );
    case 'settings': return (
      <div className="lp-col">
        <label><input type="checkbox" checked={l.settings.sound} onChange={(e) => upd((x) => ({ ...x, settings: { ...x.settings, sound: e.target.checked } }))} /> {P('Âm thanh', 'Sound')}</label>
        <label><input type="checkbox" checked={l.settings.music} onChange={(e) => upd((x) => ({ ...x, settings: { ...x.settings, music: e.target.checked } }))} /> {P('Nhạc nền', 'Music')}</label>
        <small>{P('Chưa có file âm thanh/nhạc trong bản Preview (sẽ bổ sung asset có bản quyền phù hợp).', 'No sound/music files in the Preview yet (licensed assets to be added).')}</small>
        <div className="lp-row">{P('Đồ họa', 'Graphics')}: {(['high', 'low'] as const).map((q) => <button key={q} className={'btn small' + (l.settings.effects === q ? ' primary' : '')} onClick={() => upd((x) => ({ ...x, settings: { ...x.settings, effects: q } }))}>{q === 'high' ? P('Cao', 'High') : P('Tiết kiệm', 'Low')}</button>)}</div>
        <div className="lp-row">{P('Ngôn ngữ', 'Language')}: <button className={'btn small' + (lang === 'vi' ? ' primary' : '')} onClick={() => setLang('vi')}>Tiếng Việt</button><button className={'btn small' + (lang === 'en' ? ' primary' : '')} onClick={() => setLang('en')}>English</button></div>
        <small>{P('Tài khoản', 'Account')}: {l.account?.name} · UID {l.account?.uid}</small>
        <button className="btn danger" onClick={logout} data-testid="logout">{P('Đăng xuất / Đổi tài khoản', 'Log out / Switch account')}</button>
        <small className="muted">{P('Dữ liệu Preview lưu trong trình duyệt này (localStorage). Mỗi tab có thể đăng nhập một tài khoản khác nhau.', 'Preview data is stored in this browser (localStorage). Each tab can sign in as a different account.')}</small>
      </div>
    );
    case 'notices': return (
      <div className="lp-col">
        <div className="lp-card"><b>📢 {L(EVENT)}</b><small>{P('Sự kiện đang diễn ra đến', 'Running until')} {EVENT.ends.slice(0, 10)}</small></div>
        {SEASON_TIERS.some((t, i) => l.season.xp >= t.xp && !l.season.claimed.includes(i)) && <div className="lp-card"><b>🎖 {P('Có phần thưởng Thẻ Mùa chờ nhận', 'Season Pass reward ready')}</b><button className="btn small" onClick={() => setPanel('season')}>{P('Xem', 'Open')}</button></div>}
        {l.mail.filter((m) => !m.read && !m.hidden).length > 0 && <div className="lp-card"><b>✉️ {P('Tin nhắn chưa đọc', 'Unread messages')}: {l.mail.filter((m) => !m.read && !m.hidden).length}</b><button className="btn small" onClick={() => setPanel('mail')}>{P('Mở', 'Open')}</button></div>}
        {myNotifs(h.w, h.me).filter((n) => !n.read).length > 0 && <div className="lp-card"><b>💞 {P('Thông báo xã hội chưa đọc', 'Unread social notifications')}: {myNotifs(h.w, h.me).filter((n) => !n.read).length}</b><button className="btn small" onClick={() => setPanel('social')}>Social Hub</button></div>}
      </div>
    );
    case 'help': case 'center': return (
      <div className="lp-col">
        {panel === 'center' && <div className="lp-card"><b>📰 {P('Tin nhà phát triển', 'Developer news')}</b><small>{P('Mới: Social Hub (bảng tin, Pet Showcase, Guild, xếp hạng), Chợ Giao Dịch chỉ dùng Đậu Xanh Mini, chat 3 kênh, khung avatar, bong bóng chat và Tiệm Skin Trainer.', 'New: Social Hub (feed, Pet Showcase, Guild, rankings), Pet Market in Mini Green Beans only, 3-channel chat, avatar frames, chat bubbles and the Trainer Skin Shop.')}</small></div>}
        <div className="lp-card"><b>📘 {P('Hướng dẫn nhanh', 'Quick guide')}</b><small>{P('1) Mở gói thẻ ở Tiệm (5 thẻ/gói). 2) Chọn 3 Pet ở Quản lý đội hình. 3) Bắt Đầu → PvE. Mỗi vòng chọn lệnh cho 3 Pet rồi Khóa lệnh; hệ thống xử lý theo ưu tiên kỹ năng rồi SPD.', '1) Open packs in the Shop (5 cards each). 2) Pick 3 Pets in Team management. 3) Battle → PvE. Each round give orders to your 3 Pets and lock them; the round resolves by skill priority, then SPD.')}</small></div>
        <div className="lp-card"><b>❓ FAQ</b><small>{P('EN: bắt đầu 50/100, đánh thường +15, hết vòng +10. Kỹ năng tốn 20–60 EN, tuyệt kỹ có hồi chiêu. Khống chế cứng chỉ 1 vòng rồi miễn khống chế 1 vòng.', 'EN: start 50/100, basic attack +15, +10 each round. Skills cost 20–60 EN, ultimates have cooldowns. Hard control lasts 1 round, then 1 round of immunity.')}</small></div>
        <div className="lp-card"><b>🛠 {P('Hỗ trợ & báo lỗi', 'Support & bug report')}</b><small>{P('Bản Preview không gửi dữ liệu ra ngoài. Báo lỗi trong bản thật dùng mục Báo cáo (có kiểm duyệt).', 'The Preview sends nothing out. In the real game use Report (moderated).')}</small></div>
        <div className="lp-card"><b>📜 {P('Quy định', 'Rules')}</b><small>{P('Không spam, không quấy rối, không gian lận. Vi phạm có thể bị khóa tài khoản.', 'No spam, harassment or cheating. Violations can lead to account restrictions.')}</small></div>
        <small>{P('Phiên bản', 'Version')}: Preview · battle data team-1</small>
        <button className="btn small" onClick={() => setPanel('admin')}>🛡 Admin Dashboard</button>
      </div>
    );
    case 'admin': return (
      <div className="lp-col">
        <small>{P('Admin Dashboard thật nằm trong web client và cần máy chủ + tài khoản có quyền Owner/Admin/Moderator (kiểm tra phía server; Admin/Mod không thể tự nâng quyền Owner). Bản Preview tĩnh không có máy chủ nên chỉ liệt kê các chức năng đã có ở backend:', 'The real Admin Dashboard is in the web client and needs the server plus an Owner/Admin/Moderator account (checked server-side; Admin/Mod cannot grant Owner). The static Preview has no server, so it only lists what the backend provides:')}</small>
        <ul className="lp-list">{[P('Người chơi, khóa tài khoản, vai trò', 'Players, bans, roles'), P('Pet Card, gói thẻ, tỷ lệ có phiên bản', 'Pet cards, packs, versioned odds'), P('Điều chỉnh tiền tệ (có lý do, audit)', 'Currency adjustments (reason + audit)'), P('Giftcode, sự kiện, đăng nhập', 'Giftcodes, events, login rewards'), P('Kiểm duyệt chat, báo cáo', 'Chat moderation, reports'), P('Chợ giao dịch', 'Marketplace'), P('Bảo trì máy chủ, thông báo', 'Maintenance, announcements'), P('Audit log', 'Audit log')].map((x) => <li key={x}>✅ {x}</li>)}
          {[P('Season Pass, mini-game, background sảnh, Battle 3v3 (quản trị)', 'Season Pass, mini games, lobby backgrounds, Battle 3v3 (admin screens)')].map((x) => <li key={x}>⏳ {x} — {P('chưa có màn quản trị', 'admin screens not built yet')}</li>)}</ul>
      </div>
    );
    case 'mail': return (
      <div className="lp-col">
        {l.mail.filter((m) => !m.hidden).map((m) => (
          <div key={m.id} className={'lp-card' + (m.read ? '' : ' unread')}>
            <b>{({ system: '📢', friend: '💬', battle: '⚔️', trade: '🔄', gift: '🎁' } as Record<string, string>)[m.kind]} {L(m.title)}</b><small>{m.from} · {m.at.slice(0, 10)}</small><small>{L(m.body)}</small>
            <div className="lp-row">
              {!m.read && <button className="btn small" onClick={() => upd((x) => ({ ...x, mail: x.mail.map((y) => (y.id === m.id ? { ...y, read: true } : y)) }))}>{P('Đánh dấu đã đọc', 'Mark read')}</button>}
              {m.gift && !m.claimed && <button className="btn small primary" onClick={() => { pay(m.gift!); upd((x) => ({ ...x, mail: x.mail.map((y) => (y.id === m.id ? { ...y, claimed: true, read: true } : y)) })); say(rewardText(m.gift!)); }} data-testid={`mail-claim-${m.id}`}>{P('Nhận quà', 'Claim')}</button>}
              {m.kind === 'battle' && <button className="btn small" onClick={() => startBattle({ mode: 'practice' })}>{P('Đấu luyện tập', 'Practice match')}</button>}
              <button className="btn small ghost" onClick={() => upd((x) => ({ ...x, mail: x.mail.map((y) => (y.id === m.id ? { ...y, hidden: true } : y)) }))}>{P('Ẩn/Xóa', 'Hide/Delete')}</button>
              {m.kind === 'friend' && <button className="btn small ghost" onClick={() => say(P('Đã báo cáo (Preview)', 'Reported (Preview)'))}>{P('Báo cáo', 'Report')}</button>}
            </div>
          </div>
        ))}
        {l.mail.every((m) => m.hidden) && <small>{P('Hộp thư trống.', 'Inbox is empty.')}</small>}
      </div>
    );
    case 'starter': return (
      <div className="lp-col center">
        <img src={uiArt('ui_icon_starter_gift')} alt="" className="lp-big-icon" />
        <b>{P('Quà tài khoản mới (nhận 1 lần)', 'New account gift (once)')}</b>
        <small>{P('1 Gói Cơ Bản (5 thẻ) + 1.000 Đậu Xanh Mini + 10 Sao Ngũ Sắc', '1 Basic Pack (5 cards) + 1,000 Mini Green Beans + 10 Rainbow Stars')}</small>
        {l.starterClaimed ? <small>✅ {P('Đã nhận', 'Claimed')} · {P('Lịch sử: nhận khi tạo tài khoản Preview', 'History: claimed with this Preview account')}</small>
          : <button className="btn primary" data-testid="starter-claim" onClick={() => { pay({ beans: 1000, stars: 10 }); setS((x) => ({ ...x, packs: { ...x.packs, basic_pack: (x.packs.basic_pack ?? 0) + 1 } })); upd((x) => ({ ...x, starterClaimed: true })); say(P('Đã nhận quà tân thủ!', 'Starter gift claimed!')); }}>{P('Nhận quà', 'Claim')}</button>}
      </div>
    );
    case 'gifts': return <GiftsPanel l={l} upd={upd} P={P} say={say} pay={pay} rewardText={rewardText} />;
    case 'season': return (
      <div className="lp-col">
        <small>{P('EXP mùa nhận từ trận đấu, Mini Game và nhiệm vụ. Chỉ có phần thưởng miễn phí; không có thanh toán thật.', 'Season EXP comes from battles, mini games and missions. Free track only; no real payments.')}</small>
        <div className="lp-season"><i style={{ width: `${Math.min(100, (100 * l.season.xp) / SEASON_TIERS[SEASON_TIERS.length - 1].xp)}%` }} /></div>
        <small>EXP {l.season.xp} · {P('Cấp mùa', 'Season level')} {SEASON_TIERS.filter((t) => l.season.xp >= t.xp).length}/{SEASON_TIERS.length}</small>
        <div className="lp-tiers">{SEASON_TIERS.map((t, i) => {
          const ready = l.season.xp >= t.xp, got = l.season.claimed.includes(i);
          return <button key={i} className={'lp-tier' + (ready ? ' ready' : '') + (got ? ' got' : '')} disabled={!ready || got} onClick={() => { pay(t.reward); upd((x) => ({ ...x, season: { ...x.season, claimed: [...x.season.claimed, i] } })); say(rewardText(t.reward)); }} data-testid={`tier-${i}`}>
            <b>{P('Cấp', 'Tier')} {i + 1}</b><small>{t.xp} EXP</small><small>{rewardText(t.reward)}</small>{got && <small>✅</small>}</button>;
        })}</div>
        <small>{P('Nhiệm vụ mùa: thắng trận (+40 EXP), Mini Game (+15 EXP), nhiệm vụ ngày (+30 EXP). Kênh phần thưởng đặc biệt: cấu trúc sẵn, chưa bật.', 'Season tasks: battle wins (+40 EXP), mini games (+15 EXP), daily missions (+30 EXP). Special track: structure ready, not enabled.')}</small>
      </div>
    );
    case 'events': {
      const ms = Math.max(0, Date.parse(EVENT.ends) - Date.now());
      return (
        <div className="lp-col">
          <div className="lp-card"><b>🎆 {L(EVENT)}</b><small>⏳ {Math.floor(ms / 86400000)} {P('ngày', 'days')} {Math.floor((ms % 86400000) / 3600000)} {P('giờ', 'h')}</small></div>
          {EVENT.tasks.map((t) => { const have = t.id === 'ev_win3' ? l.record.pveWins + l.record.pvpWins : mission('mini_total'); const got = l.missions.claimed.includes(t.id);
            return <div key={t.id} className="lp-card"><b>{L(t)}</b><small>{Math.min(have, t.goal)}/{t.goal} · {rewardText(t.reward)}</small>
              <button className="btn small primary" disabled={have < t.goal || got} onClick={() => { pay(t.reward); upd((x) => ({ ...x, missions: { ...x.missions, claimed: [...x.missions.claimed, t.id] } })); say(rewardText(t.reward)); }}>{got ? '✅' : P('Nhận', 'Claim')}</button></div>; })}
          <div className="lp-card"><b>🏆 {P('Giải đấu & lịch sự kiện', 'Tournaments & calendar')}</b><small>{P('Giải đấu cần máy chủ (ghép trận nhiều người) — chưa có trong Preview.', 'Tournaments need the server (multi-player brackets) — not in the Preview.')}</small></div>
        </div>
      );
    }
    case 'games': return (
      <MiniGames P={P} best={l.mini.best} history={l.mini.history ?? []}
        playsLeft={(g: GameId) => Math.max(0, GAME_LIMITS.rewardedPlaysPerDay - (l.mini.plays[`${today()}:${g}`] ?? 0))}
        onScore={(g, score) => {
          const key = `${today()}:${g}`; const used = l.mini.plays[key] ?? 0;
          const reward = used < GAME_LIMITS.rewardedPlaysPerDay ? rewardFor(score) : 0;
          if (reward > 0) pay({ beans: reward });
          upd((x) => ({ ...x, season: { ...x.season, xp: x.season.xp + 15 }, mini: { best: { ...x.mini.best, [g]: Math.max(x.mini.best[g] ?? 0, score) }, plays: { ...x.mini.plays, [key]: used + 1 }, history: [{ g, score, reward, at: Date.now() }, ...(x.mini.history ?? [])].slice(0, 20) },
            missions: { ...x.missions, counters: { ...x.missions.counters, mini_game: (x.missions.counters.mini_game ?? 0) + 1, mini_total: (x.missions.counters.mini_total ?? 0) + 1 } } }));
          say(reward > 0 ? `${P('Điểm', 'Score')} ${score} · +${reward} ${P('Đậu Xanh Mini', 'Mini Green Beans')}` : `${P('Điểm', 'Score')} ${score} · ${P('hết lượt thưởng hôm nay', 'no rewarded plays left today')}`);
        }} />
    );
    case 'world': return (
      <div className="lp-tiles">
        {([['📚', 'Album Pet', () => openLegacy('collection')], ['🎴', 'Booster Pack', () => openLegacy('shop')], ['🛡', P('Đội hình', 'Team'), () => setPanel('team')],
          ['🧬', P('Tiến hóa', 'Evolution'), () => openLegacy('collection')], ['📋', P('Nhiệm vụ', 'Missions'), () => setPanel('missions')], ['🔄', P('Chợ Giao Dịch', 'Pet Market'), () => setPanel('market')],
          ['🏆', P('Bảng xếp hạng', 'Leaderboard'), () => setPanel('leaders')], ['👥', 'Social Hub', () => setPanel('social')], ['🎆', P('Sự kiện', 'Events'), () => setPanel('events')],
          ['✨', P('15 Thần Pet', '15 Divine Pets'), () => openLegacy('divine')]] as [string, string, () => void][]).map(([i, label, go]) => (
          <button key={label} className="lp-tile" onClick={go} data-testid={`world-${label}`}><span>{i}</span><b>{label}</b></button>))}
        <small className="lp-wide">{P('Tiến hóa: mở Album → chọn Pet → Tiến hóa (cần cấp và tinh chất; không tiêu thẻ trùng). Chợ Giao Dịch: mua/bán bằng Đậu Xanh Mini và trao đổi thẻ (giữ thẻ trong escrow, kiểm tra lại khi chấp nhận).', 'Evolution: Album → pick a Pet → Evolve (needs level and essence; never consumes duplicates). Pet Market: buy/sell with Mini Green Beans and card trades (escrowed, re-checked on accept).')}</small>
      </div>
    );
    case 'battle': return (
      <div className="lp-tiles">
        {([['🗺️', 'PvE', P('Chương thử thách', 'Chapters'), () => setPanel('pve'), true], ['👑', 'Boss', P('Boss chương', 'Chapter bosses'), () => setPanel('pve'), true],
          ['🎯', P('Luyện tập', 'Practice'), P('Không phần thưởng', 'No rewards'), () => startBattle({ mode: 'practice' }), true],
          ['🤝', P('Đấu bạn bè', 'Friend battle'), P('2 người cùng máy (Preview)', '2 players, same device (Preview)'), () => startBattle({ mode: 'hotseat' }), true],
          ['⚔️', P('PvP thường', 'Casual PvP'), P('Cần máy chủ', 'Needs the server'), () => say(P('Ghép trận online chạy trên máy chủ (đã có API + test 2 tài khoản). Bản Preview tĩnh dùng chế độ 2 người cùng máy.', 'Online matchmaking runs on the server (API + two-account tests done). The static Preview uses same-device 2-player mode.')), false],
          ['🏅', P('PvP xếp hạng', 'Ranked PvP'), P('Cần máy chủ', 'Needs the server'), () => say(P('Xếp hạng Elo theo mùa chạy trên máy chủ.', 'Seasonal Elo ranking runs on the server.')), false],
          ['🔐', P('Phòng riêng', 'Private room'), P('Mã 6 ký tự · cần máy chủ', '6-char code · server'), () => say(P('Tạo/vào phòng bằng mã có ở API máy chủ.', 'Create/join by code is in the server API.')), false],
          ['📜', P('Lịch sử & Replay', 'History & Replay'), `${l.battles.length}`, () => setPanel('history'), true],
          ['🛡', P('Đội hình', 'Team'), P('3 Pet', '3 Pets'), () => setPanel('team'), true]] as [string, string, string, () => void, boolean][]).map(([i, label, sub, go, on]) => (
          <button key={label} className={'lp-tile' + (on ? '' : ' off')} onClick={go} data-testid={`battle-${label}`}><span>{i}</span><b>{label}</b><small>{sub}</small></button>))}
        <div className="lp-wide lp-teamline">{P('Đội hiện tại', 'Current team')}: {team.map((id) => { const c = s.cards.find((x) => x.id === id)!; return <img key={id} src={petArt(c.formId, true)} alt="" title={cat.forms[c.formId]?.name} />; })}</div>
      </div>
    );
    case 'pve': {
      const stages = previewStages(Object.keys(cat.forms).filter((f) => !f.startsWith('pet_dv_')).sort());
      return (
        <div className="lp-col">{[1, 2, 3].map((ch) => (
          <div key={ch} className="lp-chapter"><b>{P('Chương', 'Chapter')} {ch}</b>
            <div className="lp-row">{stages.filter((x) => x.chapter === ch).map((st, i, arr) => {
              const unlocked = i === 0 ? (ch === 1 || l.pveCleared.includes(`pv_c${ch - 1}_s5`)) : l.pveCleared.includes(arr[i - 1].id);
              return <button key={st.id} className={'lp-stage' + (st.boss ? ' boss' : '') + (l.pveCleared.includes(st.id) ? ' clear' : '')} disabled={!unlocked} onClick={() => startBattle({ mode: st.boss ? 'boss' : 'pve', stageId: st.id })} data-testid={`stage-${st.id}`}>
                <b>{st.boss ? '👑' : ''}{ch}-{st.index}</b><small>Lv {st.level}</small><span>{st.forms.map((f) => <img key={f} src={petArt(f, true)} alt="" />)}</span></button>;
            })}</div></div>))}
          <small>{P('Phần thưởng PvE trong Preview là mô phỏng; bản thật dùng bảng thưởng màn và giới hạn mỗi ngày của máy chủ.', 'Preview PvE rewards are simulated; the real game uses the server stage tables and daily caps.')}</small>
        </div>
      );
    }
    case 'team': return <TeamPanel s={s} l={l} upd={upd} P={P} say={say} current={team} />;
    case 'missions': return (
      <div className="lp-col">{MISSIONS.map((m) => { const have = mission(m.id); const got = l.missions.claimed.includes(`${today()}:${m.id}`);
        return <div key={m.id} className="lp-card"><b>{lang === 'vi' ? m.vi : m.en}</b><small>{Math.min(have, m.goal)}/{m.goal} · {rewardText(m.reward)}</small>
          <button className="btn small primary" disabled={have < m.goal || got} onClick={() => { pay(m.reward); upd((x) => ({ ...x, season: { ...x.season, xp: x.season.xp + 30 }, missions: { ...x.missions, claimed: [...x.missions.claimed, `${today()}:${m.id}`] } })); say(rewardText(m.reward)); }} data-testid={`mission-${m.id}`}>{got ? '✅' : P('Nhận', 'Claim')}</button></div>; })}</div>
    );
    case 'history': return (
      <div className="lp-col">{l.battles.length === 0 && <small>{P('Chưa có trận nào.', 'No battles yet.')}</small>}
        {l.battles.map((b, i) => <div key={i} className="lp-card"><b>{({ pve: 'PvE', boss: 'Boss', practice: P('Luyện tập', 'Practice'), hotseat: P('Đấu bạn bè', 'Friend battle'), replay: 'Replay' } as Record<string, string>)[b.mode]} · {b.winner === 0 ? P('Thắng', 'Win') : b.winner === 1 ? P('Thua', 'Loss') : P('Hòa', 'Draw')}</b>
          <small>{new Date(b.at).toLocaleString(lang)} · {b.a.name} vs {b.b.name} · {b.steps.length} {P('lượt lệnh', 'order steps')}</small>
          <button className="btn small" onClick={() => startBattle({ mode: 'practice', replay: b })} data-testid={`replay-${i}`}>▶ Replay</button></div>)}</div>
    );
  }
  return null;
}

function GiftsPanel({ l, upd, P, say, pay, rewardText }: { l: LobbyState; upd: Upd; P: PFn; say: (m: string) => void; pay: (r: { beans?: number; stars?: number }) => void; rewardText: (r: { beans?: number; stars?: number }) => string }) {
  const [code, setCode] = useState('');
  const day = today();
  const yesterday = new Date(Date.now() - 86400000).toISOString().slice(0, 10);
  const checkinReward = (streak: number) => (streak % 7 === 0 ? { stars: 5 } : { beans: 100 + streak * 20 });
  const redeem = () => {
    const c = code.trim().toUpperCase(); const g = GIFTCODES[c];
    if (!g) return say(P('Mã không tồn tại', 'Unknown code'));
    if (l.giftcodes.includes(c)) return say(P('Bạn đã dùng mã này', 'Code already used'));
    if (day > g.expires) return say(P('Mã đã hết hạn', 'Code expired'));
    pay(g.reward); upd((x) => ({ ...x, giftcodes: [...x.giftcodes, c] })); say(rewardText(g.reward)); setCode('');
  };
  return (
    <div className="lp-col">
      <div className="lp-card"><b>📅 {P('Điểm danh', 'Daily check-in')}</b><small>{P('Chuỗi', 'Streak')}: {l.checkin.streak} {P('ngày', 'days')} · {P('Ngày thứ 7 nhận Sao Ngũ Sắc', 'Day 7 gives Rainbow Stars')}</small>
        <button className="btn small primary" disabled={l.checkin.last === day} data-testid="checkin" onClick={() => { const streak = l.checkin.last === yesterday ? l.checkin.streak + 1 : 1; const r = checkinReward(streak); pay(r); upd((x) => ({ ...x, checkin: { last: day, streak } })); say(rewardText(r)); }}>
          {l.checkin.last === day ? P('Đã điểm danh hôm nay', 'Checked in today') : P('Điểm danh', 'Check in')}</button></div>
      <div className="lp-card"><b>🎟 Giftcode</b><small>{P('Mỗi mã có hạn dùng, giới hạn lượt và không nhận trùng. Mã thử: PETSTAR2026, NEWTRAINER', 'Each code has an expiry, a use limit and cannot be claimed twice. Try: PETSTAR2026, NEWTRAINER')}</small>
        <div className="lp-row"><input className="lb-input" value={code} onChange={(e) => setCode(e.target.value)} placeholder="CODE" data-testid="giftcode" /><button className="btn small primary" onClick={redeem} data-testid="giftcode-go">{P('Nhập', 'Redeem')}</button></div></div>
      <div className="lp-card"><b>🎁 {P('Quà hệ thống & sự kiện', 'System & event gifts')}</b><small>{P('Quà được gửi vào Tin Nhắn (hộp thư phần thưởng).', 'Gifts arrive in Messages (reward mailbox).')}</small></div>
    </div>
  );
}

function TeamPanel({ s, l, upd, P, say, current }: { s: SimState; l: LobbyState; upd: Upd; P: PFn; say: (m: string) => void; current: number[] }) {
  const cat = useCatalog();
  const [pick, setPick] = useState<number[]>(current);
  const toggle = (c: SimCard) => setPick((x) => (x.includes(c.id) ? x.filter((y) => y !== c.id) : x.length < 3 ? [...x, c.id] : x));
  return (
    <div className="lp-col">
      <small>{P('Chọn đúng 3 Pet khác nhau. Chỉ số trận đấu tính từ thẻ bạn sở hữu (cấp, sao, cường hóa…); MAG/RES suy ra theo vai trò.', 'Pick exactly 3 different Pets. Battle stats come from the cards you own (level, stars, enhancement…); MAG/RES derive from the role.')}</small>
      <div className="lp-pets">{[...s.cards].sort((a, b) => stats(cat, b).power - stats(cat, a).power).map((c) => { const st = stats(cat, c);
        return <button key={c.id} className={'lp-pet' + (pick.includes(c.id) ? ' on' : '')} onClick={() => toggle(c)} data-testid={`team-${c.id}`}>
          <img src={petArt(c.formId, true)} alt="" /><b>{cat.forms[c.formId]?.name}</b><small>HP {st.hp} · ATK {st.atk} · DEF {st.def} · SPD {st.spd}</small>{pick.includes(c.id) && <em>#{pick.indexOf(c.id) + 1}</em>}</button>; })}</div>
      <button className="btn primary" disabled={pick.length !== 3} onClick={() => { upd((x) => ({ ...x, team: pick })); say(P('Đã lưu đội hình', 'Team saved')); }} data-testid="team-save">{P('Lưu đội hình', 'Save team')} ({pick.length}/3)</button>
      {l.team.length === 3 && <small>{P('Đội đã lưu', 'Saved team')}: {l.team.map((id) => cat.forms[s.cards.find((c) => c.id === id)?.formId ?? '']?.name).join(', ')}</small>}
    </div>
  );
}
