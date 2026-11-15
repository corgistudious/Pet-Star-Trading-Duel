import { useState } from 'react';
import { api } from '../api/client';
import type { CardDto } from '../api/types';
import { useT } from '../i18n';
import { PageHead } from '../components/Layout';
import { CardGrid } from '../components/CardGrid';
import { CardView } from '../components/CardView';
import { Confirm, Loading, Modal, useAction, useLoad } from '../components/ui';

interface Albums {
  codexCompletion: number;
  system: { id: string; total: number; discovered: number; complete: boolean }[];
  custom: { id: number; name: string; createdAt: string; cardIds: number[] }[];
}

export default function AlbumsScreen() {
  const t = useT();
  const { data, error, reload } = useLoad(() => api.get<Albums>('/api/collections'), []);
  const { busy, run } = useAction();
  const [edit, setEdit] = useState<{ id?: number; name: string; cardIds: number[] } | null>(null);
  const [view, setView] = useState<{ name: string; cardIds: number[] } | null>(null);
  const [del, setDel] = useState<number | null>(null);
  if (!data) return <div className="page"><PageHead title={t('albums.title')} back="/" /><Loading error={error} onRetry={reload} /></div>;
  const save = async () => {
    if (!edit) return;
    const body = { name: edit.name, cardIds: edit.cardIds };
    if (await run(() => (edit.id ? api.put(`/api/collections/${edit.id}`, body) : api.post('/api/collections', body)), t('web.saved'))) { setEdit(null); void reload(); }
  };
  return (
    <div className="page">
      <PageHead title={t('albums.title')} back="/" />
      <p>{t('albums.codex_completion', data.codexCompletion)}</p>
      <div className="album-grid">
        {data.system.map((a) => (
          <div key={a.id} className={`album ${a.complete ? 'complete' : ''}`}>
            <b>{t('album.' + a.id)}</b>
            {a.total === 0 ? <span className="muted small">{t('albums.coming_soon')}</span> : (
              <><div className="progress"><div style={{ width: `${(100 * a.discovered) / a.total}%` }} /></div><span className="muted small">{a.discovered}/{a.total}</span></>
            )}
          </div>
        ))}
      </div>
      <h3>{t('albums.custom')} <button className="btn primary small" onClick={() => setEdit({ name: '', cardIds: [] })}>{t('albums.create')}</button></h3>
      <ul className="list">
        {data.custom.map((c) => (
          <li key={c.id} className="list-row">
            <div className="grow" onClick={() => setView(c)}><b>{c.name}</b> <span className="muted small">{c.cardIds.length} {t('albums.cards')}</span></div>
            <button className="btn ghost" onClick={() => setEdit({ id: c.id, name: c.name, cardIds: c.cardIds })}>{t('web.edit')}</button>
            <button className="btn ghost" onClick={() => api.post('/api/feed/posts', { type: 'album', caption: '', refId: String(c.id) }).then(() => reload())}>{t('feed.share')}</button>
            <button className="btn ghost danger" onClick={() => setDel(c.id)}>✕</button>
          </li>
        ))}
      </ul>
      {edit && (
        <Modal title={t('albums.create')} onClose={() => setEdit(null)} wide>
          <input placeholder={t('albums.name_hint')} maxLength={32} value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} />
          <p className="muted small">{edit.cardIds.length} {t('albums.cards')}</p>
          <CardGrid selectedIds={edit.cardIds} pageSize={30} onPick={(c) => setEdit({ ...edit, cardIds: edit.cardIds.includes(c.id) ? edit.cardIds.filter((x) => x !== c.id) : [...edit.cardIds, c.id] })} />
          <button className="btn primary" disabled={busy || !edit.name.trim()} onClick={save}>{t('web.save')}</button>
        </Modal>
      )}
      {view && <AlbumView album={view} onClose={() => setView(null)} />}
      {del && <Confirm text={t('web.delete') + '?'} busy={busy} onNo={() => setDel(null)} onYes={() => run(() => api.del(`/api/collections/${del}`)).then(() => { setDel(null); void reload(); })} />}
    </div>
  );
}

function AlbumView({ album, onClose }: { album: { name: string; cardIds: number[] }; onClose: () => void }) {
  const { data } = useLoad(() => Promise.all(album.cardIds.map((id) => api.get<CardDto>(`/api/cards/${id}`).catch(() => null))), [album]);
  return (
    <Modal title={album.name} onClose={onClose} wide>
      {!data ? <Loading /> : <div className="card-grid">{data.filter((c): c is CardDto => !!c).map((c) => <CardView key={c.id} card={c} compact />)}</div>}
    </Modal>
  );
}
