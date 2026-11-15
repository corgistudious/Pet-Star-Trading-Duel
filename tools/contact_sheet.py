#!/usr/bin/env python3
"""Contact sheet of pet artworks for review: python3 tools/contact_sheet.py out.png id1 id2 ..."""
import json, sys
from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
m = json.loads((ROOT / "asset-manifests/asset-manifest.json").read_text())["assets"]
out, ids = sys.argv[1], sys.argv[2:]
W, cols = 220, 5
rows = (len(ids) + cols - 1) // cols
sheet = Image.new("RGB", (W * cols, rows * (W + 22)), (232, 240, 250))
d = ImageDraw.Draw(sheet)
font = ImageFont.truetype(str(ROOT / "client-unity/Assets/Fonts/BeVietnamPro-Bold.ttf"), 15)
for i, pid in enumerate(ids):
    a = m.get("art_" + pid)
    if not a: continue
    im = Image.open(ROOT / a["filePath"]).convert("RGBA").resize((W, W))
    x, y = (i % cols) * W, (i // cols) * (W + 22)
    sheet.paste(im, (x, y), im)
    d.text((x + 6, y + W + 2), pid[4:], fill=(30, 40, 80), font=font)
sheet.save(out)
