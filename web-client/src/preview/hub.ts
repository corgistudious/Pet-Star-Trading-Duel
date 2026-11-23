// Shared context for the Social Hub / Guild / Market / Chat / Shop / Profile screens of the Preview.
import type { CosData } from './Cosmetics';
import type { LobbyState } from './lobbyState';
import type { SimState } from './sim';
import { WorldError, type CardSnap, type World } from './world';

export type PFn = (vi: string, en: string) => string;
export interface Hub {
  me: number; w: World; s: SimState; setS: (f: (x: SimState) => SimState) => void; l: LobbyState; upd: (f: (l: LobbyState) => LobbyState) => void;
  P: PFn; lang: string; say: (m: string) => void; d: CosData | null; openProfile: (uid: number) => void;
  /** Guild Boss: runs the shared 3v3 engine (your team vs the boss formation, AI both sides) and returns damage dealt. */
  bossFight: (formId: string, guildLevel: number) => number | null;
  /** Guild War: a playable 3v3 vs the opponent member's published team (AI controlled). */
  startWar: (team: CardSnap[], name: string) => void;
}

const CODES: Record<string, [string, string]> = {
  text_required: ['Hãy nhập nội dung', 'Please write something'], text_too_long: ['Nội dung quá dài', 'Text is too long'],
  text_inappropriate: ['Nội dung không phù hợp', 'Inappropriate content'], text_links_not_allowed: ['Không được chèn liên kết', 'Links are not allowed'],
  text_spam: ['Nội dung giống spam', 'Looks like spam'], text_invalid: ['Nội dung không hợp lệ', 'Invalid text'],
  posting_too_fast: ['Bạn đăng bài quá nhanh (tối đa 10 bài/giờ)', 'Posting too fast (max 10 posts/hour)'], duplicate_post: ['Bài đăng trùng lặp', 'Duplicate post'],
  commenting_too_fast: ['Bình luận quá nhanh', 'Commenting too fast'], duplicate_comment: ['Bình luận trùng lặp', 'Duplicate comment'],
  not_in_guild: ['Bạn chưa có Guild', 'You are not in a guild'], post_not_found: ['Không tìm thấy bài (hoặc bạn không có quyền xem)', 'Post not found (or not visible to you)'],
  comment_not_found: ['Không tìm thấy bình luận', 'Comment not found'], reply_depth_limit: ['Chỉ hỗ trợ một cấp trả lời', 'Only one reply level'],
  user_blocked: ['Đã chặn người chơi này', 'This player is blocked'], post_not_shareable: ['Bài riêng tư/bạn bè không thể chia sẻ', 'Private or friends-only posts cannot be shared'],
  dm_friends_only: ['Chỉ nhắn riêng với bạn bè', 'Direct messages are for friends only'], already_reported: ['Bạn đã báo cáo nội dung này', 'Already reported'],
  too_many_reports: ['Quá nhiều báo cáo', 'Too many reports'], edit_window_closed: ['Chỉ sửa được trong 24 giờ', 'Editable for 24 hours only'],
  post_under_review: ['Bài đang được kiểm duyệt', 'Post is under review'], chat_too_fast: ['Gửi chậm lại chút nhé', 'Slow down a little'],
  duplicate_message: ['Tin nhắn trùng lặp', 'Duplicate message'], player_not_found: ['Không tìm thấy người chơi', 'Player not found'],
  cannot_friend_self: ['Không thể kết bạn với chính mình', 'You cannot friend yourself'], already_friends: ['Đã là bạn bè', 'Already friends'],
  request_exists: ['Đã gửi lời mời', 'Request already sent'], request_not_found: ['Không có lời mời', 'No such request'],
  guild_name_too_short: ['Tên Guild tối thiểu 3 ký tự', 'Guild name needs 3+ characters'], invalid_guild_tag: ['Tag 2–5 chữ/số', 'Tag must be 2–5 letters/digits'],
  already_in_guild: ['Bạn đã ở trong một Guild', 'Already in a guild'], guild_name_taken: ['Tên Guild đã tồn tại', 'Guild name taken'], guild_tag_taken: ['Tag đã được dùng', 'Tag taken'],
  guild_not_found: ['Không tìm thấy Guild', 'Guild not found'], guild_invite_only: ['Guild riêng tư — cần lời mời', 'Private guild — invite only'],
  guild_level_too_low: ['Chưa đủ cấp độ yêu cầu', 'Level requirement not met'], join_request_exists: ['Đã gửi yêu cầu', 'Request already sent'], guild_full: ['Guild đã đầy', 'Guild is full'],
  guild_officer_required: ['Cần quyền Officer trở lên', 'Officer or higher required'], guild_leader_required: ['Chỉ Guild Master', 'Guild Master only'],
  guild_rank_too_low: ['Không đủ quyền', 'Rank too low'], cannot_change_leader: ['Không thể đổi vai trò Guild Master', 'Cannot change the Guild Master'],
  member_not_found: ['Không tìm thấy thành viên', 'Member not found'], invite_exists: ['Đã mời người này', 'Already invited'],
  confirm_name_mismatch: ['Nhập đúng tên Guild để xác nhận', 'Type the guild name to confirm'], guild_has_members: ['Còn thành viên — chưa thể giải tán', 'Members remain — cannot dissolve'],
  mission_not_completed: ['Nhiệm vụ chưa hoàn thành', 'Mission not completed'], already_claimed: ['Đã nhận', 'Already claimed'], mission_not_found: ['Không có nhiệm vụ', 'Mission not found'],
  boss_defeated: ['Boss tuần này đã bị hạ', 'This week\'s boss is defeated'], boss_attacks_used: ['Hết 3 lượt đánh Boss hôm nay', 'All 3 boss attacks used today'],
  already_registered: ['Đã đăng ký Guild War tuần này', 'Already registered this week'],
  price_out_of_range: ['Giá không hợp lệ', 'Invalid price'], too_many_listings: ['Tối đa 20 tin đăng', 'Max 20 listings'], listing_not_found: ['Không tìm thấy tin đăng', 'Listing not found'],
  listing_closed: ['Tin đã đóng (có người mua trước)', 'Listing closed (someone bought it first)'], cannot_buy_own_listing: ['Không thể mua tin của mình', 'Cannot buy your own listing'],
  insufficient_mini_green_bean: ['Không đủ Đậu Xanh Mini', 'Not enough Mini Green Beans'], insufficient_rainbow_star: ['Không đủ Sao Ngũ Sắc', 'Not enough Rainbow Stars'],
  trade_cards_1_to_5: ['Mỗi bên 1–5 thẻ', '1–5 cards per side'], cannot_trade_self: ['Không thể tự trao đổi', 'Cannot trade with yourself'],
  too_many_open_trades: ['Quá nhiều đề nghị đang mở', 'Too many open offers'], trade_not_found: ['Không tìm thấy đề nghị', 'Trade not found'],
  trade_closed: ['Đề nghị đã đóng', 'Trade closed'], trade_cards_changed: ['Thẻ đã thay đổi — đề nghị bị hủy, không thẻ nào bị chuyển', 'Cards changed — offer failed, nothing moved'],
  already_owned: ['Bạn đã sở hữu', 'Already owned'], item_not_for_sale: ['Không bán vật phẩm này', 'Not for sale'], outfit_wrong_character: ['Skin dành cho Trainer khác', 'Skin is for the other Trainer'],
  requires_guild: ['Cần tham gia Guild', 'Requires a guild'], daily_limit_reached: ['Đạt giới hạn mua hôm nay', 'Daily limit reached'], item_not_owned: ['Chưa sở hữu', 'Not owned'],
};
export function errText(e: unknown, P: PFn): string {
  const code = e instanceof WorldError ? e.code : e instanceof Error ? e.message : String(e);
  const m = CODES[code];
  return m ? P(m[0], m[1]) : P('Có lỗi: ', 'Error: ') + code;
}
export const ago = (t: number, P: PFn) => {
  const s = Math.max(1, Math.floor((Date.now() - t) / 1000));
  if (s < 60) return P(`${s} giây`, `${s}s`);
  if (s < 3600) return P(`${Math.floor(s / 60)} phút`, `${Math.floor(s / 60)}m`);
  if (s < 86400) return P(`${Math.floor(s / 3600)} giờ`, `${Math.floor(s / 3600)}h`);
  return P(`${Math.floor(s / 86400)} ngày`, `${Math.floor(s / 86400)}d`);
};
