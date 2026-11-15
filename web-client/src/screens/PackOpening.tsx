// Pack opening: the server rolls and stores all five cards in one idempotent request BEFORE the animation.
// The idempotency key is kept in sessionStorage until the result is shown, so a reload or a dropped connection
// replays the same request (same opening) instead of spending another pack. Results are then reachable at
// /open/:openingId and via the lobby "resume" banner until acknowledged.
import { useCallback, useEffect, useReducer, useRef, useState, type PointerEvent as RPointerEvent } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api, newIdempotencyKey } from '../api/client';
import type { PackOpeningDto } from '../api/types';
import { useT } from '../i18n';
import { useSession } from '../state/session';
import { CardBack, CardView } from '../components/CardView';
import { useErrorText } from '../components/ui';
import { dragTear, flipNext, initialPack, rareLevel, releaseTear, skipAll, startReveal, type PackState } from '../logic/pack';
import { uiArt } from '../state/catalog';

type Act = { k: 'drag'; f: number } | { k: 'release' } | { k: 'reveal' } | { k: 'flip' } | { k: 'skip' } | { k: 'reset' };
function reducer(s: PackState, a: Act): PackState {
  switch (a.k) {
    case 'drag': return dragTear(s, a.f);
    case 'release': return releaseTear(s);
    case 'reveal': return startReveal(s);
    case 'flip': return flipNext(s);
    case 'skip': return skipAll(s);
    case 'reset': return initialPack();
  }
}

const KEY_STORE = 'petstar.openKey.';

