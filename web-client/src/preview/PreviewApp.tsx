import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { CardBack, CardView } from '../components/CardView';
import { useI18n } from '../i18n';
import { RARITY_COLORS, petArt, uiArt, useCatalog } from '../state/catalog';
import type { TeamRules, TeamSetupPlayer, TeamSetupUnit, TeamState } from '../team/model';
import { LobbyView, LoginScreen, Sheet, TrainerSelect, previewStages, type BattleStart, type Legacy } from './Lobby';
import { useCosmetics } from './Cosmetics';
import { EXP_PER_LEVEL, currentUid, freshLobby, loadLobbyFor, saveLobbyFor, setCurrentUid, type LobbyState } from './lobbyState';
import { create as teamCreate, apply as teamApply, decide as teamDecide } from '../team/engine';
import { drainInbox, guildEvent, loadWorld, loginOrRegister, patchAccount, rarityRank, setGuildMissions, snap, warResult, type CardSnap, type GuildMission } from './world';
import { TeamBattle, type ReplayData } from './TeamBattle';
import {
  RARITY_ORDER, applyUpgrade, blocked, cost, evolution, levelCap, openPack, packBlocked, packOdds, seed, stats, toDto, vipLevel,
  type FullCatalog, type PackDef, type SimCard, type SimState,
} from './sim';

