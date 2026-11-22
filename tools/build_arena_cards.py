#!/usr/bin/env python3
"""Builds database/seed/arena_cards.json: battle definitions for the Pet Battle Arena (20-card, Active + 3 Bench,
Energy Zone, 3 KO points) from the existing Pet Star design data. Nothing in pets.json / skills.json is changed.

Every Pet form keeps its identity: element, family (species), evolution chain, skill, ultimate and passive.
Numbers are derived deterministically so re-running the tool gives the same file. When the formula changes,
bump VERSION: finished matches store the version they were played with, so old replays stay correct.

All Item / Tool / Supporter cards below are original Pet Star designs (names, texts and effects)."""
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SEED = ROOT / "database/seed"
VERSION = "arena-1"

CONFIG = {
    # deck
    "deckSize": 20, "maxCopies": 2, "minBasic": 1, "maxEnergyTypes": 3,
    # board
    "benchSize": 3, "handLimit": 10, "startHand": 5,
    # scoring
    "pointsToWin": 3, "exPoints": 2, "normalPoints": 1,
    "simultaneousWin": "draw",          # both players reach a win condition in the same resolution step
    # first turns (see docs/arena-rules.md: verification status)
    "firstPlayerDraws": False, "firstPlayerEnergy": False,
    "attackBanFirstTurn": "both",        # "both" = neither player attacks on their own first turn; "first_player"
    "noEvolveFirstTurn": True, "supporterFirstTurn": True,
    # actions
    "retreatsPerTurn": 1, "supportersPerTurn": 1, "energyCarryOver": False,
    "weaknessBonus": 20,
    # special conditions
    "poisonDamage": 10, "burnDamage": 20, "burnCureOnHeads": True, "confusedSelfDamage": 0,
    # limits and timers
    "maxTurnsPerPlayer": 30, "pveMaxTurnsPerPlayer": 50, "turnSeconds": 90, "setupSeconds": 60,
    "timeoutsToForfeit": 3, "maxTriggerDepth": 8, "maxActionsPerTurn": 200,
}

ROLE_RETREAT = {"guardian": 2, "controller": 2, "striker": 1, "mage": 1, "support": 1}


def r10(x):
    return int(max(0, math.floor(x / 10 + 0.5)) * 10)


def load():
    pets = json.loads((SEED / "pets.json").read_text())
    skills = json.loads((SEED / "skills.json").read_text())
    elements = json.loads((SEED / "elements.json").read_text())["elements"]
    return pets, {s["id"]: s for s in skills["skills"]}, {p["id"]: p for p in skills["passives"]}, elements


def weakness_of(elements, el):
    for e in elements:
        if el in e["strongAgainst"]:
            return e["id"]
    return None


