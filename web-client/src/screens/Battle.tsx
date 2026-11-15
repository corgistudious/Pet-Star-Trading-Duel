// Duel board for PvE / ranked / friend (server-simulated opponent) and live PvP (sealed plans, 30 s deadline).
// The 3+2 slots are drawn here on top of the slot-free board art; the server validates and resolves every round.
import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { ActionType, BattleDto, BattleEvent, BattleUnitDto, PlannedAction } from '../api/types';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { useRealtime, useRealtimeEvent } from '../state/realtime';
import { ELEMENT_COLORS, RARITY_COLORS, petArt, uiArt, useCatalog } from '../state/catalog';
import { Confirm, Countdown, Loading, fmt, useAction, useErrorText, useToast } from '../components/ui';
import { actableUnits, actionCost, alive, boardRows, legalEnemyTargets, planCost, setOrder, targetKind, type SkillLookup } from '../logic/battle';

interface LiveView { battle: BattleDto; submitted: boolean }

export default function Battle({ live }: { live: boolean }) {
  const { id } = useParams();
  const { t, lang } = useI18n();
  const nav = useNavigate();
  const cat = useCatalog();
  const toast = useToast();
  const errText = useErrorText();
  const { reloadPlayer } = useSession();
  const { generation } = useRealtime();
  const skills: SkillLookup = useCallback((sid: string) => cat.skills[sid], [cat]);

  const [shown, setShown] = useState<BattleDto | null>(null); // state currently on screen (lags during playback)
  const [submitted, setSubmitted] = useState(false);
  const [opponentReady, setOpponentReady] = useState(false);
  const [plan, setPlan] = useState<PlannedAction[]>([]);
  const [sel, setSel] = useState<number | null>(null);
  const [pending, setPending] = useState<{ unit: number; type: ActionType; kind: 'enemy' | 'ally' } | null>(null);
  const [playing, setPlaying] = useState<BattleEvent | null>(null);
  const [log, setLog] = useState<string[]>([]);
  const [error, setError] = useState<unknown>();
  const [surrender, setSurrender] = useState(false);
  const { busy, run } = useAction();
  const queue = useRef<Promise<void>>(Promise.resolve());
  const shownRef = useRef<BattleDto | null>(null);

  const formName = useCallback((u?: BattleUnitDto) => (u ? cat.forms[u.formId]?.name ?? u.formId : '?'), [cat]);

  /** Plays the events of a resolved round one by one, then shows the new state. */
  const apply = useCallback((next: BattleDto) => {
    queue.current = queue.current.then(async () => {
      const prev = shownRef.current;
      if (prev && next.round !== prev.round || (prev && next.status !== prev.status)) {
        const lines: string[] = [];
        for (const ev of next.lastEvents.filter((e) => ['damage', 'heal', 'shield', 'status_applied', 'defend', 'skipped_stunned', 'skipped_energy'].includes(e.type))) {
          setPlaying(ev);
          const actor = (prev ?? next).units.find((u) => u.side === ev.actorSide && u.index === ev.actorIndex);
          const target = (prev ?? next).units.find((u) => u.side === ev.targetSide && u.index === ev.targetIndex);
          lines.push(describe(ev, formName(actor), formName(target), t, lang, cat.skills));
          await sleep(document.visibilityState === 'visible' ? 420 : 0);
        }
        setPlaying(null);
        setLog((l) => [...lines.reverse(), ...l].slice(0, 40));
      }
      setShown(next);
      shownRef.current = next;
      setPlan([]); setSel(null); setPending(null);
    });
  }, [formName, t, lang, cat.skills]);

  const load = useCallback(async () => {
    try {
      if (live) {
        const v = await api.get<LiveView>(`/api/pvp/battles/${id}`);
        setSubmitted(v.submitted);
        apply(v.battle);
      } else {
        apply(await api.get<BattleDto>(`/api/battles/${id}`));
      }
    } catch (e) { setError(e); }
  }, [id, live, apply]);

  useEffect(() => { void load(); }, [load, generation]); // refetch after (re)connect: never trust missed pushes
  useRealtimeEvent<BattleDto>('pvp_round', (b) => { if (live && b.id === id) { setSubmitted(false); setOpponentReady(false); apply(b); } });
  useRealtimeEvent<{ battleId: string }>('pvp_opponent_ready', (d) => { if (d.battleId === id) setOpponentReady(true); });

  useEffect(() => { if (shown && shown.status !== 'active') void reloadPlayer(); }, [shown?.status]); // eslint-disable-line react-hooks/exhaustive-deps

  const me = shown?.mySide ?? 0;
  const energyLeft = shown ? shown.myEnergy - planCost(shown.units, me, plan, skills) : 0;
  const actable = useMemo(() => (shown ? actableUnits(shown, me).map((u) => u.index) : []), [shown, me]);
  const legalEnemies = useMemo(() => (shown ? legalEnemyTargets(shown.units, me).map((u) => u.index) : []), [shown, me]);

  if (!shown) return <div className="battle"><Loading error={error} onRetry={load} /></div>;
  const active = shown.status === 'active';
  const selUnit = sel != null ? shown.units.find((u) => u.side === me && u.index === sel) : undefined;

  const choose = (type: ActionType) => {
    if (!selUnit) return;
    const kind = targetKind(selUnit, type, skills);
    if (kind) { setPending({ unit: selUnit.index, type, kind }); return; }
    commit({ unitIndex: selUnit.index, type });
  };
  const commit = (order: PlannedAction) => {
    const next = setOrder(plan, order, shown.units, me, shown.myEnergy, skills);
    if (!next) { toast(t('error.not_enough_energy'), 'err'); return; }
    setPlan(next); setPending(null); setSel(null);
  };
  const tapUnit = (u: BattleUnitDto) => {
    if (!active || submitted || playing) return;
    if (pending) {
      const ok = pending.kind === 'enemy' ? u.side !== me && legalEnemies.includes(u.index) : u.side === me && alive(u);
      if (ok) commit({ unitIndex: pending.unit, type: pending.type, targetIndex: u.index });
      return;
    }
    if (u.side === me && actable.includes(u.index)) setSel(sel === u.index ? null : u.index);
    else if (u.side === me) toast(u.statuses.includes('stun') ? t('error.unit_stunned') : t('duel.select_card_first'), 'info');
  };

  const endTurn = async () => {
    if (live) {
      const r = await run(() => api.post<{ submitted: boolean; resolved: boolean }>(`/api/pvp/battles/${id}/rounds`, { round: shown.round, actions: plan }));
      if (r) { setSubmitted(true); if (r.resolved) void load(); }
    } else {
      try {
        const b = await api.post<BattleDto>(`/api/battles/${id}/rounds`, { round: shown.round, actions: plan });
        apply(b);
      } catch (e) { toast(errText(e), 'err'); void load(); }
    }
  };
  const doSurrender = async () => {
    setSurrender(false);
    const r = await run(() => api.post(live ? `/api/pvp/battles/${id}/surrender` : `/api/battles/${id}/surrender`));
    if (r !== undefined) void load();
  };

  const orderOf = (idx: number) => plan.find((a) => a.unitIndex === idx);
  const rows = (side: number) => boardRows(shown.units, side);
  const [eFront, eBack] = rows(1 - me);
  const [mFront, mBack] = rows(me);

  const slot = (u: BattleUnitDto | null, key: string) => {
    if (!u) return <div key={key} className="slot empty" />;
    const mine = u.side === me;
    const o = mine ? orderOf(u.index) : undefined;
    const targetable = pending && (pending.kind === 'enemy' ? !mine && legalEnemies.includes(u.index) : mine && alive(u));
    const hit = playing && playing.targetSide === u.side && playing.targetIndex === u.index;
    const acting = playing && playing.actorSide === u.side && playing.actorIndex === u.index;
    const style = { '--rarity': RARITY_COLORS[u.rarity], '--element': ELEMENT_COLORS[u.element] } as CSSProperties;
    return (
      <button key={key} style={style} onClick={() => tapUnit(u)} aria-label={`${formName(u)} ${u.hp}/${u.maxHp}`}
        className={`slot unit ${alive(u) ? '' : 'dead'} ${sel === u.index && mine ? 'sel' : ''} ${targetable ? 'targetable' : ''} ${acting ? 'acting' : ''} ${hit ? 'hit' : ''}`}>
        <img src={petArt(u.formId, true)} alt="" draggable={false} />
        <div className="hpbar"><div style={{ width: `${(100 * u.hp) / u.maxHp}%` }} />{u.shield > 0 && <div className="shield" style={{ width: `${Math.min(100, (100 * u.shield) / u.maxHp)}%` }} />}</div>
        <span className="lv">{u.level}</span>
        {u.statuses.length > 0 && <span className="st">{u.statuses.map((s) => STATUS_GLYPH[s] ?? '•').join('')}</span>}
        {o && <span className="order">{ORDER_GLYPH[o.type]}</span>}
        {hit && playing && playing.amount > 0 && <span className={`float ${playing.type}`}>{playing.type === 'heal' ? '+' : '-'}{playing.amount}{playing.crit ? '!' : ''}</span>}
      </button>
    );
  };

  const won = shown.winnerSide === me;
  return (
    <div className="battle" style={{ '--board-l': `url(${uiArt('bg_duel_board_landscape')})` } as CSSProperties}>
      <div className="battle-top">
        <button className="icon-btn" onClick={() => nav('/duel')} aria-label="back">‹</button>
        <span>{t('duel.round', shown.round)}</span>
        {live && active && shown.roundDeadline && <Countdown key={shown.round} until={shown.roundDeadline} />}
        <span className="energy enemy">{t('duel.enemy')} ⚡{shown.opponentEnergy}</span>
        {active && <button className="btn ghost small" onClick={() => setSurrender(true)}>{t('duel.surrender')}</button>}
      </div>
      {/* Landscape 16:9 board: my Backline | my Frontline || enemy Frontline | enemy Backline (3 front + 2 back each) */}
      <div className="board" data-testid="board">
        <div className="side mine">
          <div className="brow two back" aria-label={t('web.back_row')}>{mBack.map((u, i) => slot(u, 'mb' + i))}</div>
          <div className="brow three front" aria-label={t('web.front_row')}>{mFront.map((u, i) => slot(u, 'mf' + i))}</div>
        </div>
        <div className="midline" />
        <div className="side enemy">
          <div className="brow three front">{eFront.map((u, i) => slot(u, 'ef' + i))}</div>
          <div className="brow two back">{eBack.map((u, i) => slot(u, 'eb' + i))}</div>
        </div>
      </div>

      {active ? (
        <div className="command">
          <div className="energy mine" data-testid="energy">{t('duel.energy', energyLeft, shown.myEnergy)}</div>
          {pending ? (
            <div className="hint">{t('web.pick_target')} <button className="btn ghost small" onClick={() => setPending(null)}>{t('web.cancel')}</button></div>
          ) : selUnit ? (
            <div className="action-row">
              {(['Basic', 'Skill', 'Ultimate', 'Defend'] as ActionType[]).map((a) => {
                const c = actionCost(selUnit, a, skills);
                const sid = a === 'Skill' ? selUnit.skillId : a === 'Ultimate' ? selUnit.ultimateId : null;
                return (
                  <button key={a} className={`btn act act-${a}`} disabled={c == null || c > energyLeft + (actionCost(selUnit, orderOf(selUnit.index)?.type ?? 'Basic', skills) ?? 0)} onClick={() => choose(a)}>
                    <b>{t('duel.' + a.toLowerCase())}</b>
                    {sid && <small>{cat.skills[sid]?.name[lang] ?? sid}</small>}
                    {c ? <small>⚡{c}</small> : null}
                  </button>
                );
              })}
            </div>
          ) : (
            <div className="hint">{submitted ? (opponentReady ? t('web.resolving') : t('web.waiting_opponent')) : t('web.tap_unit')}</div>
          )}
          <button className="btn primary big" disabled={busy || submitted || !!playing} onClick={endTurn} data-testid="end-turn">{t('duel.end_turn')}</button>
        </div>
      ) : (
        <div className="result">
          <h2 className={won ? 'win' : 'lose'}>{shown.winnerSide == null || shown.winnerSide < 0 ? t('duel.draw') : won ? t('duel.victory') : t('duel.defeat')}</h2>
          {shown.endReason && <p className="muted small">{t('web.end_' + shown.endReason)}</p>}
          {shown.rewards && (
            <div className="rewards">
              {Object.entries(shown.rewards.currencies).map(([c, v]) => <span key={c}>+{fmt(v)} {t('currency.' + c.toLowerCase())}</span>)}
              {Object.entries(shown.rewards.items).map(([c, v]) => <span key={c}>+{v} {t('item.' + c)}</span>)}
              {Object.entries(shown.rewards.packs).map(([c, v]) => <span key={c}>+{v} {t('web.pack')}</span>)}
              {shown.rewards.cardExp > 0 && <span>{t('duel.card_exp', shown.rewards.cardExp)}</span>}
              {shown.rewards.firstClear && <span>{t('duel.first_clear')}</span>}
              {shown.rewards.dailyCapReached && <span className="muted">{t('duel.daily_cap')}</span>}
            </div>
          )}
          <div className="row gap center">
            <button className="btn primary" onClick={() => nav('/duel')}>{t('web.continue')}</button>
            <button className="btn" onClick={() => api.post('/api/feed/posts', { type: 'duel', caption: '', refId: shown.id }).then(() => toast(t('web.shared'), 'ok')).catch((e) => toast(errText(e), 'err'))}>{t('passport.share')}</button>
          </div>
        </div>
      )}
      <ol className="battle-log" aria-live="polite">{log.map((l, i) => <li key={i}>{l}</li>)}</ol>
      {surrender && <Confirm text={t('duel.surrender') + '?'} onYes={doSurrender} onNo={() => setSurrender(false)} />}
    </div>
  );
}

