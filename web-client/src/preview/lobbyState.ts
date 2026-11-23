// Preview-only player state for the new lobby (account, Trainer, companion, mail, friends, gifts, season, missions,
// mini games, teams and battle history). Stored in sessionStorage next to the simulated collection; everything is
// marked as Preview data. The real game keeps all of this on the server (trainer_profiles, team_lineups, mail, ...).
import type { ReplayData } from './TeamBattle';

export interface Mail { id: number; kind: 'system' | 'friend' | 'battle' | 'trade' | 'gift'; from: string; title: { vi: string; en: string }; body: { vi: string; en: string }; read: boolean; hidden: boolean; gift?: { beans?: number; stars?: number }; claimed?: boolean; at: string }
export interface Friend { name: string; uid: number; online: boolean; trainer: 'trainer_boy' | 'trainer_girl'; level: number }
export interface LobbyState {
  version: 1;
  account: { name: string; uid: number; createdAt: string } | null;
  loggedIn: boolean;
  trainer: 'trainer_boy' | 'trainer_girl' | null;
  outfit: string | null;
  companionId: number | null;
  frame: 'gold' | 'star' | 'leaf';
  bio: string;
  privacy: { showOnline: boolean; allowInvites: boolean };
  settings: { sound: boolean; music: boolean; effects: 'high' | 'low' };
  level: number; exp: number;
  mail: Mail[];
  friends: Friend[]; requests: Friend[]; blocked: string[]; reported: string[];
  starterClaimed: boolean;
  checkin: { last: string | null; streak: number };
  giftcodes: string[];
  season: { xp: number; claimed: number[] };
  missions: { claimed: string[]; counters: Record<string, number> };
  mini: { best: Record<string, number>; plays: Record<string, number> };
  team: number[];
  battles: ReplayData[];
  pveCleared: string[];
  record: { pveWins: number; pveLosses: number; pvpWins: number; pvpLosses: number };
  seen: Record<string, boolean>;
}

export const LOBBY_KEY = 'petstar.preview.lobby.v1';
export const EXP_PER_LEVEL = (lvl: number) => 100 + lvl * 50;
export const SEASON_TIERS = [
  { xp: 100, reward: { beans: 300 } }, { xp: 250, reward: { stars: 5 } }, { xp: 450, reward: { beans: 800 } },
  { xp: 700, reward: { stars: 10 } }, { xp: 1000, reward: { beans: 1500 } }, { xp: 1400, reward: { stars: 20 } },
];
export const GIFTCODES: Record<string, { reward: { beans?: number; stars?: number }; expires: string; limit: number }> = {
  PETSTAR2026: { reward: { beans: 500 }, expires: '2026-12-31', limit: 1 },
  NEWTRAINER: { reward: { stars: 5 }, expires: '2027-03-31', limit: 1 },
};
export const MISSIONS = [
  { id: 'win_battle', goal: 1, reward: { beans: 200 }, vi: 'Thắng 1 trận 3v3', en: 'Win 1 Battle 3v3' },
  { id: 'open_pack', goal: 1, reward: { beans: 150 }, vi: 'Mở 1 gói thẻ', en: 'Open 1 booster pack' },
  { id: 'mini_game', goal: 2, reward: { stars: 2 }, vi: 'Chơi 2 Mini Game', en: 'Play 2 Mini Games' },
  { id: 'evolve', goal: 1, reward: { stars: 3 }, vi: 'Tiến hóa 1 Pet', en: 'Evolve 1 Pet' },
];
export const EVENT = { id: 'ev_starlight_2026', vi: 'Lễ hội Sao Băng', en: 'Starlight Festival', ends: '2026-12-31T23:59:59Z',
  tasks: [{ id: 'ev_win3', vi: 'Thắng 3 trận', en: 'Win 3 battles', goal: 3, reward: { stars: 8 } }, { id: 'ev_mini5', vi: 'Chơi 5 Mini Game', en: 'Play 5 Mini Games', goal: 5, reward: { beans: 600 } }] };

const NPC_NAMES = ['Mây Hồng', 'Tiểu Long', 'StarRunner', 'Bông Gòn', 'Kai', 'Mochi'];

export function freshLobby(): LobbyState {
  const now = new Date().toISOString();
  return {
    version: 1, account: null, loggedIn: false, trainer: null, outfit: null, companionId: null, frame: 'gold', bio: '',
    privacy: { showOnline: true, allowInvites: true }, settings: { sound: true, music: true, effects: 'high' },
    level: 1, exp: 0,
    mail: [
      { id: 1, kind: 'system', from: 'Pet Star', title: { vi: 'Chào mừng Trainer mới!', en: 'Welcome, new Trainer!' }, body: { vi: 'Cảm ơn bạn đã thử bản Preview. Đây là quà chào mừng (dữ liệu mô phỏng).', en: 'Thanks for trying the Preview. Here is a welcome gift (simulated).' }, read: false, hidden: false, gift: { beans: 300 }, at: now },
      { id: 2, kind: 'battle', from: 'Tiểu Long', title: { vi: 'Lời mời đấu 3v3', en: '3v3 battle invite' }, body: { vi: 'Đấu thử một trận không? (NPC mô phỏng — PvP thật cần máy chủ)', en: 'Fancy a match? (simulated NPC — real PvP needs the server)' }, read: false, hidden: false, at: now },
      { id: 3, kind: 'trade', from: 'Pet Star', title: { vi: 'Thông báo giao dịch', en: 'Trade notice' }, body: { vi: 'Giao dịch thẻ trực tiếp sẽ mở sau khi qua kiểm thử bảo mật. Chợ giao dịch đã có.', en: 'Direct card trades open after security testing. The Marketplace is available.' }, read: false, hidden: false, at: now },
    ],
    friends: [
      { name: NPC_NAMES[0], uid: 2001001, online: true, trainer: 'trainer_girl', level: 12 },
      { name: NPC_NAMES[2], uid: 2001003, online: false, trainer: 'trainer_boy', level: 25 },
    ],
    requests: [{ name: NPC_NAMES[3], uid: 2001004, online: true, trainer: 'trainer_girl', level: 7 }],
    blocked: [], reported: [],
    starterClaimed: false, checkin: { last: null, streak: 0 }, giftcodes: [], season: { xp: 0, claimed: [] },
    missions: { claimed: [], counters: {} }, mini: { best: {}, plays: {} }, team: [], battles: [], pveCleared: [],
    record: { pveWins: 0, pveLosses: 0, pvpWins: 0, pvpLosses: 0 }, seen: {},
  };
}

export function loadLobby(): LobbyState {
  try { const j = sessionStorage.getItem(LOBBY_KEY); if (j) return { ...freshLobby(), ...(JSON.parse(j) as LobbyState) }; } catch { /* storage blocked */ }
  return freshLobby();
}
export function saveLobby(l: LobbyState) { try { sessionStorage.setItem(LOBBY_KEY, JSON.stringify(l)); } catch { /* ignore */ } }
export { NPC_NAMES };
