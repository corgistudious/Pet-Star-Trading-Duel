import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api, newIdempotencyKey } from '../api/client';
import type { CardDto, EvolutionPreviewDto, TrainingInfo } from '../api/types';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { useCatalog } from '../state/catalog';
import { PageHead } from '../components/Layout';
import { CardView } from '../components/CardView';
import { AuctionModal, SellModal } from '../components/TradeModals';
import { Loading, Modal, useAction, useLoad } from '../components/ui';

export default function CardDetail() {
  const { id } = useParams();
  const { t, lang } = useI18n();
  const nav = useNavigate();
  const cat = useCatalog();
  const { applyCurrencies, reloadPlayer } = useSession();
  const { data: card, setData, error, reload } = useLoad(() => api.get<CardDto>(`/api/cards/${id}`), [id]);
  const { busy, run } = useAction();
  const [modal, setModal] = useState<'evolve' | 'train' | 'sell' | 'auction' | null>(null);

  if (!card) return <div className="page"><PageHead title="" back /><Loading error={error} onRetry={reload} /></div>;
  const form = cat.forms[card.formId];
  const passive = card.passiveId ? t('passive.' + card.passiveId) : null;

  const flag = async (kind: 'favorite' | 'lock', value: boolean) => {
    const c = await run(() => api.post<CardDto>(`/api/cards/${card.id}/${kind}`, { value }));
    if (c) setData(c);
  };
  const share = async () => {
    const r = await run(() => api.post<{ id: number }>('/api/feed/posts', { type: 'card', caption: '', cardIds: [card.id] }), t('web.shared'));
    if (r) nav('/feed');
  };

  return (
    <div className="page">
      <PageHead title={card.name} back />
      <div className="detail">
        <div className="detail-card"><CardView card={card} /></div>
        <div className="detail-side">
          <div className="stats">
            <div>{t('card.power')}<b>{card.power}</b></div>
            <div>SPD<b>{card.spd}</b></div>
            <div>CRIT<b>{Math.round(card.crit * 100)}%</b></div>
            <div>{t('card.stage', card.stage)}</div>
            <div>{t('card.exp', card.exp, card.expToNext)}</div>
            <div>{t('card.skill_level', card.skillLevel)}</div>
          </div>
          {passive && <p><b>{t('card.passive')}:</b> {passive}</p>}
          {form?.lore?.[lang] && <p className="lore">{form.lore[lang]}</p>}
          <p className="serial">#{card.serial} · {t('finish.' + card.finish)}</p>
          <div className="actions">
            <button className="btn" disabled={busy} onClick={() => flag('favorite', !card.isFavorite)}>{card.isFavorite ? t('card.unfavorite') : t('card.favorite')}</button>
            <button className="btn" disabled={busy} onClick={() => flag('lock', !card.isLocked)}>{card.isLocked ? t('card.unlock') : t('card.lock')}</button>
            <button className="btn" onClick={() => setModal('evolve')}>{t('card.evolve')}</button>
            <button className="btn" onClick={() => setModal('train')}>{t('card.train')}</button>
            <button className="btn" disabled={card.isLocked || card.isBusy} onClick={() => setModal('sell')}>{t('market.sell')}</button>
            <button className="btn" disabled={card.isLocked || card.isBusy} onClick={() => setModal('auction')}>{t('market.auction')}</button>
            <button className="btn" onClick={() => nav(`/passport/${card.serial}`)}>{t('passport.open')}</button>
            <button className="btn" onClick={share}>{t('passport.share')}</button>
          </div>
        </div>
      </div>
      {modal === 'evolve' && <EvolveModal card={card} onClose={() => setModal(null)} onDone={(c, cur) => { setData(c); applyCurrencies(cur); void reloadPlayer(); setModal(null); }} />}
      {modal === 'train' && <TrainModal card={card} onClose={() => setModal(null)} onDone={(c, cur) => { setData(c); applyCurrencies(cur); void reloadPlayer(); setModal(null); }} />}
      {modal === 'sell' && <SellModal card={card} onClose={() => setModal(null)} onDone={() => { setModal(null); void reload(); }} />}
      {modal === 'auction' && <AuctionModal card={card} onClose={() => setModal(null)} onDone={() => { setModal(null); void reload(); }} />}
    </div>
  );
}

function EvolveModal({ card, onClose, onDone }: { card: CardDto; onClose: () => void; onDone: (c: CardDto, cur: Record<string, number>) => void }) {
  const t = useT2();
  const { data, error } = useLoad(() => api.get<EvolutionPreviewDto>(`/api/cards/${card.id}/evolution-preview`), [card.id]);
  const { busy, run } = useAction();
  const [key] = useState(newIdempotencyKey);
  const evolve = async () => {
    const r = await run(() => api.post<{ card: CardDto; currencies: Record<string, number> }>(`/api/cards/${card.id}/evolve`, {}, { idem: key }));
    if (r) { onDone(r.card, r.currencies); }
  };
  return (
    <Modal title={t('evolution.title')} onClose={onClose} wide>
      {!data ? <Loading error={error} /> : !data.next ? <p>{t('evolution.final_form')}</p> : (
        <>
          <div className="evo-row">
            <div className="evo-card"><CardView card={data.current} compact /></div>
            <span className="evo-arrow">➜</span>
            <div className="evo-card"><CardView card={data.next} compact /></div>
          </div>
          <ul className="reqs">
            <li className={data.current.level >= data.requiredLevel ? 'ok' : 'no'}>{t('evolution.req_level', data.requiredLevel)}</li>
            <li className={data.essenceOwned >= data.essenceRequired ? 'ok' : 'no'}>{t('evolution.req_essence', t('item.' + data.essenceItemId), data.essenceOwned, data.essenceRequired)}</li>
            <li className={data.beansOwned >= data.beansRequired ? 'ok' : 'no'}>{t('evolution.req_beans', data.beansOwned, data.beansRequired)}</li>
          </ul>
          {data.check !== 'ok' && data.check !== 'Ok' && <p className="warn">{t('error.evolution_' + data.check.toLowerCase())}</p>}
          <button className="btn primary big" disabled={busy || (data.check !== 'ok' && data.check !== 'Ok')} onClick={evolve}>{t('evolution.confirm')}</button>
        </>
      )}
    </Modal>
  );
}

function TrainModal({ card, onClose, onDone }: { card: CardDto; onClose: () => void; onDone: (c: CardDto, cur: Record<string, number>) => void }) {
  const t = useT2();
  const { data, error } = useLoad(() => api.get<TrainingInfo>(`/api/cards/${card.id}/training`), [card.id]);
  const { busy, run } = useAction();
  const [key] = useState(newIdempotencyKey);
  const train = async () => {
    const r = await run(() => api.post<{ card: CardDto; currencies: Record<string, number> }>(`/api/cards/${card.id}/train`, {}, { idem: key }), t('training.done'));
    if (r) onDone(r.card, r.currencies);
  };
  return (
    <Modal title={t('training.title')} onClose={onClose}>
      {!data ? <Loading error={error} /> : !data.next ? <p>{t('training.max')}</p> : (
        <>
          <p className="pre">{t('training.body', data.skillLevel + 1, data.next.minCardLevel, data.next.scrolls, data.scrollsOwned, data.next.beans)}</p>
          <button className="btn primary big" disabled={busy || data.cardLevel < data.next.minCardLevel || data.scrollsOwned < data.next.scrolls || data.beansOwned < data.next.beans} onClick={train}>{t('card.train')}</button>
        </>
      )}
    </Modal>
  );
}

const useT2 = () => useI18n().t;
