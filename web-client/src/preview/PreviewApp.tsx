import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { CardBack, CardView } from '../components/CardView';
import { useI18n } from '../i18n';
import { RARITY_COLORS, petArt, uiArt, useCatalog } from '../state/catalog';
import type { TeamRules, TeamSetupPlayer, TeamSetupUnit, TeamState } from '../team/model';
import { LobbyView, LoginScreen, Sheet, TrainerSelect, previewStages, type BattleStart, type Legacy } from './Lobby';
import { EXP_PER_LEVEL, loadLobby, saveLobby, type LobbyState } from './lobbyState';
import { TeamBattle, type ReplayData } from './TeamBattle';
import {
  RARITY_ORDER, applyUpgrade, blocked, cost, evolution, levelCap, openPack, packBlocked, packOdds, seed, stats, toDto, vipLevel,
  type FullCatalog, type PackDef, type SimCard, type SimState,
} from './sim';

type Screen = { name: Legacy } | { name: 'open'; cards: SimCard[]; pack: PackDef } | { name: 'hub'; id: number };
const KEY = 'petstar.preview.state.v1';

function load(cat: FullCatalog): SimState {
  try { const j = sessionStorage.getItem(KEY); if (j) return JSON.parse(j) as SimState; } catch { /* storage blocked: fresh sandbox */ }
  return seed(cat);
}

let teamRulesCache: TeamRules | null = null;
async function loadTeamRules(): Promise<TeamRules> {
  if (teamRulesCache) return teamRulesCache;
  teamRulesCache = await (await fetch(`${import.meta.env.BASE_URL}data/team_battle.json`)).json() as TeamRules;
  return teamRulesCache;
}

/** Battle stats for a preview card: the same collection stats the Pet Hub shows; MAG/RES derived by role (team_battle.json). */
function unitOf(cat: FullCatalog, rules: TeamRules, c: SimCard, hpPct = 100): TeamSetupUnit {
  const st = stats(cat, c);
  const role = rules.roles[rules.kits[c.formId]?.role ?? 'striker'] ?? { magPct: 100, resPct: 100, dmg: 'phys' };
  return { formId: c.formId, cardInstanceId: c.id, hp: Math.round(st.hp * hpPct / 100), atk: st.atk, def: st.def, mag: Math.floor(st.atk * role.magPct / 100), res: Math.floor(st.def * role.resPct / 100), spd: st.spd, critBp: Math.round(st.crit * 10000) };
}
const synthetic = (formId: string, level: number, id: number): SimCard => ({ id, serial: 'AI', formId, rarity: 'R', quality: 'B', finish: 'Normal', level, stars: 1, enhance: 0, breakthrough: 0, awakened: false, skillLevel: 1, divineSet: null, origin: 'ai' });

interface Fight { id: number; a: TeamSetupPlayer; b: TeamSetupPlayer; start: BattleStart; stage?: ReturnType<typeof previewStages>[number] }
let fightSeq = 0;

