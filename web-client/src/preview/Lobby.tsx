// New main lobby for the Interactive Web Preview: landscape 16:9 layout inspired by the owner's reference (layout and
// interaction only; every character, Pet, background and icon is original Pet Star art). Header with avatar/name/UID/
// level, both currencies, settings/notifications/help; Messages and Friends on the left; the chosen chibi Trainer on a
// podium with the companion Pet; four hexagon hubs; utility bar with NEW badges. All panels work on preview data.
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { petArt, uiArt, useCatalog } from '../state/catalog';
import { GAME_LIMITS, MiniGames, rewardFor, today, type GameId } from './MiniGames';
import { EVENT, EXP_PER_LEVEL, GIFTCODES, MISSIONS, NPC_NAMES, SEASON_TIERS, type Friend, type LobbyState } from './lobbyState';
import { stats, type SimCard, type SimState } from './sim';
import type { ReplayData } from './TeamBattle';

type PFn = (vi: string, en: string) => string;
type Upd = (f: (l: LobbyState) => LobbyState) => void;
export type Panel = 'profile' | 'settings' | 'notices' | 'help' | 'mail' | 'friends' | 'starter' | 'center' | 'gifts' | 'season' | 'events'
  | 'plaza' | 'games' | 'world' | 'battle' | 'team' | 'missions' | 'leaders' | 'history' | 'companion' | 'pve' | 'admin';
export type Legacy = 'shop' | 'collection' | 'divine' | 'market' | 'status';
export interface BattleStart { mode: 'pve' | 'boss' | 'practice' | 'hotseat'; stageId?: string; replay?: ReplayData }

const BEANS = 'MINI_GREEN_BEAN', STARS = 'RAINBOW_STAR';
export const trainerArt = (id: string, frame: 'idle' | 'blink' | 'happy' = 'idle') => uiArt(frame === 'idle' ? id : `${id}_${frame}`);

// ------------------------------------------------------------------ login + trainer choice

