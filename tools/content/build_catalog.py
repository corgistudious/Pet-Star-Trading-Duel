#!/usr/bin/env python3
"""Builds database/seed/pets.json + skills.json for the full catalog:
5 hand-tuned prototype lines (kept as-is) + the lines in families.py.
Guarantees: 200 forms (50x3 + 20x2 + 10x1), unique ids/names, every form has a mechanically distinct skill kit,
stats derived from role/stage with deterministic per-family variance (balance baseline, configurable)."""
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SEED = ROOT / "database/seed"
sys.path.insert(0, str(Path(__file__).parent))
from families import F  # noqa: E402

PROTO_IDS = {"fam_emberkit", "fam_puddlotl", "fam_zipmunk", "fam_halowl", "fam_gloomoth"}
PROTO_SKILL_NAMES = {
    "ember_pounce": ("Ember Pounce", "Vồ Than Hồng"), "cinder_claws": ("Cinder Claws", "Vuốt Tro Lửa"),
    "lynx_flare_rush": ("Lynx Flare Rush", "Lao Lửa Linh Miêu"), "pyre_fang": ("Pyre Fang", "Nanh Hỏa Đài"),
    "sunfall_blaze": ("Sunfall Blaze", "Mặt Trời Rực Cháy"), "bubble_guard": ("Bubble Guard", "Khiên Bong Bóng"),
    "ripple_wall": ("Ripple Wall", "Tường Gợn Sóng"), "tidal_taunt": ("Tidal Taunt", "Thủy Triều Khiêu Chiến"),
    "coral_bastion": ("Coral Bastion", "Thành Lũy San Hô"), "crown_of_tides": ("Crown of Tides", "Vương Miện Thủy Triều"),
    "static_nibble": ("Static Nibble", "Cắn Tĩnh Điện"), "chain_spark": ("Chain Spark", "Chuỗi Tia Điện"),
    "storm_acorn": ("Storm Acorn", "Hạt Dẻ Bão Tố"), "dawn_hymn": ("Dawn Hymn", "Khúc Ca Bình Minh"),
    "halo_sanctuary": ("Halo Sanctuary", "Thánh Địa Hào Quang"), "dusk_powder": ("Dusk Powder", "Phấn Hoàng Hôn"),
    "eclipse_veil": ("Eclipse Veil", "Màn Che Nhật Thực"),
}

BASE = {  # stage-1 baseline per role
    "striker": dict(hp=430, atk=70, def_=38, spd=80, crit=0.09),
    "guardian": dict(hp=540, atk=48, def_=60, spd=52, crit=0.05),
    "mage": dict(hp=420, atk=74, def_=40, spd=70, crit=0.07),
    "support": dict(hp=470, atk=62, def_=48, spd=66, crit=0.05),
    "controller": dict(hp=440, atk=60, def_=44, spd=84, crit=0.06),
}
STAGE_MULT = {1: 1.0, 2: 1.45, 3: 1.9}
STANDALONE_MULT = 1.55
STATUS_BY_ELEMENT = {"fire": "burn", "water": "weaken", "nature": "poison", "earth": "weaken", "lightning": "stun",
                     "wind": "stun", "light": None, "dark": "poison"}
MOVE = {  # (en, vi) move words per role, for skill / ultimate display names
    "striker": [("Strike", "Đòn"), ("Rush", "Lao Tới"), ("Fang", "Nanh"), ("Slash", "Chém")],
    "guardian": [("Bulwark", "Thành Lũy"), ("Guard", "Hộ Thể"), ("Aegis", "Khiên Thần"), ("Ward", "Phòng Ngự")],
    "mage": [("Burst", "Bùng Nổ"), ("Nova", "Tân Tinh"), ("Torrent", "Cuồng Lưu"), ("Spell", "Phép")],
    "support": [("Blessing", "Phúc Lành"), ("Hymn", "Thánh Ca"), ("Mend", "Hồi Phục"), ("Grace", "Ân Điển")],
    "controller": [("Snare", "Trói Buộc"), ("Hex", "Ếm"), ("Jolt", "Chấn"), ("Lock", "Khóa")],
}
ULT_WORD = ("Ultimate", "Tuyệt Kỹ")