def text_for(effects, damage, lang):
    vi = lang == "vi"
    parts = []
    for e in effects:
        op, a = e["op"], e.get("amount", 0)
        heads = e.get("cond") == "heads"
        pre = ("Tung đồng xu. Nếu ngửa, " if vi else "Flip a coin. If heads, ") if heads else ""
        if op == "coin_damage":
            parts.append(f"Tung {e['coins']} đồng xu. Gây thêm {a} sát thương cho mỗi mặt ngửa." if vi else f"Flip {e['coins']} coins. +{a} damage for each heads.")
        elif op == "status":
            st = {"poisoned": ("Trúng độc", "Poisoned"), "burned": ("Bỏng", "Burned"), "asleep": ("Ngủ", "Asleep"),
                  "paralyzed": ("Tê liệt", "Paralyzed"), "confused": ("Bối rối", "Confused")}[e["status"]]
            parts.append(pre + (f"Pet Active của đối thủ bị {st[0]}." if vi else f"the opponent's Active Pet is now {st[1]}."))
        elif op == "bench_damage":
            if e["target"] == "opp_bench_all":
                parts.append(f"Gây {a} sát thương cho mỗi Pet Bench của đối thủ." if vi else f"Also does {a} damage to each of the opponent's Benched Pets.")
            else:
                parts.append(f"Gây {a} sát thương cho 1 Pet Bench của đối thủ." if vi else f"Also does {a} damage to 1 of the opponent's Benched Pets.")
        elif op == "heal":
            who = {"self": ("Pet này", "this Pet"), "own_all": ("mỗi Pet của bạn", "each of your Pets"), "own_lowest": ("Pet của bạn bị thương nặng nhất", "your most damaged Pet"),
                   "own_active": ("Pet Active của bạn", "your Active Pet"), "own_chosen": ("1 Pet của bạn", "1 of your Pets")}[e["target"]]
            parts.append(pre + (f"Hồi {a} HP cho {who[0]}." if vi else f"heal {a} damage from {who[1]}."))
        elif op == "shield":
            parts.append(pre + (f"Trong lượt kế của đối thủ, Pet này nhận ít hơn {a} sát thương từ đòn đánh." if vi else f"during the opponent's next turn, this Pet takes {a} less damage from attacks."))
        elif op == "prevent":
            parts.append(pre + ("trong lượt kế của đối thủ, ngăn mọi sát thương lên Pet này." if vi else "during the opponent's next turn, prevent all damage done to this Pet."))
        elif op == "attack_reduce":
            parts.append(pre + (f"Trong lượt kế, đòn đánh của Pet Active đối thủ gây ít hơn {a} sát thương." if vi else f"during the opponent's next turn, the Defending Pet's attacks do {a} less damage."))
        elif op == "attack_lock_self":
            parts.append("Pet này không thể tấn công trong lượt kế của bạn." if vi else "This Pet can't attack during your next turn.")
        elif op == "attack_lock_target":
            parts.append(pre + ("Pet Active của đối thủ không thể tấn công trong lượt kế." if vi else "the Defending Pet can't attack during the opponent's next turn."))
        elif op == "cure":
            parts.append("Pet này hồi phục mọi trạng thái đặc biệt." if vi else "This Pet recovers from all Special Conditions.")
        elif op == "energy_attach":
            parts.append(f"Lấy 1 năng lượng {e['element']} từ Vùng Năng Lượng gắn vào 1 Pet Bench của bạn." if vi else f"Take 1 {e['element']} Energy from your Energy Zone and attach it to 1 of your Benched Pets.")
        elif op == "energy_discard":
            parts.append(pre + ("bỏ 1 năng lượng ngẫu nhiên khỏi Pet Active của đối thủ." if vi else "discard a random Energy from the opponent's Active Pet."))
        elif op == "bonus":
            cond = {"target_status": ("nếu Pet Active đối thủ đang có trạng thái đặc biệt", "if the Defending Pet has a Special Condition"),
                    "target_damaged": ("nếu Pet Active đối thủ đã bị thương", "if the Defending Pet is already damaged"),
                    "target_ex": ("nếu Pet Active đối thủ là Pet EX", "if the Defending Pet is a Pet EX"),
                    "self_damaged": ("nếu Pet này đã bị thương", "if this Pet is damaged")}[e["cond"]]
            parts.append(f"Gây thêm {a} sát thương {cond[0]}." if vi else f"+{a} damage {cond[1]}.")
    return " ".join(p[0].upper() + p[1:] if p else p for p in parts)


