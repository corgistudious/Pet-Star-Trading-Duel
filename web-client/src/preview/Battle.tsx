// Preview battle: the real 3-front + 2-back board, energy, skills and ultimates, resolved in a simplified way in the
// browser so the owner can judge the 16:9 layout. The real game resolves every battle on the server (BattleEngine).
import { useEffect, useMemo, useState } from 'react';
import { useI18n } from '../i18n';
import { petArt, uiArt, useCatalog, type Catalog } from '../state/catalog';
import { stats, type SimState } from './sim';

interface Unit { key: string; side: 0 | 1; pos: number; formId: string; maxHp: number; hp: number; atk: number; def: number; spd: number; crit: number; element: string; skill?: string | null; ult?: string | null; act: 'Basic' | 'Skill' | 'Ultimate'; hit?: number }
type PFn = (vi: string, en: string) => string;

function build(cat: Catalog, s: SimState): Unit[] {
  const mine = s.cards.filter((c) => !c.listed).map((c) => ({ c, st: stats(cat, c) })).sort((a, b) => b.st.power - a.st.power).slice(0, 5);
  // the strongest go to the back only if they are supports/mages, like a sensible formation
  const order = [...mine].sort((a, b) => (['support', 'mage'].includes(cat.forms[a.c.formId].role) ? 1 : 0) - (['support', 'mage'].includes(cat.forms[b.c.formId].role) ? 1 : 0));
  const avg = Math.round(mine.reduce((a, m) => a + m.c.level, 0) / Math.max(1, mine.length));
  const pool = Object.keys(cat.forms).filter((f) => !f.startsWith('pet_dv_') && cat.forms[f].stage === 3);
  const enemies = Array.from({ length: 5 }, (_, i) => ({ formId: pool[(i * 37 + avg) % pool.length], level: avg }));
  const mk = (side: 0 | 1, pos: number, formId: string, st: { hp: number; atk: number; def: number; spd: number; crit: number }): Unit => {
    const f = cat.forms[formId];
    return { key: `${side}-${pos}`, side, pos, formId, maxHp: st.hp, hp: st.hp, atk: st.atk, def: st.def, spd: st.spd, crit: st.crit, element: f.element, skill: f.skill, ult: f.ultimate, act: 'Basic' };
  };
  return [
    ...order.map((m, i) => mk(0, i, m.c.formId, m.st)),
    ...enemies.map((e, i) => mk(1, i, e.formId, stats(cat, { id: 0, serial: '', formId: e.formId, rarity: 'SR', quality: 'B', finish: 'Normal', level: e.level, stars: 4, enhance: 6, breakthrough: 0, awakened: false, skillLevel: 3, divineSet: null, origin: '' }))),
  ];
}

