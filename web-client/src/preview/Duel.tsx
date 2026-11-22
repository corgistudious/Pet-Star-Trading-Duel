// Playable Pet Card Tactical Duel for the Interactive Web Preview (landscape 16:9).
// Rules run in the TypeScript port of the server engine (src/duel/engine.ts), proven identical to the C# engine by
// cross-engine fixtures. The opponent is the same rule-abiding AI. Nothing is sent to a server or saved.
import { useEffect, useReducer, useRef, useState } from 'react';
import { decide } from '../duel/ai';
import { apply, canAct, cardOf, create, legalTargets, timeout, trapReady, tributesFor, unitOf } from '../duel/engine';
import { DuelRuleError, finished, toAct, type DuelAction, type DuelCard, type DuelEvent, type DuelRules, type DuelState, type DuelUnit } from '../duel/model';
import { useI18n } from '../i18n';
import { petArt, useCatalog } from '../state/catalog';
import { aiDeck, playerDeck } from './duelSetup';
import type { SimState } from './sim';

type PFn = (vi: string, en: string) => string;
type Sel =
  | { kind: 'hand'; card: number; tributes: number[] }
  | { kind: 'unit'; uid: number; mode: 'menu' | 'move' | 'attack' | 'skill' | 'ultimate' }
  | null;
interface Fx { id: number; uid?: number; text: string; cls: string }
const ME = 0, AI = 1;

