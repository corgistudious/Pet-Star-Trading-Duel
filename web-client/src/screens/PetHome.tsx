import { useNavigate } from 'react-router-dom';
import { useT } from '../i18n';
import { useSession } from '../state/session';
import { PageHead } from '../components/Layout';
import { CardGrid } from '../components/CardGrid';

export default function PetHome() {
  const t = useT();
  const nav = useNavigate();
  const { player } = useSession();
  return (
    <div className="page">
      <PageHead title={t('home.title')} right={<span className="muted">{t('home.count', player?.cardCount ?? 0)}</span>} />
      <CardGrid onPick={(c) => nav(`/card/${c.id}`)} />
    </div>
  );
}
