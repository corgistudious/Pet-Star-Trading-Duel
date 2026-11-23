#!/usr/bin/env python3
"""Builds database/seed/team_battle.json: the data for Pet Star's 3v3 turn-based battle (Battle 3v3).

Every Pet form gets a battle kit (Basic attack, Skill, and Ultimate when the form has one) derived from its existing
skill / ultimate / passive in skills.json, so the Pet Card system is unchanged. All numbers are integers (percent /
basis points) so the C# server engine and the TypeScript preview port compute identical results. Bump VERSION when
the output changes: finished matches keep the version they were played with (replays stay exact)."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SEED = ROOT / "database/seed"
VERSION = "team-1"

CONFIG = {
    "teamSize": 3, "startEn": 50, "maxEn": 100, "basicEnGain": 15, "roundEnGain": 10,
    "maxRounds": 30, "damageScalePct": 160, "critDamagePct": 150, "strongPct": 150, "weakPct": 75,
    "guardReductionPct": 50,
    # statuses
    "burnPct": 6, "poisonPct": 5, "poisonMaxStacks": 3, "regenPct": 6, "hastePct": 30, "weakenPct": 25,
    "counterPct": 20, "reducePassivePct": 10, "drainPassivePct": 10, "regenPassivePct": 4, "startEnPassive": 20,
    "hardCcTurns": 1, "ccImmunityTurns": 1, "shieldCapPct": 60,
    # timers / PvP
    "planSeconds": 30, "timeoutsToForfeit": 3,
}

# derived magic stats per role (MAG = ATK × pct, RES = DEF × pct); damage type of the role's attacks
ROLE = {
    "striker": {"magPct": 60, "resPct": 80, "dmg": "phys"},
    "guardian": {"magPct": 50, "resPct": 110, "dmg": "phys"},
    "controller": {"magPct": 100, "resPct": 100, "dmg": "mag"},
    "mage": {"magPct": 125, "resPct": 95, "dmg": "mag"},
    "support": {"magPct": 115, "resPct": 115, "dmg": "mag"},
}
TARGET = {"enemy_single": "enemy_single", "enemy_all": "enemy_all", "enemy_front_row": "enemy_all", "ally_all": "ally_all",
          "self": "self", "ally_lowest_hp": "ally_lowest"}

BASIC_NAMES = {
    "phys": {"en": "Strike", "vi": "Đánh thường"},
    "mag": {"en": "Spark Bolt", "vi": "Tia Sáng"},
}


def pct(x):
    return int(round(x * 100))


def convert(skill, element, dmg_type):
    kind = skill["kind"]
    target = TARGET.get(skill["target"], "enemy_single")
    row = skill["target"] == "enemy_front_row"
    cost = {1: 20, 2: 30, 3: 40, 4: 50, 5: 60}.get(skill["cost"], 30) if kind == "skill" else {4: 50, 5: 60}.get(skill["cost"], 60)
    cooldown = (1 if cost >= 30 else 0) if kind == "skill" else 3
    effects = []
    for e in skill["effects"]:
        t = e["type"]
        if t == "damage":
            power = pct(e["power"])
            if row:
                power = int(power * 85 / 100)
            effects.append({"op": "damage", "power": power})
        elif t == "heal":
            effects.append({"op": "heal", "power": pct(e["power"])})
            if element == "nature" or target == "ally_all":
                effects.append({"op": "status", "status": "regen", "chance": 100, "turns": 2, "on": "ally"})
        elif t == "shield":
            effects.append({"op": "shield", "power": pct(e["power"]), "scale": e.get("scale", "def")})
            if element in ("wind", "lightning", "fire"):
                effects.append({"op": "status", "status": "haste", "chance": 100, "turns": 2, "on": "ally"})
        elif t == "cleanse":
            effects.append({"op": "cleanse"})
        elif t == "energy":
            effects.append({"op": "energy", "amount": 10 * int(e["power"])})
        elif t == "status":
            st, ch, dur = e["status"], pct(e.get("chance", 1.0)), int(e.get("duration", 2))
            if st == "stun":
                effects.append({"op": "status", "status": "freeze" if element in ("water", "wind", "earth") else "stun", "chance": ch, "turns": 1, "on": "enemy"})
            elif st in ("poison", "burn"):
                effects.append({"op": "status", "status": st, "chance": ch, "turns": max(2, dur), "on": "enemy"})
            elif st == "weaken":
                if element in ("light", "dark"):
                    effects.append({"op": "status", "status": "silence", "chance": ch, "turns": 1, "on": "enemy"})
                else:
                    effects.append({"op": "status", "status": "weaken", "chance": ch, "turns": max(1, dur), "on": "enemy"})
            elif st == "taunt":
                effects.append({"op": "status", "status": "taunt", "chance": 100, "turns": max(1, dur), "on": "self"})
                effects.append({"op": "status", "status": "counter", "chance": 100, "turns": max(1, dur), "on": "self"})
    if not any(x["op"] == "damage" for x in effects) and target.startswith("enemy"):
        effects.insert(0, {"op": "damage", "power": 80})
    return {"id": skill["id"], "name": skill["name"], "kind": kind, "cost": cost, "cooldown": cooldown, "priority": int(skill.get("priority", 0)),
            "target": target, "dmg": dmg_type, "effects": effects}


PASSIVE = {
    "regen_round_end": {"op": "regen", "pct": CONFIG["regenPassivePct"]},
    "battle_start_energy": {"op": "start_en", "amount": CONFIG["startEnPassive"]},
    "lifesteal": {"op": "drain", "pct": CONFIG["drainPassivePct"]},
    "damage_reduction": {"op": "reduce", "pct": CONFIG["reducePassivePct"]},
    "low_hp_stat_bonus": {"op": "rage", "pct": 20, "thresholdPct": 50},
    "heal_bonus": {"op": "heal_bonus", "pct": 15},
}


def build():
    pets = json.loads((SEED / "pets.json").read_text())
    skills_all = json.loads((SEED / "skills.json").read_text())
    elements = json.loads((SEED / "elements.json").read_text())["elements"]
    skills = {s["id"]: s for s in skills_all["skills"]}
    passives = {p["id"]: p for p in skills_all["passives"]}
    out_skills = {}
    for dmg in ("phys", "mag"):
        out_skills[f"basic_{dmg}"] = {"id": f"basic_{dmg}", "name": BASIC_NAMES[dmg], "kind": "basic", "cost": 0, "cooldown": 0, "priority": 0,
                                      "target": "enemy_single", "dmg": dmg, "effects": [{"op": "damage", "power": 100}]}
    kits = {}
    for fam in pets["families"]:
        role = ROLE[fam["role"]]
        for f in fam["forms"]:
            kit = {"basic": f"basic_{role['dmg']}", "skill": None, "ultimate": None, "passive": None, "element": fam["element"], "role": fam["role"]}
            for slot in ("skill", "ultimate"):
                sid = f.get(slot)
                if sid in skills:
                    key = f"{sid}@{fam['element']}"
                    if key not in out_skills:
                        conv = convert(skills[sid], fam["element"], role["dmg"])
                        conv["id"] = key
                        out_skills[key] = conv
                    kit[slot] = key
            pas = passives.get(f.get("passive") or "")
            if pas and pas["type"] in PASSIVE:
                kit["passive"] = dict(PASSIVE[pas["type"]], id=pas["id"])
            kits[f["id"]] = kit
    out = {
        "version": VERSION, "config": CONFIG, "roles": ROLE,
        "strong": {e["id"]: e["strongAgainst"] for e in elements},
        "skills": out_skills, "kits": kits,
    }
    (SEED / "team_battle.json").write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n")
    print(f"{VERSION}: {len(kits)} Pet kits, {len(out_skills)} skills")


if __name__ == "__main__":
    build()