def attack_from(skill, atk, stage, element, ex, is_ult):
    effs = skill["effects"]
    dmg_eff = next((e for e in effs if e["type"] == "damage"), None)
    power = dmg_eff["power"] if dmg_eff else 0
    target = skill["target"]
    k = 0.30 if not ex else 0.36
    if target == "enemy_all":
        k *= 0.75
    elif target == "enemy_front_row":
        k *= 0.85
    damage = r10(atk * power * k) if dmg_eff else 0
    out = []
    for e in effs:
        t = e["type"]
        ch = e.get("chance", 1.0)
        if t == "status":
            st = e["status"]
            if st == "stun":
                out.append({"op": "status", "target": "opp_active", "status": "paralyzed" if ch >= 0.5 else "asleep", "cond": "heads" if ch >= 0.5 else None})
            elif st == "poison":
                out.append({"op": "status", "target": "opp_active", "status": "poisoned", "cond": "heads" if ch < 0.5 else None})
            elif st == "burn":
                out.append({"op": "status", "target": "opp_active", "status": "burned", "cond": "heads" if ch < 0.5 else None})
            elif st == "weaken":
                out.append({"op": "attack_reduce", "target": "opp_active", "amount": 20, "turns": 1})
            elif st == "taunt":
                out.append({"op": "status", "target": "opp_active", "status": "confused", "cond": "heads"})
        elif t == "heal":
            tg = {"self": "self", "ally_all": "own_all", "ally_lowest_hp": "own_lowest"}.get(target, "self")
            out.append({"op": "heal", "target": tg, "amount": 10 if tg == "own_all" else (20 if damage > 0 else 40)})
        elif t == "shield":
            out.append({"op": "shield", "target": "self", "amount": 20, "turns": 1})
        elif t == "cleanse":
            out.append({"op": "cure", "target": "self"})
        elif t == "energy":
            out.append({"op": "energy_attach", "target": "own_bench_chosen", "element": element, "count": 1})
    if target == "enemy_all" and damage > 0:
        out.append({"op": "bench_damage", "target": "opp_bench_all", "amount": 10 if damage < 90 else 20})
    elif target == "enemy_front_row" and damage > 0:
        out.append({"op": "bench_damage", "target": "opp_bench_chosen", "amount": 20 if damage >= 60 else 10})
    if out and damage > 0:
        damage = max(10, damage - 10 * min(2, len(out)))
    # a few attacks use coin flips / conditional damage to vary play (deterministic choice by skill id)
    h = sum(map(ord, skill["id"])) % 7
    if damage >= 40 and h == 0:
        coins = 2 if damage < 100 else 3
        per = r10(damage / (coins + 1))
        out.insert(0, {"op": "coin_damage", "target": "opp_active", "coins": coins, "amount": per})
        damage = max(10, damage - per)
    elif damage >= 30 and h == 1:
        out.append({"op": "bonus", "target": "opp_active", "cond": "target_status", "amount": r10(damage * 0.5)})
    elif damage >= 30 and h == 2:
        out.append({"op": "bonus", "target": "opp_active", "cond": "target_damaged", "amount": 20})
    for e in out:
        e.setdefault("target", "opp_active")
        for key in [k for k, v in e.items() if v is None]:
            del e[key]
    return {"id": skill["id"], "name": skill["name"], "cost": [], "damage": min(damage, 200), "effects": out}


def price(a, element, ex, is_ult):
    """Energy cost from the attack's value; text from its effects (called after damage adjustments)."""
    out, damage = a["effects"], a["damage"]
    value = damage + sum(e.get("amount", 0) * max(1, e.get("coins", 1)) for e in out if e["op"] in ("coin_damage", "bonus")) // 2 \
        + 10 * len([e for e in out if e["op"] in ("status", "heal", "shield", "attack_reduce", "bench_damage", "energy_attach")])
    n = max(1, min(4, math.ceil(value / 40)))
    if is_ult:
        n = max(2, n)
    typed = max(1, math.ceil(n / 2)) if not ex else max(1, n - 1)
    a["cost"] = [element] * typed + ["colorless"] * (n - typed)
    a["text"] = {"vi": text_for(out, damage, "vi"), "en": text_for(out, damage, "en")}
    return a


ABILITY_NAMES = {
    "regen_round_end": ({"en": "Soft Regrowth", "vi": "Tái Sinh Dịu Dàng"}, "between_turns", [{"op": "heal", "target": "self", "amount": 10, "cond": "is_active"}],
                        {"en": "Between turns, if this Pet is your Active Pet, heal 10 damage from it.", "vi": "Giữa các lượt, nếu Pet này là Pet Active, hồi 10 HP cho nó."}),
    "battle_start_energy": ({"en": "Star Charge", "vi": "Nạp Sao"}, "activated", [{"op": "energy_attach", "target": "self", "count": 1, "cond": "is_active"}],
                            {"en": "Once during your turn, if this Pet is your Active Pet, you may take 1 {el} Energy from your Energy Zone and attach it to this Pet.",
                             "vi": "Một lần mỗi lượt, nếu Pet này là Pet Active, bạn có thể lấy 1 năng lượng {el} từ Vùng Năng Lượng gắn vào Pet này."}),
    "lifesteal": ({"en": "Drain Bite", "vi": "Hút Sinh Lực"}, "on_attack", [{"op": "heal", "target": "self", "amount": 10}],
                  {"en": "After this Pet's attack does damage, heal 10 damage from it.", "vi": "Sau khi đòn đánh của Pet này gây sát thương, hồi 10 HP cho nó."}),
    "damage_reduction": ({"en": "Sturdy Hide", "vi": "Da Cứng Cáp"}, "passive", [{"op": "reduce_taken", "target": "self", "amount": 10}],
                         {"en": "This Pet takes 10 less damage from attacks.", "vi": "Pet này nhận ít hơn 10 sát thương từ đòn đánh."}),
    "heal_bonus": ({"en": "Gentle Glow", "vi": "Ánh Sáng Dịu"}, "activated", [{"op": "heal", "target": "own_active", "amount": 20}],
                   {"en": "Once during your turn, you may heal 20 damage from your Active Pet.", "vi": "Một lần mỗi lượt, bạn có thể hồi 20 HP cho Pet Active."}),
    "low_hp_stat_bonus": ({"en": "Last Spark", "vi": "Tia Lửa Cuối"}, "passive", [{"op": "damage_boost", "target": "self", "amount": 20, "cond": "self_damaged"}],
                          {"en": "If this Pet is damaged, its attacks do 20 more damage to the opponent's Active Pet.", "vi": "Nếu Pet này đã bị thương, đòn đánh của nó gây thêm 20 sát thương lên Pet Active đối thủ."}),
}

