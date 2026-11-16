#!/usr/bin/env python3
"""Pet Star brand files for the web (Cloudflare Pages / PWA) made from the OFFICIAL logo file.

The logo is never redrawn or regenerated: every output is the original image, resized with a high-quality filter.
Aspect ratio is always kept; nothing is cropped. A non-square original is centred on the brand background.

  python3 tools/brand/make_brand_assets.py --out <site dir>          # Unity WebGL site: icons + manifest + OG image
  python3 tools/brand/make_brand_assets.py --out web-client/public --icons-only

Source (first one found): assets-source/brand/petstar-logo-original.{png,jpg,jpeg,webp}
Exit code 0 with a notice and no files when the original has not been uploaded yet (builds keep working).
"""
import argparse
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SOURCES = [ROOT / "assets-source/brand" / f"petstar-logo-original.{e}" for e in ("png", "jpg", "jpeg", "webp")]
SITE = "https://petstar.pages.dev"
NAVY = (10, 20, 64)        # deep blue of the logo background: splash / maskable background
PURPLE = (58, 32, 120)     # ribbon purple: OG gradient
THEME = "#000000"          # the game page itself is pure black around the canvas (official viewport rule)


def find_source(path=None):
    if path:
        return Path(path)
    return next((p for p in SOURCES if p.exists()), None)


def load(src):
    im = Image.open(src)
    im.load()
    return im.convert("RGBA")


def fit(im, box, bg=None):
    """Logo scaled to fit inside box (w, h) keeping its aspect ratio, centred; transparent or bg elsewhere."""
    w, h = box
    s = min(w / im.width, h / im.height)
    tw, th = max(1, round(im.width * s)), max(1, round(im.height * s))
    logo = im.resize((tw, th), Image.LANCZOS)
    canvas = Image.new("RGBA", box, bg + (255,) if bg else (0, 0, 0, 0))
    canvas.alpha_composite(logo, ((w - tw) // 2, (h - th) // 2))
    return canvas


def square(im, size, bg=None):
    return fit(im, (size, size), bg)


def rounded(im, radius_ratio=0.12):
    """Soft rounded corners (12 % of the side: only the blurred corner margin, never the lettering)."""
    s = im.width * 4
    mask = Image.new("L", (s, s * im.height // im.width), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, mask.width - 1, mask.height - 1], radius=round(mask.width * radius_ratio), fill=255)
    mask = mask.resize(im.size, Image.LANCZOS)
    out = im.copy()
    out.putalpha(Image.composite(im.getchannel("A"), Image.new("L", im.size, 0), mask))
    return out


def opaque(im, bg):
    base = Image.new("RGBA", im.size, bg + (255,))
    base.alpha_composite(im)
    return base.convert("RGB")


def gradient(size, top, bottom):
    w, h = size
    g = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / max(1, h - 1)
        g.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    return g.resize(size)


def og_image(im):
    """1200x630 share card: the whole logo (not cropped) on a blue-purple gradient with a soft gold glow."""
    W, H = 1200, 630
    card = gradient((W, H), NAVY, PURPLE).convert("RGBA")
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([W / 2 - 330, H / 2 - 300, W / 2 + 330, H / 2 + 300], fill=(232, 196, 106, 90))
    card.alpha_composite(glow.filter(ImageFilter.GaussianBlur(70)))
    logo = rounded(square(im, 560))
    card.alpha_composite(logo, ((W - 560) // 2, (H - 560) // 2))
    return card.convert("RGB")


def manifest():
    return {
        "name": "Pet Star: Trading Duel",
        "short_name": "Pet Star",
        "description": "Sưu tầm, tiến hoá và đấu thẻ thú cưng. Cần kết nối Internet. / Collect, evolve and duel with Pet cards.",
        "id": "/",
        "start_url": "/",
        "scope": "/",
        "display": "fullscreen",
        "display_override": ["fullscreen", "standalone"],
        "orientation": "any",  # lobby portrait 9:16, duels landscape 16:9 (the page asks the player to rotate)
        "background_color": "#%02x%02x%02x" % NAVY,  # Android splash: logo icon on the logo's deep blue
        "theme_color": THEME,
        "lang": "vi",
        "categories": ["games", "entertainment"],
        "icons": [
            {"src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png", "purpose": "any"},
            {"src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png", "purpose": "any"},
            {"src": "/icons/icon-maskable-192.png", "sizes": "192x192", "type": "image/png", "purpose": "maskable"},
            {"src": "/icons/icon-maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable"},
        ],
    }


def build(src, out, icons_only=False):
    im = load(src)
    if min(im.size) < 512:
        print(f"warning: logo is only {im.width}x{im.height}; 1024x1024 or larger gives sharp Retina icons", file=sys.stderr)
    out.mkdir(parents=True, exist_ok=True)
    (out / "icons").mkdir(exist_ok=True)
    written = []

    def save(img, rel, **kw):
        p = out / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        img.save(p, **kw)
        written.append(rel)

    # "any" icons: the original, full bleed (browsers/launchers show it as is)
    for n in (192, 512):
        save(square(im, n, NAVY if im.width != im.height else None), f"icons/icon-{n}.png", optimize=True)
    # maskable: logo inside the 80 % safe circle; 72 % keeps "Pet Star" and the ribbon inside every launcher mask
    for n in (192, 512):
        c = Image.new("RGBA", (n, n), NAVY + (255,))
        inner = round(n * 0.72)
        logo = rounded(square(im, inner))
        c.alpha_composite(logo, ((n - inner) // 2, (n - inner) // 2))
        save(c.convert("RGB"), f"icons/icon-maskable-{n}.png", optimize=True)
    # iOS home screen: 180x180, opaque (iOS adds its own rounded mask; no custom splash on iOS)
    save(opaque(square(im, 180, NAVY if im.width != im.height else None), NAVY), "icons/apple-touch-icon.png", optimize=True)
    if not icons_only:
        save(opaque(square(im, 180), NAVY), "apple-touch-icon.png", optimize=True)  # default path iOS also probes
        save(opaque(square(im, 32), NAVY), "favicon-32.png", optimize=True)
        save(opaque(square(im, 16), NAVY), "favicon-16.png", optimize=True)
        opaque(square(im, 48), NAVY).save(out / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)])
        written.append("favicon.ico")
        # loading / rotate screens (CSS rounds the corners); 2x and 3x Retina sizes
        for n in (256, 512, 768):
            save(square(im, n), f"brand/logo-{n}.webp", quality=88, method=6)
        save(og_image(im), "brand/og-image.jpg", quality=86, optimize=True, progressive=True)
        (out / "manifest.webmanifest").write_text(json.dumps(manifest(), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        written.append("manifest.webmanifest")
    total = sum((out / w).stat().st_size for w in written)
    print(f"brand assets from {src.relative_to(ROOT) if src.is_relative_to(ROOT) else src}: {len(written)} files, {total // 1024} KiB -> {out}")
    return written


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--src")
    ap.add_argument("--icons-only", action="store_true", help="only icons/ (for a site that keeps its own manifest)")
    a = ap.parse_args()
    src = find_source(a.src)
    if not src or not src.exists():
        print("notice: official logo not uploaded yet (assets-source/brand/petstar-logo-original.png); brand files skipped")
        return 0
    build(src, Path(a.out), a.icons_only)
    return 0


if __name__ == "__main__":
    sys.exit(main())