export function Duel({ s, P, exit }: { s: SimState; P: PFn; exit: () => void }) {
  const cat = useCatalog() as ReturnType<typeof useCatalog> & { duelRules: DuelRules };
  const rules = cat.duelRules;
  const { t, lang } = useI18n();
  const level = Math.max(1, Math.round(s.cards.reduce((a, c) => a + c.level, 0) / Math.max(1, s.cards.length)));
  const newDuel = () => create(rules, { name: s.player.nickname, deck: playerDeck(cat, rules, s) }, { name: 'AI', deck: aiDeck(cat, rules, level, Date.now() % 997) },
    BigInt(crypto.getRandomValues(new Uint32Array(1))[0]));
  const stRef = useRef<DuelState>(newDuel());
  const st = stRef.current;
  const [, force] = useReducer((x: number) => x + 1, 0);
  const [sel, setSel] = useState<Sel>(null);
  const [fx, setFx] = useState<Fx[]>([]);
  const [banner, setBanner] = useState<string | null>(null);
  const [coin, setCoin] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [showLog, setShowLog] = useState(false);
  const [timerOn, setTimerOn] = useState(true);
  const [left, setLeft] = useState(rules.config.turnSeconds);
  const [graveOpen, setGraveOpen] = useState<null | 'pick'>(null);
  const [portrait, setPortrait] = useState(false);
  const seen = useRef(0);
  const fxId = useRef(0);

  useEffect(() => {
    const f = () => setPortrait(innerHeight > innerWidth && Math.min(screen.width, screen.height) < 600);
    f(); addEventListener('resize', f); return () => removeEventListener('resize', f);
  }, []);

  const name = (c: DuelCard | null | undefined) => {
    if (!c) return '?';
    if (c.kind === 'tactic') return rules.tactics[c.tacticId!].name[lang] ?? c.tacticId!;
    const id = c.forms[0]?.formId ?? '';
    return t('pet.' + id) !== 'pet.' + id ? t('pet.' + id) : cat.forms[id]?.name ?? id;
  };
  const unitName = (u: DuelUnit) => (t('pet.' + u.formId) !== 'pet.' + u.formId ? t('pet.' + u.formId) : cat.forms[u.formId]?.name ?? u.formId);

  // turn new log events into floating numbers / banners
  const collect = () => {
    const evs = st.log.slice(seen.current);
    seen.current = st.log.length;
    const add: Fx[] = [];
    for (const e of evs) {
      const id = ++fxId.current;
      if (e.type === 'damage') add.push({ id, uid: e.a, text: `−${e.n - e.b}${e.b ? ` (🛡${e.b})` : ''}`, cls: 'dmg' });
      else if (e.type === 'heal') add.push({ id, uid: e.a, text: `+${e.n}`, cls: 'heal' });
      else if (e.type === 'destroyed') add.push({ id, uid: e.a, text: P('Bị tiêu diệt! −500 LP', 'Destroyed! −500 LP'), cls: 'death' });
      else if (e.type === 'status') add.push({ id, uid: e.a, text: statusName(e.s), cls: 'status' });
      else if (e.type === 'evolve') add.push({ id, uid: e.a, text: P('Tiến hóa!', 'Evolved!'), cls: 'evolve' });
      else if (e.type === 'shield') add.push({ id, uid: e.a, text: `🛡 ${e.n}`, cls: 'heal' });
      else if (e.type === 'spell' || e.type === 'trap') setBanner(`${e.p === ME ? P('Bạn', 'You') : 'AI'} · ${e.type === 'spell' ? 'Spell' : 'Trap'}: ${rules.tactics[e.s].name[lang]}`);
      else if (e.type === 'negate') setBanner(P('Vô hiệu hóa!', 'Negated!'));
      else if (e.type === 'turn') setBanner(e.p === ME ? P('Lượt của bạn', 'Your turn') : P('Lượt của đối thủ', "Opponent's turn"));
    }
    if (add.length) {
      setFx((f) => [...f, ...add]);
      setTimeout(() => setFx((f) => f.filter((x) => !add.includes(x))), 1300);
    }
  };
  const statusName = (s: string) => ({ stun: P('Choáng', 'Stun'), weaken: P('Suy yếu', 'Weaken'), burn: P('Bỏng', 'Burn'), poison: P('Độc', 'Poison'), taunt: P('Khiêu khích', 'Taunt'), guard: P('Hộ vệ', 'Guard'), endure: P('Trụ vững', 'Endure') } as Record<string, string>)[s] ?? s;

  const run = (a: DuelAction) => {
    try { apply(rules, st, ME, a); setErr(null); }
    catch (e) { setErr(e instanceof DuelRuleError ? errorText(e.code) : String(e)); }
    setSel(null); collect(); force();
  };
  const errorText = (code: string) => {
    const k = 'duel.err.' + code; const v = t(k);
    return v !== k ? v : code.replace(/_/g, ' ');
  };

  // AI acts one step at a time so every move is visible
  useEffect(() => {
    if (coin || finished(st) || toAct(st) !== AI) return;
    const h = setTimeout(() => { apply(rules, st, AI, decide(rules, st, AI)); collect(); force(); }, st.priority === AI ? 500 : 750);
    return () => clearTimeout(h);
  });
  // turn / reaction timer (same values as the server; can be paused in the preview)
  useEffect(() => { setLeft(st.priority >= 0 ? rules.config.reactionSeconds : rules.config.turnSeconds); }, [st.version, st.priority, rules]);
  useEffect(() => {
    if (!timerOn || coin || finished(st) || toAct(st) !== ME) return;
    const h = setInterval(() => setLeft((l) => {
      if (l <= 1) { timeout(rules, st); collect(); force(); return rules.config.turnSeconds; }
      return l - 1;
    }), 1000);
    return () => clearInterval(h);
  });
  useEffect(() => { if (!banner) return; const h = setTimeout(() => setBanner(null), 1500); return () => clearTimeout(h); }, [banner]);

  const me = st.players[ME], ai = st.players[AI];
  const myControl = !finished(st) && st.priority < 0 && st.active === ME;
  const myReaction = !finished(st) && st.priority === ME;
  const firstTurnNoBattle = st.first === ME && me.turnsTaken === 1;

  // ---------------------------------------------------------------- interaction
  const selectedCard = sel?.kind === 'hand' ? cardOf(st, sel.card) : null;
  const selectedTactic = selectedCard?.kind === 'tactic' ? rules.tactics[selectedCard.tacticId!] : null;
  const selectedUnit = sel?.kind === 'unit' ? unitOf(st, sel.uid) : null;

  const tapHand = (c: number) => {
    if (!myControl && !myReaction) return;
    setSel(sel?.kind === 'hand' && sel.card === c ? null : { kind: 'hand', card: c, tributes: [] });
  };
  const tapPetZone = (side: number, zone: number) => {
    const u = st.players[side].units.find((x) => x.zone === zone);
    if (side === ME) {
      if (sel?.kind === 'hand' && selectedCard?.kind === 'pet') {
        const need = tributesFor(rules, selectedCard.forms[0]);
        if (u && sel.tributes.length < need) { setSel({ ...sel, tributes: sel.tributes.includes(u.uid) ? sel.tributes.filter((x) => x !== u.uid) : [...sel.tributes, u.uid] }); return; }
        if (sel.tributes.length === need) { run({ type: 'summon', card: sel.card, zone, tributes: sel.tributes }); return; }
      }
      if (sel?.kind === 'hand' && selectedTactic && u) {
        if (selectedTactic.targeting === 'own_pet') { run({ type: 'spell', card: sel.card, targets: [u.uid] }); return; }
        if (selectedTactic.targeting === 'own_two') { const picks = sel.tributes; if (picks.length === 1 && picks[0] !== u.uid) run({ type: 'spell', card: sel.card, targets: [picks[0], u.uid] }); else setSel({ ...sel, tributes: [u.uid] }); return; }
      }
      if (sel?.kind === 'unit' && sel.mode === 'move' && selectedUnit) { run({ type: 'move', unit: selectedUnit.uid, zone }); return; }
      if (u && myControl) setSel({ kind: 'unit', uid: u.uid, mode: 'menu' });
      return;
    }
    if (!u) return;
    if (sel?.kind === 'unit' && selectedUnit && (sel.mode === 'attack' || sel.mode === 'skill' || sel.mode === 'ultimate')) { run({ type: sel.mode, unit: selectedUnit.uid, target: u.uid }); return; }
    if (sel?.kind === 'hand' && selectedTactic?.targeting === 'enemy_pet') { run({ type: 'spell', card: sel.card, targets: [u.uid] }); return; }
  };
  const tapStZone = (side: number, z: number) => {
    if (side === ME) {
      if (myReaction && trapReady(rules, st, ME, me.zones[z])) { run({ type: 'activate', zone: z }); return; }
      if (sel?.kind === 'hand' && selectedTactic?.type === 'trap' && myControl) { run({ type: 'set', card: sel.card, zone: z }); return; }
      return;
    }
    if (sel?.kind === 'hand' && selectedTactic?.targeting === 'enemy_st') run({ type: 'spell', card: sel.card, targets: [z] });
  };
  const activateSelected = () => {
    if (sel?.kind !== 'hand' || !selectedTactic) return;
    if (selectedTactic.targeting === 'none') run({ type: 'spell', card: sel.card, targets: [] });
    else if (selectedTactic.targeting === 'grave_pet') setGraveOpen('pick');
  };

  // highlight helpers
  const legal = sel?.kind === 'unit' && (sel.mode === 'attack' || sel.mode === 'skill' || sel.mode === 'ultimate') ? legalTargets(st, ME).map((u) => u.uid) : [];
  const targetEnemy = (u: DuelUnit) => legal.includes(u.uid) || (selectedTactic?.targeting === 'enemy_pet');
  const targetOwn = (_u: DuelUnit) => (selectedTactic?.targeting === 'own_pet' || selectedTactic?.targeting === 'own_two') || (selectedCard?.kind === 'pet' && tributesFor(rules, selectedCard.forms[0]) > 0);

  // ---------------------------------------------------------------- render
  const Unit = ({ u }: { u: DuelUnit }) => {
    const pct = Math.max(0, Math.round((100 * u.hp) / u.maxHp));
    const mine = u.owner === ME;
    const glow = mine ? targetOwn(u) || (sel?.kind === 'unit' && sel.uid === u.uid) : targetEnemy(u);
    const tribute = sel?.kind === 'hand' && sel.tributes.includes(u.uid);
    return (
      <div className={'du-unit' + (glow ? ' glow' : '') + (tribute ? ' tribute' : '') + (mine && st.active === ME && st.phase === 'battle' && !canAct(st, u) ? ' spent' : '') + (u.divine ? ' divine' : '')}>
        <img src={petArt(u.formId, true)} alt="" draggable={false} />
        <span className="du-name">{unitName(u)}</span>
        <span className="du-stats">⚔{u.atk} 🛡{u.def}</span>
        <span className="du-hpbar"><i style={{ width: pct + '%' }} className={pct < 30 ? 'low' : ''} /></span>
        <span className="du-hp">{u.hp}/{u.maxHp}{u.shield ? ` +${u.shield}` : ''}</span>
        {u.statuses.length > 0 && <span className="du-st">{u.statuses.map((x) => statusName(x.type)).join(' · ')}</span>}
        {u.summonedTurn === st.turn && <span className="du-new">{P('Mới', 'New')}</span>}
        {fx.filter((f) => f.uid === u.uid).map((f) => <span key={f.id} className={'du-fx ' + f.cls}>{f.text}</span>)}
      </div>
    );
  };
  const Side = ({ side }: { side: number }) => {
    const pl = st.players[side];
    const row = (zones: number[]) => (
      <div className="du-row">
        {zones.map((z) => {
          const u = pl.units.find((x) => x.zone === z);
          return <button key={z} className={'du-zone' + (u ? '' : ' empty')} onClick={() => tapPetZone(side, z)} data-testid={`zone-${side}-${z}`}>{u ? <Unit u={u} /> : <span>{z < 3 ? P('Trước', 'Front') : P('Sau', 'Back')}</span>}</button>;
        })}
      </div>
    );
    const st3 = (
      <div className="du-strow">
        {pl.zones.map((zc, z) => (
          <button key={z} className={'du-st' + (zc ? ' set' : '') + (side === ME && myReaction && trapReady(rules, st, ME, zc) ? ' ready' : '')} onClick={() => tapStZone(side, z)} data-testid={`st-${side}-${z}`}>
            {zc ? (side === ME ? rules.tactics[zc.tacticId].name[lang] : P('Úp', 'Set')) : 'S/T'}
          </button>
        ))}
      </div>
    );
    return side === AI
      ? <div className="du-side top">{st3}{row([3, 4])}{row([0, 1, 2])}</div>
      : <div className="du-side bottom">{row([0, 1, 2])}{row([3, 4])}{st3}</div>;
  };

  if (portrait) return (
    <div className="pv-rotate"><div className="pv-phone">📱</div><p>{P('Xoay ngang điện thoại để vào trận đấu', 'Rotate your phone to landscape to play the duel')}</p>
      <button className="btn ghost small" onClick={exit}>‹ {P('Về Lobby', 'Lobby')}</button></div>
  );

  const reason: Record<string, string> = { lp_zero: P('LP về 0', 'LP reached 0'), deck_out: P('Hết bài để rút', 'Deck out'), surrender: P('Đầu hàng', 'Surrender'), timeout: P('Hết giờ nhiều lần', 'Repeated timeouts'),
    both_lp_zero: P('Cả hai cùng 0 LP', 'Both reached 0 LP'), turn_limit_lp: P('Hết lượt — so LP', 'Turn limit — LP'), turn_limit_hp: P('Hết lượt — so HP Pet', 'Turn limit — Pet HP'), turn_limit_draw: P('Hết lượt — hòa', 'Turn limit — draw') };
  return (
    <div className="du">
      <div className="du-board">
        <div className="du-hud top">
          <span className="du-player red">🔴 AI</span>
          <span className="du-lp">LP <b>{ai.lp}</b></span><span>⚡{ai.energy}/{ai.maxEnergy}</span>
          <span>🂠 {ai.hand.length}</span><span>📚 {ai.deck.length}</span><span>⚰ {ai.grave.length}</span>
        </div>
        <Side side={AI} />
        <div className="du-mid">
          <span className="du-turn">{P('Lượt', 'Turn')} {st.turn} · {st.active === ME ? P('Bạn', 'You') : 'AI'}</span>
          <span className="du-phases">{['Rút', 'Chính', 'Chiến đấu', 'Kết thúc'].map((x, i) => <i key={x} className={(i === 1 && st.phase === 'main') || (i === 2 && st.phase === 'battle') ? 'on' : ''}>{lang === 'vi' ? x : ['Draw', 'Main', 'Battle', 'End'][i]}</i>)}</span>
          {st.chain.length > 0 && <span className="du-chain">{P('Chuỗi', 'Chain')} ×{st.chain.length}</span>}
          {toAct(st) === ME && !finished(st) && <span className={'du-timer' + (left <= 10 ? ' warn' : '')}>⏱ {timerOn ? left : '∞'}</span>}
        </div>
        <Side side={ME} />
        <div className="du-hud bottom">
          <span className="du-player blue">🔵 {me.name}</span>
          <span className="du-lp">LP <b>{me.lp}</b></span><span>⚡{me.energy}/{me.maxEnergy}</span><span>📚 {me.deck.length}</span>
          <button className="du-link" onClick={() => setGraveOpen(graveOpen ? null : 'pick')}>⚰ {me.grave.length}</button>
        </div>
        <div className="du-hand">
          {me.hand.map((c, i) => {
            const cd = cardOf(st, c);
            const n = me.hand.length, angle = (i - (n - 1) / 2) * Math.min(7, 40 / Math.max(1, n));
            const d = cd.kind === 'tactic' ? rules.tactics[cd.tacticId!] : null;
            const usable = myReaction ? d?.type === 'spell' && d.speed === 'quick' && me.energy >= d.cost : myControl;
            return (
              <button key={c} className={'du-card ' + (cd.kind === 'pet' ? 'pet' : d!.type) + (sel?.kind === 'hand' && sel.card === c ? ' up' : '') + (usable ? '' : ' dim')}
                style={{ transform: `rotate(${angle}deg) translateY(${Math.abs(angle) * 0.4}px)` }} onClick={() => tapHand(c)} data-testid={`hand-${i}`}>
                {cd.kind === 'pet' ? <img src={petArt(cd.forms[0].formId, true)} alt="" draggable={false} /> : <span className="du-cat">{d!.type === 'spell' ? (d!.speed === 'quick' ? '⚡' : '✦') : '⛓'}</span>}
                <b>{name(cd)}</b>
                <small>{cd.kind === 'pet' ? `⚔${cd.forms[0].atk} ❤${cd.forms[0].hp}${tributesFor(rules, cd.forms[0]) ? ` · ${P('hiến', 'trib.')} ${tributesFor(rules, cd.forms[0])}` : ''}` : `⚡${d!.cost}`}</small>
              </button>
            );
          })}
        </div>
      </div>

      <aside className="du-panel">
        {banner && <div className="du-banner">{banner}</div>}
        {myReaction && (
          <div className="du-react">
            <b>{P('Cửa sổ phản ứng', 'Reaction window')}</b>
            <small>{st.chain.length ? describeLink(st, rules, lang, P) : st.event === 'summon' ? P('Đối thủ vừa triệu hồi', 'Opponent summoned') : P('Pet của bạn bị tiêu diệt', 'Your Pet was destroyed')}</small>
            <small>{P('Chạm Trap đang sáng hoặc Spell Nhanh trên tay.', 'Tap a glowing Trap or a Quick Spell in your hand.')}</small>
            <button className="btn primary small" onClick={() => run({ type: 'pass' })} data-testid="duel-pass">{P('Bỏ qua', 'Pass')}</button>
          </div>
        )}
        {selectedCard && (
          <div className="du-detail">
            <b>{name(selectedCard)}</b>
            {selectedTactic ? <>
              <small>{selectedTactic.type === 'spell' ? (selectedTactic.speed === 'quick' ? 'Quick Spell' : 'Spell') : 'Trap'} · ⚡{selectedTactic.cost}</small>
              <p>{selectedTactic.text[lang]}</p>
              {selectedTactic.type === 'trap' ? <small>{P('Chạm 1 ô S/T trống để úp.', 'Tap an empty S/T zone to set it.')}</small>
                : ['none', 'grave_pet'].includes(selectedTactic.targeting) ? <button className="btn primary small" onClick={activateSelected} data-testid="duel-activate">{P('Kích hoạt', 'Activate')}</button>
                : selectedTactic.targeting === 'hand_pet' ? <div className="du-pick">{me.hand.filter((h) => h !== (sel as { card: number }).card && cardOf(st, h).kind === 'pet' && cardOf(st, h).forms[0].stage <= 1 && !cardOf(st, h).forms[0].divine).map((h) =>
                    <button key={h} className="btn small" onClick={() => run({ type: 'spell', card: (sel as { card: number }).card, targets: [h] })}>{name(cardOf(st, h))}</button>)}</div>
                : <small>{P('Chọn mục tiêu trên sân.', 'Pick a target on the field.')}</small>}
            </> : <>
              <small>{P('Hình thái', 'Stage')} {selectedCard.forms[0].stage}{selectedCard.forms[0].divine ? ' · ' + P('Thần Pet', 'Divine') : ''} · ⚔{selectedCard.forms[0].atk} 🛡{selectedCard.forms[0].def} ❤{selectedCard.forms[0].hp}</small>
              <small>{tributesFor(rules, selectedCard.forms[0]) ? P(`Chạm ${tributesFor(rules, selectedCard.forms[0])} Pet để hiến tế, rồi chạm ô.`, `Tap ${tributesFor(rules, selectedCard.forms[0])} Pet(s) to tribute, then a zone.`) : P('Chạm ô Pet trống để triệu hồi.', 'Tap an empty Pet zone to summon.')}</small>
            </>}
          </div>
        )}
        {selectedUnit && sel?.kind === 'unit' && (
          <div className="du-detail">
            <b>{unitName(selectedUnit)}</b>
            <small>❤{selectedUnit.hp}/{selectedUnit.maxHp} ⚔{selectedUnit.atk} 🛡{selectedUnit.def} · {P('Hình thái', 'Stage')} {selectedUnit.stage}</small>
            {st.phase === 'main' ? <div className="du-pick">
              {(() => {
                const cd = cardOf(st, selectedUnit.cardId); const can = selectedUnit.formIndex + 1 < cd.forms.length;
                const cost = rules.config.evolveCost[Math.min(rules.config.evolveCost.length - 1, Math.max(1, selectedUnit.stage))];
                return can ? <button className="btn small" onClick={() => run({ type: 'evolve', unit: selectedUnit.uid })}>{P('Tiến hóa', 'Evolve')} ⚡{cost}</button> : null;
              })()}
              <button className="btn small" onClick={() => setSel({ kind: 'unit', uid: selectedUnit.uid, mode: 'move' })}>{P('Đổi vị trí', 'Move')}</button>
            </div> : canAct(st, selectedUnit) ? <div className="du-pick">
              <button className={'btn small' + (sel.mode === 'attack' ? ' primary' : '')} onClick={() => setSel({ kind: 'unit', uid: selectedUnit.uid, mode: 'attack' })}>{P('Tấn công', 'Attack')}</button>
              {selectedUnit.skill && rules.skills[selectedUnit.skill] && <button className={'btn small' + (sel.mode === 'skill' ? ' primary' : '')} disabled={me.energy < rules.skills[selectedUnit.skill].cost}
                onClick={() => rules.skills[selectedUnit.skill!].target === 'enemy_single' ? setSel({ kind: 'unit', uid: selectedUnit.uid, mode: 'skill' }) : run({ type: 'skill', unit: selectedUnit.uid, target: legalTargets(st, ME)[0]?.uid })}>
                {cat.skills[selectedUnit.skill]?.name[lang] ?? 'Skill'} ⚡{rules.skills[selectedUnit.skill].cost}</button>}
              {selectedUnit.ultimate && rules.skills[selectedUnit.ultimate] && <button className={'btn small' + (sel.mode === 'ultimate' ? ' primary' : '')} disabled={me.energy < rules.skills[selectedUnit.ultimate].cost}
                onClick={() => rules.skills[selectedUnit.ultimate!].target === 'enemy_single' ? setSel({ kind: 'unit', uid: selectedUnit.uid, mode: 'ultimate' }) : run({ type: 'ultimate', unit: selectedUnit.uid, target: legalTargets(st, ME)[0]?.uid })}>
                ★ {cat.skills[selectedUnit.ultimate]?.name[lang] ?? 'Ultimate'} ⚡{rules.skills[selectedUnit.ultimate].cost}</button>}
            </div> : <small>{P('Pet này không thể hành động lúc này.', 'This Pet cannot act now.')}</small>}
            {sel.mode !== 'menu' && <small>{sel.mode === 'move' ? P('Chạm ô đích.', 'Tap the destination zone.') : P('Chạm mục tiêu đang sáng.', 'Tap a glowing target.')}</small>}
          </div>
        )}
        {err && <div className="du-err">{err}</div>}
        <div className="du-actions">
          {myControl && st.phase === 'main' && <button className="btn primary small" disabled={firstTurnNoBattle} onClick={() => run({ type: 'battle' })} data-testid="duel-battle">{firstTurnNoBattle ? P('Lượt đầu: không tấn công', 'First turn: no attack') : P('Vào chiến đấu', 'Battle phase')}</button>}
          {myControl && <button className="btn small" onClick={() => run({ type: 'end' })} data-testid="duel-end">{P('Kết thúc lượt', 'End turn')}</button>}
          {sel && <button className="btn ghost small" onClick={() => setSel(null)}>{P('Hủy chọn', 'Cancel')}</button>}
        </div>
        <div className={'du-log' + (showLog ? ' open' : '')} onClick={() => setShowLog(!showLog)}>
          {[...st.log].reverse().filter((e) => !['draw_extra', 'coin'].includes(e.type)).slice(0, showLog ? 60 : 4).map((e, i) => <div key={i}>{line(e, st, rules, lang, P)}</div>)}
        </div>
        <div className="du-foot">
          <label className="small"><input type="checkbox" checked={timerOn} onChange={(e) => setTimerOn(e.target.checked)} /> {P('Đồng hồ', 'Timer')}</label>
          <button className="btn ghost small" onClick={() => { if (!finished(st)) { apply(rules, st, ME, { type: 'surrender' }); collect(); force(); } }}>{P('Đầu hàng', 'Surrender')}</button>
          <button className="btn ghost small" onClick={exit}>‹ {P('Thoát', 'Exit')}</button>
        </div>
        <small className="du-note">{P('Engine TypeScript đối chiếu từng sự kiện với engine server (C#).', 'TypeScript engine verified event-by-event against the server (C#) engine.')}</small>
      </aside>

      {graveOpen && (
        <div className="du-modal" onClick={() => setGraveOpen(null)}>
          <div className="du-sheet" onClick={(e) => e.stopPropagation()}>
            <b>{P('Mộ bài của bạn', 'Your graveyard')}</b>
            <div className="du-pick">{me.grave.map((g) => {
              const cd = cardOf(st, g);
              const pick = selectedTactic?.targeting === 'grave_pet' && cd.kind === 'pet' && cd.forms[0].stage <= 2 && !cd.forms[0].divine;
              return <button key={g} className={'btn small' + (pick ? ' primary' : '')} disabled={!pick} onClick={() => { setGraveOpen(null); run({ type: 'spell', card: sel!.kind === 'hand' ? sel!.card : 0, targets: [g] }); }}>{name(cd)}</button>;
            })}</div>
            <button className="btn small" onClick={() => setGraveOpen(null)}>{P('Đóng', 'Close')}</button>
          </div>
        </div>
      )}
      {coin && (
        <div className="du-modal">
          <div className="du-sheet center">
            <div className={'du-coin ' + (st.first === ME ? 'blue' : 'red')}>{st.first === ME ? '🔵' : '🔴'}</div>
            <b>{st.first === ME ? P('Đồng xu: XANH — bạn đi trước', 'Coin: BLUE — you go first') : P('Đồng xu: ĐỎ — đối thủ đi trước', 'Coin: RED — opponent goes first')}</b>
            <small>{P('Người đi trước không được tấn công ở lượt đầu. Mỗi bên 2.500 LP; Pet bị tiêu diệt: −500 LP.', 'The first player cannot attack on turn 1. 2,500 LP each; a destroyed Pet costs 500 LP.')}</small>
            <button className="btn primary" onClick={() => { setCoin(false); collect(); }} data-testid="duel-start">{P('Bắt đầu', 'Start')}</button>
          </div>
        </div>
      )}
      {finished(st) && (
        <div className="du-modal">
          <div className={'du-sheet center ' + (st.winner === ME ? 'win' : st.winner === AI ? 'lose' : '')}>
            <div className="du-result">{st.winner === ME ? P('CHIẾN THẮNG!', 'VICTORY!') : st.winner === AI ? P('THẤT BẠI', 'DEFEAT') : P('HÒA', 'DRAW')}</div>
            <small>{reason[st.endReason ?? ''] ?? st.endReason} · {P('Lượt', 'Turn')} {st.turn} · LP {me.lp} / {ai.lp}</small>
            <small>{P('Pet bị tiêu diệt', 'Pets destroyed')}: {st.log.filter((e) => e.type === 'destroyed' && e.p === AI).length} / {st.log.filter((e) => e.type === 'destroyed' && e.p === ME).length}</small>
            <div className="du-pick">
              <button className="btn primary" onClick={() => { stRef.current = newDuel(); seen.current = 0; setSel(null); setCoin(true); force(); }}>{P('Đấu lại', 'Rematch')}</button>
              <button className="btn" onClick={exit}>{P('Về Lobby', 'Lobby')}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function describeLink(st: DuelState, rules: DuelRules, lang: string, P: PFn): string {
  const l = st.chain[st.chain.length - 1];
  if (l.kind === 'spell' || l.kind === 'trap') return `${l.player === ME ? P('Bạn', 'You') : 'AI'}: ${rules.tactics[l.tacticId!].name[lang]}`;
  return P('Đối thủ tuyên bố tấn công', 'Opponent declared an attack');
}

function line(e: DuelEvent, _st: DuelState, rules: DuelRules, lang: string, P: PFn): string {
  const who = e.p === ME ? P('Bạn', 'You') : e.p === AI ? 'AI' : '';
  switch (e.type) {
    case 'turn': return `— ${P('Lượt', 'Turn')} ${e.a}: ${who} —`;
    case 'draw': return `${who} ${P('rút', 'draws')} ${e.n}`;
    case 'summon': return `${who} ${P('triệu hồi', 'summons')} ${e.s.split('|')[1]?.replace('pet_', '') ?? ''}`;
    case 'tribute': return `${who} ${P('hiến tế 1 Pet', 'tributes a Pet')}`;
    case 'spell': case 'trap': return `${who} ${e.type === 'spell' ? 'Spell' : 'Trap'}: ${rules.tactics[e.s]?.name[lang] ?? e.s}`;
    case 'set': return `${who} ${P('úp 1 lá', 'sets a card')}`;
    case 'attack': return `${who} ${P('tấn công', 'attacks')}`;
    case 'skill': case 'ultimate': return `${who} ${P('dùng kỹ năng', 'uses a skill')}`;
    case 'damage': return `  −${e.n} HP`;
    case 'heal': return `  +${e.n} HP`;
    case 'destroyed': return `${who}: ${P('Pet bị tiêu diệt, −500 LP', 'Pet destroyed, −500 LP')}`;
    case 'evolve': return `${who} ${P('tiến hóa', 'evolves')} ${e.s.split('>')[1]?.replace('pet_', '') ?? ''}`;
    case 'negate': return P('Vô hiệu hóa!', 'Negated!');
    case 'timeout': return `${who} ${P('hết giờ', 'timed out')}`;
    case 'finish': return `${P('Kết thúc', 'Finished')}: ${e.s.replace(/_/g, ' ')}`;
    case 'end_turn': return `${who} ${P('kết thúc lượt', 'ends the turn')}`;
    case 'discard': return `${who} ${P('bỏ bài (quá 7 lá)', 'discards (over 7)')}`;
    case 'battle': return `${who} ${P('vào pha chiến đấu', 'enters Battle Phase')}`;
    case 'move': return `${who} ${P('đổi vị trí Pet', 'moves a Pet')}`;
    case 'buff': return `  ${P('tăng chỉ số', 'stat boost')}`;
    case 'shield': return `  🛡 +${e.n}`;
    case 'status': return `  ${e.s}`;
    case 'energy': return `${who} +${e.n} ⚡`;
    case 'search': return `${who} ${P('tìm 1 Pet', 'searches a Pet')}`;
    case 'cleanse': return `  ${P('giải trạng thái', 'cleansed')}`;
    case 'destroy_st': return `${P('Phá 1 lá Spell/Trap', 'A Spell/Trap is destroyed')}`;
    case 'fizzle': return `  ${P('không còn hiệu lực', 'fizzles')}`;
    case 'negated': return `  ${P('bị vô hiệu', 'negated')}`;
    case 'redirect': return `  ${P('chuyển hướng tấn công', 'attack redirected')}`;
    case 'swap': return `${who} ${P('hoán đổi vị trí', 'swaps positions')}`;
    default: return `${e.type}`;
  }
}