def h(s: str, mod: int) -> int:
    return int(hashlib.sha256(s.encode()).hexdigest(), 16) % mod


def stats(fid, role, stage, standalone):
    b = BASE[role]
    m = STANDALONE_MULT if standalone else STAGE_MULT[stage]
    var = lambda k: 1 + (h(fid + k, 15) - 7) / 100  # ±7% family identity, same across its stages
    return {
        "hp": round(b["hp"] * m * var("hp")), "atk": round(b["atk"] * m * var("atk")), "def": round(b["def_"] * m * var("def")),
        "spd": round(b["spd"] * (1 + 0.06 * ((stage if not standalone else 2) - 1)) * var("spd")),
        "crit": round(b["crit"] + (0.01 if stage == 3 else 0) + (h(fid + "crit", 3) - 1) * 0.01, 2),
    }


def kit(fid, role, element, stage, standalone, variant):
    """Returns (skill, ultimate|None). Variant (0-3) selects a family-specific pattern."""
    st = STATUS_BY_ELEMENT[element]
    lvl = (stage if not standalone else 2.5)
    p = lambda base, step=0.15: round(base + step * (lvl - 1) + 0.05 * variant, 2)
    ch = lambda base: round(min(0.9, base + 0.05 * variant + 0.1 * (lvl - 1)), 2)
    status = (lambda c, d=2: [{"type": "status", "status": st, "chance": ch(c), "duration": 1 if st == "stun" else d}]) if st else (lambda c, d=2: [])
    if role == "striker":
        targets = ["enemy_single", "enemy_single", "enemy_front_row", "enemy_single"]
        skill = {"cost": 1 if lvl < 2 else 2, "priority": 0, "target": targets[variant],
                 "effects": [{"type": "damage", "power": p(1.45 if targets[variant] == "enemy_single" else 0.95)}] + status(0.2)}
        ult = {"cost": 4 + (variant % 2), "priority": 0, "target": ["enemy_front_row", "enemy_all", "enemy_single", "enemy_all"][variant],
               "effects": [{"type": "damage", "power": p([1.5, 1.2, 2.6, 1.25][variant])}] + status(0.3)}
    elif role == "guardian":
        skill = {"cost": 1 if lvl < 2 else 2, "priority": 1, "target": ["self", "ally_all", "self", "ally_lowest_hp"][variant],
                 "effects": [{"type": "shield", "power": p([1.5, 0.6, 1.3, 1.1][variant], 0.2), "scale": "def"}]}
        if variant == 2:
            skill["effects"].append({"type": "status", "status": "taunt", "chance": 1.0, "duration": 1})
        ult = {"cost": 4 + (variant % 2), "priority": 1, "target": ["self", "enemy_all", "ally_all", "self"][variant],
               "effects": [[{"type": "status", "status": "taunt", "chance": 1.0, "duration": 2}, {"type": "shield", "power": p(2.0, 0.2), "scale": "def"}],
                           [{"type": "damage", "power": p(0.9)}, {"type": "shield", "power": p(0.7, 0.1), "scale": "def", "targetOverride": "ally_all"}],
                           [{"type": "shield", "power": p(1.0, 0.15), "scale": "def"}, {"type": "cleanse"}],
                           [{"type": "shield", "power": p(2.6, 0.2), "scale": "maxhp" if False else "def"}, {"type": "heal", "power": p(0.8)}]][variant]}
    elif role == "mage":
        skill = {"cost": 2, "priority": 0, "target": ["enemy_all", "enemy_front_row", "enemy_single", "enemy_all"][variant],
                 "effects": [{"type": "damage", "power": p([0.75, 0.95, 1.6, 0.7][variant])}] + status([0.35, 0.3, 0.45, 0.5][variant])}
        ult = {"cost": 5 if variant != 2 else 4, "priority": 0, "target": ["enemy_all", "enemy_all", "enemy_single", "enemy_front_row"][variant],
               "effects": [{"type": "damage", "power": p([1.4, 1.3, 2.8, 1.8][variant])}] + status(0.4)}
    elif role == "support":
        skill = {"cost": 2, "priority": 0, "target": ["ally_lowest_hp", "ally_all", "ally_lowest_hp", "ally_all"][variant],
                 "effects": [{"type": "heal", "power": p([1.5, 0.6, 1.3, 0.5][variant])}] + ([{"type": "cleanse"}] if variant == 2 else [])
                 + ([{"type": "shield", "power": p(0.4, 0.05), "scale": "atk"}] if variant == 3 else [])}
        ult = {"cost": 4 + (variant % 2), "priority": 0, "target": "ally_all",
               "effects": [[{"type": "heal", "power": p(1.0)}, {"type": "cleanse"}],
                           [{"type": "heal", "power": p(0.8)}, {"type": "shield", "power": p(0.6, 0.1), "scale": "atk"}],
                           [{"type": "heal", "power": p(1.2)}],
                           [{"type": "heal", "power": p(0.7)}, {"type": "energy", "power": 2}]][variant]}
    else:  # controller
        cs = st if st in ("stun", "weaken", "poison") else ["stun", "weaken", "stun", "weaken"][variant]
        ctrl = lambda c: [{"type": "status", "status": cs, "chance": ch(c), "duration": 1 if cs == "stun" else 2}]
        skill = {"cost": 1 if lvl < 2 else 2, "priority": 0, "target": ["enemy_single", "enemy_front_row", "enemy_single", "enemy_all"][variant],
                 "effects": [{"type": "damage", "power": p([1.15, 0.85, 1.0, 0.6][variant])}] + ctrl([0.3, 0.2, 0.35, 0.15][variant])}
        ult = {"cost": 4 + (variant % 2), "priority": 0, "target": ["enemy_single", "enemy_all", "enemy_front_row", "enemy_all"][variant],
               "effects": [{"type": "damage", "power": p([2.0, 1.0, 1.3, 1.1][variant])}] + ctrl([0.55, 0.3, 0.4, 0.35][variant])}
    has_ult = standalone or stage >= 2
    return skill, (ult if has_ult else None)


