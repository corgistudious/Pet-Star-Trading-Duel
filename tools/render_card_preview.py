#!/usr/bin/env python3
"""Renders review previews of the Master Pet Card Template (same layout file Unity uses).
Design-review tool only: the game renders cards live in Unity (CardView.cs) from card-layout.json.

  python3 tools/render_card_preview.py   -> docs/previews/card_rarities.png, docs/previews/card_<pet>.png
"""
from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]
LAYOUT = json.loads((ROOT / "assets-source/card-template/card-layout.json").read_text())
SEED = ROOT / "database/seed"
FONTS = ROOT / "client-unity/Assets/Fonts"
OUT = ROOT / "docs/previews"
W, H = LAYOUT["canvas"]
R = LAYOUT["regions"]
PAL = LAYOUT["palette"]

pets = {f["id"]: {**f, "family": fam} for fam in json.loads((SEED / "pets.json").read_text())["families"] for f in fam["forms"]}
skills = {s["id"]: s for s in json.loads((SEED / "skills.json").read_text())["skills"]}
elements = {e["id"]: e for e in json.loads((SEED / "elements.json").read_text())["elements"]}
rar = json.loads((SEED / "rarities.json").read_text())
rarities = {r["id"]: r for r in rar["rarities"]}
qmult = {q["id"]: q["statMultiplier"] for q in rar["qualities"]}


def font(weight: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / f"BeVietnamPro-{weight}.ttf"), size)


def hex2rgb(h: str, a: int = 255):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def mix(c1, c2, t):
    return tuple(round(a + (b - a) * t) for a, b in zip(c1, c2))


def box(r):
    x, y, w, h = r
    return [x, y, x + w, y + h]


def vertical_gradient(size, top, bottom):
    g = Image.new("RGBA", size)
    d = ImageDraw.Draw(g)
    for yy in range(size[1]):
        d.line([(0, yy), (size[0], yy)], fill=mix(top, bottom, yy / max(1, size[1] - 1)))
    return g


def rounded_mask(size, radius):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius, fill=255)
    return m


def stats(form, level, rarity, quality):
    m = (1 + 0.04 * (level - 1)) * rarities[rarity]["statMultiplier"] * qmult[quality]
    s = form["stats"]
    return round(s["hp"] * m), round(s["atk"] * m), round(s["def"] * m)


