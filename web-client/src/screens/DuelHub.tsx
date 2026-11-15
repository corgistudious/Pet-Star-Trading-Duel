import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, newIdempotencyKey } from '../api/client';
import type { BattleDto, PveStageDto } from '../api/types';
import { useI18n } from '../i18n';
import { useRealtimeEvent } from '../state/realtime';
import { PageHead } from '../components/Layout';
import { Empty, Loading, Tabs, timeAgo, useAction, useLoad, useToast } from '../components/ui';
import { petArt } from '../state/catalog';

type Tab = 'pve' | 'ranked' | 'pvp' | 'history';

export default function DuelHub() {
  const { t } = useI18n();
  const [tab, setTab] = useState<Tab>('pve');
  const nav = useNavigate();
  return (
    <div className="page">
      <PageHead title={t('duel.title')} right={<button className="btn ghost" onClick={() => nav('/decks')}>{t('web.decks')}</button>} />
      <Tabs value={tab} onChange={setTab} items={[['pve', t('battle.pve')], ['ranked', t('battle.ranked')], ['pvp', t('web.live_pvp')], ['history', t('battle.history')]]} />
      {tab === 'pve' && <PveTab />}
      {tab === 'ranked' && <RankedTab />}
      {tab === 'pvp' && <PvpTab />}
      {tab === 'history' && <HistoryTab />}
    </div>
  );
}

