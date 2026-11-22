// Playable Pet Battle Arena for the Interactive Web Preview (portrait, phone first).
// Rules run in the TypeScript port of the server engine (src/arena/engine.ts), checked event-by-event against the
// C# engine by cross-engine fixtures. The opponent is the same rule-abiding AI as on the server. Nothing is saved.
import { useEffect, useReducer, useRef, useState } from 'react';
import { decide } from '../arena/ai';
import { activeMods, apply, attackProblem, card, create, maxHp, mustAct, retreatCost, timeout, trainerPlayable } from '../arena/engine';
import { ArenaRuleError, ArenaRules, attacksOf, effectsOf, finished, inPlay, isBasic, type ArenaAction, type ArenaCardDef, type ArenaPet, type ArenaRulesData } from '../arena/model';
import { useI18n } from '../i18n';
import { ELEMENT_COLORS, petArt } from '../state/catalog';
import { aiDeck, playerDeck } from './arenaSetup';
import type { SimState } from './sim';

type PFn = (vi: string, en: string) => string;
type Sel = { kind: 'hand'; card: number } | { kind: 'energy' } | { kind: 'pet'; uid: number } | { kind: 'attack'; index: number } | null;
interface Fx { id: number; uid: number; text: string; cls: string }
const ME = 0, AI = 1;
const ICON: Record<string, string> = { item: '🧪', tool: '🎒', supporter: '🧑‍🏫' };

let cachedRules: ArenaRules | null = null;
async function loadRules(): Promise<ArenaRules> {
  if (cachedRules) return cachedRules;
  const r = await fetch(`${import.meta.env.BASE_URL}data/arena_cards.json`);
  cachedRules = new ArenaRules(await r.json() as ArenaRulesData);
  return cachedRules;
}

export function Arena({ s, P, exit }: { s: SimState; P: PFn; exit: () => void }) {
  const [rules, setRules] = useState<ArenaRules | null>(cachedRules);
  useEffect(() => { if (!rules) loadRules().then(setRules).catch(() => setRules(null)); }, [rules]);
  if (!rules) return <div className="ar-loading">{P('Đang tải dữ liệu thẻ…', 'Loading card data…')}</div>;
  return <Board rules={rules} s={s} P={P} exit={exit} />;
}