const STATUS_GLYPH: Record<string, string> = { burn: '🔥', poison: '☠', stun: '💫', taunt: '🛡', weaken: '⬇' };
const ORDER_GLYPH: Record<ActionType, string> = { Basic: '⚔', Skill: '✦', Ultimate: '★', Defend: '🛡' };
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

function describe(ev: BattleEvent, actor: string, target: string, t: (k: string, ...a: (string | number)[]) => string, lang: string,
  skills: Record<string, { name: Record<string, string> }>): string {
  const sk = ev.skillId ? ` (${skills[ev.skillId]?.name[lang] ?? ev.skillId})` : '';
  switch (ev.type) {
    case 'damage': return `${actor}${sk} → ${target}: -${ev.amount}${ev.crit ? ' CRIT' : ''}${ev.elementMultiplier > 1 ? ' ▲' : ev.elementMultiplier < 1 ? ' ▼' : ''}`;
    case 'heal': return `${actor}${sk} → ${target}: +${ev.amount}`;
    case 'shield': return `${actor}${sk} → ${target}: 🛡${ev.amount}`;
    case 'status_applied': return `${target}: ${t('status.' + (ev.status ?? ''))}`;
    case 'defend': return `${actor}: ${t('duel.defend')}`;
    case 'skipped_stunned': return `${actor}: ${t('status.stun')}`;
    case 'skipped_energy': return `${actor}: ${t('error.not_enough_energy')}`;
    default: return ev.type;
  }
}