export default function PreviewApp() {
  const cat = useCatalog() as FullCatalog;
  const { lang, setLang, t } = useI18n();
  const P = (vi: string, en: string) => (lang === 'vi' ? vi : en);
  const [s, setS] = useState<SimState>(() => load(cat));
  const [l, setL] = useState<LobbyState>(() => loadLobby());
  const [screen, setScreen] = useState<Screen | null>(null);
  const [fight, setFight] = useState<Fight | null>(null);
  const [rules, setRules] = useState<TeamRules | null>(teamRulesCache);
  const [toast, setToast] = useState<string | null>(null);
  const [portrait, setPortrait] = useState(false);
  useEffect(() => { try { sessionStorage.setItem(KEY, JSON.stringify(s)); } catch { /* ignore */ } }, [s]);
  useEffect(() => { saveLobby(l); }, [l]);
  useEffect(() => { if (!toast) return; const h = setTimeout(() => setToast(null), 2600); return () => clearTimeout(h); }, [toast]);
  useEffect(() => { loadTeamRules().then(setRules).catch(() => setRules(null)); }, []);
  useEffect(() => { const f = () => setPortrait(innerHeight > innerWidth); f(); addEventListener('resize', f); return () => removeEventListener('resize', f); }, []);
  // count evolutions done in the Pet Hub for the "Evolve 1 Pet" mission
  const forms = useRef(new Map(s.cards.map((c) => [c.id, c.formId])));
  useEffect(() => {
    let evolved = 0;
    for (const c of s.cards) { const before = forms.current.get(c.id); if (before && before !== c.formId) evolved++; }
    forms.current = new Map(s.cards.map((c) => [c.id, c.formId]));
    if (evolved > 0) setL((x) => ({ ...x, missions: { ...x.missions, counters: { ...x.missions.counters, evolve: (x.missions.counters.evolve ?? 0) + evolved } } }));
  }, [s.cards]);
  const say = (m: string) => setToast(m);
  const err = (code: string) => say(t('error.' + code));
  const packs = useMemo(() => [...cat.packs.packTypes].sort((a, b) => a.order - b.order), [cat]);
  const go = (sc: Screen | null) => setScreen(sc);
  const upd = (f: (x: LobbyState) => LobbyState) => setL(f);

  const startBattle = (b: BattleStart) => {
    if (!rules) return say(P('Đang tải dữ liệu trận đấu…', 'Loading battle data…'));
    if (b.replay) { setFight({ id: ++fightSeq, a: b.replay.a, b: b.replay.b, start: b }); return; }
    const best = [...s.cards].sort((x, y) => stats(cat, y).power - stats(cat, x).power);
    const teamIds = l.team.length === 3 && l.team.every((id) => s.cards.some((c) => c.id === id)) ? l.team : best.slice(0, 3).map((c) => c.id);
    const mine = teamIds.map((id) => s.cards.find((c) => c.id === id)!);
    if (mine.length < 3) return say(P('Cần ít nhất 3 Pet', 'You need at least 3 Pets'));
    const a: TeamSetupPlayer = { name: l.account?.name ?? 'You', units: mine.map((c) => unitOf(cat, rules, c)) };
    if (b.mode === 'pve' || b.mode === 'boss') {
      const stage = previewStages(Object.keys(cat.forms).filter((f) => !f.startsWith('pet_dv_')).sort()).find((x) => x.id === b.stageId)!;
      const ai = stage.forms.map((f, i) => unitOf(cat, rules, synthetic(f, stage.level, -1 - i), stage.boss && i === 0 ? 250 : 100));
      if (stage.boss) { ai[0].atk = Math.round(ai[0].atk * 1.37); ai[0].mag = Math.round(ai[0].mag * 1.37); }
      // preview difficulty follows the player's team: chapter c, stage i → 75% .. 135% of the team's strength
      const power = (us: TeamSetupUnit[]) => us.reduce((x, u) => x + u.hp * (u.atk + u.mag) / 2, 0);
      const target = 0.75 + 0.06 * (stage.index - 1) + 0.15 * (stage.chapter - 1);
      const k = Math.sqrt(power(a.units) * target / Math.max(1, power(ai)));
      for (const u of ai) { u.hp = Math.max(1, Math.round(u.hp * k)); u.atk = Math.round(u.atk * k); u.mag = Math.round(u.mag * k); u.def = Math.round(u.def * k); u.res = Math.round(u.res * k); }
      setFight({ id: ++fightSeq, a, b: { name: (stage.boss ? 'Boss ' : 'AI ') + `${stage.chapter}-${stage.index}`, units: ai }, start: b, stage });
    } else if (b.mode === 'practice') {
      const lvl = Math.max(1, Math.round(mine.reduce((x, c) => x + c.level, 0) / 3));
      setFight({ id: ++fightSeq, a, b: { name: P('Luyện tập', 'Practice'), units: mine.map((c, i) => unitOf(cat, rules, synthetic(c.formId, lvl, -10 - i))) }, start: b });
    } else {
      const p2 = best.filter((c) => !teamIds.includes(c.id)).slice(0, 3);
      if (p2.length < 3) return say(P('Đấu 2 người cần ít nhất 6 Pet (mở thêm gói thẻ)', '2-player battles need at least 6 Pets (open more packs)'));
      setFight({ id: ++fightSeq, a: { ...a, name: P('Người chơi 1', 'Player 1') }, b: { name: P('Người chơi 2', 'Player 2'), units: p2.map((c) => unitOf(cat, rules, c)) }, start: b });
    }
  };

  const finishBattle = (st: TeamState, rp: ReplayData) => {
    if (!fight || fight.start.replay) return;
    const win = st.winner === 0, mode = fight.start.mode;
    let reward = { beans: 0, stars: 0 };
    if ((mode === 'pve' || mode === 'boss') && win && fight.stage) {
      const first = !l.pveCleared.includes(fight.stage.id);
      reward = { beans: fight.stage.reward.beans, stars: fight.stage.reward.stars + (first ? 1 : 0) };
      setS((x) => ({ ...x, beans: x.beans + reward.beans, stars: x.stars + reward.stars }));
    }
    setL((x) => {
      let exp = x.exp + (win ? 40 : 15), level = x.level;
      while (exp >= EXP_PER_LEVEL(level)) { exp -= EXP_PER_LEVEL(level); level++; }
      const pvp = mode === 'hotseat';
      return {
        ...x, exp, level, battles: [rp, ...x.battles].slice(0, 12),
        season: { ...x.season, xp: x.season.xp + (win ? 40 : 10) },
        pveCleared: win && fight.stage && !x.pveCleared.includes(fight.stage.id) ? [...x.pveCleared, fight.stage.id] : x.pveCleared,
        record: pvp ? x.record : { ...x.record, pveWins: x.record.pveWins + (win && mode !== 'practice' ? 1 : 0), pveLosses: x.record.pveLosses + (!win && st.winner !== null && mode !== 'practice' ? 1 : 0) },
        missions: { ...x.missions, counters: { ...x.missions.counters, win_battle: (x.missions.counters.win_battle ?? 0) + (win && mode !== 'practice' ? 1 : 0) } },
      };
    });
    if (reward.beans || reward.stars) say(`${P('Phần thưởng (mô phỏng)', 'Rewards (simulated)')}: +${reward.beans} ${P('Đậu Xanh Mini', 'Mini Green Beans')}${reward.stars ? ` · +${reward.stars} ${P('Sao Ngũ Sắc', 'Rainbow Stars')}` : ''}`);
  };

  const login = (name: string) => setL((x) => ({ ...x, loggedIn: true, account: x.account ?? { name, uid: 100000000 + Math.floor(Math.random() * 899999999), createdAt: new Date().toISOString() } }));
  const legacyTitle = (sc: Screen) => ({ shop: P('Tiệm', 'Shop'), collection: 'Album Pet', divine: P('15 Thần Pet', '15 Divine Pets'), market: P('Chợ giao dịch', 'Marketplace'), status: P('Trạng thái', 'Status'), open: P('Mở gói', 'Pack opening'), hub: 'Pet Hub' } as Record<string, string>)[sc.name];

  return (
    <div className="pv-stage pv-landscape">
      <div className={'pv-frame lb-frame' + (portrait ? ' portrait' : '')}>
        {!l.loggedIn || !l.account ? <LoginScreen P={P} known={l.account?.name ?? null} onLogin={login} />
          : !l.trainer ? <TrainerSelect P={P} onPick={(id) => setL((x) => ({ ...x, trainer: id, outfit: id === 'trainer_boy' ? 'outfit_boy_default' : 'outfit_girl_default', seen: { ...x.seen } }))} />
          : fight && rules ? <TeamBattle key={fight.id} rules={rules} a={fight.a} b={fight.b}
              mode={fight.start.replay ? 'replay' : fight.start.mode} replay={fight.start.replay} P={P} onExit={() => setFight(null)} onFinish={finishBattle} />
          : <LobbyView s={s} setS={setS} l={l} upd={upd} P={P} lang={lang} setLang={setLang} say={say} openLegacy={(x) => go({ name: x })} startBattle={startBattle}
              logout={() => setL((x) => ({ ...x, loggedIn: false }))} />}
        {screen && (
          <Sheet title={legacyTitle(screen)} wide onClose={() => go(screen.name === 'hub' ? { name: 'collection' } : null)}>
            <div className="pv-legacy">
              <div className="pv-top"><Wallet s={s} /></div>
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
                  setL((x) => ({ ...x, missions: { ...x.missions, counters: { ...x.missions.counters, open_pack: (x.missions.counters.open_pack ?? 0) + 1 } } }));
                  go({ name: 'open', cards, pack: p });
                }} />}
              {screen.name === 'open' && <Opening cards={screen.cards} pack={screen.pack} P={P} t={t} done={() => go({ name: 'collection' })} />}
              {screen.name === 'collection' && <Collection s={s} P={P} open={(id) => go({ name: 'hub', id })} />}
              {screen.name === 'hub' && <Hub s={s} id={screen.id} P={P} t={t} setS={setS} say={say} err={err} />}
              {screen.name === 'divine' && <Divine s={s} P={P} />}
              {screen.name === 'market' && <Market s={s} setS={setS} P={P} say={say} err={err} />}
              {screen.name === 'status' && <Status P={P} />}
            </div>
          </Sheet>
        )}
        {portrait && <div className="lb-rotate-hint">↻ {P('Xoay ngang để chơi tốt nhất', 'Rotate to landscape for the best view')}</div>}
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
    P('Sảnh chính mới theo bố cục tham khảo: 4 nút lục giác, menu trái, thanh dưới, huy hiệu NEW theo trạng thái thật', 'New main lobby following the reference layout: 4 hex buttons, left menu, bottom bar, NEW badges from real state'),
    P('2 Huấn Luyện Viên chibi (nam/nữ) với khung chớp mắt, vui vẻ, chạm để phản ứng; Pet đồng hành đổi được', '2 chibi Trainers (boy/girl) with blink and happy frames and tap reactions; changeable companion Pet'),
    P('Đấu Pet 3v3 theo lượt: ra lệnh đồng thời, ưu tiên rồi SPD, EN, hồi chiêu, 10 trạng thái (engine TS đối chiếu với C# của server)', 'Turn-based 3v3 battle: simultaneous orders, priority then SPD, EN, cooldowns, 10 statuses (TS engine checked against the server C#)'),
    P('PvE theo chương, Boss, Luyện tập, 2 người cùng máy, xem lại trận (replay)', 'PvE chapters, Boss, Practice, same-device 2 players, match replay'),
    P('5 trò chơi nhỏ có giới hạn lượt thưởng và chống gian lận phía client', '5 mini games with reward-play limits and client-side anti-cheat'),
    P('Cửa hàng 9 gói, mở gói 5 thẻ với bảng tỷ lệ thật, bộ sưu tập, tiến hóa, chợ, 15 Thần Pet', 'Shop with 9 packs, 5-card opening with real odds tables, collection, evolution, market, 15 Divine Pets'),
  ];
  const missing = [
    P('Mọi số dư, phần thưởng và đối thủ trong bản xem trước là mô phỏng trong trình duyệt (không phải tiền tệ thật của server)', 'All balances, rewards and opponents in the preview are simulated in the browser (not real server currency)'),
    P('PvP 2 tài khoản qua mạng chỉ có trong game thật (server); đã kiểm thử bằng API tests, chưa có máy chủ Preview công khai', 'Online PvP between 2 accounts exists only in the real game (server); verified by API tests, no hosted Preview server yet'),
    P('Trò chơi nhỏ, Quảng Trường Pet, Thẻ Mùa: chưa có backend, chỉ mô phỏng ở đây', 'Mini games, Pet Plaza, Season Pass: no backend yet, simulated here only'),
    P('Âm thanh/nhạc, trang phục thêm, giao dịch trực tiếp giữa người chơi: chưa làm', 'Sound/music, extra outfits, direct player trading: not made yet'),
    P('Bản Unity chưa được build bằng Unity Editor thật (chỉ qua kiểm tra biên dịch)', 'Unity has not been built in a real Unity Editor yet (compile check only)'),
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
