#!/usr/bin/env python3
"""Source of truth for client strings (en + vi). Writes client-unity/Assets/Resources/Localization/{en,vi}.json.
Dynamic keys (skills, passives, stages, elements...) are generated from the design database so new content
cannot ship without a translation entry. Run tools/check_localization.py to verify coverage."""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from localization_extra import EXTRA  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "client-unity/Assets/Resources/Localization"
SEED = ROOT / "database/seed"

S = {  # key: (en, vi)
    "common.loading": ("Loading…", "Đang tải…"),
    "common.close": ("Close", "Đóng"),
    "common.continue": ("Continue", "Tiếp tục"),
    "auth.welcome": ("Welcome back!", "Chào mừng trở lại!"),
    "auth.login_subtitle": ("Sign in to continue your collection.", "Đăng nhập để tiếp tục bộ sưu tập."),
    "auth.email": ("Email", "Email"),
    "auth.password": ("Password", "Mật khẩu"),
    "auth.password_rules": ("Password (8+ chars, letters & digits)", "Mật khẩu (8+ ký tự, gồm chữ và số)"),
    "auth.display_name": ("Display name", "Tên hiển thị"),
    "auth.login": ("Sign in", "Đăng nhập"),
    "auth.register": ("Create account", "Tạo tài khoản"),
    "auth.create_account": ("Create account", "Tạo tài khoản"),
    "auth.forgot": ("Forgot password?", "Quên mật khẩu?"),
    "auth.enter_email_first": ("Enter your email first.", "Hãy nhập email trước."),
    "auth.reset_sent": ("If the account exists, a reset email has been sent.", "Nếu tài khoản tồn tại, email đặt lại mật khẩu đã được gửi."),
    "auth.verify_note": ("We will send a verification email. You can play right away.", "Chúng tôi sẽ gửi email xác thực. Bạn có thể chơi ngay."),
    "auth.check_email": ("Check your inbox to verify your email.", "Hãy kiểm tra hộp thư để xác thực email."),
    "server.title": ("Choose a server", "Chọn máy chủ"),
    "server.none": ("No servers available.", "Chưa có máy chủ."),
    "server.your_progress": ("{0} · Lv.{1}", "{0} · Cấp {1}"),
    "server.new_start": ("New start – independent progress", "Khởi đầu mới – tiến trình độc lập"),
    "server.registration_closed": ("Registration closed", "Đã đóng đăng ký"),
    "server.join": ("Join", "Tham gia"),
    "server.continue": ("Play", "Vào chơi"),
    "server.status.online": ("● Online", "● Hoạt động"),
    "server.status.maintenance": ("● Maintenance", "● Bảo trì"),
    "server.status.coming_soon": ("● Coming soon", "● Sắp ra mắt"),
    "server.status.merged": ("● Merged", "● Đã gộp"),
    "lobby.level": ("Lv.{0}", "Cấp {0}"),
    "lobby.open_pack": ("Open pack ({0})", "Mở gói ({0})"),
    "lobby.go_shop": ("Get packs", "Mua gói"),
    "lobby.shop": ("Shop", "Cửa hàng"),
    "lobby.pet_home": ("Pet Home", "Nhà Pet"),
    "lobby.duel": ("Duel", "Đấu"),
    "lobby.codex": ("Codex", "Sách Pet"),
    "lobby.profile": ("Profile", "Hồ sơ"),
    "lobby.settings": ("Settings", "Cài đặt"),
    "settings.language": ("Language", "Ngôn ngữ"),
    "settings.sfx": ("Sound effects", "Hiệu ứng âm thanh"),
    "settings.on": ("On", "Bật"),
    "settings.off": ("Off", "Tắt"),
    "settings.switch_server": ("Switch server", "Đổi máy chủ"),
    "settings.logout": ("Sign out", "Đăng xuất"),
    "currency.rainbow_star": ("Rainbow Star", "Sao Ngũ Sắc"),
    "currency.mini_green_bean": ("Mini Green Bean", "Đậu Xanh Mini"),
    "shop.title": ("Shop", "Cửa hàng"),
    "shop.tab_packs": ("Booster Packs", "Gói thẻ"),
    "shop.tab_rainbow_star": ("Rainbow Star", "Sao Ngũ Sắc"),
    "shop.owned": ("Owned: {0}", "Đang có: {0}"),
    "shop.buy": ("Buy", "Mua"),
    "shop.open": ("Open", "Mở"),
    "shop.odds": ("Odds", "Tỷ lệ"),
    "shop.odds_title": ("Published drop rates", "Tỷ lệ rơi công khai"),
    "shop.odds_slots": ("Cards 1–4 rarity", "Độ hiếm thẻ 1–4"),
    "shop.odds_final": ("Card 5 rarity (own table)", "Độ hiếm thẻ thứ 5 (bảng riêng)"),
    "shop.odds_quality": ("Quality", "Phẩm chất"),
    "shop.odds_finish": ("Finish (cosmetic)", "Hiệu ứng (trang trí)"),
    "shop.iap_unavailable": ("Store not available yet", "Cửa hàng chưa mở"),
    "shop.iap_unavailable_detail": ("Purchases open after store setup and owner approval.", "Mua bán sẽ mở sau khi cấu hình cửa hàng và được chủ dự án phê duyệt."),
    "pack.basic.name": ("Basic Pack", "Gói Cơ Bản"),
    "pack.premium.name": ("Premium Pack", "Gói Cao Cấp"),
    "pack.legendary.name": ("Legendary Pack", "Gói Huyền Thoại"),
    "pack.event.name": ("Event Pack", "Gói Sự Kiện"),
    "pack.skip": ("Skip", "Bỏ qua"),
    "pack.swipe_to_tear": ("Swipe across the top edge to tear the pack", "Vuốt ngang mép trên để xé gói"),
    "pack.swipe_to_flip": ("Swipe or tap to flip each card", "Vuốt hoặc chạm để lật từng thẻ"),
    "pack.to_pet_home": ("Pet Home", "Nhà Pet"),
    "pack.open_another": ("Open another", "Mở tiếp"),
    "pack.network_recover": ("Connection lost. Your cards are safe and will appear in the lobby.", "Mất kết nối. Thẻ của bạn vẫn an toàn và sẽ hiện ở sảnh."),
    "home.title": ("Pet Home", "Nhà Pet"),
    "home.count": ("{0} cards", "{0} thẻ"),
    "home.empty": ("No cards yet. Open a pack to start your collection!", "Chưa có thẻ. Hãy mở gói để bắt đầu bộ sưu tập!"),
    "filter.rarity": ("Rarity", "Độ hiếm"),
    "filter.element": ("Element", "Hệ"),
    "filter.stage": ("Stage", "Bậc"),
    "filter.sort": ("Sort", "Sắp xếp"),
    "filter.all": ("All", "Tất cả"),
    "sort.newest": ("Newest", "Mới nhất"),
    "sort.power": ("Power", "Sức mạnh"),
    "sort.rarity": ("Rarity", "Độ hiếm"),
    "sort.level": ("Level", "Cấp"),
    "sort.name": ("Name", "Tên"),
    "card.level": ("Lv.{0}", "Cấp {0}"),
    "card.ultimate": ("ULTIMATE", "TUYỆT KỸ"),
    "card.passive": ("Passive", "Nội tại"),
    "card.power": ("Power", "Sức mạnh"),
    "card.exp": ("EXP {0} / {1}", "EXP {0} / {1}"),
    "card.favorite": ("Favorite", "Yêu thích"),
    "card.unfavorite": ("Unfavorite", "Bỏ yêu thích"),
    "card.lock": ("Lock", "Khóa"),
    "card.unlock": ("Unlock", "Mở khóa"),
    "card.locked_short": ("LOCK", "KHÓA"),
    "card.evolve": ("Evolution", "Tiến hóa"),
    "evolution.title": ("Evolution", "Tiến hóa"),
    "evolution.final_form": ("This Pet is in its final form.", "Pet này đã ở hình thái cuối."),
    "evolution.req_level": ("Card level {0}+", "Cấp thẻ {0}+"),
    "evolution.req_essence": ("{0}: {1} / {2}", "{0}: {1} / {2}"),
    "evolution.req_beans": ("Mini Green Bean: {0} / {1}", "Đậu Xanh Mini: {0} / {1}"),
    "evolution.confirm": ("Evolve", "Tiến hóa"),
    "evolution.success": ("Evolved into {0}!", "Đã tiến hóa thành {0}!"),
    "codex.title": ("Pet Codex", "Sách Pet"),
    "codex.progress": ("Discovered {0} / {1} ({2}%)", "Đã khám phá {0} / {1} ({2}%)"),
    "profile.title": ("Profile", "Hồ sơ"),
    "profile.cards": ("Cards: {0}", "Số thẻ: {0}"),
    "profile.codex": ("Codex: {0} / {1}", "Sách Pet: {0} / {1}"),
    "profile.email_verified": ("Email verified", "Email đã xác thực"),
    "profile.email_unverified": ("Email not verified", "Email chưa xác thực"),
    "profile.resend": ("Resend verification email", "Gửi lại email xác thực"),
    "duel.title": ("Duel", "Đấu"),
    "duel.no_deck": ("No default deck yet", "Chưa có đội hình mặc định"),
    "duel.deck_power": ("{0} · Power {1}", "{0} · Sức mạnh {1}"),
    "duel.auto_deck": ("Auto deck", "Tự xếp đội"),
    "duel.deck_saved": ("Deck saved", "Đã lưu đội hình"),
    "duel.default_deck_name": ("Main deck", "Đội chính"),
    "duel.need_cards": ("You need at least 5 suitable cards.", "Cần ít nhất 5 thẻ phù hợp."),
    "duel.pve_chapter": ("Chapter {0}", "Chương {0}"),
    "duel.stage_info": ("Cleared {0}× · rewarded clears left today: {1}", "Đã thắng {0} lần · lượt nhận thưởng hôm nay: {1}"),
    "duel.fight": ("Fight", "Chiến"),
    "duel.locked": ("Locked", "Khóa"),
    "duel.pvp_next_phase": ("PvP duels open in the next milestone.", "Đấu PvP sẽ mở ở cột mốc tiếp theo."),
    "duel.round": ("Round {0}", "Lượt {0}"),
    "duel.energy": ("⚡ {0} / {1}", "⚡ {0} / {1}"),
    "duel.surrender": ("Surrender", "Đầu hàng"),
    "duel.basic": ("Attack", "Đánh"),
    "duel.skill": ("Skill", "Kỹ năng"),
    "duel.ultimate": ("Ultimate", "Tuyệt kỹ"),
    "duel.defend": ("Defend", "Phòng thủ"),
    "duel.end_turn": ("End turn", "Kết thúc lượt"),
    "duel.select_card_first": ("Select one of your cards first.", "Hãy chọn một thẻ của bạn trước."),
    "duel.no_such_skill": ("This Pet has no such skill.", "Pet này không có kỹ năng đó."),
    "duel.short.Basic": ("ATK", "ĐÁNH"),
    "duel.short.Skill": ("SKILL", "KN"),
    "duel.short.Ultimate": ("ULT", "TK"),
    "duel.short.Defend": ("DEF", "THỦ"),
    "duel.enemy": ("Enemy", "Địch"),
    "duel.victory": ("Victory!", "Chiến thắng!"),
    "duel.defeat": ("Defeat", "Thất bại"),
    "duel.draw": ("Draw", "Hòa"),
    "duel.card_exp": ("+{0} EXP to each card", "+{0} EXP cho mỗi thẻ"),
    "duel.first_clear": ("First clear bonus!", "Thưởng lần đầu chiến thắng!"),
    "duel.daily_cap": ("Daily reward limit reached for this stage.", "Đã đạt giới hạn thưởng hôm nay của màn này."),
    "role.guardian": ("Guardian", "Hộ vệ"),
    "role.striker": ("Striker", "Tấn công"),
    "role.mage": ("Mage", "Pháp sư"),
    "role.support": ("Support", "Hỗ trợ"),
    "role.controller": ("Controller", "Khống chế"),
    "role.": ("", ""),
    "finish.Normal": ("Normal", "Thường"),
    "finish.Foil": ("Foil", "Ánh kim"),
    "finish.Holographic": ("Holographic", "Ảnh 3 chiều"),
    "finish.Shiny": ("Shiny", "Lấp lánh"),
    "finish.Prismatic": ("Prismatic", "Lăng kính"),
    "status.burn": ("Burn", "Bỏng"), "status.poison": ("Poison", "Độc"), "status.stun": ("Stun", "Choáng"),
    "status.taunt": ("Taunt", "Khiêu khích"), "status.weaken": ("Weaken", "Suy yếu"),
    "status.short.burn": ("BRN", "BỎNG"), "status.short.poison": ("PSN", "ĐỘC"), "status.short.stun": ("STN", "CHOÁNG"),
    "status.short.taunt": ("TNT", "KHIÊU"), "status.short.weaken": ("WKN", "YẾU"),
    "effect.damage": ("Deal {0}% ATK damage", "Gây {0}% ATK sát thương"),
    "effect.heal": ("Heal {0}% ATK", "Hồi {0}% ATK"),
    "effect.shield": ("Shield {0}% {1}", "Khiên {0}% {1}"),
    "effect.status": ("{0}% {1} ({2} rounds)", "{0}% {1} ({2} lượt)"),
    "effect.cleanse": ("cleanse debuffs", "xóa hiệu ứng xấu"),
    "target.enemy_single": ("one enemy", "một kẻ địch"), "target.enemy_all": ("all enemies", "toàn bộ kẻ địch"),
    "target.enemy_front_row": ("enemy front row", "hàng trước địch"), "target.self": ("self", "bản thân"),
    "target.ally_all": ("all allies", "toàn đội"), "target.ally_single": ("one ally", "một đồng đội"),
    "target.ally_lowest_hp": ("lowest-HP ally", "đồng đội ít máu nhất"),
    "error.generic": ("Something went wrong. Please try again.", "Đã có lỗi. Vui lòng thử lại."),
    "error.network": ("No connection. Pet Star needs internet.", "Không có kết nối. Pet Star cần Internet."),
    "error.invalid_credentials": ("Wrong email or password.", "Sai email hoặc mật khẩu."),
    "error.account_locked_temporarily": ("Too many attempts. Try again in 15 minutes.", "Thử quá nhiều lần. Hãy thử lại sau 15 phút."),
    "error.account_banned": ("This account is banned.", "Tài khoản đã bị khóa."),
    "error.email_taken": ("This email is already registered.", "Email này đã được đăng ký."),
    "error.invalid_email": ("Invalid email.", "Email không hợp lệ."),
    "error.weak_password": ("Password needs 8+ characters with letters and digits.", "Mật khẩu cần 8+ ký tự gồm chữ và số."),
    "error.invalid_display_name": ("Name must be 3–24 characters.", "Tên cần 3–24 ký tự."),
    "error.nickname_taken": ("This name is taken on this server.", "Tên này đã có người dùng trên máy chủ."),
    "error.server_registration_closed": ("Registration on this server is closed.", "Máy chủ này đã đóng đăng ký."),
    "error.server_maintenance": ("Server under maintenance.", "Máy chủ đang bảo trì."),
    "error.server_unavailable": ("Server unavailable.", "Máy chủ không khả dụng."),
    "error.server_coming_soon": ("This server opens soon.", "Máy chủ sắp mở."),
    "error.rate_limited": ("Too many requests. Slow down a little.", "Quá nhiều yêu cầu. Hãy chậm lại một chút."),
    "error.insufficient_mini_green_bean": ("Not enough Mini Green Beans.", "Không đủ Đậu Xanh Mini."),
    "error.insufficient_rainbow_star": ("Not enough Rainbow Stars.", "Không đủ Sao Ngũ Sắc."),
    "error.no_pack_available": ("You have no pack of this type.", "Bạn không có gói loại này."),
    "error.pack_not_available": ("This pack is not available.", "Gói này hiện không mở bán."),
    "error.evolution_leveltoolow": ("Card level is too low.", "Cấp thẻ chưa đủ."),
    "error.evolution_notenoughessence": ("Not enough essence.", "Không đủ tinh chất."),
    "error.evolution_notenoughbeans": ("Not enough Mini Green Beans.", "Không đủ Đậu Xanh Mini."),
    "error.evolution_finalform": ("Already in final form.", "Đã ở hình thái cuối."),
    "error.evolution_cardlocked": ("This card is busy in another action.", "Thẻ đang bận trong giao dịch khác."),
    "error.deck_wrong_size": ("A deck needs exactly 5 cards.", "Đội hình cần đúng 5 thẻ."),
    "error.deck_duplicate_card": ("The same card cannot be used twice.", "Không thể dùng một thẻ hai lần."),
    "error.deck_too_many_same_family": ("Too many cards from the same family.", "Quá nhiều thẻ cùng dòng."),
    "error.deck_invalid": ("Your deck is no longer valid.", "Đội hình không còn hợp lệ."),
    "error.no_deck": ("Set a default deck first.", "Hãy đặt đội hình mặc định trước."),
    "error.stage_locked": ("Clear the previous stage first.", "Hãy vượt màn trước đó."),
    "error.not_enough_energy": ("Not enough energy.", "Không đủ năng lượng."),
    "error.unit_stunned": ("This Pet is stunned.", "Pet này đang bị choáng."),
    "error.duplicate_action": ("Each Pet can act once per round.", "Mỗi Pet chỉ hành động một lần mỗi lượt."),
    "error.battle_finished": ("The battle is over.", "Trận đấu đã kết thúc."),
    "error.iap_unavailable": ("Store purchases are not available yet.", "Chưa thể mua trên cửa hàng."),
    "error.purchase_already_used": ("This purchase was already used.", "Giao dịch này đã được sử dụng."),
    "error.server_not_selected": ("Choose a server first.", "Hãy chọn máy chủ trước."),
    "error.idempotency_key_reused": ("Request conflict. Please retry.", "Xung đột yêu cầu. Vui lòng thử lại."),
}