function PveTab() {
  const { t } = useI18n();
  const nav = useNavigate();
  const { data, error, reload } = useLoad(() => api.get<PveStageDto[]>('/api/pve/stages'), []);
  const { busy, run } = useAction();
  const start = async (s: PveStageDto) => {
    const b = await run(() => api.post<BattleDto>('/api/battles/pve', { stageId: s.id }, { idem: newIdempotencyKey() }));
    if (b) nav(`/battle/${b.id}`);
  };
  if (!data) return <Loading error={error} onRetry={reload} />;
  const chapters = [...new Set(data.map((s) => s.chapter))];
  return (
    <div>
      {chapters.map((c) => (
        <section key={c}>
          <h3>{t('duel.pve_chapter', c)}</h3>
          <ul className="list">
            {data.filter((s) => s.chapter === c).map((s) => (
              <li key={s.id} className={`list-row stage ${s.unlocked ? '' : 'locked'}`}>
                <div className="enemy-strip">{s.enemyForms.slice(0, 5).map((f, i) => <img key={i} src={petArt(f, true)} alt="" width={36} height={36} loading="lazy" />)}</div>
                <div className="grow">
                  <b>{t('stage.' + s.id)}</b> <span className="muted small">{'★'.repeat(Math.min(5, s.difficulty))}</span>
                  <div className="muted small">{t('duel.stage_info', s.clearCount, s.rewardedClearsLeftToday)}</div>
                </div>
                <button className="btn primary" disabled={!s.unlocked || busy} onClick={() => start(s)} data-testid={`fight-${s.id}`}>{s.unlocked ? t('duel.fight') : t('duel.locked')}</button>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

interface RankedInfo { season: { id: number; name?: string; endsAt?: string } | null; rating: number; wins: number; losses: number; streak: number; rank?: number | null }
interface Board { entries: { rank: number; uid: number; nickname: string; rating: number; wins: number; losses: number }[] }

function RankedTab() {
  const { t } = useI18n();
  const nav = useNavigate();
  const info = useLoad(() => api.get<RankedInfo>('/api/ranked'), []);
  const board = useLoad(() => api.get<Board>('/api/ranked/leaderboard'), []);
  const { busy, run } = useAction();
  const find = async () => {
    const b = await run(() => api.post<BattleDto>('/api/ranked/match', {}, { idem: newIdempotencyKey() }));
    if (b) nav(`/battle/${b.id}`);
  };
  return (
    <div>
      {!info.data ? <Loading error={info.error} /> : (
        <div className="panel">
          <h3>{info.data.season?.name ?? t('battle.ranked')}</h3>
          <p>{t('ranked.rating', info.data.rating)} {info.data.rank ? `· #${info.data.rank}` : ''}</p>
          <p className="muted">{t('ranked.record', info.data.wins, info.data.losses, info.data.streak)}</p>
          <button className="btn primary big" disabled={busy} onClick={find}>{t('ranked.find_match')}</button>
          <p className="muted small">{t('ranked.fair_play')}</p>
        </div>
      )}
      <h3>{t('ranked.leaderboard')}</h3>
      {!board.data ? <Loading error={board.error} /> : board.data.entries.length === 0 ? <Empty>{t('market.no_history')}</Empty> : (
        <ol className="board">
          {board.data.entries.map((e) => <li key={e.uid}><span>#{e.rank}</span><a href={`/player/${e.uid}`} onClick={(ev) => { ev.preventDefault(); nav(`/player/${e.uid}`); }}>{e.nickname}</a><b>{e.rating}</b><span className="muted small">{e.wins}W/{e.losses}L</span></li>)}
        </ol>
      )}
    </div>
  );
}

interface Challenge { id: number; incoming: boolean; otherUid: number; otherNickname: string; expiresAt: string }
interface Friend { uid: number; nickname: string; status: string }

function PvpTab() {
  const { t } = useI18n();
  const nav = useNavigate();
  const toast = useToast();
  const { busy, run } = useAction();
  const [queued, setQueued] = useState(false);
  const ch = useLoad(() => api.get<Challenge[]>('/api/pvp/challenges'), []);
  const friends = useLoad(() => api.get<Friend[]>('/api/friends'), []);
  useRealtimeEvent('notification', () => void ch.reload());
  useRealtimeEvent('pvp_declined', () => { toast(t('web.challenge_declined'), 'info'); void ch.reload(); });

  const queue = async () => {
    const r = await run(() => api.post<{ status: string; battleId?: string }>('/api/pvp/queue'));
    if (!r) return;
    if (r.status === 'matched' && r.battleId) nav(`/pvp/${r.battleId}`);
    else if (r.status === 'queued') setQueued(true);
    else toast(t('web.queue_retry'), 'info');
  };
  const leave = async () => { await run(() => api.del('/api/pvp/queue')); setQueued(false); };
  const decide = async (c: Challenge, d: 'accept' | 'decline' | 'cancel') => {
    const r = await run(() => api.post<{ battleId?: string }>(`/api/pvp/challenges/${c.id}/${d}`));
    if (r?.battleId) nav(`/pvp/${r.battleId}`); else void ch.reload();
  };
  const challenge = async (f: Friend) => { if (await run(() => api.post('/api/pvp/challenges', { uid: f.uid }), t('web.challenge_sent'))) void ch.reload(); };

  return (
    <div>
      <div className="panel center">
        <p>{t('web.live_pvp_info')}</p>
        {queued
          ? <><p className="pulse">{t('web.searching')}</p><button className="btn" disabled={busy} onClick={leave}>{t('web.leave_queue')}</button></>
          : <button className="btn primary big" disabled={busy} onClick={queue} data-testid="pvp-queue">{t('web.quick_match')}</button>}
      </div>
      <h3>{t('web.challenges')}</h3>
      {!ch.data ? <Loading error={ch.error} /> : ch.data.length === 0 ? <Empty>{t('web.no_challenges')}</Empty> : (
        <ul className="list">
          {ch.data.map((c) => (
            <li key={c.id} className="list-row">
              <div className="grow">{c.incoming ? '⬅' : '➡'} <b>{c.otherNickname}</b></div>
              {c.incoming ? <>
                <button className="btn primary" disabled={busy} onClick={() => decide(c, 'accept')}>{t('web.accept')}</button>
                <button className="btn ghost" disabled={busy} onClick={() => decide(c, 'decline')}>{t('web.decline')}</button>
              </> : <button className="btn ghost" disabled={busy} onClick={() => decide(c, 'cancel')}>{t('web.cancel')}</button>}
            </li>
          ))}
        </ul>
      )}
      <h3>{t('web.challenge_friend')}</h3>
      {!friends.data ? <Loading error={friends.error} /> : (
        <ul className="list">
          {friends.data.filter((f) => f.status === 'friends').map((f) => (
            <li key={f.uid} className="list-row"><div className="grow">{f.nickname}</div>
              <button className="btn" disabled={busy} onClick={() => challenge(f)}>⚔️ {t('web.challenge')}</button></li>
          ))}
          {friends.data.filter((f) => f.status === 'friends').length === 0 && <Empty>{t('web.no_friends')}</Empty>}
        </ul>
      )}
    </div>
  );
}

interface HistoryRow { id: string; mode: string; stageId?: string; status: string; won: boolean; round: number; createdAt: string }

function HistoryTab() {
  const { t, lang } = useI18n();
  const nav = useNavigate();
  const { data, error } = useLoad(() => api.get<HistoryRow[]>('/api/battles/history'), []);
  if (!data) return <Loading error={error} />;
  if (data.length === 0) return <Empty>{t('market.no_history')}</Empty>;
  return (
    <ul className="list">
      {data.map((b) => (
        <li key={b.id} className="list-row" onClick={() => b.status === 'active' && nav(b.mode.startsWith('live_') ? `/pvp/${b.id}` : `/battle/${b.id}`)}>
          <span className={`tag ${b.status === 'active' ? '' : b.won ? 'win' : 'lose'}`}>{b.status === 'active' ? '▶' : b.won ? t('duel.victory') : t('duel.defeat')}</span>
          <div className="grow">{b.mode}{b.stageId ? ` · ${t('stage.' + b.stageId)}` : ''}<div className="muted small">{t('duel.round', b.round)} · {timeAgo(b.createdAt, lang)}</div></div>
        </li>
      ))}
    </ul>
  );
}
