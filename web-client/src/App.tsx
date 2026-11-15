import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { useSession } from './state/session';
import { Layout } from './components/Layout';
import { Loading } from './components/ui';
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
const Albums = lazy(() => import('./screens/Albums'));
const Achievements = lazy(() => import('./screens/Achievements'));
const Settings = lazy(() => import('./screens/Settings'));
const Admin = lazy(() => import('./screens/Admin'));

export default function App() {
  const { status } = useSession();
  if (status === 'loading') return <div className="boot"><div className="spinner" /></div>;
  if (status === 'anonymous') return <Routes><Route path="*" element={<AuthScreen />} /></Routes>;
  if (status === 'account') return <Routes><Route path="*" element={<ServersScreen />} /></Routes>;
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
          <Route path="/battle/:id" element={<Battle live={false} />} />
          <Route path="/pvp/:id" element={<Battle live />} />
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
