"""Strings for the main lobby, chibi Trainers and Battle 3v3 (Unity MainLobbyScreens/TeamScreens + server error codes).
Pet names use the existing pet.<id> keys; skill names ship bilingual in database/seed/team_battle.json."""

STATUSES = {
    "burn": ("Burn", "Bỏng"), "poison": ("Poison", "Độc"), "stun": ("Stun", "Choáng"), "freeze": ("Freeze", "Đóng băng"),
    "silence": ("Silence", "Câm lặng"), "weaken": ("Weaken", "Suy yếu"), "haste": ("Haste", "Tăng tốc"), "regen": ("Regen", "Hồi phục"),
    "shield": ("Shield", "Khiên"), "taunt": ("Taunt", "Khiêu khích"), "counter": ("Counter", "Phản đòn"), "guard": ("Guard", "Phòng thủ"),
    "cc_immune": ("CC immune", "Kháng khống chế"),
}
END_REASONS = {
    "knockout": ("All enemy Pets are down", "Toàn bộ Pet đối thủ đã gục"),
    "round_limit": ("Round limit reached: decided by remaining HP", "Hết số hiệp: phân định theo HP còn lại"),
    "surrender": ("A player surrendered", "Một người chơi đã đầu hàng"),
    "timeout": ("Forfeit after 3 missed turns", "Xử thua do bỏ lượt 3 lần"),
    "both_down": ("Both teams fell at the same time", "Cả hai đội gục cùng lúc"),
}

TEAM = {
    "trainer.choose": ("Choose your Trainer", "Chọn Huấn Luyện Viên"),
    "trainer.boy": ("Boy Trainer", "Huấn Luyện Viên Nam"),
    "trainer.girl": ("Girl Trainer", "Huấn Luyện Viên Nữ"),
    "trainer.confirm": ("Confirm", "Xác nhận"),
    "trainer.locked_note": ("Your choice is saved to your account and cannot be changed yet.", "Lựa chọn được lưu vào tài khoản và hiện chưa thể đổi."),
    "lobby.messages": ("Messages", "Tin Nhắn"),
    "lobby.friends": ("Friends", "Bạn Bè"),
    "lobby.hex_plaza": ("Pet Plaza", "Quảng Trường Pet"),
    "lobby.hex_games": ("Mini Games", "Trò Chơi Nhỏ"),
    "lobby.hex_world": ("Pet World", "Thế Giới Pet"),
    "lobby.hex_battle": ("Battle 3v3", "Bắt Đầu"),
    "lobby.games_preview": ("Mini games are playable in the web Preview only (no server rewards yet).", "Trò chơi nhỏ hiện chỉ chơi được trong bản Preview web (chưa có thưởng từ máy chủ)."),
    "lobby.starter_gift": ("Starter Gift", "Quà Tân Thủ"),
    "lobby.center": ("Center", "Trung Tâm"),
    "lobby.gifts": ("Gifts", "Quà Tặng"),
    "lobby.season": ("Season Pass", "Thẻ Mùa"),
    "lobby.season_preview": ("The free Season Pass is in the web Preview only; the server version is not built yet.", "Thẻ Mùa miễn phí mới có trong bản Preview web; bản máy chủ chưa làm."),
    "lobby.events": ("Events", "Hoạt Động"),
    "world.album": ("Album", "Album"), "world.packs": ("Packs", "Gói thẻ"), "world.team": ("Team", "Đội hình"),
    "world.evolution": ("Evolution", "Tiến hóa"), "world.missions": ("Missions", "Nhiệm vụ"), "world.trade": ("Market", "Giao dịch"),
    "world.ranking": ("Leaderboard", "Bảng xếp hạng"), "world.friends": ("Friends", "Bạn bè"), "world.events": ("Events", "Sự kiện"),
    "team.title": ("Battle 3v3", "Đấu Pet 3v3"),
    "team.resume": ("Resume match", "Tiếp tục trận"),
    "team.searching": ("Searching for an opponent…", "Đang tìm đối thủ…"),
    "team.leave_queue": ("Stop searching", "Dừng tìm trận"),
    "team.room_waiting": ("Private room {0}: waiting for your friend", "Phòng riêng {0}: đang chờ bạn bè vào"),
    "team.your_team": ("Your team ({0}/3) — tap to pick", "Đội của bạn ({0}/3) — chạm để chọn"),
    "team.save": ("Save team", "Lưu đội hình"),
    "team.saved": ("Team saved", "Đã lưu đội hình"),
    "team.casual": ("Casual", "Thường"),
    "team.ranked": ("Ranked", "Xếp hạng"),
    "team.practice": ("Practice", "Luyện tập"),
    "team.boss": ("Boss", "Boss"),
    "team.room_code": ("Room code", "Mã phòng"),
    "team.join": ("Join", "Vào phòng"),
    "team.create_room": ("Create room", "Tạo phòng"),
    "team.room_created": ("Room created: {0}", "Đã tạo phòng: {0}"),
    "team.room_code_invalid": ("Enter the 6-character room code", "Hãy nhập mã phòng 6 ký tự"),
    "team.friend_uid": ("Friend UID", "UID bạn bè"),
    "team.invite": ("Invite", "Mời đấu"),
    "team.invited_by": ("{0} invites you to a 3v3 battle!", "{0} mời bạn một trận Đấu Pet 3v3!"),
    "team.match_found": ("Opponent found!", "Đã tìm thấy đối thủ!"),
    "team.pve": ("Adventure (AI)", "Phiêu lưu (AI)"),
    "team.history": ("Match history", "Lịch sử trận"),
    "team.round": ("Round {0}/{1}", "Hiệp {0}/{1}"),
    "team.guard": ("Guard", "Phòng thủ"),
    "team.lock": ("Lock orders", "Chốt lệnh"),
    "team.waiting": ("Waiting for opponent…", "Đang chờ đối thủ…"),
    "team.fx_down": ("Down!", "Gục!"),
    "team.fx_skip": ("Can't act", "Không thể hành động"),
}
TEAM.update({f"team.status.{k}": v for k, v in STATUSES.items()})
TEAM.update({f"team.end.{k}": v for k, v in END_REASONS.items()})