def passive(fid, role, stage, standalone, variant):
    lvl = stage if not standalone else 2
    scale = [1.0, 1.0, 1.6, 2.2][lvl]
    options = {
        "striker": [("stat_bonus", "atk", 0.05), ("stat_bonus", "crit", 0.05), ("low_hp_stat_bonus", "atk", 0.1), ("lifesteal", None, 0.06)],
        "guardian": [("stat_bonus", "def", 0.05), ("damage_reduction", None, 0.05), ("regen_round_end", None, 0.025), ("stat_bonus", "def", 0.06)],
        "mage": [("stat_bonus", "atk", 0.05), ("battle_start_energy", None, 1), ("stat_bonus", "crit", 0.04), ("lifesteal", None, 0.05)],
        "support": [("heal_bonus", None, 0.08), ("regen_round_end", None, 0.02), ("battle_start_energy", None, 1), ("stat_bonus", "def", 0.04)],
        "controller": [("stat_bonus", "spd", 0.05), ("stat_bonus", "atk", 0.04), ("damage_reduction", None, 0.04), ("stat_bonus", "spd", 0.07)],
    }[role]
    t, stat, base = options[(variant + lvl) % 4]
    pd = {"type": t}
    if stat:
        pd["stat"] = stat
    if t == "battle_start_energy":
        pd["amount"] = 1
    else:
        pd["pct"] = round(base * scale, 3)
    if t == "low_hp_stat_bonus":
        pd["threshold"] = 0.5
    return pd


