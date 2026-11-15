import { useState } from 'react';
import { api } from '../api/client';
import type { CardDto, CardPageDto, DeckDto } from '../api/types';
import { useT } from '../i18n';
import { PageHead } from '../components/Layout';
import { CardView } from '../components/CardView';
import { CardGrid } from '../components/CardGrid';
import { Confirm, Empty, Loading, Modal, useAction, useLoad } from '../components/ui';
import { autoFill, toSlots, validateDeck } from '../logic/deck';

export default function Decks() {
  const t = useT();
  const decks = useLoad(() => api.get<DeckDto[]>('/api/decks'), []);
  const [editing, setEditing] = useState<DeckDto | 'new' | null>(null);
  const { busy, run } = useAction();
  const [del, setDel] = useState<DeckDto | null>(null);

  if (editing) return <DeckEditor deck={editing === 'new' ? null : editing} onClose={() => { setEditing(null); void decks.reload(); }} />;
  return (
    <div className="page">
      <PageHead title={t('web.decks')} back="/" right={<button className="btn primary" onClick={() => setEditing('new')}>{t('web.new_deck')}</button>} />
      {!decks.data ? <Loading error={decks.error} onRetry={decks.reload} /> : decks.data.length === 0 ? <Empty>{t('web.no_decks')}</Empty> : (
        <ul className="list">
          {decks.data.map((d) => (
            <li key={d.id} className="list-row">
              <div className="grow"><b>{d.name}</b> {d.isDefault && <span className="tag">{t('web.default')}</span>}<div className="muted small">{t('card.power')} {d.totalPower}</div></div>
              {!d.isDefault && <button className="btn ghost" disabled={busy} onClick={() => run(() => api.post(`/api/decks/${d.id}/default`)).then(() => decks.reload())}>{t('web.make_default')}</button>}
              <button className="btn" onClick={() => setEditing(d)}>{t('web.edit')}</button>
              <button className="btn ghost danger" onClick={() => setDel(d)}>✕</button>
            </li>
          ))}
        </ul>
      )}
      {del && <Confirm text={t('web.delete_deck', del.name)} busy={busy} onNo={() => setDel(null)}
        onYes={() => run(() => api.del(`/api/decks/${del.id}`)).then(() => { setDel(null); void decks.reload(); })} />}
    </div>
  );
}

function DeckEditor({ deck, onClose }: { deck: DeckDto | null; onClose: () => void }) {
  const t = useT();
  const { busy, run } = useAction();
  const [name, setName] = useState(deck?.name ?? 'Deck');
  const [slots, setSlots] = useState<(CardDto | null)[]>([null, null, null, null, null]);
  const [picking, setPicking] = useState<number | null>(null);
  // load the cards referenced by the deck (the deck API only returns ids)
  const all = useLoad(async () => {
    const page = await api.get<CardPageDto>('/api/cards?pageSize=200&sort=power');
    if (deck) {
      const byId = new Map(page.items.map((c) => [c.id, c]));
      const s: (CardDto | null)[] = [null, null, null, null, null];
      for (const sl of deck.slots) s[sl.position] = byId.get(sl.cardId) ?? (await api.get<CardDto>(`/api/cards/${sl.cardId}`).catch(() => null));
      setSlots(s);
    }
    return page.items;
  }, [deck?.id]);

  const err = validateDeck(slots);
  const save = async () => {
    const body = { name, slots: toSlots(slots), isDefault: deck?.isDefault ?? false };
    const r = await run(() => (deck ? api.put(`/api/decks/${deck.id}`, body) : api.post('/api/decks', body)), t('web.saved'));
    if (r !== undefined) onClose();
  };
  const slot = (i: number) => (
    <button key={i} className={`deck-slot ${slots[i] ? 'filled' : ''}`} onClick={() => setPicking(i)} aria-label={`slot ${i + 1}`}>
      {slots[i] ? <CardView card={slots[i]!} compact /> : <span>+</span>}
    </button>
  );
  return (
    <div className="page">
      <PageHead title={deck ? t('web.edit') : t('web.new_deck')} right={<button className="btn ghost" onClick={onClose}>{t('web.cancel')}</button>} />
      <label className="inline">{t('web.deck_name')}<input value={name} maxLength={32} onChange={(e) => setName(e.target.value)} /></label>
      <div className="deck-board">
        <div className="row-label">{t('web.front_row')}</div>
        <div className="deck-row three">{[0, 1, 2].map(slot)}</div>
        <div className="row-label">{t('web.back_row')}</div>
        <div className="deck-row two">{[3, 4].map(slot)}</div>
      </div>
      {err && <p className="warn">{t('error.' + err)}</p>}
      <div className="row gap center">
        <button className="btn" disabled={!all.data} onClick={() => all.data && setSlots(autoFill(all.data))}>{t('web.auto_fill')}</button>
        <button className="btn primary" disabled={busy || !!err} onClick={save}>{t('web.save')}</button>
      </div>
      {picking !== null && (
        <Modal title={t('web.pick_card')} onClose={() => setPicking(null)} wide>
          {slots[picking] && <button className="btn ghost" onClick={() => { const s = [...slots]; s[picking] = null; setSlots(s); setPicking(null); }}>{t('web.remove')}</button>}
          <CardGrid filterBusy exclude={(c) => slots.some((x, i) => x?.id === c.id && i !== picking)}
            onPick={(c) => { const s = [...slots]; s[picking] = c; setSlots(s); setPicking(null); }} />
        </Modal>
      )}
    </div>
  );
}