ERRORS = {
    "team_needs_3_different_pets": ("A team needs 3 different Pets.", "Đội hình cần 3 Pet khác nhau."),
    "team_card_not_owned": ("You don't own one of these Pets.", "Bạn không sở hữu một trong các Pet này."),
    "team_card_busy": ("A Pet in this team is listed or locked.", "Một Pet trong đội đang được rao bán hoặc bị khóa."),
    "team_not_found": ("Team not found.", "Không tìm thấy đội hình."),
    "too_many_teams": ("You have too many saved teams.", "Bạn đã lưu quá nhiều đội hình."),
    "no_team": ("Save a 3-Pet team first.", "Hãy lưu đội 3 Pet trước."),
    "plan_locked": ("Orders already locked this round.", "Bạn đã chốt lệnh ở hiệp này."),
    "not_enough_en": ("Not enough EN.", "Không đủ EN."),
    "on_cooldown": ("That skill is on cooldown.", "Kỹ năng đang hồi chiêu."),
    "silenced": ("Silenced: basic attack only.", "Bị câm lặng: chỉ được đánh thường."),
    "unit_down": ("That Pet is down.", "Pet này đã gục."),
    "invalid_unit": ("Invalid Pet.", "Pet không hợp lệ."),
    "invalid_target": ("Invalid target.", "Mục tiêu không hợp lệ."),
    "duplicate_order": ("Each Pet can get only one order.", "Mỗi Pet chỉ nhận một lệnh."),
    "stale_state": ("The match moved on — refreshing.", "Trận đấu đã cập nhật — đang tải lại."),
    "invalid_action_id": ("Invalid action.", "Hành động không hợp lệ."),
    "invalid_mode": ("Invalid mode.", "Chế độ không hợp lệ."),
    "trainer_not_chosen": ("Choose your Trainer first.", "Hãy chọn Huấn Luyện Viên trước."),
    "trainer_already_chosen": ("Your Trainer is already chosen.", "Bạn đã chọn Huấn Luyện Viên rồi."),
    "unknown_character": ("Unknown Trainer.", "Huấn Luyện Viên không tồn tại."),
    "unknown_outfit": ("Unknown outfit.", "Trang phục không tồn tại."),
    "outfit_wrong_character": ("This outfit belongs to another Trainer.", "Trang phục này dành cho Huấn Luyện Viên khác."),
    "outfit_not_owned": ("You don't own this outfit.", "Bạn chưa sở hữu trang phục này."),
    "card_not_owned": ("You don't own this Pet.", "Bạn không sở hữu Pet này."),
    "already_in_queue": ("Already searching for a match.", "Bạn đang tìm trận rồi."),
    "already_in_match": ("You are already in a match.", "Bạn đang trong một trận đấu."),
    "room_not_found": ("Room not found.", "Không tìm thấy phòng."),
    "cannot_join_own_room": ("You can't join your own room.", "Bạn không thể vào phòng của chính mình."),
    "cannot_challenge_self": ("You can't invite yourself.", "Bạn không thể tự mời mình."),
    "player_not_found": ("Player not found.", "Không tìm thấy người chơi."),
    "invite_expired": ("The invite has expired.", "Lời mời đã hết hạn."),
    "match_not_found": ("Match not found.", "Không tìm thấy trận đấu."),
    "match_finished": ("The match is over.", "Trận đấu đã kết thúc."),
    "match_not_started": ("The match hasn't started yet.", "Trận đấu chưa bắt đầu."),
    "stage_locked": ("Stage locked.", "Màn chơi chưa mở khóa."),
    "stage_not_found": ("Stage not found.", "Không tìm thấy màn chơi."),
}

def _merge():
    out = dict(TEAM)
    out.update({f"error.{k}": v for k, v in ERRORS.items()})
    return out

TEAM_ALL = _merge()
DERIVED = {f"team.status.{k}" for k in STATUSES} | {f"team.end.{k}" for k in END_REASONS}