export default function PackOpening() {
  const t = useT();
  const nav = useNavigate();
  const errText = useErrorText();
  const { openingId } = useParams();
  const [params] = useSearchParams();
  const { player, reloadPlayer } = useSession();
  const [opening, setOpening] = useState<PackOpeningDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [s, dispatch] = useReducer(reducer, undefined, () => initialPack());
  const started = useRef(false);

  const packId = params.get('pack') ?? Object.entries(player?.packs ?? {}).find(([, n]) => n > 0)?.[0];

  // Load an existing opening (recovery) or open a new pack.
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    (async () => {
      try {
        if (openingId) { setOpening(await api.get<PackOpeningDto>(`/api/packs/openings/${openingId}`)); return; }
        if (!packId) { setError(t('error.no_pack_available')); return; }
        let key = sessionStorage.getItem(KEY_STORE + packId);
        if (!key) { key = newIdempotencyKey(); sessionStorage.setItem(KEY_STORE + packId, key); }
        const o = await api.post<PackOpeningDto>('/api/packs/open', { packTypeId: packId }, { idem: key });
        sessionStorage.removeItem(KEY_STORE + packId);
        setOpening(o);
        nav(`/open/${o.openingId}`, { replace: true });
        void reloadPlayer();
      } catch (e) {
        setError(errText(e));
      }
    })();
  }, [openingId, packId]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (s.phase === 'summary' && opening && !opening.acknowledged) {
      api.post(`/api/packs/openings/${opening.openingId}/ack`).then(() => setOpening((o) => (o ? { ...o, acknowledged: true } : o))).catch(() => {});
    }
  }, [s.phase, opening]);

  // A recovered, already-acknowledged opening goes straight to the summary.
  useEffect(() => { if (opening?.acknowledged && s.phase === 'sealed') dispatch({ k: 'skip' }); }, [opening, s.phase]);

  const tearRef = useRef<{ x: number; w: number } | null>(null);
  const onDown = (e: RPointerEvent<HTMLDivElement>) => {
    if (!opening) return;
    (e.target as HTMLElement).setPointerCapture?.(e.pointerId);
    const r = e.currentTarget.getBoundingClientRect();
    tearRef.current = { x: e.clientX, w: r.width };
  };
  const onMove = (e: RPointerEvent<HTMLDivElement>) => {
    if (!tearRef.current) return;
    dispatch({ k: 'drag', f: Math.abs(e.clientX - tearRef.current.x) / tearRef.current.w });
  };
  const onUp = () => { tearRef.current = null; dispatch({ k: 'release' }); };

  useEffect(() => { if (s.phase === 'torn') { const id = setTimeout(() => dispatch({ k: 'reveal' }), 650); return () => clearTimeout(id); } }, [s.phase]);

  const flip = useCallback(() => dispatch({ k: 'flip' }), []);
  const swipe = useRef<number | null>(null);

  if (error) {
    return (
      <div className="opening" style={{ backgroundImage: `url(${uiArt('bg_pack_opening')})` }}>
        <div className="panel center"><p>{error}</p><p className="muted small">{t('pack.network_recover')}</p>
          <button className="btn" onClick={() => nav('/')}>{t('hub.home')}</button></div>
      </div>
    );
  }

  const cards = opening?.cards ?? [];
  const cur = cards[s.current];
  const level = s.phase === 'revealing' && cur && s.revealed[s.current] ? rareLevel(cur.rarity) : 0;

  return (
    <div className="opening" style={{ backgroundImage: `url(${uiArt('bg_pack_opening')})` }}>
      {s.phase !== 'summary' && <button className="btn ghost skip" onClick={() => dispatch({ k: 'skip' })} disabled={!opening}>{t('pack.skip')}</button>}

      {(s.phase === 'sealed' || s.phase === 'tearing' || s.phase === 'torn') && (
        <div className={`pack-stage ${s.phase}`}>
          <div className="sealed-pack" style={{ '--tear': s.tear } as React.CSSProperties}>
            <img src={uiArt(opening?.packTypeId === 'premium_pack' ? 'ui_pack_premium' : 'ui_pack_basic')} alt="" draggable={false} />
            <div className="tear-strip" data-testid="tear-strip" onPointerDown={onDown} onPointerMove={onMove} onPointerUp={onUp} onPointerCancel={onUp}>
              <div className="tear-progress" />
            </div>
          </div>
          <p className="hint">{opening ? t('pack.swipe_to_tear') : <span className="spinner" />}</p>
        </div>
      )}

      {s.phase === 'revealing' && cur && (
        <div className={`reveal rare-${level}`}
          onPointerDown={(e) => { swipe.current = e.clientX; }}
          onPointerUp={(e) => { if (swipe.current !== null) { swipe.current = null; flip(); } void e; }}
          data-testid="reveal">
          <div className="reveal-count">{s.current + 1}/{cards.length}{s.current === 4 ? ' ★' : ''}</div>
          <div className={`flip ${s.revealed[s.current] ? 'flipped' : ''}`} key={s.current}>
            <div className="flip-face back"><CardBack /></div>
            <div className="flip-face front"><CardView card={cur} /></div>
          </div>
          {level > 0 && <div className="rare-burst" aria-hidden />}
          <p className="hint">{t('pack.swipe_to_flip')}</p>
        </div>
      )}

      {s.phase === 'summary' && (
        <div className="summary">
          <div className="summary-grid">
            {cards.map((c, i) => (
              <div key={c.id} className={`summary-card rare-${rareLevel(c.rarity)}`} style={{ animationDelay: `${i * 80}ms` }}>
                <CardView card={c} compact onClick={() => nav(`/card/${c.id}`)} />
              </div>
            ))}
          </div>
          <div className="row gap center">
            <button className="btn" onClick={() => nav('/home')}>{t('pack.to_pet_home')}</button>
            {Object.values(player?.packs ?? {}).some((n) => n > 0)
              ? <button className="btn primary" onClick={() => { started.current = false; setOpening(null); dispatch({ k: 'reset' }); nav('/open', { replace: true }); }}>{t('pack.open_another')}</button>
              : <button className="btn primary" onClick={() => nav('/shop')}>{t('lobby.go_shop')}</button>}
          </div>
        </div>
      )}
    </div>
  );
}
