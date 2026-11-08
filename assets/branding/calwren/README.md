# Calwren brand assets

Approved production assets for the Calwren identity (v2.0.0). Source of truth for the
brand foundation is [docs/reference/brand-calwren.md](../../../docs/reference/brand-calwren.md);
the decision is [ADR 0004](../../../docs/adr/0004-rebrand-independent-product.md).

## Approved mark

**Fluent Binding** (PO-approved 2026-11-08): a warm wren perched on the ink binding bar of a
paper calendar page, with a 7-column grid and one terracotta rounded date cell under the feet.
Fluent 2 cues: rounded surfaces, layered shadow, one accent fill, no gradient/gloss.

Palette: paper `#F1E7D8`, page `#FBF6EC`, ink `#26211C`, terracotta `#B55136`, stone `#9C8873`.
Contrast: ink/paper 13.03:1; terracotta/paper 4.09:1.

## Contents

- `app/` — 512, 300 (Store), 256, 150, 100, 50 px app tiles.
- `tray/` — 16, 20, 24, 32, 48 px native renders (transparent corners).
- `windows/calwren.ico` — 16/24/32/48/64/128/256; PNG rasters alongside.
- `wordmark/` — lowercase `calwren`, 1024/512/256, light/dark.
- `lockup/` — icon + wordmark, 1024/512/256, light/dark.
- `sources/` — SVG masters (icon + wordmark).
- `final-sheet.png` — contact sheet; `proofs/` — tray proof; `final-assets.md` — notes.
- `build.py` — reproducible build (headless Chrome + Pillow; local Windows paths).

## Provenance / licence

Original artwork created for this project; no third-party icon or font is embedded. The
wordmark is custom outlined geometry. Released under the project's MIT licence together with
the repository; no trademark rights are granted by the licence (see the brand guide and
ADR 0004 for clearance status).
