#!/usr/bin/env python3
"""Asset Manifest + seed data -> web client static files (web-client/public).

  art/pets/{formId}.webp        512x512 detail art (trimmed, centered, 6% margin)
  art/pets/thumb/{formId}.webp  224x224 grid art
  art/ui/{assetId}.webp         backgrounds (max 1280 px), currency icons (256), pack art (max 640)
  data/catalog.json             display data (forms, skills, passives, elements, rarities, packs); server stays authoritative
  i18n/en.json, i18n/vi.json    same localization tables the Unity client uses

Never calls any paid API; deterministic and cheap to re-run. Fails if any output exceeds the
Cloudflare Pages 25 MiB per-file limit (they are all far below it).
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "asset-manifests" / "asset-manifest.json"
OUT = ROOT / "web-client" / "public"
SEED = ROOT / "database" / "seed"
LOC = ROOT / "client-unity" / "Assets" / "Resources" / "Localization"
PAGES_FILE_LIMIT = 25 * 1024 * 1024


def trim(im: Image.Image) -> Image.Image:
    bbox = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    return im.crop(bbox) if bbox else im


def fit(im: Image.Image, size: int, margin: float) -> Image.Image:
    im = trim(im)
    inner = int(size * (1 - 2 * margin))
    scale = inner / max(im.size)
    im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((size - im.width) // 2, (size - im.height) // 2))
    return canvas


def shrink(im: Image.Image, max_side: int) -> Image.Image:
    if max(im.size) <= max_side:
        return im
    s = max_side / max(im.size)
    return im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)


def save(im: Image.Image, path: Path, quality: int = 82) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    im.save(path, "WEBP", quality=quality, method=6)
    return path.stat().st_size


def build_art() -> dict:
    manifest = json.loads(MANIFEST.read_text())
    chosen: dict[str, dict] = {}
    for asset_id, a in sorted(manifest["assets"].items()):
        if a["status"] not in ("generated", "approved"):
            continue
        cat = a["category"]
        key = a["petId"] if cat == "pet" else asset_id.rsplit("_v", 1)[0]
        if cat == "ui" and not (asset_id.startswith("ui_") or asset_id.startswith("bg_")):
            continue  # probes are never shipped
        prev = chosen.get(key)
        if prev is None or a["version"] >= prev["version"]:
            chosen[key] = a  # latest approved version wins (e.g. Twinkbun line v2)
    sizes = {}
    for key, a in sorted(chosen.items()):
        src = Image.open(ROOT / a["filePath"])
        if a["category"] == "pet":
            im = src.convert("RGBA")
            sizes[f"pets/{key}"] = save(fit(im, 512, 0.06), OUT / "art" / "pets" / f"{key}.webp")
            sizes[f"pets/thumb/{key}"] = save(fit(im, 224, 0.06), OUT / "art" / "pets" / "thumb" / f"{key}.webp", 80)
        elif key.startswith("bg_"):
            sizes[f"ui/{key}"] = save(shrink(src.convert("RGB"), 1280), OUT / "art" / "ui" / f"{key}.webp", 78)
        elif key.startswith("ui_pack_"):
            sizes[f"ui/{key}"] = save(shrink(trim(src.convert("RGBA")), 640), OUT / "art" / "ui" / f"{key}.webp")
        else:
            sizes[f"ui/{key}"] = save(fit(src.convert("RGBA"), 256, 0.08), OUT / "art" / "ui" / f"{key}.webp")
    return sizes


def build_catalog() -> dict:
    pets = json.loads((SEED / "pets.json").read_text())
    skills = json.loads((SEED / "skills.json").read_text())
    rar = json.loads((SEED / "rarities.json").read_text())
    elements = json.loads((SEED / "elements.json").read_text())
    packs = json.loads((SEED / "packs.json").read_text())
    forms = {}
    families = {}
    for fam in pets["families"]:
        families[fam["id"]] = {"element": fam["element"], "role": fam["role"], "forms": [f["id"] for f in fam["forms"]]}
        for f in fam["forms"]:
            forms[f["id"]] = {
                "name": f["name"], "family": fam["id"], "stage": f["stage"], "element": fam["element"], "role": fam["role"],
                "stats": f["stats"], "skill": f.get("skill"), "ultimate": f.get("ultimate"), "passive": f.get("passive"),
                "lore": f.get("lore", {}),
            }
    return {
        "version": 1,
        "forms": forms,
        "families": families,
        "skills": {s["id"]: {"kind": s["kind"], "cost": s["cost"], "target": s["target"], "name": s["name"],
                              "effects": s.get("effects", [])} for s in skills["skills"]},
        "passives": {p["id"]: {k: v for k, v in p.items() if k != "id"} for p in skills["passives"]},
        "rarities": rar["rarities"],
        "qualities": rar.get("qualities", []),
        "finishes": rar.get("finishes", []),
        "elements": elements,
        "packs": packs,
    }


def main() -> int:
    sizes = build_art()
    data = OUT / "data"
    data.mkdir(parents=True, exist_ok=True)
    cat = build_catalog()
    (data / "catalog.json").write_text(json.dumps(cat, ensure_ascii=False, separators=(",", ":")))
    i18n = OUT / "i18n"
    i18n.mkdir(parents=True, exist_ok=True)
    for lang in ("en", "vi"):
        (i18n / f"{lang}.json").write_text((LOC / f"{lang}.json").read_text())
    missing = [f for f in cat["forms"] if f"pets/{f}" not in sizes]
    if missing:
        print("missing pet art:", missing)
        return 1
    too_big = [p for p in OUT.rglob("*") if p.is_file() and p.stat().st_size > PAGES_FILE_LIMIT]
    if too_big:
        print("files over 25 MiB:", too_big)
        return 1
    total = sum(p.stat().st_size for p in OUT.rglob("*") if p.is_file())
    biggest = max((p for p in OUT.rglob("*") if p.is_file()), key=lambda p: p.stat().st_size)
    print(f"web assets: {len(sizes)} images, {len(cat['forms'])} forms, total {total / 1048576:.1f} MiB, "
          f"largest {biggest.relative_to(OUT)} {biggest.stat().st_size / 1024:.0f} KiB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
