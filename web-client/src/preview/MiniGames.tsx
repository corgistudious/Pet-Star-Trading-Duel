// Mini Games for the Interactive Web Preview: Pet Memory, Pet Puzzle, Pet Sprint, Pet Quiz and a Daily Challenge.
// Rules, scoring and limits run in the browser (simulated preview economy); rewarded plays are capped per day and
// scores are bounded so a modified client cannot produce impossible results. A server-validated version is listed
// as not done in docs/status (scores would be checked by the API before any real reward).
import { useEffect, useMemo, useRef, useState } from 'react';
import { useI18n } from '../i18n';
import { petArt, useCatalog } from '../state/catalog';

type PFn = (vi: string, en: string) => string;
export type GameId = 'memory' | 'puzzle' | 'sprint' | 'quiz' | 'daily';
export const GAME_LIMITS = { rewardedPlaysPerDay: 3, maxScore: { memory: 1000, puzzle: 1000, sprint: 1200, quiz: 1000, daily: 1000 } as Record<GameId, number> };
export const rewardFor = (score: number) => Math.min(150, Math.floor(score / 10));   // preview beans per rewarded play

function rng(seed: number) { let s = seed >>> 0; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; }
function shuffle<T>(xs: T[], r: () => number) { const a = [...xs]; for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(r() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; }
export const today = () => new Date().toISOString().slice(0, 10);
const dateSeed = () => Number(today().replace(/-/g, ''));

export function MiniGames({ P, onScore, best, playsLeft }: { P: PFn; onScore: (g: GameId, score: number) => void; best: Record<string, number>; playsLeft: (g: GameId) => number }) {
  const [game, setGame] = useState<GameId | null>(null);
  const games: [GameId, string, string, string][] = [
    ['memory', P('Pet Memory', 'Pet Memory'), P('Lật thẻ tìm cặp Pet giống nhau', 'Flip cards to find matching Pet pairs'), '🃏'],
    ['puzzle', P('Pet Puzzle', 'Pet Puzzle'), P('Trượt mảnh ghép để hoàn thành ảnh Pet', 'Slide tiles to complete the Pet picture'), '🧩'],
    ['sprint', P('Pet Sprint', 'Pet Sprint'), P('Chạm thật nhanh để Pet về đích trong 10 giây', 'Tap fast to race your Pet in 10 seconds'), '🏃'],
    ['quiz', P('Pet Quiz', 'Pet Quiz'), P('5 câu hỏi về hệ và tiến hóa của Pet', '5 questions about Pet elements and evolutions'), '❓'],
    ['daily', P('Thử thách ngày', 'Daily Challenge'), P('Một ván Memory theo đề chung của hôm nay', "Today's shared Memory board"), '📅'],
  ];
  if (game) {
    const done = (score: number) => { onScore(game, Math.max(0, Math.min(GAME_LIMITS.maxScore[game], Math.round(score)))); };
    return (
      <div className="mg">
        <div className="mg-head"><button className="btn ghost small" onClick={() => setGame(null)}>‹ {P('Danh sách', 'Games')}</button><b>{games.find((g) => g[0] === game)![1]}</b>
          <small>{P('Lượt có thưởng còn lại hôm nay', 'Rewarded plays left today')}: {playsLeft(game)}</small></div>
        {game === 'memory' && <Memory P={P} seed={Date.now()} onDone={done} />}
        {game === 'daily' && <Memory P={P} seed={dateSeed()} onDone={done} />}
        {game === 'puzzle' && <Puzzle P={P} onDone={done} />}
        {game === 'sprint' && <Sprint P={P} onDone={done} />}
        {game === 'quiz' && <Quiz P={P} onDone={done} />}
      </div>
    );
  }
  return (
    <div className="mg-list">
      {games.map(([id, name, desc, icon]) => (
        <button key={id} className="mg-card" onClick={() => setGame(id)} data-testid={`game-${id}`}>
          <span className="mg-icon">{icon}</span><b>{name}</b><small>{desc}</small>
          <small className="mg-best">{P('Kỷ lục', 'Best')}: {best[id] ?? 0} · {P('Còn', 'Left')} {playsLeft(id)}/{GAME_LIMITS.rewardedPlaysPerDay}</small>
        </button>
      ))}
      <small className="mg-note">{P('Điểm bị giới hạn tối đa và mỗi trò chỉ có 3 lượt nhận thưởng mỗi ngày (chống gian lận). Phần thưởng trong Preview là mô phỏng.', 'Scores are capped and each game pays at most 3 times a day (anti-cheat). Preview rewards are simulated.')}</small>
    </div>
  );
}

function usePets(n: number, seed: number) {
  const cat = useCatalog();
  return useMemo(() => shuffle(Object.keys(cat.forms).sort(), rng(seed)).slice(0, n), [cat, n, seed]);
}

function Memory({ P, seed, onDone }: { P: PFn; seed: number; onDone: (s: number) => void }) {
  const pets = usePets(6, seed);
  const [deck] = useState(() => shuffle([...pets, ...pets], rng(seed + 1)));
  const [open, setOpen] = useState<number[]>([]);
  const [found, setFound] = useState<string[]>([]);
  const [moves, setMoves] = useState(0);
  const start = useRef(Date.now());
  const [over, setOver] = useState<number | null>(null);
  const flip = (i: number) => {
    if (over !== null || open.includes(i) || found.includes(deck[i]) || open.length === 2) return;
    const next = [...open, i];
    setOpen(next);
    if (next.length === 2) {
      setMoves((m) => m + 1);
      if (deck[next[0]] === deck[next[1]]) { const f = [...found, deck[i]]; setFound(f); setOpen([]); if (f.length === pets.length) { const sec = (Date.now() - start.current) / 1000; const sc = Math.max(100, 1000 - (moves + 1 - 6) * 30 - Math.floor(sec) * 4); setOver(sc); onDone(sc); } }
      else setTimeout(() => setOpen([]), 700);
    }
  };
  return (
    <div className="mg-game">
      <div className="mg-grid mem">{deck.map((p, i) => (
        <button key={i} className={'mg-tile' + (open.includes(i) || found.includes(p) ? ' up' : '')} onClick={() => flip(i)} data-testid={`mem-${i}`}>
          {open.includes(i) || found.includes(p) ? <img src={petArt(p, true)} alt="" /> : <span>★</span>}
        </button>))}</div>
      <small>{P('Lượt lật', 'Moves')}: {moves} · {P('Cặp', 'Pairs')}: {found.length}/6</small>
      {over !== null && <b className="mg-score">{P('Điểm', 'Score')}: {over}</b>}
    </div>
  );
}

function Puzzle({ P, onDone }: { P: PFn; onDone: (s: number) => void }) {
  const [pet] = usePets(1, Date.now() % 9973);
  const solved = [0, 1, 2, 3, 4, 5, 6, 7, 8];
  const [tiles, setTiles] = useState(() => {
    let t = [...solved]; const r = rng(Date.now());
    for (let k = 0; k < 60; k++) { const e = t.indexOf(8); const n = [e - 3, e + 3, e % 3 ? e - 1 : -1, e % 3 < 2 ? e + 1 : -1].filter((x) => x >= 0 && x < 9); const j = n[Math.floor(r() * n.length)]; [t[e], t[j]] = [t[j], t[e]]; }
    if (t.every((v, i) => v === i)) t = [1, 0, 2, 3, 4, 5, 6, 7, 8].map((x, i) => (i < 2 ? [0, 1][1 - i] : x));
    return t;
  });
  const [moves, setMoves] = useState(0);
  const [over, setOver] = useState<number | null>(null);
  const tap = (i: number) => {
    if (over !== null) return;
    const e = tiles.indexOf(8);
    const adj = Math.abs(e - i) === 3 || (Math.abs(e - i) === 1 && Math.floor(e / 3) === Math.floor(i / 3));
    if (!adj) return;
    const t = [...tiles]; [t[e], t[i]] = [t[i], t[e]]; setTiles(t); setMoves(moves + 1);
    if (t.every((v, k) => v === k)) { const sc = Math.max(100, 1000 - (moves + 1) * 10); setOver(sc); onDone(sc); }
  };
  return (
    <div className="mg-game">
      <div className="mg-grid puz">{tiles.map((v, i) => (
        <button key={i} className={'mg-ptile' + (v === 8 ? ' empty' : '')} onClick={() => tap(i)} data-testid={`puz-${i}`}
          style={v === 8 ? undefined : { backgroundImage: `url(${petArt(pet, true)})`, backgroundPosition: `${(v % 3) * 50}% ${Math.floor(v / 3) * 50}%` }} />))}</div>
      <small>{P('Bước', 'Moves')}: {moves}</small>
      {over !== null && <b className="mg-score">{P('Điểm', 'Score')}: {over}</b>}
    </div>
  );
}

function Sprint({ P, onDone }: { P: PFn; onDone: (s: number) => void }) {
  const [pet] = usePets(1, Date.now() % 7919);
  const [taps, setTaps] = useState(0);
  const [left, setLeft] = useState<number | null>(null);
  const [over, setOver] = useState<number | null>(null);
  const times = useRef<number[]>([]);
  useEffect(() => {
    if (left === null || over !== null) return;
    if (left <= 0) {
      // anti-cheat: taps faster than 18/s are impossible by hand and are not counted
      const valid = times.current.filter((t, i, a) => i === 0 || t - a[i - 1] >= 55).length;
      const sc = Math.min(GAME_LIMITS.maxScore.sprint, valid * 10); setOver(sc); onDone(sc); return;
    }
    const h = setTimeout(() => setLeft(left - 1), 1000); return () => clearTimeout(h);
  }, [left, over, onDone]);
  const tap = () => { if (over !== null) return; if (left === null) setLeft(10); times.current.push(performance.now()); setTaps(taps + 1); };
  return (
    <div className="mg-game">
      <div className="mg-track"><img src={petArt(pet, true)} alt="" style={{ left: `${Math.min(88, taps * 0.8)}%` }} /><span className="mg-flag">🏁</span></div>
      <button className="btn primary big" onClick={tap} data-testid="sprint-tap">{left === null ? P('Chạm để bắt đầu', 'Tap to start') : P('Chạm!', 'Tap!')}</button>
      <small>⏱ {left ?? 10}s · {taps} {P('lần chạm', 'taps')}</small>
      {over !== null && <b className="mg-score">{P('Điểm', 'Score')}: {over}</b>}
    </div>
  );
}

function Quiz({ P, onDone }: { P: PFn; onDone: (s: number) => void }) {
  const cat = useCatalog(); const { t } = useI18n();
  const qs = useMemo(() => {
    const r = rng(Date.now()); const ids = shuffle(Object.keys(cat.forms).sort(), r);
    const els = ['fire', 'water', 'nature', 'earth', 'lightning', 'wind', 'light', 'dark'];
    return ids.slice(0, 5).map((id, i) => {
      const f = cat.forms[id];
      if (i % 2 === 0) {
        const wrong = shuffle(els.filter((e) => e !== f.element), r).slice(0, 3);
        return { pet: id, q: P('Pet này thuộc hệ nào?', 'Which element is this Pet?'), options: shuffle([f.element, ...wrong], r).map((e) => ({ v: e, label: t('element.' + e) })), answer: f.element };
      }
      const stageQ = [1, 2, 3].filter((s) => s !== f.stage);
      return { pet: id, q: P('Pet này ở giai đoạn tiến hóa nào?', 'Which evolution stage is this Pet?'), options: shuffle([f.stage, ...stageQ], r).map((s) => ({ v: String(s), label: P(`Giai đoạn ${s}`, `Stage ${s}`) })), answer: String(f.stage) };
    });
  }, [cat, P, t]);
  const [i, setI] = useState(0); const [right, setRight] = useState(0); const [picked, setPicked] = useState<string | null>(null);
  const [over, setOver] = useState<number | null>(null);
  const q = qs[i];
  const choose = (v: string) => {
    if (picked !== null) return;
    setPicked(v); const ok = v === q.answer; const r = right + (ok ? 1 : 0); setRight(r);
    setTimeout(() => { setPicked(null); if (i + 1 < qs.length) setI(i + 1); else { const sc = r * 200; setOver(sc); onDone(sc); } }, 700);
  };
  return (
    <div className="mg-game">
      {over === null ? <>
        <img className="mg-qimg" src={petArt(q.pet, true)} alt="" />
        <b>{i + 1}/5 · {q.q}</b>
        <div className="mg-opts">{q.options.map((o) => <button key={o.v} className={'btn' + (picked !== null && o.v === q.answer ? ' primary' : '') + (picked === o.v && o.v !== q.answer ? ' danger' : '')} onClick={() => choose(o.v)} data-testid="quiz-opt">{o.label}</button>)}</div>
      </> : <b className="mg-score">{P('Đúng', 'Correct')} {right}/5 · {P('Điểm', 'Score')}: {over}</b>}
    </div>
  );
}