export function LoginScreen({ P, known, onLogin }: { P: PFn; known: string | null; onLogin: (name: string) => void }) {
  const [name, setName] = useState(known ?? '');
  return (
    <div className="lb-gate" style={{ backgroundImage: `url(${uiArt('bg_lobby_main')})` }}>
      <div className="lb-gate-card">
        <img src={`${import.meta.env.BASE_URL}brand/logo-512.webp`} alt="Pet Star: Trading Duel" className="lb-logo" onError={(e) => ((e.target as HTMLImageElement).style.display = 'none')} />
        <b>{known ? P('Đăng nhập lại', 'Sign in again') : P('Tạo tài khoản Preview', 'Create a Preview account')}</b>
        <small>{P('Tài khoản Preview chỉ lưu trong trình duyệt này (không phải tài khoản Production). Đăng ký/đăng nhập thật dùng máy chủ.', 'Preview accounts live only in this browser (not a Production account). Real sign-up/login uses the server.')}</small>
        <input className="lb-input" value={name} maxLength={16} placeholder={P('Tên Trainer', 'Trainer name')} onChange={(e) => setName(e.target.value)} data-testid="login-name" />
        <button className="btn primary" disabled={name.trim().length < 2} onClick={() => onLogin(name.trim())} data-testid="login-go">{known ? P('Đăng nhập', 'Sign in') : P('Đăng ký & vào game', 'Sign up & play')}</button>
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

export function LobbyView({ s, setS, l, upd, P, lang, setLang, say, openLegacy, startBattle, logout }: {
  s: SimState; setS: (f: (x: SimState) => SimState) => void; l: LobbyState; upd: Upd; P: PFn; lang: string; setLang: (x: 'vi' | 'en') => void;
  say: (m: string) => void; openLegacy: (x: Legacy) => void; startBattle: (b: BattleStart) => void; logout: () => void;
}) {
  const cat = useCatalog();
  const [panel, setPanel] = useState<Panel | null>(null);
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
        <span className={'lb-avatar frame-' + l.frame} style={{ backgroundImage: `url(${trainerArt(trainer)})` }} />
        <span className="lb-pinfo"><b>{l.account?.name}</b><small>UID {l.account?.uid}</small>
          <span className="lb-lv">Lv {l.level}<i><em style={{ width: `${Math.min(100, (100 * l.exp) / expNeed)}%` }} /></i></span></span>
      </button>
      <div className="lb-wallet">
        {[[BEANS, 'ui_currency_mini_green_bean', s.beans], [STARS, 'ui_currency_rainbow_star', s.stars]].map(([id, icon, v]) => (
          <span key={id as string} className="lb-coin"><img src={uiArt(icon as string)} alt="" /><b data-testid={`bal-${id}`}>{(v as number).toLocaleString(lang)}</b><button onClick={() => openLegacy('shop')} aria-label="+">+</button></span>
        ))}
      </div>
      <div className="lb-tools">
        <button onClick={() => open('notices')} aria-label={P('Thông báo', 'Notifications')}>🔔{(badges.season || unreadMail > 0) && <em className="lb-dot" />}</button>
        <button onClick={() => open('help')} aria-label={P('Trợ giúp', 'Help')}>❔</button>
        <button onClick={() => open('settings')} aria-label={P('Cài đặt', 'Settings')} data-testid="open-settings">⚙️</button>
      </div>

      {/* left menu */}
      <div className="lb-left">
        <button onClick={() => open('mail')} data-testid="open-mail"><img src={uiArt('ui_icon_mail')} alt="" /><span>{P('Tin Nhắn', 'Messages')}</span>{unreadMail > 0 && <em className="lb-badge">{unreadMail}</em>}</button>
        <button onClick={() => open('friends')} data-testid="open-friends"><img src={uiArt('ui_icon_friends')} alt="" /><span>{P('Bạn Bè', 'Friends')}</span>{l.requests.length > 0 && <em className="lb-badge">{l.requests.length}</em>}</button>
      </div>

      {/* stage: podium + trainer + companion */}
      <div className="lb-stage" style={{ transform: `translate(${par.x * 8}px, ${par.y * 4}px)` }}>
        <img className="lb-podium" src={uiArt('ui_podium')} alt="" />
        <button className={'lb-trainer ' + frame} onClick={tapTrainer} aria-label={P('Trainer', 'Trainer')} data-testid="trainer">
          <span className="lb-shadow" />
          <img src={trainerArt(trainer, 'idle')} alt="" className={frame === 'idle' ? 'on' : ''} />
          <img src={trainerArt(trainer, 'blink')} alt="" className={frame === 'blink' ? 'on' : ''} />
          <img src={trainerArt(trainer, 'happy')} alt="" className={frame === 'happy' ? 'on' : ''} />
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

      {/* four hexagon hubs */}
      <div className="lb-hexes">
        <Hex cls="plaza" label={P('Sảnh Tương Tác', 'Pet Plaza')} pets={['pet_bloomhare', 'pet_boltcub']} onClick={() => open('plaza')} id="hex-plaza" />
        <Hex cls="games" label={P('Trò Chơi Khác', 'Mini Games')} pets={['pet_floatlet', 'pet_inklet']} icon="🧩" onClick={() => open('games')} id="hex-games" />
        <Hex cls="world" label={P('Sảnh Liên Thông', 'Pet World')} pets={['pet_dandefluff', 'pet_emberkit']} icon="🗺️" onClick={() => open('world')} id="hex-world" />
        <Hex cls="battle" label={P('Bắt Đầu', 'Battle 3v3')} pets={['pet_pyrelynx', 'pet_tidecrown', 'pet_aurelion'.replace('pet_aurelion', 'pet_celestallion')]} onClick={() => open('battle')} id="hex-battle" />
      </div>

      {/* utility bar */}
      <div className="lb-bottom">
        {bottom.map(([p, label, icon]) => (
          <button key={p} onClick={() => { if (p === 'shop') { upd((x) => ({ ...x, seen: { ...x.seen, shop: true } })); openLegacy('shop'); } else open(p); }} data-testid={`bar-${p}`}>
            <img src={uiArt(icon)} alt="" /><span>{label}</span>{badges[p] ? <em className="lb-new">NEW</em> : null}
          </button>
        ))}
      </div>
      <div className="lb-preview-tag">{P('PREVIEW · dữ liệu mô phỏng', 'PREVIEW · simulated data')}</div>

      {panel && (
        <Sheet title={panelTitle(panel, P)} onClose={() => setPanel(null)} wide={panel === 'plaza' || panel === 'games'}>
          <Panels panel={panel} setPanel={setPanel} s={s} setS={setS} l={l} upd={upd} P={P} lang={lang} setLang={setLang} say={say}
            openLegacy={(x) => { setPanel(null); openLegacy(x); }} startBattle={(b) => { setPanel(null); startBattle(b); }} logout={logout} />
        </Sheet>
      )}
    </div>
  );
}

function Hex({ cls, label, pets, icon, onClick, id }: { cls: string; label: string; pets: string[]; icon?: string; onClick: () => void; id: string }) {
  return (
    <button className={'lb-hex ' + cls} onClick={onClick} data-testid={id}>
      <span className="lb-hex-rim" />
      <span className="lb-hex-body">
        <span className="lb-hex-shine" />
        <span className="lb-hex-art">{pets.map((p, i) => <img key={p} src={petArt(p, true)} alt="" className={'p' + i} onError={(e) => ((e.target as HTMLImageElement).style.visibility = 'hidden')} />)}{icon && <i className="lb-hex-icon">{icon}</i>}</span>
        <span className="lb-hex-label">{label}</span>
      </span>
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
    gifts: P('Quà Tặng', 'Gifts'), season: P('Thẻ Mùa', 'Season Pass'), events: P('Hoạt Động', 'Events'), plaza: P('Sảnh Tương Tác — Pet Plaza', 'Pet Plaza'),
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

function Panels({ panel, setPanel, s, setS, l, upd, P, lang, setLang, say, openLegacy, startBattle, logout }: {
  panel: Panel; setPanel: (p: Panel | null) => void; s: SimState; setS: (f: (x: SimState) => SimState) => void; l: LobbyState; upd: Upd; P: PFn; lang: string;
  setLang: (x: 'vi' | 'en') => void; say: (m: string) => void; openLegacy: (x: Legacy) => void; startBattle: (b: BattleStart) => void; logout: () => void;
}) {
  const cat = useCatalog();
  const L = (o: { vi: string; en: string }) => (lang === 'vi' ? o.vi : o.en);
  const pay = (r: { beans?: number; stars?: number }) => setS((x) => ({ ...x, beans: x.beans + (r.beans ?? 0), stars: x.stars + (r.stars ?? 0) }));
  const rewardText = (r: { beans?: number; stars?: number }) => [r.beans ? `+${r.beans} ${P('Đậu Xanh Mini', 'Mini Green Beans')}` : '', r.stars ? `+${r.stars} ${P('Sao Ngũ Sắc', 'Rainbow Stars')}` : ''].filter(Boolean).join(' · ');
  const owned = new Set(s.cards.map((c) => c.formId));
  const best = [...s.cards].sort((a, b) => stats(cat, b).power - stats(cat, a).power);
  const team = (l.team.length === 3 && l.team.every((id) => s.cards.some((c) => c.id === id)) ? l.team : best.slice(0, 3).map((c) => c.id));
  const mission = (id: string) => l.missions.counters[id] ?? 0;

  switch (panel) {
    case 'profile': return (
      <div className="lp-grid">
        <div className="lp-card center">
          <span className={'lb-avatar big frame-' + l.frame} style={{ backgroundImage: `url(${trainerArt(l.trainer ?? 'trainer_boy')})` }} />
          <b>{l.account?.name}</b><small>UID {l.account?.uid} · Lv {l.level}</small>
          <small>{l.trainer === 'trainer_girl' ? P('Trainer Nữ · Nhà Thám Hiểm Bạc Hà', 'Female Trainer · Mint Explorer') : P('Trainer Nam · Nhà Thám Hiểm Bầu Trời', 'Male Trainer · Sky Explorer')}</small>
          <div className="lp-row">{(['gold', 'star', 'leaf'] as const).map((f) => <button key={f} className={'btn small' + (l.frame === f ? ' primary' : '')} onClick={() => upd((x) => ({ ...x, frame: f }))}>{P('Khung', 'Frame')} {f}</button>)}</div>
          <textarea className="lb-input" maxLength={80} placeholder={P('Giới thiệu bản thân', 'About me')} value={l.bio} onChange={(e) => upd((x) => ({ ...x, bio: e.target.value }))} />
        </div>
        <div className="lp-card">
          <b>{P('Thống kê', 'Stats')}</b>
          <small>{P('Album', 'Album')}: {owned.size}/{Object.keys(cat.forms).length} {P('dạng Pet', 'Pet forms')} · {s.cards.length} {P('thẻ', 'cards')}</small>
          <small>PvE: {l.record.pveWins}W / {l.record.pveLosses}L · PvP: {l.record.pvpWins}W / {l.record.pvpLosses}L</small>
          <small>{P('Huy hiệu', 'Badges')}: {[l.record.pveWins >= 1 && '🥉', l.record.pveWins >= 5 && '🥈', owned.size >= 20 && '📚', l.pveCleared.length >= 5 && '🏆'].filter(Boolean).join(' ') || '—'}</small>
          <small>{P('Thành tích', 'Achievements')}: {[l.starterClaimed, l.record.pveWins > 0, l.pveCleared.length >= 5, Object.keys(l.mini.best).length >= 3].filter(Boolean).length}/4</small>
          <b>{P('Riêng tư', 'Privacy')}</b>
          <label className="small"><input type="checkbox" checked={l.privacy.showOnline} onChange={(e) => upd((x) => ({ ...x, privacy: { ...x.privacy, showOnline: e.target.checked } }))} /> {P('Hiện trạng thái online', 'Show online status')}</label>
          <label className="small"><input type="checkbox" checked={l.privacy.allowInvites} onChange={(e) => upd((x) => ({ ...x, privacy: { ...x.privacy, allowInvites: e.target.checked } }))} /> {P('Cho phép mời đấu', 'Allow battle invites')}</label>
          <div className="lp-row"><button className="btn small" onClick={() => setPanel('companion')}>🐾 {P('Pet đồng hành', 'Companion')}</button><button className="btn small" onClick={() => setPanel('history')}>📜 {P('Lịch sử trận', 'History')}</button><button className="btn small" onClick={() => openLegacy('collection')}>📚 Album</button></div>
        </div>
      </div>
    );
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
        <button className="btn danger" onClick={logout} data-testid="logout">{P('Đăng xuất', 'Log out')}</button>
      </div>
    );
    case 'notices': return (
      <div className="lp-col">
        <div className="lp-card"><b>📢 {L(EVENT)}</b><small>{P('Sự kiện đang diễn ra đến', 'Running until')} {EVENT.ends.slice(0, 10)}</small></div>
        {SEASON_TIERS.some((t, i) => l.season.xp >= t.xp && !l.season.claimed.includes(i)) && <div className="lp-card"><b>🎖 {P('Có phần thưởng Thẻ Mùa chờ nhận', 'Season Pass reward ready')}</b><button className="btn small" onClick={() => setPanel('season')}>{P('Xem', 'Open')}</button></div>}
        {l.mail.filter((m) => !m.read && !m.hidden).length > 0 && <div className="lp-card"><b>✉️ {P('Tin nhắn chưa đọc', 'Unread messages')}: {l.mail.filter((m) => !m.read && !m.hidden).length}</b><button className="btn small" onClick={() => setPanel('mail')}>{P('Mở', 'Open')}</button></div>}
      </div>
    );
    case 'help': case 'center': return (
      <div className="lp-col">
        {panel === 'center' && <div className="lp-card"><b>📰 {P('Tin nhà phát triển', 'Developer news')}</b><small>{P('Battle 3v3 theo lượt thay thế hoàn toàn cơ chế đấu bài kiểu TCG Pocket. Trainer Nam/Nữ và sảnh mới đã có trong Preview.', 'Turn-based Battle 3v3 fully replaces the TCG-Pocket-style card battle. Male/Female Trainers and the new lobby are in the Preview.')}</small></div>}
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
    case 'friends': return <FriendsPanel l={l} upd={upd} P={P} say={say} startBattle={startBattle} />;
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
    case 'plaza': return <Plaza l={l} upd={upd} P={P} say={say} startBattle={startBattle} s={s} />;
    case 'games': return (
      <MiniGames P={P} best={l.mini.best}
        playsLeft={(g: GameId) => Math.max(0, GAME_LIMITS.rewardedPlaysPerDay - (l.mini.plays[`${today()}:${g}`] ?? 0))}
        onScore={(g, score) => {
          const key = `${today()}:${g}`; const used = l.mini.plays[key] ?? 0;
          const reward = used < GAME_LIMITS.rewardedPlaysPerDay ? rewardFor(score) : 0;
          if (reward > 0) pay({ beans: reward });
          upd((x) => ({ ...x, season: { ...x.season, xp: x.season.xp + 15 }, mini: { best: { ...x.mini.best, [g]: Math.max(x.mini.best[g] ?? 0, score) }, plays: { ...x.mini.plays, [key]: used + 1 } },
            missions: { ...x.missions, counters: { ...x.missions.counters, mini_game: (x.missions.counters.mini_game ?? 0) + 1, mini_total: (x.missions.counters.mini_total ?? 0) + 1 } } }));
          say(reward > 0 ? `${P('Điểm', 'Score')} ${score} · +${reward} ${P('Đậu Xanh Mini', 'Mini Green Beans')}` : `${P('Điểm', 'Score')} ${score} · ${P('hết lượt thưởng hôm nay', 'no rewarded plays left today')}`);
        }} />
    );
    case 'world': return (
      <div className="lp-tiles">
        {([['📚', 'Album Pet', () => openLegacy('collection')], ['🎴', 'Booster Pack', () => openLegacy('shop')], ['🛡', P('Đội hình', 'Team'), () => setPanel('team')],
          ['🧬', P('Tiến hóa', 'Evolution'), () => openLegacy('collection')], ['📋', P('Nhiệm vụ', 'Missions'), () => setPanel('missions')], ['🔄', P('Giao dịch', 'Trading'), () => openLegacy('market')],
          ['🏆', P('Bảng xếp hạng', 'Leaderboard'), () => setPanel('leaders')], ['👥', P('Bạn bè', 'Friends'), () => setPanel('friends')], ['🎆', P('Sự kiện', 'Events'), () => setPanel('events')],
          ['✨', P('15 Thần Pet', '15 Divine Pets'), () => openLegacy('divine')]] as [string, string, () => void][]).map(([i, label, go]) => (
          <button key={label} className="lp-tile" onClick={go} data-testid={`world-${label}`}><span>{i}</span><b>{label}</b></button>))}
        <small className="lp-wide">{P('Tiến hóa: mở Album → chọn Pet → Tiến hóa (cần cấp và tinh chất; không tiêu thẻ trùng). Giao dịch trực tiếp giữa hai người: chờ kiểm thử bảo mật; Chợ giao dịch đã hoạt động.', 'Evolution: Album → pick a Pet → Evolve (needs level and essence; never consumes duplicates). Direct player-to-player trades wait for security testing; the Marketplace works.')}</small>
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
    case 'leaders': {
      const me = { name: l.account?.name ?? '', rating: 1000 + 16 * (l.record.pvpWins - l.record.pvpLosses) + 8 * l.record.pveWins, me: true };
      const rows = [...NPC_NAMES.map((n, i) => ({ name: n, rating: 1180 - i * 37, me: false })), me].sort((a, b) => b.rating - a.rating);
      return <div className="lp-col"><small>{P('Bảng xếp hạng trong Preview dùng đối thủ mô phỏng; bản thật lấy Elo theo mùa từ máy chủ.', 'The Preview leaderboard uses simulated rivals; the real one reads seasonal Elo from the server.')}</small>
        {rows.map((r, i) => <div key={r.name} className={'lp-rank' + (r.me ? ' me' : '')}><b>#{i + 1}</b><span>{r.name}</span><small>{r.rating}</small></div>)}</div>;
    }
    case 'history': return (
      <div className="lp-col">{l.battles.length === 0 && <small>{P('Chưa có trận nào.', 'No battles yet.')}</small>}
        {l.battles.map((b, i) => <div key={i} className="lp-card"><b>{({ pve: 'PvE', boss: 'Boss', practice: P('Luyện tập', 'Practice'), hotseat: P('Đấu bạn bè', 'Friend battle'), replay: 'Replay' } as Record<string, string>)[b.mode]} · {b.winner === 0 ? P('Thắng', 'Win') : b.winner === 1 ? P('Thua', 'Loss') : P('Hòa', 'Draw')}</b>
          <small>{new Date(b.at).toLocaleString(lang)} · {b.a.name} vs {b.b.name} · {b.steps.length} {P('lượt lệnh', 'order steps')}</small>
          <button className="btn small" onClick={() => startBattle({ mode: 'practice', replay: b })} data-testid={`replay-${i}`}>▶ Replay</button></div>)}</div>
    );
  }
  return null;
}

function FriendsPanel({ l, upd, P, say, startBattle }: { l: LobbyState; upd: Upd; P: PFn; say: (m: string) => void; startBattle: (b: BattleStart) => void }) {
  const [uid, setUid] = useState('');
  const row = (f: Friend, kind: 'friend' | 'request') => (
    <div key={f.uid} className="lp-friend">
      <span className="lb-avatar small" style={{ backgroundImage: `url(${trainerArt(f.trainer)})` }} />
      <span><b>{f.name}</b><small>UID {f.uid} · Lv {f.level} · {f.online ? '🟢 online' : '⚪ offline'}</small></span>
      {kind === 'request' ? <>
        <button className="btn small primary" onClick={() => upd((x) => ({ ...x, requests: x.requests.filter((r) => r.uid !== f.uid), friends: [...x.friends, f] }))}>{P('Chấp nhận', 'Accept')}</button>
        <button className="btn small ghost" onClick={() => upd((x) => ({ ...x, requests: x.requests.filter((r) => r.uid !== f.uid) }))}>{P('Từ chối', 'Decline')}</button>
      </> : <>
        <button className="btn small" disabled={!f.online} onClick={() => startBattle({ mode: 'practice' })}>{P('Mời đấu', 'Invite')}</button>
        <button className="btn small ghost" onClick={() => upd((x) => ({ ...x, friends: x.friends.filter((r) => r.uid !== f.uid) }))}>{P('Xóa bạn', 'Remove')}</button>
        <button className="btn small ghost" onClick={() => { upd((x) => ({ ...x, friends: x.friends.filter((r) => r.uid !== f.uid), blocked: [...x.blocked, f.name] })); say(P('Đã chặn', 'Blocked')); }}>{P('Chặn', 'Block')}</button>
      </>}
    </div>
  );
  return (
    <div className="lp-col">
      <div className="lp-row"><input className="lb-input" placeholder={P('Tìm bằng UID', 'Find by UID')} value={uid} onChange={(e) => setUid(e.target.value.replace(/\D/g, ''))} data-testid="friend-uid" />
        <button className="btn small primary" disabled={uid.length < 4} onClick={() => { say(P(`Đã gửi lời mời kết bạn tới UID ${uid} (Preview: chưa có máy chủ để gửi thật)`, `Friend request sent to UID ${uid} (Preview: no server to deliver it)`)); setUid(''); }}>{P('Gửi lời mời', 'Send request')}</button></div>
      {l.requests.length > 0 && <b>{P('Lời mời kết bạn', 'Friend requests')}</b>}
      {l.requests.map((f) => row(f, 'request'))}
      <b>{P('Bạn bè', 'Friends')} ({l.friends.length})</b>
      {l.friends.map((f) => row(f, 'friend'))}
      {l.blocked.length > 0 && <small>{P('Đã chặn', 'Blocked')}: {l.blocked.join(', ')}</small>}
      <small>{P('Danh sách bạn bè thật, trạng thái online và lời mời chạy trên máy chủ (đã có API). Trong Preview là dữ liệu mô phỏng.', 'Real friends, presence and requests run on the server (API exists). Preview data is simulated.')}</small>
    </div>
  );
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

const BAD = ['đm', 'dm', 'fuck', 'shit', 'ngu'];
function Plaza({ l, upd, P, say, startBattle, s }: { l: LobbyState; upd: Upd; P: PFn; say: (m: string) => void; startBattle: (b: BattleStart) => void; s: SimState }) {
  const cat = useCatalog();
  const npcs = NPC_NAMES.slice(0, 5).map((n, i) => ({ name: n, trainer: (i % 2 ? 'trainer_girl' : 'trainer_boy') as 'trainer_boy' | 'trainer_girl', x: 12 + i * 17, uid: 2001001 + i, pet: Object.keys(cat.forms).sort()[(i * 41) % Object.keys(cat.forms).length] }));
  const [chat, setChat] = useState<{ from: string; text: string }[]>([{ from: NPC_NAMES[1], text: P('Chào cả nhà! 👋', 'Hi everyone! 👋') }]);
  const [text, setText] = useState(''); const last = useRef(0);
  const [who, setWho] = useState<typeof npcs[number] | null>(null);
  const send = (t: string) => {
    const now = Date.now();
    if (now - last.current < 2000) return say(P('Chậm lại chút nhé (chống spam: 1 tin/2 giây)', 'Slow down (anti-spam: 1 message / 2 s)'));
    const clean = t.trim().slice(0, 80); if (!clean) return;
    const filtered = BAD.reduce((acc, w) => acc.replace(new RegExp(`\\b${w}\\b`, 'gi'), '***'), clean);
    last.current = now; setChat((c) => [...c.slice(-30), { from: l.account?.name ?? 'Me', text: filtered }]); setText('');
    if (Math.random() < 0.5) setTimeout(() => setChat((c) => [...c.slice(-30), { from: npcs[Math.floor(Math.random() * npcs.length)].name, text: ['😄', P('Hay quá!', 'Nice!'), '🔥', P('Đấu không?', 'Battle?')][Math.floor(Math.random() * 4)] }]), 900);
  };
  const me = s.cards.find((c) => c.id === l.companionId) ?? s.cards[0];
  return (
    <div className="pz">
      <div className="pz-floor" style={{ backgroundImage: `url(${uiArt('bg_lobby_main')})` }}>
        {npcs.filter((n) => !l.blocked.includes(n.name)).map((n, i) => (
          <button key={n.name} className="pz-player" style={{ left: `${n.x}%`, animationDelay: `${i * 1.3}s` }} onClick={() => setWho(n)} data-testid={`plaza-${i}`}>
            <img src={trainerArt(n.trainer)} alt="" /><img className="pz-pet" src={petArt(n.pet, true)} alt="" /><span>{n.name}</span></button>))}
        <div className="pz-player me" style={{ left: '84%' }}><img src={trainerArt(l.trainer ?? 'trainer_boy')} alt="" />{me && <img className="pz-pet" src={petArt(me.formId, true)} alt="" />}<span>{l.account?.name}</span></div>
      </div>
      <div className="pz-side">
        <div className="pz-chat">{chat.filter((m) => !l.blocked.includes(m.from)).map((m, i) => <div key={i}><b>{m.from}:</b> {m.text}</div>)}</div>
        <div className="lp-row">{['👋', '😄', '😮', '🎉', '💖'].map((e) => <button key={e} className="btn small" onClick={() => send(e)}>{e}</button>)}</div>
        <div className="lp-row"><input className="lb-input" value={text} maxLength={80} onChange={(e) => setText(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && send(text)} placeholder={P('Nhắn tin…', 'Message…')} data-testid="plaza-input" /><button className="btn small primary" onClick={() => send(text)}>{P('Gửi', 'Send')}</button></div>
        {who && <div className="lp-card"><b>{who.name}</b><small>UID {who.uid}</small>
          <div className="lp-row">
            <button className="btn small" onClick={() => { upd((x) => (x.friends.some((f) => f.uid === who.uid) ? x : { ...x, friends: [...x.friends, { name: who.name, uid: who.uid, online: true, trainer: who.trainer, level: 10 }] })); say(P('Đã kết bạn', 'Friend added')); }}>{P('Kết bạn', 'Add friend')}</button>
            <button className="btn small" onClick={() => startBattle({ mode: 'practice' })}>{P('Mời đấu', 'Invite')}</button>
            <button className="btn small ghost" onClick={() => { upd((x) => ({ ...x, reported: [...x.reported, who.name] })); say(P('Đã gửi báo cáo tới kiểm duyệt (Preview)', 'Report sent to moderation (Preview)')); }}>{P('Báo cáo', 'Report')}</button>
            <button className="btn small ghost" onClick={() => { upd((x) => ({ ...x, blocked: [...x.blocked, who.name] })); setWho(null); }}>{P('Chặn', 'Block')}</button>
          </div></div>}
        <small>{P('Người chơi khác ở đây là NPC mô phỏng. Plaza online thật (đồng bộ vị trí, chat, kiểm duyệt) cần máy chủ realtime.', 'Other players here are simulated NPCs. The real online Plaza (positions, chat, moderation) needs the realtime server.')}</small>
      </div>
    </div>
  );
}
