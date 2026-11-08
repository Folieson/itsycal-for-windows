# Calwren brand foundation

Status: name chosen and brand direction recorded (PO, 2026-11-08); logo concepts, trademark
clearance, domains, registry IDs and final artwork are still pending. Applies to the v2.0.0
independent identity; the shipped 1.x product keeps its current name until that migration.
Decision: [ADR 0004](../adr/0004-rebrand-independent-product.md). Work:
[BRAND-901/902](../BACKLOG.md).

## Name and story

**Calwren** (pronounced *KAL-ren*) is a coined blend of "cal" from **calendar** and "wren", the
small, alert, plain-coloured bird. It is not a real word, a place or a bird species, so it can
stand as a searchable product identity; trademark clearance is still required before launch.

The story is **"small bird, clear day"**: a small, quiet tool that makes the day legible at a
glance. A wren is not a raptor or a mascot; it is unobtrusive, precise and at home in a hedge.
The product should feel the same: small, calm and useful.

- Descriptor: **Calwren — calendar for Windows** (the descriptor is not part of the wordmark).
- Tagline: **Small bird, clear day.**
- Product name in prose: `Calwren`, one word, capital C.
- Wordmark: lowercase `calwren`.
- Never "CalWren", "Cal Wren", "Calwren Calendar" (the descriptor covers the category) or
  "Itsycal/Calwren".

## Voice

Warm, calm, concise. Write like a thoughtful colleague pointing at the day, not like a
corporate productivity suite.

- Short sentences, plain verbs, sentence case. Prefer "Today", "No events", "Join meeting".
- Warm without being cute: no puns, no exclamation marks, no "tweet"/"cluck" bird jokes.
- Calm in errors: say what happened and the next step; never blame the user.
- Concise in the UI: labels are one to three words; help text explains only what is not visible.
- No corporate filler: "leverage", "seamless", "empower", "solution", "delight", "supercharge".

## Palette

Warm neutrals with a terracotta/amber accent: paper, ink and clay, not a blue SaaS palette. The
values below are the starting direction and may move during icon/design review; keep the roles
and the contrast, not the exact hex.

| Role | Light | Dark | Use |
|---|---|---|---|
| Paper | `#FAF5EE` | `#171412` | App/page background |
| Surface | `#F1E8DD` | `#241F1C` | Cards, rows, selected-day fill |
| Ink | `#2A2522` | `#F4ECE3` | Primary text and the wren silhouette |
| Muted ink | `#73685E` | `#B9ADA2` | Secondary text, metadata |
| Terracotta | `#B85C3E` | `#E08A67` | Primary accent, today/selection, wordmark |
| Amber | `#D89A3C` | `#E8B45C` | Highlight, event dots, hover warmth |

- Accent is for selection, today and actions, not decoration; most of the surface stays
  paper/ink.
- Check text contrast at 4.5:1 and icon/non-text contrast at 3:1. Terracotta on paper is for
  large text and icons, not body copy; dark mode uses lifted tints, not the pure accent.
- Do not introduce a second brand hue. Calendar colour dots are data colours, not brand colours.

## Logo

Concept: a **wren perched on a calendar row**. The bird's body and the row/cell share a
semantic calendar overlap: the belly or wing negative space is a date cell, and the perch is
the row line, so the mark reads as bird and calendar in one gesture. It is not a bird added
next to a calendar grid.

- Silhouette first: one solid ink-or-terracotta shape, no gradient, no mascot face, no eye
  needed at small sizes.
- Must read at **16 px** (tray/favicon) and stay clear at 256 px; test the 16 px version in
  light and dark, on a busy taskbar.
- The tray variant is monochrome and theme-aware; the package icon may use terracotta/amber.
- Clear space: at least the height of the wren's head on all sides; no text inside the mark.
- The mark and the lowercase `calwren` wordmark can lock up horizontally; the descriptor stays
  separate and is never part of the app icon.

## Do / don't

Do:

- Fuse the wren and the calendar row; prefer negative space over outlines.
- Keep the palette warm, the type quiet and the layouts airy.
- Write "Calwren — calendar for Windows" once per surface (Store, README, About).
- Credit ItsyCal only as inspiration and MIT provenance, in the attribution line.

Don't:

- Use clip-art birds, stock calendar grids, flags, or a bird sitting beside a calendar.
- Echo macOS or ItsyCal: no menu-bar mock, no ItsyCal icon shapes, no "menu bar calendar" copy.
- Use Microsoft/Fluent material-brand stems, Segoe UI brand lockups, Windows logos or Microsoft
  product colours as brand elements. Using Fluent controls inside the app is fine.
- Use a corporate abstract mark (swoosh, orbit, chat bubble, generic leaf/feather).
- Commit candidate concepts or image files to this repository.

## Asset checklist (v2.0.0)

- [ ] Primary mark: SVG master plus monochrome SVG, approved by the PO.
- [ ] App icon: `.ico` with 16/20/24/32/40/48/64/128/256 px; final 16 px tray read checked.
- [ ] MSIX package assets: Square44x44, Square150x150, Wide310x150, StoreLogo, SplashScreen
      (scale-100 through scale-400); tray icon light/dark.
- [ ] Lowercase `calwren` wordmark: SVG and PNG for README/Store; horizontal lockup.
- [ ] Descriptor "Calwren — calendar for Windows" on Store listing, README and About.
- [ ] README header, Store screenshots and social preview using the approved palette.
- [ ] No image file in this repository until the PO approves the final set; candidate concepts
      stay in the private gitignored scratch area, not in git.