TRAINERS = [
    # items
    {"id": "at_herb_salve", "kind": "item", "name": {"en": "Herb Salve", "vi": "Cao Thảo Mộc"}, "target": "own_pet",
     "effects": [{"op": "heal", "target": "own_chosen", "amount": 20}],
     "text": {"en": "Heal 20 damage from 1 of your Pets.", "vi": "Hồi 20 HP cho 1 Pet của bạn."}},
    {"id": "at_star_lantern", "kind": "item", "name": {"en": "Star Lantern", "vi": "Đèn Lồng Sao"}, "target": "none",
     "effects": [{"op": "search", "target": "own_deck", "count": 1}],
     "text": {"en": "Put a random Basic Pet from your deck into your hand. Shuffle your deck.", "vi": "Đưa 1 Pet Basic ngẫu nhiên từ bộ bài lên tay. Xáo lại bộ bài."}},
    {"id": "at_wind_boots", "kind": "item", "name": {"en": "Breeze Boots", "vi": "Giày Gió Nhẹ"}, "target": "none",
     "effects": [{"op": "retreat_mod", "target": "own_active", "amount": -1, "turns": 0}],
     "text": {"en": "During this turn, your Active Pet's Retreat Cost is 1 less.", "vi": "Trong lượt này, chi phí rút lui của Pet Active giảm 1."}},
    {"id": "at_lucky_coin", "kind": "item", "name": {"en": "Lucky Acorn", "vi": "Hạt Dẻ May Mắn"}, "target": "none",
     "effects": [{"op": "draw", "target": "own_deck", "count": 2, "cond": "heads"}],
     "text": {"en": "Flip a coin. If heads, draw 2 cards.", "vi": "Tung đồng xu. Nếu ngửa, rút 2 lá."}},
    # tools (attached to a Pet; one per Pet)
    {"id": "at_leaf_armor", "kind": "tool", "name": {"en": "Leaf Mantle", "vi": "Áo Choàng Lá"}, "target": "own_pet",
     "effects": [{"op": "max_hp", "target": "self", "amount": 20}],
     "text": {"en": "The Pet this card is attached to gets +20 HP.", "vi": "Pet được gắn thẻ này có thêm 20 HP tối đa."}},
    {"id": "at_thorn_band", "kind": "tool", "name": {"en": "Bramble Band", "vi": "Vòng Gai Mâm Xôi"}, "target": "own_pet",
     "effects": [{"op": "retaliate", "target": "attacker", "amount": 20}],
     "text": {"en": "If the Active Pet with this card is damaged by an attack, do 20 damage to the attacking Pet.", "vi": "Nếu Pet Active mang thẻ này bị đòn đánh gây sát thương, gây 20 sát thương cho Pet tấn công."}},
    {"id": "at_purity_charm", "kind": "tool", "name": {"en": "Dewdrop Charm", "vi": "Bùa Giọt Sương"}, "target": "own_pet",
     "effects": [{"op": "status_immunity", "target": "self"}],
     "text": {"en": "The Pet this card is attached to can't be affected by Special Conditions.", "vi": "Pet được gắn thẻ này không bị ảnh hưởng bởi trạng thái đặc biệt."}},
    {"id": "at_ember_charm", "kind": "tool", "name": {"en": "Farewell Spark", "vi": "Tia Lửa Từ Biệt"}, "target": "own_pet",
     "effects": [{"op": "on_ko_damage", "target": "attacker", "amount": 30}],
     "text": {"en": "If the Pet with this card is Knocked Out by an attack, do 30 damage to the attacking Pet.", "vi": "Nếu Pet mang thẻ này bị hạ bởi đòn đánh, gây 30 sát thương cho Pet tấn công."}},
    # supporters (one per turn)
    {"id": "su_scholar", "kind": "supporter", "name": {"en": "Star Scholar", "vi": "Học Giả Sao"}, "target": "none",
     "effects": [{"op": "draw", "target": "own_deck", "count": 2}],
     "text": {"en": "Draw 2 cards.", "vi": "Rút 2 lá."}},
    {"id": "su_captain", "kind": "supporter", "name": {"en": "Storm Captain", "vi": "Đội Trưởng Bão Tố"}, "target": "opp_bench",
     "effects": [{"op": "force_switch", "target": "opp_bench_chosen"}],
     "text": {"en": "Switch the opponent's Active Pet with 1 of their Benched Pets of your choice.", "vi": "Đổi Pet Active của đối thủ với 1 Pet Bench của họ do bạn chọn."}},
    {"id": "su_medic", "kind": "supporter", "name": {"en": "Forest Medic", "vi": "Y Sư Rừng Xanh"}, "target": "own_pet",
     "effects": [{"op": "heal", "target": "own_chosen", "amount": 40}, {"op": "cure", "target": "own_chosen"}],
     "text": {"en": "Heal 40 damage from 1 of your Pets and remove all Special Conditions from it.", "vi": "Hồi 40 HP cho 1 Pet của bạn và xóa mọi trạng thái đặc biệt của nó."}},
    {"id": "su_smith", "kind": "supporter", "name": {"en": "Energy Smith", "vi": "Thợ Rèn Năng Lượng"}, "target": "own_bench",
     "effects": [{"op": "energy_transfer", "target": "own_bench_chosen"}],
     "text": {"en": "Move all Energy from 1 of your Benched Pets to your Active Pet.", "vi": "Chuyển toàn bộ năng lượng từ 1 Pet Bench sang Pet Active của bạn."}},
    {"id": "su_strategist", "kind": "supporter", "name": {"en": "Battle Strategist", "vi": "Chiến Lược Gia"}, "target": "none",
     "effects": [{"op": "damage_boost", "target": "own_turn", "amount": 10, "turns": 0}],
     "text": {"en": "During this turn, your attacks do 10 more damage to the opponent's Active Pet.", "vi": "Trong lượt này, đòn đánh của bạn gây thêm 10 sát thương lên Pet Active đối thủ."}},
    {"id": "su_guide", "kind": "supporter", "name": {"en": "Trail Guide", "vi": "Người Dẫn Đường"}, "target": "own_bench",
     "effects": [{"op": "switch_self", "target": "own_bench_chosen"}],
     "text": {"en": "Switch your Active Pet with 1 of your Benched Pets.", "vi": "Đổi Pet Active với 1 Pet Bench của bạn."}},
    {"id": "su_tracker", "kind": "supporter", "name": {"en": "Spark Tracker", "vi": "Thợ Săn Tia Sáng"}, "target": "none",
     "effects": [{"op": "energy_discard", "target": "opp_active", "cond": "heads"}],
     "text": {"en": "Flip a coin. If heads, discard a random Energy from the opponent's Active Pet.", "vi": "Tung đồng xu. Nếu ngửa, bỏ 1 năng lượng ngẫu nhiên khỏi Pet Active của đối thủ."}},
    {"id": "su_warden", "kind": "supporter", "name": {"en": "Seal Warden", "vi": "Người Giữ Ấn"}, "target": "none",
     "effects": [{"op": "ability_lock", "target": "opp_active", "turns": 1}, {"op": "retreat_mod", "target": "opp_active", "amount": 99, "turns": 1}],
     "text": {"en": "During the opponent's next turn, their Active Pet can't use Abilities or retreat.", "vi": "Trong lượt kế của đối thủ, Pet Active của họ không thể dùng Ability hoặc rút lui."}},
]
STARTER = {t["id"]: 2 for t in TRAINERS}


