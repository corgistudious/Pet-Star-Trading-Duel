#!/usr/bin/env python3
"""Asset Manifest -> Unity import step (Optimization stage of the pipeline).

Reads asset-manifests/asset-manifest.json and writes optimized copies into the Unity project:
  pets:      trimmed to the alpha bounding box, centered on a square canvas with 6% margin, 768x768 PNG
  ui icons:  trimmed, centered with a 12% safe margin, 512x512 PNG
Also writes client-unity/Assets/Art/asset-index.json (asset id -> Unity path, sha256 of source).
Never calls any API; re-running is cheap and deterministic.
"""
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "asset-manifests" / "asset-manifest.json"
UNITY_ART = ROOT / "client-unity" / "Assets" / "Art"

TARGETS = {
    "pet": {"dir": "Pets", "size": 640, "margin": 0.06},
    "ui": {"dir": "UI/Currency", "size": 512, "margin": 0.12},
}


def fit(src: Path, size: int, margin: float) -> Image.Image:
    im = Image.open(src).convert("RGBA")
    bbox = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    if bbox:
        im = im.crop(bbox)
    inner = int(size * (1 - 2 * margin))
    scale = inner / max(im.size)
    im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((size - im.width) // 2, (size - im.height) // 2))
    return canvas


def main() -> int:
    manifest = json.loads(MANIFEST.read_text())
    index = {}
    for asset_id, a in sorted(manifest["assets"].items()):
        if a["status"] not in ("generated", "approved"):
            continue
        cat = a["category"]
        if cat == "ui" and not asset_id.startswith("ui_"):
            continue  # probe images are not shipped
        src = ROOT / a["filePath"]
        key = a["petId"] if cat == "pet" else asset_id
        if asset_id.startswith("bg_"):
            out = UNITY_ART / "UI/Backgrounds" / f"{key}.png"
            out.parent.mkdir(parents=True, exist_ok=True)
            Image.open(src).convert("RGB").save(out, optimize=True)  # full-bleed, keep native 1536 px
        elif asset_id.startswith("ui_pack_"):
            out = UNITY_ART / "UI/Packs" / f"{key}.png"
            out.parent.mkdir(parents=True, exist_ok=True)
            im = Image.open(src).convert("RGBA")
            im.crop(im.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()).save(out, optimize=True)
        else:
            t = TARGETS[cat]
            out = UNITY_ART / t["dir"] / f"{key}.png"
            out.parent.mkdir(parents=True, exist_ok=True)
            fit(src, t["size"], t["margin"]).save(out, optimize=True)
        index[key] = {"path": str(out.relative_to(ROOT / "client-unity")), "category": cat, "sourceSha256": a["sha256"],
                      "version": a["version"], "status": a["status"]}
        print(f"{key:40s} -> {out.relative_to(ROOT)} ({out.stat().st_size // 1024} KB)")
    (UNITY_ART / "asset-index.json").write_text(json.dumps(index, indent=2) + "\n")
    # design data the client needs for display (server stays authoritative)
    res = ROOT / "client-unity" / "Assets" / "Resources" / "Data"
    res.mkdir(parents=True, exist_ok=True)
    for name in ("pets.json", "skills.json", "elements.json", "rarities.json", "economy.json"):
        (res / name).write_text((ROOT / "database" / "seed" / name).read_text())
    (res / "card-layout.json").write_text((ROOT / "assets-source" / "card-template" / "card-layout.json").read_text())
    return 0


if __name__ == "__main__":
    sys.exit(main())