export function Battle({ s, P, exit }: { s: SimState; P: PFn; exit: () => void }) {
  const cat = useCatalog();
  const { t, lang } = useI18n();
  const initial = useMemo(() => build(cat, s), [cat, s]);
  const [units, setUnits] = useState<Unit[]>(initial);
  const [energy, setEnergy] = useState<[number, number]>([3, 3]);
  const [round, setRound] = useState(1);
  const [log, setLog] = useState<string[]>([]);
  const [winner, setWinner] = useState<number | null>(null);
  const [portraitPhone, setPortraitPhone] = useState(false);
  const [forceShow, setForceShow] = useState(false);
  useEffect(() => {
    const f = () => setPortraitPhone(innerHeight > innerWidth && Math.min(screen.width, screen.height) < 600);
    f(); addEventListener('resize', f); return () => removeEventListener('resize', f);
  }, []);

  const cost = (id?: string | null) => (id ? cat.skills[id]?.cost ?? 99 : 99);
  const planCost = units.filter((u) => u.side === 0 && u.hp > 0).reduce((a, u) => a + (u.act === 'Skill' ? cost(u.skill) : u.act === 'Ultimate' ? cost(u.ult) : 0), 0);
  const cycle = (u: Unit) => {
    if (winner != null || u.side !== 0 || u.hp <= 0) return;
    const nextAct = u.act === 'Basic' ? (u.skill ? 'Skill' : u.ult ? 'Ultimate' : 'Basic') : u.act === 'Skill' ? (u.ult ? 'Ultimate' : 'Basic') : 'Basic';
    setUnits((us) => us.map((x) => (x.key === u.key ? { ...x, act: nextAct } : x)));
  };

  const resolve = () => {
    if (winner != null || planCost > energy[0]) return;
    const us = units.map((u) => ({ ...u, hit: undefined as number | undefined }));
    const en: [number, number] = [energy[0], energy[1]];
    // enemy plan: strongest affordable action per unit
    for (const u of us.filter((x) => x.side === 1 && x.hp > 0)) {
      u.act = u.ult && cost(u.ult) <= en[1] ? 'Ultimate' : u.skill && cost(u.skill) <= en[1] ? 'Skill' : 'Basic';
      en[1] -= u.act === 'Ultimate' ? cost(u.ult) : u.act === 'Skill' ? cost(u.skill) : 0;
    }
    en[0] -= planCost;
    const lines: string[] = [];
    const name = (u: Unit) => (t('pet.' + u.formId) !== 'pet.' + u.formId ? t('pet.' + u.formId) : cat.forms[u.formId].name);
    for (const a of [...us].sort((x, y) => y.spd - x.spd)) {
      if (a.hp <= 0) continue;
      const foes = us.filter((x) => x.side !== a.side && x.hp > 0);
      if (!foes.length) break;
      const front = foes.filter((x) => x.pos < 3);
      const sid = a.act === 'Skill' ? a.skill : a.act === 'Ultimate' ? a.ult : null;
      const sk = sid ? cat.skills[sid] : undefined;
      const dmgEff = sk?.effects.find((e) => e.type === 'damage') as { power?: number } | undefined;
      const healEff = sk?.effects.find((e) => e.type === 'heal' || e.type === 'shield') as { power?: number } | undefined;
      if (sk && !dmgEff && healEff) {
        const ally = us.filter((x) => x.side === a.side && x.hp > 0).sort((x, y) => x.hp / x.maxHp - y.hp / y.maxHp)[0];
        const amt = Math.round((healEff.power ?? 1) * a.atk);
        ally.hp = Math.min(ally.maxHp, ally.hp + amt);
        lines.push(`${name(a)} ${sk.name[lang] ?? sk.name.en} → +${amt} HP ${name(ally)}`);
        continue;
      }
      const targets = sk?.target === 'enemy_all' ? foes : sk?.target === 'enemy_front_row' ? (front.length ? front : foes) : [(front.length ? front : foes).sort((x, y) => x.hp - y.hp)[0]];
      for (const tg of targets) {
        const ch = cat.elements.elements.find((e) => e.id === a.element);
        const elem = ch?.strongAgainst.includes(tg.element) ? cat.elements.strongMultiplier : cat.elements.elements.find((e) => e.id === tg.element)?.strongAgainst.includes(a.element) ? cat.elements.weakMultiplier : 1;
        const crit = Math.random() < a.crit;
        const dmg = Math.max(1, Math.round((dmgEff?.power ?? 1) * a.atk * (100 / (100 + tg.def)) * elem * (crit ? 1.5 : 1)));
        tg.hp = Math.max(0, tg.hp - dmg); tg.hit = dmg;
        lines.push(`${name(a)}${sk ? ' · ' + (sk.name[lang] ?? sk.name.en) : ''} → ${name(tg)} −${dmg}${crit ? ' ✦' : ''}`);
      }
    }
    const aliveA = us.some((u) => u.side === 0 && u.hp > 0), aliveB = us.some((u) => u.side === 1 && u.hp > 0);
    setUnits(us.map((u) => (u.side === 0 ? { ...u, act: 'Basic' } : u)));
    setEnergy([Math.min(10, en[0] + 3), Math.min(10, en[1] + 3)]);
    setRound((r) => r + 1);
    setLog((l) => [`— ${P('Lượt', 'Round')} ${round} —`, ...lines, ...l].slice(0, 40));
    if (!aliveB) setWinner(0); else if (!aliveA) setWinner(1); else if (round >= 20) setWinner(-1);
  };

  const restart = () => { setUnits(build(cat, s)); setEnergy([3, 3]); setRound(1); setLog([]); setWinner(null); };
  const side = (sd: 0 | 1) => (
    <div className={'pv-side side-' + sd}>
      {[[0, 1, 2], [3, 4]].map((row, ri) => (
        <div key={ri} className={'pv-row ' + (ri === 0 ? 'front' : 'back')}>
          {row.map((pos) => {
            const u = units.find((x) => x.side === sd && x.pos === pos);
            if (!u) return <div key={pos} className="pv-unit empty" />;
            return (
              <button key={u.key} className={'pv-unit' + (u.hp <= 0 ? ' down' : '')} onClick={() => cycle(u)}>
                <img src={petArt(u.formId, true)} alt="" />
                <span className="pv-hp"><i style={{ width: `${(100 * u.hp) / u.maxHp}%` }} /></span>
                {sd === 0 && u.hp > 0 && <span className={'pv-act act-' + u.act}>{u.act === 'Basic' ? P('Thường', 'Basic') : u.act === 'Skill' ? `${P('KN', 'Skill')} ⚡${cost(u.skill)}` : `${P('TK', 'Ult')} ⚡${cost(u.ult)}`}</span>}
                {u.hit != null && <span key={round} className="pv-dmg">−{u.hit}</span>}
              </button>
            );
          })}
        </div>
      ))}
    </div>
  );

  if (portraitPhone && !forceShow) return (
    <div className="pv-rotate">
      <div className="pv-phone">📱</div>
      <p>{P('Xoay ngang điện thoại để vào trận đấu', 'Rotate your phone to landscape to play the duel')}</p>
      <button className="btn ghost small" onClick={() => setForceShow(true)}>{P('Vẫn xem ở chế độ dọc', 'Show anyway')}</button>
      <button className="btn ghost small" onClick={exit}>‹ {P('Về Lobby', 'Lobby')}</button>
    </div>
  );
  return (
    <div className="pv-battle" style={{ backgroundImage: `url(${uiArt('bg_duel_board_landscape')})` }}>
      <div className="pv-bhead">
        <button className="btn ghost small" onClick={exit}>‹ {P('Thoát', 'Exit')}</button>
        <b>{P('Lượt', 'Round')} {Math.min(round, 20)} · ⚡ {energy[0]}{planCost ? ` (−${planCost})` : ''} / 10</b>
        <span className="muted small">{P('Mô phỏng giao diện — trận thật do server tính', 'UI simulation — real battles are server-resolved')}</span>
      </div>
      <div className="pv-board">{side(0)}<div className="pv-vs">VS</div>{side(1)}</div>
      <div className="pv-bfoot">
        {winner == null ? <>
          <span className="small">{P('Chạm Pet của bạn để chọn: Thường → Kỹ năng → Tuyệt kỹ', 'Tap your Pets: Basic → Skill → Ultimate')}</span>
          <button className="btn primary" disabled={planCost > energy[0]} onClick={resolve} data-testid="battle-go">{P('Thực hiện lượt', 'Resolve round')}</button>
        </> : <>
          <b>{winner === 0 ? P('Chiến thắng!', 'Victory!') : winner === 1 ? P('Thất bại', 'Defeat') : P('Hòa', 'Draw')}</b>
          <button className="btn primary" onClick={restart}>{P('Đấu lại', 'Rematch')}</button>
        </>}
        <div className="pv-log">{log.slice(0, 6).map((l, i) => <div key={i}>{l}</div>)}</div>
      </div>
    </div>
  );
}
