import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { FeedPostView } from '../api/types';
import { useI18n } from '../i18n';
import { useCatalog } from '../state/catalog';
import { CardView } from './CardView';
import { Modal, timeAgo, useAction, useToast } from './ui';

export function PostCard({ post, onChange, onDeleted }: { post: FeedPostView; onChange: (p: FeedPostView) => void; onDeleted?: () => void }) {
  const { t, lang } = useI18n();
  const nav = useNavigate();
  const cat = useCatalog();
  const { busy, run } = useAction();
  const [report, setReport] = useState(false);
  let extra: Record<string, unknown> = {};
  try { extra = JSON.parse(post.dataJson || '{}'); } catch { /* ignore */ }
  const like = async () => {
    const liked = !post.likedByMe;
    onChange({ ...post, likedByMe: liked, likes: post.likes + (liked ? 1 : -1) }); // optimistic; reverted on error
    const r = await run(() => (liked ? api.post(`/api/feed/posts/${post.id}/like`) : api.del(`/api/feed/posts/${post.id}/like`)));
    if (r === undefined) onChange(post);
  };
  const save = async () => {
    const saved = !post.savedByMe;
    const r = await run(() => (saved ? api.post(`/api/feed/posts/${post.id}/save`) : api.del(`/api/feed/posts/${post.id}/save`)), saved ? t('feed.saved') : undefined);
    if (r !== undefined) onChange({ ...post, savedByMe: saved });
  };
  const del = async () => { if (await run(() => api.del(`/api/feed/posts/${post.id}`))) onDeleted?.(); };
  const sub = post.type === 'evolution' ? `${cat.forms[String(extra.fromFormId)]?.name ?? ''} → ${cat.forms[String(extra.toFormId)]?.name ?? ''}`
    : post.type === 'duel' ? (extra.won ? t('duel.victory') : t('duel.defeat'))
    : post.type === 'album' ? String(extra.albumName ?? '') : '';
  return (
    <article className={`post ${post.featured ? 'featured' : ''}`}>
      <header>
        <button className="link" onClick={() => nav(`/player/${post.authorUid}`)}><b>{post.authorNickname}</b></button>
        <span className="muted small"> · {t('web.post_' + post.type)} · {timeAgo(post.createdAt, lang)}</span>
      </header>
      {post.caption && <p className="caption">{post.caption}</p>}
      {sub && <p className="muted">{sub}</p>}
      {post.cards.length > 0 && (
        <div className="post-cards">
          {post.cards.map((c) => (
            <div key={c.serial} className="post-card" onClick={() => nav(`/passport/${c.serial}`)}>
              <CardView card={c.snapshot} compact />
              {c.ownerChanged && <small className="muted">{t('feed.now_owned_by', c.currentOwnerNickname)}</small>}
            </div>
          ))}
        </div>
      )}
      <footer>
        <button className={`pill ${post.likedByMe ? 'on' : ''}`} disabled={busy} onClick={like} aria-pressed={post.likedByMe}>♥ {post.likes}</button>
        <button className="pill" onClick={() => nav(`/feed/${post.id}`)}>💬 {post.comments}</button>
        <button className={`pill ${post.savedByMe ? 'on' : ''}`} disabled={busy} onClick={save}>🔖</button>
        {post.isMine ? <button className="pill" disabled={busy} onClick={del}>🗑</button> : <button className="pill" onClick={() => setReport(true)}>⚑</button>}
      </footer>
      {report && <ReportModal targetType="post" targetId={post.id} onClose={() => setReport(false)} />}
    </article>
  );
}

export function ReportModal({ targetType, targetId, onClose }: { targetType: string; targetId: number; onClose: () => void }) {
  const t = useI18n().t;
  const toast = useToast();
  const { busy, run } = useAction();
  const [reason, setReason] = useState('spam');
  const [details, setDetails] = useState('');
  const send = async () => {
    const r = await run(() => api.post('/api/reports', { targetType, targetId, reason, details }));
    if (r !== undefined) { toast(t('report.sent'), 'ok'); onClose(); }
  };
  return (
    <Modal title={t('report.title')} onClose={onClose}>
      <select value={reason} onChange={(e) => setReason(e.target.value)}>
        {['spam', 'harassment', 'inappropriate', 'scam', 'cheating', 'other'].map((r) => <option key={r} value={r}>{t('report.reason_' + r)}</option>)}
      </select>
      <textarea maxLength={500} value={details} onChange={(e) => setDetails(e.target.value)} rows={3} />
      <button className="btn primary" disabled={busy} onClick={send}>{t('report.button')}</button>
    </Modal>
  );
}
