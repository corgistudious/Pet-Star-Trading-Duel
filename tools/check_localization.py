#!/usr/bin/env python3
"""Fails if any localization key used by the Unity client (static or derived from design data / server codes)
is missing in any language file, or if languages have different key sets."""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOC = ROOT / "client-unity/Assets/Resources/Localization"
SRC = ROOT / "client-unity/Assets/Scripts"
SEED = ROOT / "database/seed"

tables = {p.stem: json.loads(p.read_text()) for p in LOC.glob("*.json")}
problems = []
if set(tables) != {"en", "vi"}:
    problems.append(f"expected en+vi, found {sorted(tables)}")
keys = set().union(*[set(t) for t in tables.values()])
for code, t in tables.items():
    for k in sorted(keys - set(t)):
        problems.append(f"{code}: missing {k}")

code = "\n".join(p.read_text() for p in SRC.rglob("*.cs"))
static = set(re.findall(r'L\.T\("([a-z0-9_.]+[a-z0-9_])"', code)) | set(re.findall(r'TitleKey => "([a-z0-9_.]+)"', code))
derived = set()
skills = json.loads((SEED / "skills.json").read_text())
derived |= {f"skill.{s['id']}" for s in skills["skills"]} | {f"passive.{p['id']}" for p in skills["passives"]}
derived |= {f"target.{s['target']}" for s in skills["skills"]}
derived |= {f"status.{e['status']}" for s in skills["skills"] for e in s["effects"] if e.get("status")}
derived |= {f"status.short.{e['status']}" for s in skills["skills"] for e in s["effects"] if e.get("status")}
elements = json.loads((SEED / "elements.json").read_text())["elements"]
derived |= {f"element.{e['id']}" for e in elements} | {f"item.essence_{e['id']}" for e in elements}
pets = json.loads((SEED / "pets.json").read_text())
derived |= {f"role.{f['role']}" for f in pets["families"]}
derived |= {f"finish.{f['id']}" for f in json.loads((SEED / "rarities.json").read_text())["finishes"]}
derived |= {f"stage.{s['id']}" for s in json.loads((SEED / "pve_stages.json").read_text())["stages"]}
derived |= {p["nameKey"] for p in json.loads((SEED / "packs.json").read_text())["packTypes"]}
derived |= {f"currency.{c['id'].lower()}" for c in json.loads((SEED / "economy.json").read_text())["currencies"]}
derived |= {f"server.status.{s}" for s in ("online", "maintenance", "coming_soon", "merged")}
derived |= {f"duel.short.{a}" for a in ("Basic", "Skill", "Ultimate", "Defend")}
for k in sorted(static | derived):
    for c, t in tables.items():
        if k not in t:
            problems.append(f"{c}: used but missing {k}")

if problems:
    print("\n".join(problems))
    sys.exit(1)
print(f"localization OK: {len(keys)} keys, {len(static)} static + {len(derived)} derived references covered in {sorted(tables)}")