def render(pet_id: str, rarity: str, quality: str = "A", level: int = 12, finish: str = "Normal", lang: str = "en") -> Image.Image:
    f = pets[pet_id]
    fam = f["family"]
    rc = hex2rgb(rarities[rarity]["color"])
    ec = hex2rgb(elements[fam["element"]]["color"])
    card = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    # frame: rarity color gradient with soft inner bevel
    frame = vertical_gradient((W, H), mix(rc, (255, 255, 255, 255), 0.35), mix(rc, (20, 30, 60, 255), 0.25))
    card.paste(frame, (0, 0), rounded_mask((W, H), LAYOUT["cornerRadius"]))
    t = LAYOUT["frameThickness"]
    inner = vertical_gradient((W - 2 * t, H - 2 * t), hex2rgb("#FFFFFF"), hex2rgb(PAL["panelSoft"]))
    card.paste(inner, (t, t), rounded_mask(inner.size, LAYOUT["cornerRadius"] - t))
    d = ImageDraw.Draw(card)

    # name bar
    d.rounded_rectangle(box(R["nameBar"]), 40, fill=hex2rgb("#FFFFFF"), outline=mix(rc, (255, 255, 255, 255), 0.4), width=4)
    d.text((R["name"][0], R["name"][1] + R["name"][3] / 2), f["name"], font=font("ExtraBold", 66), fill=hex2rgb(PAL["ink"]), anchor="lm")
    ex, ey, ew, eh = R["element"]
    d.ellipse([ex, ey, ex + ew, ey + eh], fill=ec, outline=hex2rgb("#FFFFFF"), width=6)
    code = {"fire": "FI", "water": "WA", "nature": "NA", "earth": "EA", "lightning": "EL", "wind": "WI", "light": "LI", "dark": "DA"}[fam["element"]]
    lum = 0.299 * ec[0] + 0.587 * ec[1] + 0.114 * ec[2]
    d.text((ex + ew / 2, ey + eh / 2), code, font=font("ExtraBold", 34), fill=hex2rgb(PAL["ink"]) if lum > 170 else hex2rgb("#FFFFFF"), anchor="mm")

    # rarity / quality / level pills
    d.rounded_rectangle(box(R["rarity"]), 36, fill=rc)
    d.text((R["rarity"][0] + R["rarity"][2] / 2, R["rarity"][1] + 36), rarity, font=font("ExtraBold", 42), fill=hex2rgb("#FFFFFF"), anchor="mm")
    d.rounded_rectangle(box(R["quality"]), 36, fill=hex2rgb(PAL["sapphire"]))
    d.text((R["quality"][0] + R["quality"][2] / 2, R["quality"][1] + 36), quality, font=font("ExtraBold", 40), fill=hex2rgb("#FFFFFF"), anchor="mm")
    d.rounded_rectangle(box(R["level"]), 36, fill=hex2rgb("#FFFFFF"), outline=hex2rgb(PAL["gold"]), width=4)
    d.text((R["level"][0] + R["level"][2] / 2, R["level"][1] + 36), f"Lv.{level}", font=font("Bold", 40), fill=hex2rgb(PAL["ink"]), anchor="mm")

    # art window: element-tinted gradient + soft radial glow
    aw = R["artWindow"]
    win = vertical_gradient((aw[2], aw[3]), mix(ec, (255, 255, 255, 255), 0.78), mix(ec, (255, 255, 255, 255), 0.45))
    glow = Image.new("RGBA", win.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([aw[2] * 0.15, aw[3] * 0.15, aw[2] * 0.85, aw[3] * 0.85], fill=(255, 255, 255, 150))
    win.alpha_composite(glow.filter(ImageFilter.GaussianBlur(60)))
    card.paste(win, (aw[0], aw[1]), rounded_mask(win.size, 44))
    art_path = ROOT / "client-unity/Assets/Art/Pets" / f"{pet_id}.png"
    if art_path.exists():
        art = Image.open(art_path).convert("RGBA").resize((R["artwork"][2], R["artwork"][2]), Image.LANCZOS)
        ox = R["artwork"][0]
        oy = R["artwork"][1] + (R["artwork"][3] - art.height) // 2
        shadow = Image.new("RGBA", card.size, (0, 0, 0, 0))
        ImageDraw.Draw(shadow).ellipse([ox + 200, oy + art.height - 110, ox + art.width - 200, oy + art.height - 50], fill=(30, 50, 90, 70))
        card.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(18)))
        card.alpha_composite(art, (ox, oy))
    d = ImageDraw.Draw(card)

    # finish effect preview (Unity uses shaders; here a simple diagonal sheen)
    if finish != "Normal":
        sheen = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        sd = ImageDraw.Draw(sheen)
        colors = {"Foil": (255, 255, 255, 70), "Holographic": (180, 140, 255, 80), "Shiny": (255, 240, 160, 80), "Prismatic": (120, 255, 230, 90)}
        for i in range(-H, W, 180):
            sd.polygon([(i, aw[1] + aw[3]), (i + 90, aw[1] + aw[3]), (i + 90 + 600, aw[1]), (i + 600, aw[1])], fill=colors[finish])
        mask = Image.new("L", (W, H), 0)
        ImageDraw.Draw(mask).rounded_rectangle(box(aw), 44, fill=255)
        card.paste(Image.alpha_composite(card, sheen.filter(ImageFilter.GaussianBlur(14))), (0, 0), mask)
        d = ImageDraw.Draw(card)

    # stats
    hp, atk, df = stats(f, level, rarity, quality)
    for key, label, val, col in (("statHp", "HP", hp, "#3BB273"), ("statAtk", "ATK", atk, "#F06548"), ("statDef", "DEF", df, "#3D8BF2")):
        r = R[key]
        d.rounded_rectangle(box(r), 34, fill=hex2rgb("#FFFFFF"), outline=hex2rgb("#DCE6F5"), width=4)
        d.text((r[0] + r[2] / 2, r[1] + 42), label, font=font("Bold", 34), fill=hex2rgb(col), anchor="mm")
        d.text((r[0] + r[2] / 2, r[1] + 102), str(val), font=font("ExtraBold", 58), fill=hex2rgb(PAL["ink"]), anchor="mm")

    # skills
    d.rounded_rectangle(box(R["skillPanel"]), 40, fill=hex2rgb("#FFFFFF"), outline=hex2rgb("#DCE6F5"), width=4)

    def skill_line(sid, name_r, cost_r, size):
        s = skills[sid]
        title = sid.replace("_", " ").title()
        d.text((name_r[0], name_r[1] + name_r[3] / 2), title, font=font("Bold", size), fill=hex2rgb(PAL["ink"]), anchor="lm")
        for i in range(s["cost"]):
            cx = cost_r[0] + cost_r[2] - 26 - i * 40
            cy = cost_r[1] + cost_r[3] / 2
            d.ellipse([cx - 16, cy - 16, cx + 16, cy + 16], fill=hex2rgb(PAL["gold"]), outline=hex2rgb("#C79A2E"), width=3)

    skill_line(f["skill"], R["skillName"], R["skillCost"], 46)
    s = skills[f["skill"]]
    desc = describe(s)
    d.multiline_text((R["skillText"][0], R["skillText"][1]), wrap(desc, 46), font=font("Regular", 36), fill=hex2rgb(PAL["inkSoft"]), spacing=10)
    if f.get("ultimate"):
        d.line([R["skillPanel"][0] + 40, R["ultName"][1] - 12, R["skillPanel"][0] + R["skillPanel"][2] - 40, R["ultName"][1] - 12], fill=hex2rgb("#E3EAF6"), width=3)
        d.text((R["ultName"][0], R["ultName"][1] - 2), "ULTIMATE", font=font("Bold", 24), fill=hex2rgb(PAL["sapphire"]))
        skill_line(f["ultimate"], [R["ultName"][0], R["ultName"][1] + 18, R["ultName"][2], R["ultName"][3]], [R["ultCost"][0], R["ultCost"][1] + 18, R["ultCost"][2], R["ultCost"][3]], 40)

    # footer: stage dots + role + finish
    fx, fy, fw, fh = R["footer"]
    for i in range(fam["stages"]):
        filled = i < f["stage"]
        cx = fx + 30 + i * 44
        d.ellipse([cx - 13, fy + fh / 2 - 13, cx + 13, fy + fh / 2 + 13], fill=rc if filled else hex2rgb("#FFFFFF"), outline=rc, width=4)
    d.text((fx + fw / 2, fy + fh / 2), fam["role"].upper(), font=font("Bold", 30), fill=hex2rgb(PAL["inkSoft"]), anchor="mm")
    d.text((fx + fw - 10, fy + fh / 2), finish, font=font("SemiBold", 28), fill=hex2rgb(PAL["inkSoft"]), anchor="rm")
    return card