ELEMENTS = {"fire": ("Fire", "Hỏa"), "water": ("Water", "Thủy"), "nature": ("Nature", "Mộc"), "earth": ("Earth", "Thổ"),
            "lightning": ("Lightning", "Lôi"), "wind": ("Wind", "Phong"), "light": ("Light", "Quang"), "dark": ("Dark", "Ám")}
SKILLS_VI = {
    "ember_pounce": "Vồ Than Hồng", "cinder_claws": "Vuốt Tro Lửa", "lynx_flare_rush": "Lao Lửa Linh Miêu", "pyre_fang": "Nanh Hỏa Đài",
    "sunfall_blaze": "Mặt Trời Rực Cháy", "bubble_guard": "Khiên Bong Bóng", "ripple_wall": "Tường Gợn Sóng", "tidal_taunt": "Thủy Triều Khiêu Chiến",
    "coral_bastion": "Thành Lũy San Hô", "crown_of_tides": "Vương Miện Thủy Triều", "static_nibble": "Cắn Tĩnh Điện", "chain_spark": "Chuỗi Tia Lửa Điện",
    "storm_acorn": "Hạt Dẻ Bão Tố", "dawn_hymn": "Khúc Ca Bình Minh", "halo_sanctuary": "Thánh Địa Hào Quang", "dusk_powder": "Phấn Hoàng Hôn",
    "eclipse_veil": "Màn Che Nhật Thực",
}
PASSIVES = {
    "warm_fur": ("+5% ATK", "+5% ATK"), "kindling": ("+5% critical rate", "+5% tỉ lệ chí mạng"),
    "blazing_heart": ("+15% ATK below 50% HP", "+15% ATK khi dưới 50% HP"), "slick_skin": ("+5% DEF", "+5% DEF"),
    "regen_gills": ("Recover 4% max HP each round", "Hồi 4% HP tối đa mỗi lượt"), "abyssal_guard": ("Take 10% less damage", "Giảm 10% sát thương nhận vào"),
    "quick_paws": ("+8% SPD", "+8% SPD"), "overcharge": ("Team starts with +1 energy", "Đội bắt đầu với +1 năng lượng"),
    "radiance": ("Healing +15%", "Hồi máu +15%"), "moonlit_drain": ("Heal 10% of damage dealt", "Hồi 10% sát thương gây ra"),
}
STAGES = {"c1_s1": ("1-1 Sunny Meadow", "1-1 Đồng Cỏ Nắng"), "c1_s2": ("1-2 Whisper Brook", "1-2 Suối Thì Thầm"),
          "c1_s3": ("1-3 Ember Hill", "1-3 Đồi Than Hồng"), "c1_s4": ("1-4 Storm Grove", "1-4 Rừng Giông"),
          "c1_boss": ("1-5 Boss: Tide Throne", "1-5 Trùm: Ngai Thủy Triều")}