function Board({ rules, s, P, exit }: { rules: ArenaRules; s: SimState; P: PFn; exit: () => void }) {
  const { t, lang } = useI18n();
  const mk = () => {
    const mine = playerDeck(rules, s);
    const seed = BigInt(crypto.getRandomValues(new Uint32Array(1))[0]);
    const st = create(rules, { name: s.player.nickname, deck: mine.deck, energyTypes: [] }, { name: 'AI', deck: aiDeck(rules, Number(seed % 97n)), energyTypes: [] }, seed, true);
    return { st, demo: mine.demo };
  };
  const ref = useRef(mk());
  const st = ref.current.st;
  const [, force] = useReducer((x: number) => x + 1, 0);
  const [sel, setSel] = useState<Sel>(null);
  const [setup, setSetup] = useState<{ active: number | null; bench: number[] }>({ active: null, bench: [] });
  const [fx, setFx] = useState<Fx[]>([]);
  const [banner, setBanner] = useState<string | null>(null);
  const [errMsg, setErr] = useState<string | null>(null);
  const [showLog, setShowLog] = useState(false);
  const [timerOn, setTimerOn] = useState(false);
  const [left, setLeft] = useState(rules.config.turnSeconds);
  const seen = useRef(0), fxId = useRef(0);

  const D = (c: number) => rules.def(card(st, c).defId);
  const name = (d: ArenaCardDef) => (d.kind === 'pet' ? (t('pet.' + d.id) !== 'pet.' + d.id ? t('pet.' + d.id) : d.name[lang] ?? d.id) : d.name[lang] ?? d.id);
  const me = st.players[ME], ai = st.players[AI];
  const myTurn = !finished(st) && st.phase === 'main' && mustAct(st, ME) && !me.mustPromote;

  const statusName = (x: string) => ({ poisoned: P('Trúng độc', 'Poisoned'), burned: P('Bỏng', 'Burned'), asleep: P('Ngủ', 'Asleep'), paralyzed: P('Tê liệt', 'Paralyzed'), confused: P('Bối rối', 'Confused') } as Record<string, string>)[x] ?? x;

  const collect = () => {
    const evs = st.log.slice(seen.current); seen.current = st.log.length;
    const add: Fx[] = [];
    for (const e of evs) {
      const id = ++fxId.current;
      if (e.type === 'damage' && e.n > 0) add.push({ id, uid: e.a, text: `−${e.n}`, cls: 'dmg' });
      else if (e.type === 'heal') add.push({ id, uid: e.a, text: `+${e.n}`, cls: 'heal' });
      else if (e.type === 'status') add.push({ id, uid: e.a, text: statusName(e.s), cls: 'status' });
      else if (e.type === 'evolve') add.push({ id, uid: e.a, text: P('Tiến hóa!', 'Evolved!'), cls: 'evolve' });
      else if (e.type === 'knockout') setBanner(`${e.p === ME ? P('Pet của bạn bị hạ', 'Your Pet was knocked out') : P('Hạ gục!', 'Knock out!')} +${e.n} ★`);
      else if (e.type === 'coin') setBanner(`🪙 ${e.a === 1 ? P('Ngửa', 'Heads') : P('Sấp', 'Tails')}`);
      else if (e.type === 'supporter' || e.type === 'item') setBanner(`${e.p === ME ? P('Bạn', 'You') : 'AI'}: ${name(rules.def(e.s))}`);
      else if (e.type === 'turn') setBanner(e.p === ME ? P('Lượt của bạn', 'Your turn') : P('Lượt của đối thủ', "Opponent's turn"));
      else if (e.type === 'attack_failed') setBanner(P('Bối rối: đòn đánh thất bại', 'Confused: the attack failed'));
    }
    if (add.length) { setFx((f) => [...f, ...add]); setTimeout(() => setFx((f) => f.filter((x) => !add.includes(x))), 1200); }
  };
  const run = (a: ArenaAction) => {
    try { apply(rules, st, ME, a); setErr(null); }
    catch (e) { const code = e instanceof ArenaRuleError ? e.code : String(e); const k = 'error.' + code; setErr(t(k) !== k ? t(k) : code.replace(/_/g, ' ')); }
    setSel(null); collect(); force();
  };

  // AI moves one step at a time
  useEffect(() => {
    if (finished(st) || !mustAct(st, AI) || (st.phase === 'setup' && !me.setupDone)) return;
    const h = setTimeout(() => { apply(rules, st, AI, decide(rules, st, AI)); collect(); force(); }, st.phase === 'setup' ? 300 : 700);
    return () => clearTimeout(h);
  });
  useEffect(() => { setLeft(st.phase === 'setup' ? rules.config.setupSeconds : rules.config.turnSeconds); }, [st.version, st.phase, rules]);
  useEffect(() => {
    if (!timerOn || finished(st) || !mustAct(st, ME)) return;
    const h = setInterval(() => setLeft((l) => { if (l <= 1) { timeout(rules, st); collect(); force(); return rules.config.turnSeconds; } return l - 1; }), 1000);
    return () => clearInterval(h);
  });
  useEffect(() => { if (!banner) return; const h = setTimeout(() => setBanner(null), 1300); return () => clearTimeout(h); }, [banner]);

  // ------------------------------------------------------------ choices
  const selCardDef = sel?.kind === 'hand' ? D(sel.card) : null;
  const choicePool = (effects: { op: string; target: string }[]): ArenaPet[] | null => {
    for (const e of effects) {
      if (e.op === 'bench_damage' && e.target === 'opp_bench_chosen') return ai.bench;
      if (e.op === 'heal' && e.target === 'own_chosen') return inPlay(me);
      if (e.op === 'switch_self' || (e.op === 'energy_attach' && e.target === 'own_bench_chosen')) return me.bench;
      if (e.op === 'force_switch') return ai.bench;
      if (e.op === 'energy_transfer') return me.bench.filter((b) => b.energy.length > 0);
    }
    return null;
  };
  const targets: number[] = (() => {
    if (!myTurn || !sel) return [];
    if (sel.kind === 'energy') return inPlay(me).map((x) => x.uid);
    if (sel.kind === 'attack' && me.active) return (choicePool(attacksOf(rules.def(me.active.defId))[sel.index].effects) ?? []).map((x) => x.uid);
    if (sel.kind !== 'hand' || !selCardDef) return [];
    if (selCardDef.kind === 'tool') return inPlay(me).filter((x) => x.tool === 0).map((x) => x.uid);
    if (selCardDef.kind === 'pet' && (selCardDef.stage ?? 0) > 0) return inPlay(me).filter((x) => x.defId === selCardDef.evolvesFrom && x.playedTurn !== st.turn && x.evolvedTurn !== st.turn).map((x) => x.uid);
    if (selCardDef.kind === 'supporter' && me.supportersUsed >= rules.config.supportersPerTurn) return [];
    if (selCardDef.kind !== 'pet' && !trainerPlayable(rules, st, ME, selCardDef)) return [];
    if (selCardDef.kind !== 'pet') return (choicePool(effectsOf(selCardDef)) ?? []).filter((x) => !(effectsOf(selCardDef).some((e) => e.op === 'heal') && !effectsOf(selCardDef).some((e) => e.op === 'cure') && x.damage === 0)).map((x) => x.uid);
    return [];
  })();

  const tapPet = (pet: ArenaPet) => {
    if (st.phase !== 'main') return;
    if (targets.includes(pet.uid) && sel) {
      if (sel.kind === 'energy') return run({ type: 'attach', uid: pet.uid });
      if (sel.kind === 'attack') return run({ type: 'attack', index: sel.index, targets: [pet.uid] });
      if (sel.kind === 'hand' && selCardDef) {
        if (selCardDef.kind === 'tool') return run({ type: 'tool', card: sel.card, uid: pet.uid });
        if (selCardDef.kind === 'pet') return run({ type: 'evolve', card: sel.card, uid: pet.uid });
        return run({ type: selCardDef.kind, card: sel.card, targets: [pet.uid] });
      }
    }
    if (pet.owner === ME && myTurn) { setSel(sel?.kind === 'pet' && sel.uid === pet.uid ? null : { kind: 'pet', uid: pet.uid }); return; }
    setSel({ kind: 'pet', uid: pet.uid });
  };
  const useAttack = (i: number) => {
    const atk = attacksOf(rules.def(me.active!.defId))[i];
    const pool = choicePool(atk.effects);
    if (pool && pool.length > 1) { setSel({ kind: 'attack', index: i }); return; }
    run({ type: 'attack', index: i, targets: pool && pool.length === 1 ? [pool[0].uid] : [] });
  };
  const playSelected = () => {
    if (sel?.kind !== 'hand' || !selCardDef) return;
    if (selCardDef.kind === 'pet' && isBasic(selCardDef)) return run({ type: 'bench', card: sel.card });
    if (selCardDef.kind === 'item' || selCardDef.kind === 'supporter') {
      const pool = choicePool(effectsOf(selCardDef));
      if (!pool) return run({ type: selCardDef.kind, card: sel.card, targets: [] });
      if (targets.length === 1) return run({ type: selCardDef.kind, card: sel.card, targets: [targets[0]] });
    }
  };

  // ------------------------------------------------------------ rendering
  const Orb = ({ el, small }: { el: string; small?: boolean }) => <i className={'ar-orb' + (small ? ' sm' : '')} style={{ background: el === 'colorless' ? '#cfd6e4' : ELEMENT_COLORS[el] ?? '#ccc' }} title={el} />;
  const petView = (pet: ArenaPet, big: boolean) => {
    const d = rules.def(pet.defId), hp = maxHp(rules, st, pet), cur = Math.max(0, hp - pet.damage);
    const glow = targets.includes(pet.uid);
    const picked = sel?.kind === 'pet' && sel.uid === pet.uid;
    const shield = activeMods(st, pet, 'shield').length > 0 || activeMods(st, pet, 'prevent').length > 0;
    return (
      <button key={'p' + pet.uid} className={'ar-pet' + (big ? ' big' : '') + (glow ? ' glow' : '') + (picked ? ' picked' : '') + (d.ex ? ' ex' : '')}
        style={{ ['--el' as string]: ELEMENT_COLORS[d.element ?? ''] ?? '#9ab' }} onClick={() => tapPet(pet)} data-testid={`pet-${pet.owner}-${pet.uid}`}>
        <span className="ar-hp"><b>{cur}</b>/{hp}</span>
        {d.ex && <span className="ar-ex">EX</span>}
        <img src={petArt(pet.defId, true)} alt="" draggable={false} />
        <span className="ar-name">{name(d)}</span>
        <span className="ar-bar"><i style={{ width: `${(100 * cur) / hp}%` }} className={cur * 100 < hp * 30 ? 'low' : ''} /></span>
        <span className="ar-energy">{pet.energy.map((e, i) => <Orb key={i} el={e} small />)}</span>
        <span className="ar-flags">
          {pet.poisoned && <em className="ps">☠</em>}{pet.burned && <em className="bn">🔥</em>}{pet.special && <em className="sp">{statusName(pet.special)}</em>}
          {pet.tool !== 0 && <em className="tl">{ICON.tool}</em>}{shield && <em>🛡</em>}
        </span>
        {fx.filter((f) => f.uid === pet.uid).map((f) => <span key={f.id} className={'ar-fx ' + f.cls}>{f.text}</span>)}
      </button>
    );
  };
  const side = (pl: typeof me, mine: boolean) => {
    const slots = [0, 1, 2].map((i) => pl.bench[i]);
    const bench = <div className="ar-bench">{slots.map((b, i) => (b ? petView(b, false) : <span key={'s' + i} className="ar-slot">{P('Bench', 'Bench')}</span>))}</div>;
    const active = <div className="ar-active">{pl.active ? petView(pl.active, true) : <span className="ar-slot big">{P('Active', 'Active')}</span>}</div>;
    return mine ? <>{active}{bench}</> : <>{bench}{active}</>;
  };
  const stars = (n: number) => '★'.repeat(Math.min(n, rules.config.pointsToWin)) + '☆'.repeat(Math.max(0, rules.config.pointsToWin - n));
  const hud = (pl: typeof me, mine: boolean) => (
    <div className={'ar-hud ' + (mine ? 'me' : 'op')}>
      <b>{mine ? pl.name : 'AI'}</b>
      <span className="ar-stars" title={P('Điểm', 'Points')}>{stars(pl.points)}</span>
      <span>🂠 {pl.hand.length}</span><span>📚 {pl.deck.length}</span><span>⚰ {pl.discard.length}</span>
      <span className="ar-zone">
        {mine && myTurn && me.zoneCurrent && !me.energyAttached
          ? <button className={'ar-zbtn' + (sel?.kind === 'energy' ? ' on' : '')} onClick={() => setSel(sel?.kind === 'energy' ? null : { kind: 'energy' })} data-testid="energy"><Orb el={me.zoneCurrent} /></button>
          : pl.zoneCurrent ? <Orb el={pl.zoneCurrent} /> : <i className="ar-orb none" />}
        <small>{P('tiếp', 'next')}</small>{pl.zoneNext && <Orb el={pl.zoneNext} small />}
      </span>
    </div>
  );

  const reason: Record<string, string> = { points: P('Đạt 3 điểm', 'Reached 3 points'), no_pets: P('Đối thủ hết Pet', 'No Pets left'), surrender: P('Đầu hàng', 'Surrender'),
    timeout: P('Hết giờ nhiều lần', 'Repeated timeouts'), turn_limit: P('Hết số lượt — hòa', 'Turn limit — draw'), simultaneous: P('Cùng thắng một lúc — hòa', 'Both won at once — draw') };
  const selPet = sel?.kind === 'pet' ? inPlay(me).concat(inPlay(ai)).find((x) => x.uid === sel.uid) ?? null : null;

  return (
    <div className="ar">
      {hud(ai, false)}
      <div className="ar-field op">{side(ai, false)}</div>
      <div className="ar-mid">
        <span>{P('Lượt', 'Turn')} {st.turn} · {st.active === ME ? P('Bạn', 'You') : 'AI'}</span>
        {mustAct(st, ME) && !finished(st) && timerOn && <span className={'ar-timer' + (left <= 10 ? ' warn' : '')}>⏱ {left}</span>}
        {banner && <span className="ar-banner">{banner}</span>}
      </div>
      <div className="ar-field me">{side(me, true)}</div>
      {hud(me, true)}

      {/* action panel */}
      <div className="ar-panel">
        {errMsg && <div className="ar-err">{errMsg}</div>}
        {sel?.kind === 'energy' && <small>{P('Chạm 1 Pet của bạn để gắn năng lượng.', 'Tap one of your Pets to attach the Energy.')}</small>}
        {sel?.kind === 'attack' && <small>{P('Chọn mục tiêu đang sáng.', 'Pick a glowing target.')}</small>}
        {selCardDef && (
          <div className="ar-detail">
            <b>{selCardDef.kind === 'pet' ? name(selCardDef) : `${ICON[selCardDef.kind]} ${name(selCardDef)}`}</b>
            {selCardDef.kind === 'pet'
              ? <small>{(selCardDef.stage ?? 0) === 0 ? 'Basic' : `Stage ${selCardDef.stage} · ${P('tiến hóa từ', 'evolves from')} ${name(rules.def(selCardDef.evolvesFrom!))}`} · HP {selCardDef.hp}</small>
              : <small>{selCardDef.kind === 'supporter' ? P('Supporter · 1 lá mỗi lượt', 'Supporter · 1 per turn') : selCardDef.kind === 'tool' ? P('Công cụ · gắn vào Pet', 'Tool · attach to a Pet') : P('Vật phẩm', 'Item')} — {selCardDef.text?.[lang]}</small>}
            {myTurn && selCardDef.kind === 'pet' && isBasic(selCardDef) && <button className="btn primary small" onClick={playSelected} data-testid="play">{P('Đặt lên Bench', 'Put on Bench')}</button>}
            {myTurn && selCardDef.kind === 'pet' && !isBasic(selCardDef) && <small>{targets.length ? P('Chạm Pet đang sáng để tiến hóa.', 'Tap a glowing Pet to evolve it.') : P('Chưa có Pet phù hợp để tiến hóa lúc này.', 'No Pet can evolve into this right now.')}</small>}
            {myTurn && (selCardDef.kind === 'item' || selCardDef.kind === 'supporter') && (choicePool(effectsOf(selCardDef)) && targets.length > 1
              ? <small>{P('Chạm mục tiêu đang sáng.', 'Tap a glowing target.')}</small>
              : <button className="btn primary small" disabled={!trainerPlayable(rules, st, ME, selCardDef) || (selCardDef.kind === 'supporter' && me.supportersUsed >= rules.config.supportersPerTurn)} onClick={playSelected} data-testid="play">{P('Dùng', 'Play')}</button>)}
            {myTurn && selCardDef.kind === 'tool' && <small>{P('Chạm Pet đang sáng để gắn.', 'Tap a glowing Pet to attach it.')}</small>}
          </div>
        )}
        {selPet && (
          <div className="ar-detail">
            <b>{name(rules.def(selPet.defId))} {rules.def(selPet.defId).ex ? 'EX' : ''}</b>
            <small>HP {Math.max(0, maxHp(rules, st, selPet) - selPet.damage)}/{maxHp(rules, st, selPet)} · {P('Điểm yếu', 'Weakness')} {rules.def(selPet.defId).weakness ?? '—'} (+{rules.config.weaknessBonus}) · {P('Rút lui', 'Retreat')} {rules.def(selPet.defId).retreat}</small>
            {rules.def(selPet.defId).ability && <small>✨ <b>{rules.def(selPet.defId).ability!.name[lang]}</b>: {rules.def(selPet.defId).ability!.text[lang]}</small>}
            {attacksOf(rules.def(selPet.defId)).map((a, i) => {
              const mineActive = selPet.owner === ME && me.active === selPet;
              const prob = mineActive ? attackProblem(rules, st, ME, i) : 'x';
              return (
                <button key={a.id} className={'ar-atk' + (mineActive && myTurn && !prob ? ' ready' : '')} disabled={!mineActive || !myTurn || !!prob} onClick={() => useAttack(i)} data-testid={`atk-${i}`}>
                  <span className="ar-cost">{a.cost.map((c, j) => <Orb key={j} el={c} small />)}</span>
                  <b>{a.name[lang]}</b><span className="ar-dmg">{a.damage || ''}</span>
                  {a.text[lang] && <small>{a.text[lang]}</small>}
                  {mineActive && myTurn && prob && prob !== 'not_enough_energy' && <small className="ar-why">{t('error.' + prob) !== 'error.' + prob ? t('error.' + prob) : prob.replace(/_/g, ' ')}</small>}
                </button>
              );
            })}
            {selPet.owner === ME && myTurn && (
              <div className="ar-row">
                {me.active === selPet && me.bench.length > 0 && <span className="ar-retreat">{P('Rút lui', 'Retreat')} ({retreatCost(rules, st, ME)}⚪):
                  {me.bench.map((b) => <button key={b.uid} className="btn small" disabled={me.retreats > 0 || selPet.energy.length < retreatCost(rules, st, ME) || selPet.special === 'asleep' || selPet.special === 'paralyzed'} onClick={() => run({ type: 'retreat', uid: b.uid })}>→ {name(rules.def(b.defId))}</button>)}</span>}
                {rules.def(selPet.defId).ability?.trigger === 'activated' && <button className="btn small" disabled={selPet.abilityTurn === st.turn} onClick={() => run({ type: 'ability', uid: selPet.uid })}>✨ {rules.def(selPet.defId).ability!.name[lang]}</button>}
              </div>
            )}
          </div>
        )}
        <div className="ar-row">
          {myTurn && <button className="btn primary small" onClick={() => run({ type: 'end' })} data-testid="end">{P('Kết thúc lượt', 'End turn')}</button>}
          {sel && <button className="btn ghost small" onClick={() => setSel(null)}>{P('Hủy', 'Cancel')}</button>}
          <button className="btn ghost small" onClick={() => setShowLog(!showLog)}>{P('Nhật ký', 'Log')}</button>
          <label className="small"><input type="checkbox" checked={timerOn} onChange={(e) => setTimerOn(e.target.checked)} /> ⏱</label>
          <button className="btn ghost small" onClick={() => { if (!finished(st)) { apply(rules, st, ME, { type: 'surrender' }); collect(); force(); } }}>{P('Đầu hàng', 'Surrender')}</button>
          <button className="btn ghost small" onClick={exit}>‹ {P('Thoát', 'Exit')}</button>
        </div>
        {showLog && <div className="ar-log">{[...st.log].reverse().slice(0, 40).map((e, i) => <div key={i}>{e.type} {e.p >= 0 ? (e.p === ME ? P('· bạn', '· you') : '· AI') : ''} {e.n || ''} {e.s && !e.s.startsWith('pet_') ? e.s : e.s ? name(rules.def(e.s)) : ''}</div>)}</div>}
        {ref.current.demo && <small className="ar-note">{P('Bộ sưu tập thử chưa có Pet Basic: đang dùng bộ bài demo.', 'Your simulated collection has no Basic Pet yet: using a demo deck.')}</small>}
        <small className="ar-note">{P('Engine TypeScript được đối chiếu từng sự kiện với engine server (C#).', 'TypeScript engine verified event-by-event against the server (C#) engine.')}</small>
      </div>

      {/* hand */}
      <div className="ar-hand">
        {me.hand.map((c, i) => {
          const d = D(c);
          return (
            <button key={c} className={'ar-card ' + d.kind + (sel?.kind === 'hand' && sel.card === c ? ' up' : '')} onClick={() => setSel(sel?.kind === 'hand' && sel.card === c ? null : { kind: 'hand', card: c })} data-testid={`hand-${i}`}
              style={d.kind === 'pet' ? { ['--el' as string]: ELEMENT_COLORS[d.element ?? ''] } : undefined}>
              {d.kind === 'pet' ? <img src={petArt(d.id, true)} alt="" draggable={false} /> : <span className="ar-icon">{ICON[d.kind]}</span>}
              <b>{name(d)}</b>
              <small>{d.kind === 'pet' ? ((d.stage ?? 0) === 0 ? 'Basic' : `Stage ${d.stage}`) + ` · ${d.hp}` : d.kind}</small>
            </button>
          );
        })}
      </div>

      {st.phase === 'setup' && !me.setupDone && (
        <div className="ar-modal">
          <div className="ar-sheet">
            <b>{st.first === ME ? P('Bạn đi trước', 'You go first') : P('Đối thủ đi trước', 'Opponent goes first')} 🪙</b>
            <small>{P('Chọn 1 Pet Basic làm Active, sau đó tối đa 3 Pet Basic cho Bench.', 'Choose 1 Basic Pet as Active, then up to 3 Basic Pets for the Bench.')}</small>
            <div className="ar-pick">
              {me.hand.filter((c) => isBasic(D(c))).map((c) => {
                const role = setup.active === c ? 'A' : setup.bench.includes(c) ? 'B' : '';
                return (
                  <button key={c} className={'ar-card pet' + (role ? ' up' : '')} style={{ ['--el' as string]: ELEMENT_COLORS[D(c).element ?? ''] }} data-testid={`setup-${c}`}
                    onClick={() => setSetup((x) => x.active === null ? { ...x, active: c } : x.active === c ? { active: null, bench: x.bench } : x.bench.includes(c) ? { ...x, bench: x.bench.filter((y) => y !== c) } : x.bench.length < 3 ? { ...x, bench: [...x.bench, c] } : x)}>
                    <img src={petArt(D(c).id, true)} alt="" /><b>{name(D(c))}</b><small>{role === 'A' ? 'Active' : role === 'B' ? 'Bench' : `HP ${D(c).hp}`}</small>
                  </button>
                );
              })}
            </div>
            <button className="btn primary" disabled={setup.active === null} onClick={() => { run({ type: 'setup', card: setup.active!, bench: setup.bench }); setSetup({ active: null, bench: [] }); }} data-testid="setup-go">{P('Bắt đầu', 'Start')}</button>
          </div>
        </div>
      )}
      {me.mustPromote && !finished(st) && (
        <div className="ar-modal">
          <div className="ar-sheet">
            <b>{P('Chọn Pet Active mới', 'Choose a new Active Pet')}</b>
            <div className="ar-pick">{me.bench.map((b) => <button key={b.uid} className="btn" onClick={() => run({ type: 'promote', uid: b.uid })} data-testid={`promote-${b.uid}`}>{name(rules.def(b.defId))} · HP {maxHp(rules, st, b) - b.damage}</button>)}</div>
          </div>
        </div>
      )}
      {finished(st) && (
        <div className="ar-modal">
          <div className={'ar-sheet ' + (st.winner === ME ? 'win' : st.winner === AI ? 'lose' : '')}>
            <div className="ar-result">{st.winner === ME ? P('CHIẾN THẮNG!', 'VICTORY!') : st.winner === AI ? P('THẤT BẠI', 'DEFEAT') : P('HÒA', 'DRAW')}</div>
            <small>{reason[st.endReason ?? ''] ?? st.endReason} · ★ {me.points} – {ai.points} · {P('Lượt', 'Turn')} {st.turn}</small>
            <div className="ar-row">
              <button className="btn primary" onClick={() => { ref.current = mk(); seen.current = 0; setSel(null); force(); }} data-testid="rematch">{P('Đấu lại', 'Rematch')}</button>
              <button className="btn" onClick={exit}>{P('Về Lobby', 'Lobby')}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

