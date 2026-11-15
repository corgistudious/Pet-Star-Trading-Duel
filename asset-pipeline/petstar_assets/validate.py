"""Validation of generated images (decode, dimensions, alpha coverage, hash)."""
from __future__ import annotations

import hashlib
import io

from PIL import Image


def validate_png(data: bytes, expect_size: str, expect_alpha: bool) -> dict:
    w, h = (int(x) for x in expect_size.split("x"))
    img = Image.open(io.BytesIO(data))
    img.load()
    issues = []
    if img.size != (w, h):
        issues.append(f"size {img.size} != {(w, h)}")
    coverage = None
    if expect_alpha:
        if img.mode != "RGBA":
            issues.append(f"no alpha channel (mode {img.mode})")
        else:
            alpha = img.getchannel("A")
            hist = alpha.histogram()
            opaque = sum(hist[128:])
            coverage = opaque / (img.size[0] * img.size[1])
            if coverage < 0.05:
                issues.append(f"subject covers only {coverage:.1%} of canvas")
            if coverage > 0.92:
                issues.append(f"background not transparent ({coverage:.1%} opaque)")
            bbox = alpha.point(lambda a: 255 if a > 16 else 0).getbbox()
            if bbox and (bbox[0] == 0 or bbox[1] == 0 or bbox[2] == img.size[0] or bbox[3] == img.size[1]):
                issues.append(f"subject touches canvas edge (bbox {bbox}) - possible crop")
    return {
        "width": img.size[0], "height": img.size[1], "mode": img.mode,
        "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data),
        "alphaCoverage": round(coverage, 4) if coverage is not None else None,
        "issues": issues, "passed": not [i for i in issues if not i.startswith("subject touches")],
    }