def describe(s):
    parts = []
    for e in s["effects"]:
        if e["type"] == "damage":
            parts.append(f"Deal {int(e['power'] * 100)}% ATK damage")
        elif e["type"] == "heal":
            parts.append(f"Heal {int(e['power'] * 100)}% ATK")
        elif e["type"] == "shield":
            parts.append(f"Shield {int(e['power'] * 100)}% {e.get('scale', 'atk').upper()}")
        elif e["type"] == "status":
            parts.append(f"{int(e['chance'] * 100)}% {e['status']} ({e['duration']}r)")
        elif e["type"] == "cleanse":
            parts.append("cleanse debuffs")
    target = s["target"].replace("_", " ")
    return f"{', '.join(parts)}. Target: {target}."


def wrap(text, n):
    out, line = [], ""
    for w in text.split():
        if len(line) + len(w) + 1 > n:
            out.append(line)
            line = w
        else:
            line = (line + " " + w).strip()
    out.append(line)
    return "\n".join(out[:3])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    order = ["N", "R", "SR", "SSR", "UR", "LR", "MR", "GR", "EX", "Secret"]
    pet_cycle = list(pets.keys())
    finishes = ["Normal", "Normal", "Foil", "Normal", "Holographic", "Normal", "Shiny", "Normal", "Prismatic", "Holographic"]
    thumbs = [render(pet_cycle[i % len(pet_cycle)], r, finish=finishes[i]).resize((270, 480), Image.LANCZOS) for i, r in enumerate(order)]
    sheet = Image.new("RGB", (270 * 5 + 60, 480 * 2 + 40), (234, 241, 251))
    for i, t in enumerate(thumbs):
        sheet.paste(t, (10 + (i % 5) * 280, 10 + (i // 5) * 500), t)
    sheet.save(OUT / "card_rarities.png", optimize=True)
    for pid in ("pet_emberkit", "pet_tidecrown", "pet_halowl"):
        render(pid, "SSR", "S", 30, "Holographic").resize((540, 960), Image.LANCZOS).save(OUT / f"card_{pid}.png", optimize=True)
    print("written", sorted(p.name for p in OUT.glob("card_*.png")))


if __name__ == "__main__":
    main()
