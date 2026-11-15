import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { FeedComment, FeedPostView } from '../api/types';
import { useI18n } from '../i18n';
import { PageHead } from '../components/Layout';
import { PostCard, ReportModal } from '../components/Post';
import { Loading, timeAgo, useAction, useLoad } from '../components/ui';

export default function PostDetail() {
  const { id } = useParams();
  const { t, lang } = useI18n();
  const nav = useNavigate();
  const { data, setData, error, reload } = useLoad(() => api.get<{ post: FeedPostView; comments: FeedComment[] }>(`/api/feed/posts/${id}`), [id]);
  const { busy, run } = useAction();
  const [text, setText] = useState('');
  const [report, setReport] = useState<number | null>(null);
  if (!data) return <div className="page"><PageHead title={t('feed.post')} back /><Loading error={error} onRetry={reload} /></div>;
  const send = async () => { if (await run(() => api.post(`/api/feed/posts/${id}/comments`, { text }))) { setText(''); void reload(); } };
  return (
    <div className="page">
      <PageHead title={t('feed.post')} back />
      <PostCard post={data.post} onChange={(p) => setData({ ...data, post: p })} onDeleted={() => nav('/feed')} />
      <h4>{t('feed.comments_title')}</h4>
      <ul className="comments">
        {data.comments.map((c) => (
          <li key={c.id}>
            <b>{c.authorNickname}</b> {c.text} <span className="muted small">{timeAgo(c.createdAt, lang)}</span>
            {c.mine ? <button className="link small" onClick={() => run(() => api.del(`/api/feed/comments/${c.id}`)).then(() => reload())}>✕</button>
              : <button className="link small" onClick={() => setReport(c.id)}>⚑</button>}
          </li>
        ))}
      </ul>
      <form className="row gap" onSubmit={(e) => { e.preventDefault(); void send(); }}>
        <input className="grow" maxLength={200} placeholder={t('feed.comment_hint')} value={text} onChange={(e) => setText(e.target.value)} />
        <button className="btn primary" disabled={busy || !text.trim()}>{t('feed.send')}</button>
      </form>
      {report && <ReportModal targetType="comment" targetId={report} onClose={() => setReport(null)} />}
    </div>
  );
}
