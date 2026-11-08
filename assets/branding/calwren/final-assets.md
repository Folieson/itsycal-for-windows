# Calwren — final assets (Fluent Binding)

Approved mark: **Fluent Binding** — warm wren perched on an ink binding bar, paper page with
7-column / 2-row grid and a terracotta rounded date cell under the feet. Fluent 2 cues:
rounded surfaces, layered shadow, accent fill, pixel-snapped tray masters.

Palette: paper #F1E7D8 · page #FBF6EC · ink #26211C · terracotta #B55136 · stone #9C8873.
Contrast: ink/paper 13.03:1; terracotta/paper 4.09:1.

## Files
- app/ — 512, 300 (Store), 256, 150, 100, 50 px RGBA tiles.
- tray/ — 16, 20, 24, 32, 48 px native renders (transparent corners).
- windows/calwren.ico — 16/24/32/48/64/128/256; PNG rasters in windows/.
- wordmark/ — lowercase `calwren` wordmark, 1024/512/256, light/dark.
- lockup/ — icon+wordmark, 1024/512/256, light/dark.
- sources/ — icon SVG and wordmark SVGs.
- final-sheet.png — contact sheet; proofs/ — tray proof.

## Notes
- Wordmark comes from the previously approved custom-outline production set; no font is embedded.
- The app tile is paper at every size; no dark-tile variant.
- 16 px uses the same geometry with pixel-snapped edges; binding holes and shadow flatten by design.
- `.ico` is generated from the PNG rasters; verify the consumer loads the sizes it needs.
- Pending: product integration (v2.0.0), MSIX/Store metadata, tray implementation using the 16/32 masters.