def main():
    pets = json.loads((SEED / "pets.json").read_text())
    skills_doc = json.loads((SEED / "skills.json").read_text())
    proto = [f for f in pets["families"] if f["id"] in PROTO_IDS]
    skills = [s for s in skills_doc["skills"] if s["id"] in PROTO_SKILL_NAMES]
    passives = [p for p in skills_doc["passives"] if not p["id"].startswith("p_")]
    for s in skills:
        en, vi = PROTO_SKILL_NAMES[s["id"]]
        s["name"] = {"en": en, "vi": vi}
    families = list(proto)
    used_names = {f["name"].lower() for fam in proto for f in fam["forms"]}
    signatures = {}

    def sig(sk):
        return json.dumps({k: sk[k] for k in ("cost", "priority", "target", "effects")}, sort_keys=True)

    for s in skills:
        signatures[sig(s)] = s["id"]

    for (key, element, role, names, identity, arts, lore_en, lore_vi) in F:
        fid = "fam_" + key
        n = len(names)
        standalone = n == 1
        variant = h(fid, 4)
        forms = []
        for i, name in enumerate(names):
            stage = i + 1
            if name.lower() in used_names:
                raise SystemExit(f"duplicate pet name {name}")
            used_names.add(name.lower())
            pid = "pet_" + name.lower()
            sk, ult = kit(fid, role, element, stage, standalone, variant)
            ids = {}
            for kind, k in (("skill", sk), ("ultimate", ult)):
                if k is None:
                    continue
                k = {"id": f"{name.lower()}_{kind}", "kind": kind, **k}
                bump = 0
                while sig(k) in signatures:  # guarantee mechanically distinct kits
                    bump += 1
                    k["effects"][0]["power"] = round(k["effects"][0].get("power", 1) + 0.05, 2)
                    if bump > 20:
                        raise SystemExit("cannot make skill unique: " + k["id"])
                signatures[sig(k)] = k["id"]
                mv = MOVE[role][(variant + stage + (kind == "ultimate")) % 4]
                k["name"] = {"en": f"{name} {mv[0]}" if kind == "skill" else f"{name} {ULT_WORD[0]}: {mv[0]}",
                             "vi": f"{mv[1]} {name}" if kind == "skill" else f"{ULT_WORD[1]} {mv[1]} {name}"}
                skills.append(k)
                ids[kind] = k["id"]
            pas = {"id": f"p_{name.lower()}", **passive(fid, role, stage, standalone, variant)}
            passives.append(pas)
            if standalone or stage == 1:
                le, lv = lore_en, lore_vi
            elif stage == 2:
                le, lv = f"{lore_en} As {name}, it has grown bolder and stronger.", f"{lore_vi} Khi trở thành {name}, nó dạn dĩ và mạnh mẽ hơn."
            else:
                le, lv = f"{lore_en} In its final form, {name} protects everything it loves.", f"{lore_vi} Ở hình thái cuối, {name} bảo vệ mọi điều nó yêu quý."
            forms.append({"id": pid, "name": name, "stage": stage, "stats": stats(fid, role, stage, standalone),
                          "skill": ids["skill"], "ultimate": ids.get("ultimate"), "passive": pas["id"],
                          "art": arts[i], "lore": {"en": le, "vi": lv}})
        families.append({"id": fid, "element": element, "role": role, "stages": n, "identity": identity, "forms": forms})

    counts = {k: sum(1 for f in families if f["stages"] == k) for k in (1, 2, 3)}
    total = sum(f["stages"] for f in families)
    assert counts == {1: 10, 2: 20, 3: 50}, counts
    assert total == 200, total
    per_el = {}
    for f in families:
        per_el[f["element"]] = per_el.get(f["element"], 0) + 1
    pets["families"] = families
    pets["notes"] = "Full catalog: 80 lines / 200 forms (50x3 + 20x2 + 10 standalone). Prototype lines are hand-tuned; others generated by tools/content/build_catalog.py from tools/content/families.py."
    skills_doc["skills"] = skills
    skills_doc["passives"] = passives
    (SEED / "pets.json").write_text(json.dumps(pets, ensure_ascii=False, indent=2) + "\n")
    (SEED / "skills.json").write_text(json.dumps(skills_doc, ensure_ascii=False, indent=2) + "\n")
    print(f"families={len(families)} forms={total} by stages={counts} elements={per_el} skills={len(skills)} passives={len(passives)}")


if __name__ == "__main__":
    main()