def main():
    en, vi = {}, {}
    for k, (e, v) in S.items():
        en[k], vi[k] = e, v
    for k, (e, v) in ELEMENTS.items():
        en[f"element.{k}"], vi[f"element.{k}"] = e, v
        en[f"item.essence_{k}"], vi[f"item.essence_{k}"] = f"{e} Essence", f"Tinh chất {v}"
    skills = json.loads((SEED / "skills.json").read_text())
    for k, (e, v) in EXTRA.items():
        en[k], vi[k] = e, v
    for s in skills["skills"]:
        en[f"skill.{s['id']}"] = s["name"]["en"]
        vi[f"skill.{s['id']}"] = s["name"]["vi"]
    stat_vi = {"atk": "ATK", "def": "DEF", "spd": "SPD", "crit": "tỉ lệ chí mạng"}
    for p in skills["passives"]:
        if p["id"] in PASSIVES:
            en[f"passive.{p['id']}"], vi[f"passive.{p['id']}"] = PASSIVES[p["id"]]
            continue
        t, st = p["type"], p.get("stat", "")
        pct = round(p.get("pct", 0) * 100, 1)
        pct = int(pct) if pct == int(pct) else pct
        en[f"passive.{p['id']}"], vi[f"passive.{p['id']}"] = {
            "stat_bonus": (f"+{pct}% {'critical rate' if st == 'crit' else st.upper()}", f"+{pct}% {stat_vi.get(st, st)}"),
            "low_hp_stat_bonus": (f"+{pct}% {st.upper()} below 50% HP", f"+{pct}% {stat_vi.get(st, st)} khi dưới 50% HP"),
            "regen_round_end": (f"Recover {pct}% max HP each round", f"Hồi {pct}% HP tối đa mỗi lượt"),
            "damage_reduction": (f"Take {pct}% less damage", f"Giảm {pct}% sát thương nhận vào"),
            "battle_start_energy": ("Team starts with +1 energy", "Đội bắt đầu với +1 năng lượng"),
            "heal_bonus": (f"Healing +{pct}%", f"Hồi máu +{pct}%"),
            "lifesteal": (f"Heal {pct}% of damage dealt", f"Hồi {pct}% sát thương gây ra"),
        }[t]
    trig = {"pack_opened": ("Open {n} pack(s)", "Mở {n} gói thẻ"), "rare_obtained": ("Get your first rare card (SR+)", "Nhận thẻ hiếm đầu tiên (SR+)"),
            "evolution": ("Evolve a Pet {n} time(s)", "Tiến hóa Pet {n} lần"), "duel_win": ("Win {n} duel(s)", "Thắng {n} trận đấu"),
            "market_sale": ("Sell {n} card(s) on the Market", "Bán {n} thẻ trên Chợ"), "market_purchase": ("Buy {n} card(s) on the Market", "Mua {n} thẻ trên Chợ"),
            "auction_win": ("Win {n} auction(s)", "Thắng {n} phiên đấu giá"), "feed_post": ("Share your first post", "Đăng bài viết đầu tiên"),
            "codex_count": ("Discover {n} Pets in the Codex", "Khám phá {n} Pet trong Sách Pet"),
            "family_complete": ("Complete {n} evolution line(s)", "Hoàn thành {n} dòng tiến hóa"),
            "rare_owned_distinct": ("Own {n} different SSR+ Pets", "Sở hữu {n} Pet SSR+ khác nhau"), "ranked_win": ("Win {n} ranked duel(s)", "Thắng {n} trận xếp hạng"),
            "element_complete": ("Discover every {el} Pet", "Khám phá toàn bộ Pet hệ {el}")}
    for a in json.loads((SEED / "achievements.json").read_text())["achievements"]:
        te, tv = trig[a["trigger"]]
        el = a.get("element")
        en[f"ach.{a['id']}"] = te.format(n=a["threshold"], el=ELEMENTS[el][0] if el else "")
        vi[f"ach.{a['id']}"] = tv.format(n=a["threshold"], el=ELEMENTS[el][1] if el else "")
    for el, (e, v) in ELEMENTS.items():
        en[f"album.element_{el}"], vi[f"album.element_{el}"] = f"{e} Collection", f"Bộ sưu tập hệ {v}"
    for k, (e, v) in STAGES.items():
        en[f"stage.{k}"], vi[f"stage.{k}"] = e, v
    OUT.mkdir(parents=True, exist_ok=True)
    for code, table in (("en", en), ("vi", vi)):
        (OUT / f"{code}.json").write_text(json.dumps(dict(sorted(table.items())), ensure_ascii=False, indent=1) + "\n")
    print(f"wrote {len(en)} keys per language")


if __name__ == "__main__":
    main()
