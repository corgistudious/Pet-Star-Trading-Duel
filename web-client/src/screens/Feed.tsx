import { useState } from 'react';
import { api, qs } from '../api/client';
import type { CardDto, FeedPostView } from '../api/types';
import { useT } from '../i18n';
import { PageHead } from '../components/Layout';
import { PostCard } from '../components/Post';
import { CardGrid } from '../components/CardGrid';
import { CardView } from '../components/CardView';
import { Empty, Loading, Modal, Tabs, useAction, useLoad } from '../components/ui';

type Tab = 'global' | 'following' | 'friends' | 'featured' | 'mine' | 'saved';

export function useFeed(url: (before?: number) => string, deps: unknown[]) {
  const [items, setItems] = useState<FeedPostView[]>([]);
  const [more, setMore] = useState(true);
  const first = useLoad(async () => {
    const r = await api.get<{ items: FeedPostView[] }>(url());
    setItems(r.items); setMore(r.items.length >= 20);
    return true;
  }, deps);
  const loadMore = async () => {
    const last = items[items.length - 1];
    if (!last) return;
    const r = await api.get<{ items: FeedPostView[] }>(url(last.id));
    setItems((x) => [...x, ...r.items]); setMore(r.items.length >= 20);
  };
  const update = (p: FeedPostView) => setItems((x) => x.map((y) => (y.id === p.id ? p : y)));
  const remove = (id: number) => setItems((x) => x.filter((y) => y.id !== id));
  return { items, more, loadMore, update, remove, first };
}

export default function Feed() {
  const t = useT();
  const [tab, setTab] = useState<Tab>('global');
  const [composing, setComposing] = useState(false);
  const feed = useFeed((before) => '/api/feed' + qs({ tab, before }), [tab]);
  return (
    <div className="page">
      <PageHead title={t('feed.title')} right={<button className="btn primary" onClick={() => setComposing(true)}>{t('feed.create')}</button>} />
      <Tabs value={tab} onChange={setTab} items={[['global', t('feed.global')], ['following', t('feed.following')], ['friends', t('feed.friends')], ['featured', t('feed.featured')], ['mine', t('feed.mine')], ['saved', t('feed.saved_btn')]]} />
      <FeedList feed={feed} />
      {composing && <Composer endpoint="/api/feed/posts" onClose={() => setComposing(false)} onDone={() => { setComposing(false); void feed.first.reload(); }} />}
    </div>
  );
}

export function FeedList({ feed }: { feed: ReturnType<typeof useFeed> }) {
  const t = useT();
  if (feed.first.loading && feed.items.length === 0) return <Loading error={feed.first.error} onRetry={feed.first.reload} />;
  if (feed.items.length === 0) return <Empty>{t('feed.empty')}</Empty>;
  return (
    <div className="feed">
      {feed.items.map((p) => <PostCard key={p.id} post={p} onChange={feed.update} onDeleted={() => feed.remove(p.id)} />)}
      {feed.more && <button className="btn ghost" onClick={feed.loadMore}>{t('web.load_more')}</button>}
    </div>
  );
}

export function Composer({ endpoint, onClose, onDone }: { endpoint: string; onClose: () => void; onDone: () => void }) {
  const t = useT();
  const { busy, run } = useAction();
  const [caption, setCaption] = useState('');
  const [cards, setCards] = useState<CardDto[]>([]);
  const [picking, setPicking] = useState(false);
  const publish = async () => {
    const body = cards.length ? { type: 'card', caption, cardIds: cards.map((c) => c.id) } : { type: 'text', caption };
    if (await run(() => api.post(endpoint, body), t('feed.published'))) onDone();
  };
  return (
    <Modal title={t('feed.create')} onClose={onClose} wide>
      <textarea rows={3} maxLength={280} placeholder={t('feed.caption_hint')} value={caption} onChange={(e) => setCaption(e.target.value)} />
      <p className="muted small">{t('feed.rules')}</p>
      <div className="post-cards">{cards.map((c) => <div key={c.id} className="post-card" onClick={() => setCards(cards.filter((x) => x.id !== c.id))}><CardView card={c} compact /></div>)}</div>
      {picking ? (
        <CardGrid selectedIds={cards.map((c) => c.id)} pageSize={30}
          onPick={(c) => setCards((x) => (x.some((y) => y.id === c.id) ? x.filter((y) => y.id !== c.id) : x.length < 5 ? [...x, c] : x))} />
      ) : <button className="btn ghost" onClick={() => setPicking(true)}>{t('feed.pick_cards', cards.length)}</button>}
      <div className="row end gap">
        <button className="btn primary" disabled={busy || (!caption.trim() && cards.length === 0)} onClick={publish}>{t('feed.publish')}</button>
      </div>
    </Modal>
  );
}
