// Battle 3v3 for the Interactive Web Preview (landscape). Rules run in the TypeScript port of the server engine
// (src/team/engine.ts), checked event-by-event against the C# engine. Both sides lock hidden orders; the round then
// resolves by skill priority and SPD and is played back step by step. Nothing is sent to a server.
import { useEffect, useRef, useState } from 'react';
import { apply, create, decide, has, mustAct, orderProblem, skillFor, timeout } from '../team/engine';
import { TeamRuleError, alive, type TeamEvent, type TeamOrder, type TeamRules, type TeamSetupPlayer, type TeamState, type TeamUnit } from '../team/model';
import { useI18n } from '../i18n';
import { ELEMENT_COLORS, petArt, uiArt, useCatalog } from '../state/catalog';

type PFn = (vi: string, en: string) => string;
export type BattleMode = 'pve' | 'boss' | 'practice' | 'hotseat' | 'replay';
export interface ReplayData { seed: string; a: TeamSetupPlayer; b: TeamSetupPlayer; steps: { p: number; orders: TeamOrder[] | null }[]; mode: BattleMode; at: number; winner: number | null }
interface Fx { id: number; uid: number; text: string; cls: string }

const SLOTS = ['basic', 'skill', 'ultimate', 'guard'] as const;
const STATUS_ICON: Record<string, string> = { burn: '🔥', poison: '☠', stun: '💫', freeze: '❄', silence: '🤐', regen: '✚', haste: '⚡', weaken: '⬇', taunt: '🛡️', counter: '↩', guard: '🧱' };

