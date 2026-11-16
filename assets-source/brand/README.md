# Official logo

Put the **original** Pet Star: Trading Duel logo file here, unchanged, as:

    assets-source/brand/petstar-logo-original.png      (or .jpg)

Square, 1024×1024 or larger. It is never redrawn or regenerated. Everything else is made from it automatically:

| Where | What | Made by |
|---|---|---|
| Unity (all screens) | sprite `ui/brand_logo` (exact copy of the file) | `CloudBuild.PreExport` → `PetStarBuild.CopyBrandLogo` |
| petstar.pages.dev | favicon (ico/16/32), PWA icons 192/512 + maskable 192/512, Apple Touch Icon 180, loading-screen logo 256/512/768 (WebP), Open Graph 1200×630, `manifest.webmanifest` | `tools/brand/make_brand_assets.py` in the "Unity WebGL publish" workflow |
| React web client | `public/icons/*` | same script (`--icons-only`) in the web workflow |

Upload from an iPhone: github.com → repository → switch to the working branch → `assets-source/brand` →
**Add file → Upload files** → choose the logo → rename it to `petstar-logo-original.png` → Commit.
