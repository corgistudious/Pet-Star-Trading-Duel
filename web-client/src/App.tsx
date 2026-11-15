import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { useSession } from './state/session';
import { useT } from './i18n';
import { Layout } from './components/Layout';
import { Loading } from './components/ui';
import { BattleFrame, PortraitFrame } from './components/Frames';
import AuthScreen from './screens/Auth';
import ServersScreen from './screens/Servers';
import LobbyScreen from './screens/Lobby';

// Secondary screens are split into separate chunks so the first paint on a phone stays small.
const Shop = lazy(() => import('./screens/Shop'));
const PackOpening = lazy(() => import('./screens/PackOpening'));
const PetHome = lazy(() => import('./screens/PetHome'));
const CardDetail = lazy(() => import('./screens/CardDetail'));
const Passport = lazy(() => import('./screens/Passport'));
const Codex = lazy(() => import('./screens/Codex'));
const Decks = lazy(() => import('./screens/Decks'));
const DuelHub = lazy(() => import('./screens/DuelHub'));
const Battle = lazy(() => import('./screens/Battle'));
const Market = lazy(() => import('./screens/Market'));
const ListingDetail = lazy(() => import('./screens/ListingDetail'));
const Auctions = lazy(() => import('./screens/Auctions'));
const AuctionDetail = lazy(() => import('./screens/AuctionDetail'));
const Feed = lazy(() => import('./screens/Feed'));
const PostDetail = lazy(() => import('./screens/PostDetail'));
const Guild = lazy(() => import('./screens/Guild'));
const Chat = lazy(() => import('./screens/Chat'));
const Friends = lazy(() => import('./screens/Friends'));
const PlayerProfile = lazy(() => import('./screens/PlayerProfile'));
const Notifications = lazy(() => import('./screens/Notifications'));
const Mailbox = lazy(() => import('./screens/Mailbox'));
const Rewards = lazy(() => import('./screens/Rewards'));
const Missions = lazy(() => import('./screens/Missions'));
const Albums = lazy(() => import('./screens/Albums'));
const Achievements = lazy(() => import('./screens/Achievements'));
const Settings = lazy(() => import('./screens/Settings'));
const Admin = lazy(() => import('./screens/Admin'));

export default function App() {
  const { status } = useSession();
  if (status === 'loading') return <PortraitFrame><div className="boot"><div className="spinner" /></div></PortraitFrame>;
  if (status === 'unreachable') return <PortraitFrame><Unreachable /></PortraitFrame>;
  if (status === 'anonymous') return <PortraitFrame><Routes><Route path="*" element={<AuthScreen />} /></Routes></PortraitFrame>;
  if (status === 'account') return <PortraitFrame><Routes><Route path="*" element={<ServersScreen />} /></Routes></PortraitFrame>;
  // Duels are the only Landscape 16:9 screens; everything else lives in the Portrait 9:16 frame.
  return (
    <Routes>
      <Route path="/battle/:id" element={<BattleFrame><Suspense fallback={<Loading />}><Battle live={false} /></Suspense></BattleFrame>} />
      <Route path="/pvp/:id" element={<BattleFrame><Suspense fallback={<Loading />}><Battle live /></Suspense></BattleFrame>} />
      <Route path="*" element={<PortraitFrame><PortraitRoutes /></PortraitFrame>} />
    </Routes>
  );
}

function PortraitRoutes() {
  return (
    <Layout>
      <Suspense fallback={<Loading />}>
        <Routes>
          <Route path="/" element={<LobbyScreen />} />
          <Route path="/shop" element={<Shop />} />
          <Route path="/open" element={<PackOpening />} />
          <Route path="/open/:openingId" element={<PackOpening />} />
          <Route path="/home" element={<PetHome />} />
          <Route path="/card/:id" element={<CardDetail />} />
          <Route path="/passport/:serial" element={<Passport />} />
          <Route path="/codex" element={<Codex />} />
          <Route path="/decks" element={<Decks />} />
          <Route path="/duel" element={<DuelHub />} />
          <Route path="/market" element={<Market />} />
          <Route path="/market/:id" element={<ListingDetail />} />
          <Route path="/auctions" element={<Auctions />} />
          <Route path="/auctions/:id" element={<AuctionDetail />} />
          <Route path="/feed" element={<Feed />} />
          <Route path="/feed/:id" element={<PostDetail />} />
          <Route path="/guild" element={<Guild />} />
          <Route path="/chat" element={<Chat />} />
          <Route path="/friends" element={<Friends />} />
          <Route path="/player/:uid" element={<PlayerProfile />} />
          <Route path="/notifications" element={<Notifications />} />
          <Route path="/mailbox" element={<Mailbox />} />
          <Route path="/rewards" element={<Rewards />} />
          <Route path="/missions" element={<Missions />} />
          <Route path="/albums" element={<Albums />} />
          <Route path="/achievements" element={<Achievements />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="/admin/*" element={<Admin />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </Suspense>
    </Layout>
  );
}

function Unreachable() {
  const { retry } = useSession();
  const t = useT();
  return (
    <div className="boot">
      <div><p>{t('web.server_unreachable')}</p><button className="btn primary" onClick={retry}>{t('web.retry')}</button></div>
    </div>
  );
}
