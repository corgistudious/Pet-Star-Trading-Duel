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
static = set(re.findall(r'L\.T\("([a-z0-9_.]+[a-z0-9_])"(?!\s*\+)', code)) | set(re.findall(r'TitleKey => "([a-z0-9_.]+)"', code))
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
derived |= {f"ach.{a['id']}" for a in json.loads((SEED / "achievements.json").read_text())["achievements"]}
derived |= {f"album.{x}" for x in ("starter", "evolution", "standalone", "rare", "event")} | {f"album.element_{e['id']}" for e in elements}
derived |= {f"hub.{x}" for x in ("home", "collection", "market", "social", "battle")}
derived |= {f"web.role_{x}" for x in ("leader", "officer", "member")}
_missions = json.loads((SEED / "missions.json").read_text())
derived |= {f"mission.{m['id']}" for m in _missions["daily"] + _missions["weekly"]}
derived |= {f"market.status.{x}" for x in ("active", "sold", "cancelled", "expired", "removed", "settled", "expired_no_bids")}
derived |= {f"market.kind.{x}" for x in ("listing", "auction")}
derived |= {f"market.warning.{x}" for x in ("price_far_below_reference", "price_far_above_reference")}
derived |= {f"auction.status.{x}" for x in ("active", "settled", "cancelled", "expired_no_bids")}
derived |= {f"confidence.{x}" for x in ("initial_only", "low", "medium", "high")}
derived |= {f"passport.lock.{x}" for x in ("free", "player_locked", "listed", "in_auction", "under_review")}
derived |= {f"passport.upgrade.{x}" for x in ("level_up", "skill_upgrade")}
derived |= {f"report.reason.{x}" for x in ("spam", "harassment", "inappropriate", "scam", "cheating", "other")}
derived |= {f"feed.type.{x}" for x in ("text", "card", "pack_opening", "evolution", "duel", "achievement", "album", "share")}
derived |= {f"notif.{x}" for x in ("market_sold", "market_bought", "listing_expired", "wishlist_match", "auction_outbid", "auction_won", "auction_sold",
            "auction_ended_no_sale", "post_liked", "post_commented", "new_follower", "friend_request", "friend_accepted", "duel_challenge",
            "achievement_unlocked", "admin_notice")}
derived |= {f"battle.mode.{x}" for x in ("pve", "ranked", "friend")} | {f"origin.{x}" for x in ("pack", "pve_reward", "event", "admin")}
derived |= {f"friends.status.{x}" for x in ("friends", "incoming", "outgoing")} | {f"moderation.{x}" for x in ("dismiss", "hide", "restrict")}
derived |= {"item.training_scroll", "mail.admin"}
sys.path.insert(0, str(ROOT / "tools"))
from localization_duel import STATUSES, END_REASONS, LOG_TYPES, MODES  # noqa: E402
from localization_team import DERIVED as TEAM_DERIVED  # noqa: E402
derived |= TEAM_DERIVED
derived |= {f"tduel.status.{x}" for x in STATUSES} | {f"tduel.end.{x}" for x in END_REASONS} | {f"tduel.log.{x}" for x in LOG_TYPES} | {f"tduel.mode.{x}" for x in MODES}
for k in sorted(static | derived):
    for c, t in tables.items():
        if k not in t:
            problems.append(f"{c}: used but missing {k}")

if problems:
    print("\n".join(problems))
    sys.exit(1)
print(f"localization OK: {len(keys)} keys, {len(static)} static + {len(derived)} derived references covered in {sorted(tables)}")
