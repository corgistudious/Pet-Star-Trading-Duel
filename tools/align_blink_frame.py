# Aligns an edited "blink" frame to the base frame (scale + shift search on the head) and pastes only the eye band,
# so swapping base <-> blink does not jitter.
import sys
import numpy as np
from PIL import Image
base_p, blink_p, out_p = sys.argv[1:4]
B = Image.open(base_p).convert('RGBA'); K = Image.open(blink_p).convert('RGBA')
W, H = B.size
def gray(im, s=4):
    a = np.asarray(im.resize((W // s, H // s)).convert('L')).astype(float); return a
gb = gray(B)
head = (slice(int(H/4*0.05), int(H/4*0.42)), slice(int(W/4*0.1), int(W/4*0.9)))
best = None
for sc in np.arange(0.92, 1.09, 0.01):
    ks = K.resize((int(W*sc), int(H*sc)), Image.LANCZOS)
    canvas = Image.new('RGBA', (W, H), (0,0,0,0)); canvas.paste(ks, ((W-ks.width)//2, (H-ks.height)//2))
    gk = gray(canvas)
    for dx in range(-12, 13, 2):
        for dy in range(-12, 13, 2):
            sh = np.roll(np.roll(gk, dy, 0), dx, 1)
            err = np.abs(sh[head] - gb[head]).mean()
            if best is None or err < best[0]: best = (err, sc, dx*4, dy*4)
err, sc, dx, dy = best
for ddx in range(-6, 7, 1):
    pass
ks = K.resize((int(W*sc), int(H*sc)), Image.LANCZOS)
al = Image.new('RGBA', (W, H), (0,0,0,0)); al.paste(ks, ((W-ks.width)//2 + dx, (H-ks.height)//2 + dy))
A = np.asarray(B).astype(int); L = np.asarray(al).astype(int)
diff = np.abs(A[..., :3] - L[..., :3]).sum(2) * (A[..., 3] > 200)
# eye band: rows in the upper face with the largest difference
import os
band = os.environ.get("BAND")
rows = diff[: H // 2].sum(1); r0 = int(np.argmax(rows)) if not band else int(band)
half = int(os.environ.get("HALF", "70"))
band_top, band_bot = max(0, r0 - half), min(H, r0 + half)
cols = diff[band_top:band_bot].sum(0); nz = np.where(cols > cols.max() * 0.15)[0]
c0, c1 = max(0, nz.min() - 20), min(W, nz.max() + 20)
out = B.copy()
patch = al.crop((c0, band_top, c1, band_bot))
mask = Image.new('L', patch.size, 0)
m = np.zeros((patch.size[1], patch.size[0]), dtype=float)
yy, xx = np.mgrid[0:patch.size[1], 0:patch.size[0]]
fx = np.minimum(xx, patch.size[0]-1-xx) / 20.0; fy = np.minimum(yy, patch.size[1]-1-yy) / 20.0
m = np.clip(np.minimum(fx, fy), 0, 1) * 255
mask = Image.fromarray(m.astype('uint8'))
out.paste(patch, (c0, band_top), mask)
out.save(out_p)
print(f"aligned scale={sc:.2f} dx={dx} dy={dy} err={err:.1f} band=({c0},{band_top})-({c1},{band_bot})")