def build():
    pets, skills, passives, elements = load()
    cards = []
    for fam in pets["families"]:
        forms = fam["forms"]
        divine = fam["id"].startswith("fam_dv_")
        last = max(f["stage"] for f in forms)
        for i, f in enumerate(forms):
            stage = f["stage"] - 1
            ex = divine
            st = f["stats"]
            hp = r10(st["hp"] / 7.5) + 10 * stage + (20 if ex else 0)
            hp = max(40, min(hp if not ex else hp, 200))
            attacks = []
            if f.get("skill") in skills:
                attacks.append(attack_from(skills[f["skill"]], st["atk"], stage, fam["element"], ex, False))
            if f.get("ultimate") in skills:
                attacks.append(attack_from(skills[f["ultimate"]], st["atk"], stage, fam["element"], ex, True))
            # every Pet has a damaging basic attack; an ultimate is always clearly stronger than the skill
            if attacks:
                floor = max(10, r10(st["atk"] * (0.30 if attacks[0]["effects"] else 0.40)))
                attacks[0]["damage"] = max(attacks[0]["damage"], floor)
            if len(attacks) > 1:
                attacks[1]["damage"] = min(200, max(attacks[1]["damage"], attacks[0]["damage"] + 30 + 10 * stage, 90 if ex else 0))
                if attacks[1]["damage"] >= 140 and not any(e["op"] == "attack_lock_self" for e in attacks[1]["effects"]):
                    attacks[1]["effects"].append({"op": "attack_lock_self", "target": "self", "turns": 1})
            attacks = [price(a, fam["element"], ex, j == 1) for j, a in enumerate(attacks)]
            ability = None
            pas = passives.get(f.get("passive") or "")
            if pas and pas["type"] in ABILITY_NAMES and (f["stage"] == last or divine):
                name, trig, effs, text = ABILITY_NAMES[pas["type"]]
                effs = [dict(e) for e in effs]
                for e in effs:
                    if e["op"] == "energy_attach":
                        e["element"] = fam["element"]
                ability = {"id": pas["id"], "name": name, "trigger": trig, "effects": effs,
                           "text": {k: v.replace("{el}", fam["element"]) for k, v in text.items()}}
            retreat = ROLE_RETREAT[fam["role"]] + (1 if fam["role"] == "guardian" and stage >= 1 else 0) - (1 if stage == 0 and fam["role"] != "guardian" and len(forms) > 1 else 0)
            cards.append({
                "id": f["id"], "kind": "pet", "species": fam["id"], "name": {"en": f["name"], "vi": f.get("names", {}).get("vi", f["name"])},
                "element": fam["element"], "role": fam["role"], "stage": stage, "evolvesFrom": forms[i - 1]["id"] if i > 0 else None,
                "hp": hp, "ex": ex, "koPoints": CONFIG["exPoints"] if ex else CONFIG["normalPoints"],
                "weakness": weakness_of(elements, fam["element"]), "retreat": max(0, min(4, retreat)),
                "attacks": attacks, "ability": ability,
            })
    cards.extend(TRAINERS)
    out = {"version": VERSION, "config": CONFIG, "elements": [e["id"] for e in elements], "cards": cards, "starter": STARTER}
    (SEED / "arena_cards.json").write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n")
    pc = [c for c in cards if c["kind"] == "pet"]
    print(f"{VERSION}: {len(pc)} Pet cards ({sum(c['ex'] for c in pc)} EX), {len(TRAINERS)} trainer cards")
    for s in (0, 1, 2):
        xs = [c for c in pc if c["stage"] == s and not c["ex"]]
        dm = [a["damage"] for c in xs for a in c["attacks"]]
        print(f"  stage {s}: {len(xs)} cards, HP {min(c['hp'] for c in xs)}-{max(c['hp'] for c in xs)}, damage {min(dm)}-{max(dm)}")
    ex = [c for c in pc if c["ex"]]
    print(f"  EX: HP {min(c['hp'] for c in ex)}-{max(c['hp'] for c in ex)}, damage {[a['damage'] for c in ex for a in c['attacks']][:12]}")


if __name__ == "__main__":
    build()