export function TeamBattle({ rules, a, b, mode, P, onExit, onFinish, replay }: {
  rules: TeamRules; a: TeamSetupPlayer; b: TeamSetupPlayer; mode: BattleMode; P: PFn;
  onExit: () => void; onFinish?: (st: TeamState, rp: ReplayData) => void; replay?: ReplayData;
}) {
  const { t, lang } = useI18n();
  const cat = useCatalog();
  const seedRef = useRef(replay ? BigInt(replay.seed) : BigInt(crypto.getRandomValues(new Uint32Array(1))[0]));
  const stRef = useRef<TeamState>(create(rules, a, b, seedRef.current));
  const steps = useRef<ReplayData['steps']>([]);
  const st = stRef.current;
  const [view, setView] = useState<TeamUnit[]>(() => structuredClone(st.units));
  const [orders, setOrders] = useState<Record<number, TeamOrder>>({});
  const [sel, setSel] = useState<number>(0);
  const [side, setSide] = useState(0);                         // whose orders are being chosen (hot-seat swaps)
  const [handoff, setHandoff] = useState(false);
  const [playing, setPlaying] = useState(false);
  const [actor, setActor] = useState(0);
  const [banner, setBanner] = useState<string | null>(null);
  const [fx, setFx] = useState<Fx[]>([]);
  const [log, setLog] = useState<string[]>([]);
  const [timerOn, setTimerOn] = useState(mode !== 'replay');
  const [left, setLeft] = useState(rules.config.planSeconds);
  const [, force] = useState(0);
  const fxId = useRef(0), seen = useRef(st.log.length), done = useRef(false);

  const name = (formId: string) => (t('pet.' + formId) !== 'pet.' + formId ? t('pet.' + formId) : cat.forms[formId]?.name ?? formId.replace('pet_', ''));
  const skillName = (id: string) => rules.skills[id]?.name[lang] ?? id;
  const unit = (uid: number) => view.find((u) => u.uid === uid);
  const realUnit = (uid: number) => st.units.find((u) => u.uid === uid)!;
  const myUnits = st.units.filter((u) => u.side === side && alive(u)).sort((x, y) => x.slot - y.slot);
  const selUnit = myUnits.find((u) => u.uid === sel) ?? myUnits[0];

  useEffect(() => { if (!selUnit && myUnits[0]) setSel(myUnits[0].uid); }, [selUnit, myUnits]);

  // ------------------------------------------------------------ playback of a resolved round
  const play = async (events: TeamEvent[]) => {
    setPlaying(true);
    const disp = structuredClone(view);
    const U = (uid: number) => disp.find((u) => u.uid === uid);
    const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));
    const pop = (uid: number, text: string, cls: string) => { const id = ++fxId.current; setFx((f) => [...f, { id, uid, text, cls }]); setTimeout(() => setFx((f) => f.filter((x) => x.id !== id)), 1100); };
    const lines: string[] = [];
    for (const e of events) {
      let pause = 0;
      switch (e.type) {
        case 'round': setBanner(P(`Vòng ${e.n}`, `Round ${e.n}`)); pause = 500; break;
        case 'act': { setActor(e.a); const u = U(e.a); if (u) u.en = e.n; setBanner(`${name(realUnit(e.a).formId)}: ${skillName(e.s)}`); lines.push(`${name(realUnit(e.a).formId)} → ${skillName(e.s)}`); pause = 420; break; }
        case 'damage': {
          const u = U(e.b); const abs = Number((e.s.match(/shield(\d+)/) ?? [])[1] ?? 0);
          if (u) { u.shield = Math.max(0, u.shield - abs); u.hp = Math.max(0, u.hp - Math.max(0, e.n - abs)); }
          pop(e.b, `-${e.n}${e.s.includes('crit') ? '!' : ''}`, e.s.includes('strong') ? 'dmg strong' : e.s.includes('weak') ? 'dmg weak' : 'dmg'); pause = 330; break;
        }
        case 'counter': { const u = U(e.b); if (u) u.hp = Math.max(0, u.hp - e.n); pop(e.b, `↩ -${e.n}`, 'dmg'); pause = 250; break; }
        case 'burn': case 'poison': { const u = U(e.b); if (u) u.hp = Math.max(0, u.hp - e.n); pop(e.b, `${STATUS_ICON[e.type]} -${e.n}`, 'dot'); pause = 220; break; }
        case 'heal': { const u = U(e.b); if (u) u.hp = Math.min(u.maxHp, u.hp + e.n); pop(e.b, `+${e.n}`, 'heal'); pause = 220; break; }
        case 'shield': { const u = U(e.b); if (u) u.shield = e.n; pop(e.b, `🛡 ${e.n}`, 'heal'); pause = 200; break; }
        case 'status': { const u = U(e.b); if (u) { u.statuses = u.statuses.filter((s) => s.type !== e.s); u.statuses.push({ type: e.s, remaining: e.n, stacks: 1 }); } pop(e.b, `${STATUS_ICON[e.s] ?? ''} ${t('team.status.' + e.s) !== 'team.status.' + e.s ? t('team.status.' + e.s) : e.s}`, 'status'); pause = 220; break; }
        case 'resist': pop(e.b, P('Kháng!', 'Resist!'), 'status'); pause = 180; break;
        case 'skip': pop(e.a, `${STATUS_ICON[e.s]} ${P('Mất lượt', 'Skipped')}`, 'status'); pause = 300; break;
        case 'cleanse': { const u = U(e.b); if (u) u.statuses = []; pop(e.b, P('Giải trừ', 'Cleansed'), 'heal'); pause = 200; break; }
        case 'energy': { const u = U(e.b); if (u) u.en = Math.min(rules.config.maxEn, u.en + e.n); pop(e.b, `+${e.n} EN`, 'heal'); pause = 150; break; }
        case 'down': { const u = U(e.b); if (u) u.hp = 0; pop(e.b, P('Gục!', 'Down!'), 'down'); lines.push(`✖ ${name(realUnit(e.b).formId)}`); pause = 450; break; }
        case 'guard': pop(e.a, P('Phòng thủ', 'Guard'), 'status'); pause = 200; break;
        case 'finish': setBanner(e.n === side ? P('CHIẾN THẮNG!', 'VICTORY!') : e.n < 0 ? P('HÒA', 'DRAW') : P('THẤT BẠI', 'DEFEAT')); pause = 600; break;
      }
      if (pause) { setView(structuredClone(disp)); await wait(pause); }
    }
    setActor(0); setBanner(null);
    setView(structuredClone(st.units));
    setLog((l) => [...lines.reverse(), ...l].slice(0, 40));
    setPlaying(false);
  };

  const collect = () => { const evs = st.log.slice(seen.current); seen.current = st.log.length; return evs; };

  const lockFor = (p: number, list: TeamOrder[] | null) => {
    if (list === null) timeout(rules, st); else apply(rules, st, p, { type: 'plan', orders: list });
    steps.current.push({ p, orders: list });
  };

  const lock = () => {
    const list = myUnits.map((u) => orders[u.uid] ?? { unit: u.uid, slot: 'basic', target: 0 });
    try { lockFor(side, list); }
    catch (e) { alert(e instanceof TeamRuleError ? (t('error.' + e.code) !== 'error.' + e.code ? t('error.' + e.code) : e.code) : String(e)); return; }
    setOrders({});
    if (mode === 'hotseat' && mustAct(st, 1 - side)) { setHandoff(true); setSide(1 - side); force((x) => x + 1); return; }
    if (mode !== 'hotseat' && mustAct(st, 1)) lockFor(1, decide(rules, st, 1).orders);
    if (mode === 'hotseat') setSide(0);
    void play(collect());
  };

  // replay mode: feed the stored steps round by round
  useEffect(() => {
    if (mode !== 'replay' || !replay || playing) return;
    if (st.phase === 'finished') return;
    const h = setTimeout(() => {
      const before = st.log.length;
      while (st.phase !== 'finished' && steps.current.length < replay.steps.length) {
        const s = replay.steps[steps.current.length];
        lockFor(s.p, s.orders);
        if (st.log.slice(before).some((e) => e.type === 'round')) break;
      }
      void play(collect());
    }, 400);
    return () => clearTimeout(h);
  });

  // plan timer (same seconds as the server; the preview can switch it off)
  useEffect(() => { setLeft(rules.config.planSeconds); }, [st.round, side, rules]);
  useEffect(() => {
    if (!timerOn || playing || handoff || st.phase === 'finished' || mode === 'replay') return;
    const h = setInterval(() => setLeft((l) => {
      if (l > 1) return l - 1;
      lockFor(side, null);
      if (mode !== 'hotseat' && mustAct(st, 1)) lockFor(1, decide(rules, st, 1).orders);
      if (mode === 'hotseat' && mustAct(st, 1 - side)) { setHandoff(true); setSide(1 - side); return rules.config.planSeconds; }
      setSide(0);
      void play(collect());
      return rules.config.planSeconds;
    }), 1000);
    return () => clearInterval(h);
  });

  useEffect(() => {
    if (st.phase === 'finished' && !playing && !done.current) {
      done.current = true;
      onFinish?.(st, { seed: seedRef.current.toString(), a, b, steps: steps.current, mode, at: Date.now(), winner: st.winner });
    }
  });

  // ------------------------------------------------------------ orders
  const setOrder = (slot: string) => {
    if (!selUnit) return;
    const sk = slot === 'guard' ? null : skillFor(rules, selUnit, slot);
    const prev = orders[selUnit.uid];
    const enemyTarget = sk?.target === 'enemy_single';
    const target = enemyTarget ? (prev?.target && st.units.find((u) => u.uid === prev.target && u.side !== side && alive(u)) ? prev.target : 0) : 0;
    setOrders({ ...orders, [selUnit.uid]: { unit: selUnit.uid, slot, target } });
    if (!enemyTarget) { const next = myUnits.find((u) => !orders[u.uid] && u.uid !== selUnit.uid); if (next) setSel(next.uid); }
  };
  const tapEnemy = (u: TeamUnit) => {
    if (!selUnit || playing || !alive(u)) return;
    const o = orders[selUnit.uid] ?? { unit: selUnit.uid, slot: 'basic', target: 0 };
    const sk = o.slot === 'guard' ? null : skillFor(rules, selUnit, o.slot);
    if (sk && sk.target !== 'enemy_single') return;
    setOrders({ ...orders, [selUnit.uid]: { ...o, slot: o.slot === 'guard' ? 'basic' : o.slot, target: u.uid } });
    const next = myUnits.find((x) => !orders[x.uid] && x.uid !== selUnit.uid); if (next) setSel(next.uid);
  };

  // ------------------------------------------------------------ rendering
  const card = (u: TeamUnit, mine: boolean) => {
    const v = unit(u.uid) ?? u;
    const pct = Math.round((100 * v.hp) / v.maxHp), en = Math.round((100 * v.en) / rules.config.maxEn);
    const isSel = mine && sel === u.uid && !playing && mode !== 'replay';
    const targeted = !mine && Object.values(orders).some((o) => o.target === u.uid);
    return (
      <button key={u.uid} className={'tb-unit' + (mine ? ' mine' : ' foe') + (v.hp <= 0 ? ' down' : '') + (isSel ? ' sel' : '') + (targeted ? ' targeted' : '') + (actor === u.uid ? ' acting' : '')}
        style={{ ['--el' as string]: ELEMENT_COLORS[u.element] ?? '#9ab' }} onClick={() => (mine ? setSel(u.uid) : tapEnemy(u))} data-testid={`unit-${u.side}-${u.slot}`}>
        <img src={petArt(u.formId, true)} alt="" draggable={false} />
        <span className="tb-name">{name(u.formId)}</span>
        <span className="tb-bars">
          <i className="hp"><b style={{ width: pct + '%' }} className={pct < 30 ? 'low' : ''} />{v.shield > 0 && <s style={{ width: Math.min(100, (100 * v.shield) / v.maxHp) + '%' }} />}</i>
          <i className="en"><b style={{ width: en + '%' }} /></i>
        </span>
        <span className="tb-nums">{v.hp}/{v.maxHp} · {v.en} EN</span>
        <span className="tb-st">{v.statuses.filter((s) => s.type !== 'guard').map((s) => <em key={s.type} title={s.type}>{STATUS_ICON[s.type] ?? '•'}{s.stacks > 1 ? s.stacks : ''}</em>)}</span>
        {mine && orders[u.uid] && !playing && <span className="tb-order">{orders[u.uid].slot === 'guard' ? '🧱' : orders[u.uid].slot === 'basic' ? '⚔' : orders[u.uid].slot === 'skill' ? '✦' : '★'}</span>}
        {fx.filter((f) => f.uid === u.uid).map((f) => <span key={f.id} className={'tb-fx ' + f.cls}>{f.text}</span>)}
      </button>
    );
  };
  const teamOf = (s2: number) => st.units.filter((u) => u.side === s2).sort((x, y) => x.slot - y.slot);
  const finished = st.phase === 'finished' && !playing;
  const iWon = st.winner === (mode === 'hotseat' ? st.winner : 0);

  return (
    <div className="tb" style={{ backgroundImage: `url(${uiArt('bg_duel_board_landscape')})` }}>
      <div className="tb-top">
        <span className="tb-player">🔵 {st.players[mode === 'hotseat' ? 0 : 0].name}</span>
        <span className="tb-round">{P('Vòng', 'Round')} {st.round}/{rules.config.maxRounds}</span>
        {!finished && mode !== 'replay' && <span className={'tb-timer' + (left <= 8 ? ' warn' : '')}>{timerOn ? `⏱ ${left}` : '⏱ ∞'}</span>}
        <span className="tb-player red">🔴 {st.players[1].name}</span>
      </div>
      <div className="tb-field">
        <div className="tb-team">{teamOf(side === 1 && mode === 'hotseat' ? 1 : 0).map((u) => card(u, true))}</div>
        <div className="tb-vs">{banner ? <span className="tb-banner">{banner}</span> : 'VS'}</div>
        <div className="tb-team">{teamOf(side === 1 && mode === 'hotseat' ? 0 : 1).map((u) => card(u, false))}</div>
      </div>
      <div className="tb-panel">
        {mode !== 'replay' && !finished && selUnit && !playing && (
          <>
            <div className="tb-who"><b>{name(selUnit.formId)}</b> <small>{P('chọn lệnh', 'choose an order')}{mode === 'hotseat' ? ` · ${P('Người chơi', 'Player')} ${side + 1}` : ''}</small></div>
            <div className="tb-actions">
              {SLOTS.map((slot) => {
                const sk = slot === 'guard' ? null : skillFor(rules, selUnit, slot);
                if (slot !== 'guard' && !sk) return null;
                const prob = orderProblem(rules, st, side, { unit: selUnit.uid, slot, target: 0 });
                const cd = sk ? selUnit.cooldowns[sk.id] ?? 0 : 0;
                const chosen = orders[selUnit.uid]?.slot === slot;
                return (
                  <button key={slot} className={'tb-act ' + slot + (chosen ? ' on' : '')} disabled={!!prob} onClick={() => setOrder(slot)} data-testid={`act-${slot}`}
                    title={sk ? sk.effects.map((e) => e.op + (e.status ? ':' + e.status : '')).join(', ') : ''}>
                    <b>{slot === 'guard' ? P('Phòng thủ', 'Guard') : skillName(sk!.id)}</b>
                    <small>{slot === 'guard' ? P('-50% sát thương', '-50% damage') : slot === 'basic' ? `+${rules.config.basicEnGain} EN` : `${sk!.cost} EN${cd > 0 ? ` · ⏳${cd}` : sk!.cooldown ? ` · CD ${sk!.cooldown}` : ''}`}</small>
                    {sk && <small className="tb-tg">{sk.target === 'enemy_single' ? P('1 địch', '1 enemy') : sk.target === 'enemy_all' ? P('Toàn bộ địch', 'All enemies') : sk.target === 'ally_all' ? P('Cả đội', 'Whole team') : sk.target === 'ally_lowest' ? P('Đồng đội yếu nhất', 'Weakest ally') : P('Bản thân', 'Self')}</small>}
                  </button>
                );
              })}
            </div>
            <div className="tb-lock">
              <small>{Object.keys(orders).length}/{myUnits.length} {P('lệnh', 'orders')} · {P('chạm địch để chọn mục tiêu', 'tap an enemy to target')}</small>
              <button className="btn primary" onClick={lock} data-testid="lock">{P('Khóa lệnh', 'Lock orders')}</button>
            </div>
          </>
        )}
        {playing && <div className="tb-who"><small>{P('Đang xử lý vòng đấu theo ưu tiên và SPD…', 'Resolving the round by priority and SPD…')}</small></div>}
        <div className="tb-log">{log.slice(0, 6).map((l, i) => <div key={i}>{l}</div>)}</div>
        <div className="tb-foot">
          {mode !== 'replay' && <label className="small"><input type="checkbox" checked={timerOn} onChange={(e) => setTimerOn(e.target.checked)} /> ⏱</label>}
          {!finished && mode !== 'replay' && <button className="btn ghost small" onClick={() => { apply(rules, st, side, { type: 'surrender', orders: [] }); steps.current.push({ p: side, orders: [] }); void play(collect()); }}>{P('Đầu hàng', 'Surrender')}</button>}
          <button className="btn ghost small" onClick={onExit}>‹ {P('Thoát', 'Exit')}</button>
        </div>
      </div>
      {handoff && (
        <div className="tb-modal"><div className="tb-sheet">
          <b>{P('Chuyển máy cho Người chơi 2', 'Pass the device to Player 2')}</b>
          <small>{P('Lệnh của Người chơi 1 đã khóa và được giấu.', "Player 1's orders are locked and hidden.")}</small>
          <button className="btn primary" onClick={() => setHandoff(false)} data-testid="handoff">{P('Sẵn sàng', 'Ready')}</button>
        </div></div>
      )}
      {finished && (
        <div className="tb-modal"><div className={'tb-sheet ' + (st.winner === null ? '' : iWon ? 'win' : 'lose')}>
          <div className="tb-result">{st.winner === null ? P('HÒA', 'DRAW') : mode === 'hotseat' ? P(`NGƯỜI CHƠI ${st.winner + 1} THẮNG`, `PLAYER ${st.winner + 1} WINS`) : st.winner === 0 ? P('CHIẾN THẮNG!', 'VICTORY!') : P('THẤT BẠI', 'DEFEAT')}</div>
          <small>{({ knockout: P('Hạ toàn bộ Pet đối thủ', 'All enemy Pets down'), round_limit: P('Hết vòng — so % HP', 'Round limit — HP share'), both_down: P('Cả hai đội cùng gục', 'Both teams down'), surrender: P('Đầu hàng', 'Surrender'), timeout: P('Hết giờ nhiều lần', 'Repeated timeouts') } as Record<string, string>)[st.endReason ?? ''] ?? st.endReason} · {P('Vòng', 'Round')} {st.round}</small>
          <button className="btn primary" onClick={onExit} data-testid="battle-exit">{P('Tiếp tục', 'Continue')}</button>
        </div></div>
      )}
    </div>
  );
}

export const _has = has;