type Screen = { name: Legacy } | { name: 'open'; cards: SimCard[]; pack: PackDef } | { name: 'hub'; id: number };
const KEY = 'petstar.preview.state.v2';
const simKey = (uid: number) => `${KEY}.${uid}`;
function load(cat: FullCatalog, uid: number | null): SimState {
  if (uid != null) { try { const j = localStorage.getItem(simKey(uid)); if (j) return JSON.parse(j) as SimState; } catch { /* storage blocked: fresh sandbox */ } }
  return seed(cat);
}
const BOSS_MINIONS: Record<string, string[]> = { pet_calderon: ['pet_magmite', 'pet_emberkit'], pet_tidecrown: ['pet_puddlotl', 'pet_pupsplash'] };

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
  const [uid, setUid] = useState<number | null>(() => currentUid());
  const [s, setS] = useState<SimState>(() => load(cat, uid));
  const [l, setL] = useState<LobbyState>(() => (uid != null ? loadLobbyFor(uid) : freshLobby()));
  const cos = useCosmetics();
  useEffect(() => { if (cos) setGuildMissions(cos.missions as GuildMission[]); }, [cos]);
  const [screen, setScreen] = useState<Screen | null>(null);
  const [fight, setFight] = useState<Fight | null>(null);
  const [rules, setRules] = useState<TeamRules | null>(teamRulesCache);
  const [toast, setToast] = useState<string | null>(null);
  const [portrait, setPortrait] = useState(false);
  useEffect(() => { if (uid != null) { try { localStorage.setItem(simKey(uid), JSON.stringify(s)); } catch { /* ignore */ } } }, [s, uid]);
  useEffect(() => { if (uid != null) saveLobbyFor(uid, l); }, [l, uid]);
  // publish this account's public data to the shared Preview world (profile, leaderboards, trade partners, war defenders)
  useEffect(() => {
    if (uid == null || !l.loggedIn) return;
    const teamCards = l.team.map((id) => s.cards.find((c) => c.id === id)).filter(Boolean) as SimCard[];
    patchAccount(uid, {
      level: l.level, trainer: l.trainer, outfit: l.outfit, companion: s.cards.find((c) => c.id === l.companionId)?.formId ?? null,
      team: teamCards.map(snap), cards: s.cards.filter((c) => !c.listed).slice(0, 60).map((c) => ({ ...snap(c), id: c.id })),
      stats: { pvpWins: l.record.pvpWins, pvpGames: l.record.pvpWins + l.record.pvpLosses, collection: new Set(s.cards.map((c) => c.formId)).size,
        rare: s.cards.filter((c) => rarityRank(c.rarity) >= 3).length, evolutions: (l.evolved ?? []).length, pve: l.pveCleared.length,
        achievements: [l.starterClaimed, l.record.pveWins > 0, l.pveCleared.length >= 5, Object.keys(l.mini.best).length >= 3].filter(Boolean).length,
        minigames: Object.values(l.mini.best).reduce((x, v) => x + v, 0), bossDamage: loadWorld().accounts.find((a) => a.uid === uid)?.stats.bossDamage ?? 0,
        power: teamCards.reduce((x, c) => x + stats(cat, c).power, 0) },
    });
  }, [s, l, uid, cat]);
  // deliveries from other accounts (sale proceeds, bought/traded cards, returned escrow)
  useEffect(() => {
    if (uid == null) return;
    const pull = () => {
      const ib = drainInbox(uid);
      if (!ib.cards.length && !ib.beans) return;
      setS((x) => ({ ...x, beans: x.beans + ib.beans, cards: [...ib.cards, ...x.cards] }));
      setToast(`${ib.beans ? `+${ib.beans} 🫘 ` : ''}${ib.cards.length ? `+${ib.cards.length} ${lang === 'vi' ? 'thẻ' : 'cards'}` : ''} (${lang === 'vi' ? 'từ Chợ/Trao đổi' : 'from Market/Trades'})`);
    };
    pull(); const hnd = setInterval(pull, 2000); return () => clearInterval(hnd);
  }, [uid, lang]);
  useEffect(() => { if (!toast) return; const h = setTimeout(() => setToast(null), 2600); return () => clearTimeout(h); }, [toast]);
  useEffect(() => { loadTeamRules().then(setRules).catch(() => setRules(null)); }, []);
  useEffect(() => { const f = () => setPortrait(innerHeight > innerWidth); f(); addEventListener('resize', f); return () => removeEventListener('resize', f); }, []);
  // count evolutions done in the Pet Hub for the "Evolve 1 Pet" mission
  const forms = useRef(new Map(s.cards.map((c) => [c.id, c.formId])));
  useEffect(() => {
    let evolved = 0;
    const before0 = forms.current;
    for (const c of s.cards) { const before = forms.current.get(c.id); if (before && before !== c.formId) evolved++; }
    forms.current = new Map(s.cards.map((c) => [c.id, c.formId]));
    if (evolved > 0) {
      const ids = s.cards.filter((c) => { const b = before0.get(c.id); return b && b !== c.formId; }).map((c) => c.id);
      setL((x) => ({ ...x, evolved: [...new Set([...(x.evolved ?? []), ...ids])], missions: { ...x.missions, counters: { ...x.missions.counters, evolve: (x.missions.counters.evolve ?? 0) + evolved } } }));
      if (uid != null) guildEvent(uid, 'evolve', evolved);
    }
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
    if (b.mode === 'war' && b.war) {
      const foe = b.war.team.map((c, i) => unitOf(cat, rules, { ...synthetic(c.formId, c.level, -20 - i), rarity: c.rarity, quality: c.quality, stars: c.stars }));
      setFight({ id: ++fightSeq, a, b: { name: b.war.name, units: foe }, start: b });
    } else if (b.mode === 'pve' || b.mode === 'boss') {
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
    if (uid != null && mode === 'war') warResult(uid, st.winner === null ? null : win);
    if (uid != null && (mode === 'pve' || mode === 'boss') && win) guildEvent(uid, 'pve_win');
    setL((x) => {
      let exp = x.exp + (win ? 40 : 15), level = x.level;
      while (exp >= EXP_PER_LEVEL(level)) { exp -= EXP_PER_LEVEL(level); level++; }
      const pvp = mode === 'hotseat';
      if (mode === 'war') return { ...x, exp, level, battles: [rp, ...x.battles].slice(0, 12), record: { ...x.record, pvpWins: x.record.pvpWins + (win ? 1 : 0), pvpLosses: x.record.pvpLosses + (st.winner === 1 ? 1 : 0) } };
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

  const login = (name: string) => {
    const acc = loginOrRegister(name);
    setCurrentUid(acc.uid);
    const lobby = loadLobbyFor(acc.uid);
    setS(load(cat, acc.uid));
    setL({ ...lobby, loggedIn: true, account: lobby.account ?? { name: acc.name, uid: acc.uid, createdAt: acc.createdAt }, trainer: lobby.trainer ?? acc.trainer });
    setUid(acc.uid);
  };
  const logout = () => { setL((x) => ({ ...x, loggedIn: false })); setTimeout(() => { setCurrentUid(null); setUid(null); setL(freshLobby()); }, 0); };
  /** Guild Boss hit: your saved team vs the boss formation for 8 rounds on the shared 3v3 engine (AI both sides). */
  const bossFight = (formId: string, guildLevel: number): number | null => {
    if (!rules) return null;
    const best = [...s.cards].filter((c) => !c.listed).sort((x, y) => stats(cat, y).power - stats(cat, x).power);
    const ids = l.team.length === 3 && l.team.every((id) => s.cards.some((c) => c.id === id && !c.listed)) ? l.team : best.slice(0, 3).map((c) => c.id);
    if (ids.length < 3) return null;
    const mine = ids.map((id) => unitOf(cat, rules, s.cards.find((c) => c.id === id)!));
    const boss = unitOf(cat, rules, { ...synthetic(formId, 6 + 2 * guildLevel, -90), rarity: 'SR' }); boss.hp *= 40;
    const minions = (BOSS_MINIONS[formId] ?? [formId, formId]).map((f, i) => unitOf(cat, rules, synthetic(f, 3 + guildLevel, -91 - i)));
    const st = teamCreate(rules, { name: 'team', units: mine }, { name: 'boss', units: [boss, ...minions] }, BigInt(Math.floor(Math.random() * 2 ** 48)));
    while (st.phase !== 'finished' && st.round <= 8) { teamApply(rules, st, 0, teamDecide(rules, st, 0)); if ((st.phase as string) !== 'finished') teamApply(rules, st, 1, teamDecide(rules, st, 1)); }
    const bossSide = new Set(st.units.filter((u) => u.side === 1).map((u) => u.uid));
    return st.log.filter((e) => ['damage', 'counter', 'burn', 'poison'].includes(e.type) && bossSide.has(e.b)).reduce((x, e) => x + e.n, 0);
  };
  const startWar = (team: CardSnap[], name: string) => startBattle({ mode: 'war', war: { team, name } });
  const legacyTitle = (sc: Screen) => ({ shop: P('Tiệm', 'Shop'), collection: 'Album Pet', divine: P('15 Thần Pet', '15 Divine Pets'), market: P('Chợ giao dịch', 'Marketplace'), status: P('Trạng thái', 'Status'), open: P('Mở gói', 'Pack opening'), hub: 'Pet Hub' } as Record<string, string>)[sc.name];

  return (
    <div className="pv-stage pv-landscape">
      <div className={'pv-frame lb-frame' + (portrait ? ' portrait' : '')}>
        {uid == null || !l.loggedIn || !l.account ? <LoginScreen P={P} known={null} accounts={loadWorld().accounts.map((a) => a.name)} onLogin={login} />
          : !l.trainer ? <TrainerSelect P={P} onPick={(id) => setL((x) => ({ ...x, trainer: id, outfit: id === 'trainer_boy' ? 'outfit_boy_default' : 'outfit_girl_default', seen: { ...x.seen } }))} />
          : fight && rules ? <TeamBattle key={fight.id} rules={rules} a={fight.a} b={fight.b}
              mode={fight.start.replay ? 'replay' : fight.start.mode === 'war' ? 'pve' : fight.start.mode} replay={fight.start.replay} P={P} onExit={() => setFight(null)} onFinish={finishBattle} />
          : <LobbyView s={s} setS={setS} l={l} upd={upd} P={P} lang={lang} setLang={setLang} say={say} openLegacy={(x) => go({ name: x })} startBattle={startBattle}
              logout={logout} bossFight={bossFight} startWar={startWar} />}
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
                  setL((x) => ({ ...x, lastPack: cards.map((c) => c.id), missions: { ...x.missions, counters: { ...x.missions.counters, open_pack: (x.missions.counters.open_pack ?? 0) + 1 } } }));
                  if (uid != null) guildEvent(uid, 'pack_open');
                  go({ name: 'open', cards, pack: p });
                }} />}
              {screen.name === 'open' && <Opening cards={screen.cards} pack={screen.pack} P={P} t={t} done={() => go({ name: 'collection' })} />}
              {screen.name === 'collection' && <Collection s={s} P={P} open={(id) => go({ name: 'hub', id })} />}
              {screen.name === 'hub' && <Hub s={s} id={screen.id} P={P} t={t} setS={setS} say={say} err={err} />}
              {screen.name === 'divine' && <Divine s={s} P={P} />}
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

function Status({ P }: { P: PFn }) {
  const done = [
    P('Sảnh 5 nút lục giác (Chợ Giao Dịch, Trò Chơi Khác, Sảnh Liên Thông, Bắt Đầu, Social Hub) với artwork gốc, nhãn không bị cắt', '5-hex lobby (Market, Mini Games, Pet World, Start, Social Hub) with original art, labels never clipped'),
    P('Social Hub: bảng tin với quyền xem, đính kèm Pet, Pet Showcase, Like/Bình luận/Trả lời/Like bình luận/Chia sẻ, thông báo, báo cáo, chặn, giữ bài nghi vấn để kiểm duyệt', 'Social Hub: feed with audiences, attached Pets, Pet Showcase, like/comment/reply/comment-like/share, notifications, report, block, risky posts held for moderation'),
    P('Guild: lập/tìm/vào/rời/giải tán an toàn, 5 vai trò, nhiệm vụ, Boss Guild (engine 3v3), Guild War, chat, bài nội bộ', 'Guild: found/find/join/leave/safe dissolve, 5 roles, missions, Guild Boss (3v3 engine), Guild War, chat, members-only posts'),
    P('Bảng xếp hạng 17 bảng, bục Top 1/2/3, bộ lọc kỳ, hạng của tôi', '17 leaderboards, Top 1/2/3 podium, period filters, my rank'),
    P('Chợ Pet chỉ Đậu Xanh Mini: đăng bán (khóa thẻ), mua, trao đổi thẻ (kiểm tra lại khi chấp nhận), lịch sử', 'Pet Market in Mini Green Beans only: list (card locked), buy, card trades (re-checked on accept), history'),
    P('Chat 3 kênh góc dưới trái, khung avatar, bong bóng chat, hiệu ứng, Tiệm Skin Trainer Nam/Nữ (thử, mua, trang bị), Shop Hub, Hồ sơ 10 tab', '3-channel chat dock, avatar frames, chat bubbles, effects, Male/Female skin shop (try, buy, equip), Shop Hub, 10-tab profile'),
    P('Giữ nguyên: Trainer, Pet đồng hành, gói 5 thẻ, Album, tiến hóa, Battle 3v3 PvE/Boss/Luyện tập/2 người, Mini Games', 'Kept: Trainers, companion, 5-card packs, Album, evolution, Battle 3v3 PvE/Boss/Practice/2-player, Mini Games'),
  ];
  const missing = [
    P('Bản Preview chạy "máy chủ Preview" trong trình duyệt (localStorage): hai tài khoản tương tác thật bằng cách đổi tài khoản hoặc mở 2 tab; không có người chơi khác qua mạng', 'The Preview runs a "Preview server" inside the browser (localStorage): two accounts interact for real by switching accounts or using 2 tabs; no other players over the network'),
    P('Bản thật dùng API máy chủ + PostgreSQL (đã có và đã kiểm thử), chưa có máy chủ Preview công khai', 'The real game uses the server API + PostgreSQL (built and tested); no hosted Preview server yet'),
    P('Tải ảnh lên bài đăng, kiểu tóc/phụ kiện riêng, âm thanh/nhạc: chưa làm', 'Image upload in posts, separate hairstyles/accessories, sound/music: not done'),
    P('Giao diện Admin cho kiểm duyệt Social/Guild/Chat/Market: chưa làm (đã có API)', 'Admin UI for Social/Guild/Chat/Market moderation: not done (API exists)'),
    P('Unity chưa có các màn Social Hub/Guild/Shop mới', 'Unity does not have the new Social Hub/Guild/Shop screens yet'),
  ];
  return (
    <div className="pv-status">
      <h2 className="pv-h">{P('Trạng thái bản xem trước', 'Preview status')}</h2>
      <h3>✅ {P('Đã có', 'Done')}</h3><ul>{done.map((x) => <li key={x}>{x}</li>)}</ul>
      <h3>⏳ {P('Còn thiếu / chỉ mô phỏng', 'Missing / simulated only')}</h3><ul>{missing.map((x) => <li key={x}>{x}</li>)}</ul>
      <p className="muted small">{P('Dữ liệu Preview lưu trong trình duyệt này (localStorage). Không có request nào tới API hay database.', 'Preview data is stored in this browser (localStorage). No request goes to any API or database.')}</p>
    </div>
  );
}
