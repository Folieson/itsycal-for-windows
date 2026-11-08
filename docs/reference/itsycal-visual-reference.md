# ItsyCal visual reference — WinUI 3 compact flyout

Reference for [spec 0004](../specs/0004-standalone-compact-flyout.md) and [ADR 0003](../adr/0003-surfaces-and-host-strategy.md). Measured from the official ItsyCal banner, 2026-11-08.
Source: [itsycalbanner2@2x.png](https://www.mowglii.com/itsycal/itsycalbanner2@2x.png) (1100×860 PNG); local copy `.dsh-tmp/ui-ref/itsycal-banner.png`. Product: [sfsam/itsycal](https://github.com/sfsam/itsycal).

**Reading the numbers.** `px` are source-image pixels. The banner is a 2× macOS capture, so `DIP ≈ px / 2`; that scale is inferred, not guaranteed, and every DIP/font value is an estimate (±1–2 px). Percentages are scale-independent. The source popover is **177 DIP wide**, not 360: do not scale this layout 2× for the WinUI flyout (§6).

## 1. Composition

Popover body **x 576–929, y 79–826 = 354 × 748 px ≈ 177 × 374 DIP**; aspect **1 : 2.11**. Caret/notch above the body: 44 × 15 px (22 × 7.5 DIP), tip at y 63; corner radius ≈ 26 px (13 DIP). The blue backdrop is the marketing background, not app material; the card is opaque `#FFFFFF` in this capture.

| Band | y (source px) | px | % of 748 | ≈ DIP | Contents |
|---|---|---:|---:|---:|---|
| Top padding | 79–104 | 26 | 3.5 | 13 | empty |
| Month block | 105–466 | 362 | 48.4 | 181 | header 105–130, weekday 156–172, six rows 190–466 |
| Toolbar band | 467–555 | 89 | 11.9 | 44.5 | handle 478–485, icons 506–538, divider 554 |
| Agenda band | 556–826 | 271 | 36.2 | 135.5 | groups 565–655 / 675–795, bottom padding 31 |
| **Total** | 79–826 | **748** | **100** | **374** | |

Month-share caveat: AC-1a's "top of month header to bottom of last week row" gives **48.4%** (47.2% if counting ink only, to the last event dot at 457); counting the 26 px above the header gives **51.9%**, which would miss AC-1a. The spec must name the measurement basis.

Whitespace (px / ≈DIP): text gutter 21 / 10.5 per side; grid inset 15–16 / 8; title→weekday 26 / 13; weekday→grid 18 / 9; grid→handle 12 / 6; divider inset 20 / 10; group header→row 16 / 8; title→time 12 / 6; time→divider 19 / 9.5; event→event 20 / 10; bottom padding 31 / 15.5. The grid is **322 px (161 DIP) = 91%** of body width.

## 2. Grid anatomy

| Element | Measured (px) | ≈ DIP | Notes |
|---|---|---|---|
| Month header "Sep 2026" | 128 × 22 cap, x597 y105 | 64 × 11 cap | ~16 DIP semibold, left aligned, one line |
| Month nav | chevrons 11 × 16, 25-gap; today dot 16 × 16 | 5.5 × 8; dot 8 | `#737373`; right edge 31 px from card edge |
| Weekday header | y156–172, seven columns | cap 8.5 | single uppercase letters, ~12 DIP semibold, `#272727` |
| Grid | 322 × 276, x591–913, y190–466 | 161 × 138 | 7 × 6; column pitch 46.0 px |
| Day cell | 46 × 46 | 23 × 23 | square; number cap 16 px (~11.5 DIP) |
| Event dot | 6 × 6 | 3 | centered per column, 5 px under the numeral; 1 DIP gap when two |
| Selected day | 38 × 38 rounded square | 19 × 19 | 2 DIP `#0088FF` stroke, white fill, radius ~3 DIP |
| Adjacent month | text `#BDBDBD`, dots still colored | — | 1.9:1 contrast; do not copy for Windows text |

- Weekdays are uppercase single letters, semibold; no weekend styling and no trailing period.
- Event dots use **calendar colours**, not semantics: blue `#0088FF`, red `#FB0054`; two events show two dots side by side. Dots stay saturated on adjacent-month days.
- The selected square does **not** tint the cell: the number stays `#272727`, and an event dot is drawn inside the square near its bottom. Here **today = selected = Sep 17**, so the reference shows no separate "today" treatment.
- A thin dark **month frame** (3 px `#272727` ≈1.5 DIP, radius 12 px) traces exactly the in-month cells, stepping around Aug 30–31 at the top and Oct 1–10 at the bottom. It reads as decoration or a focus ring but is not a platform control. **Recommendation: drop it**; if kept, never let it be the only focus indicator.

## 3. Toolbar

- Drag handle: 46 × 8 px (23 × 4 DIP), `#F2F2F2`, radius 4 px, centered, 12 px under the grid.
- Icon row y506–538: `+` 24 × 24 at far left (13 DIP inset); pin tile 36 × 30, calendar grid 30 × 30 and gear 30 × 30 grouped right, gaps 24–27 px (12–13.5 DIP), right margin 23 px.
- Icons are `#787878`; the pin sits on a `#F6F6F6` rounded tile (active/pressed state). No labels, no visible tooltips.

| Source | Inferred role | WinUI 3 mapping |
|---|---|---|
| `+` | new event | `AppBarButton`/`Button`, Add glyph, ≥32 × 32 hit target, UIA "New event" |
| pin (tiled) | keep popover open / pin | **Drop**; flyout light-dismiss is system behaviour |
| calendar grid | open Calendar app | optional "Open in Calendar" command, not primary |
| gear | settings | settings page; UIA "Settings" |
| drag handle | move the mac popover | **Drop**; anchored tray flyout |
| chevrons + dot | previous/next month + today | buttons with tooltips + UIA; make Today a **labelled** button, not a bare dot |

## 4. Agenda

- Group header: day name left (semibold ~12 DIP, `#272727`), date right (`#808080`, same size), baseline aligned; line y565–585 (10 DIP). A 1 DIP `#E6E6E6` separator sits **above** each group, inset 10 DIP (314 px wide), 5 DIP above the header text.
- Event row: gutter starts at 11 DIP; timed rows use a **6 DIP blue dot**, all-day rows a **4 × 10 DIP red rounded bar**. Title starts at 21.5 DIP from the card edge (5 DIP after the dot), 12 DIP regular `#272727`. Time is a second line aligned with the title, 11.5–12 DIP `#808080`, 6 DIP below it.
- Video glyph: 14 × 9 DIP outline camera, `#0088FF`, placed **after the time text** (9.5 DIP gap), not flush with the row's right edge; it appears only on the meeting row.
- All-day treatment is the pill alone: no time line and no "All day" label in the source. No location is shown in this banner.
- Spacing/density: timed row ≈ 18 + 12 + 16 = 46 px (23 DIP); gap to next event 20 px (10 DIP); two group headers + three events fit in the 271 px (135.5 DIP) agenda band.
- Truncation: none occurs here; the longest title is 170 px (85 DIP). At source width a title has ≈145 DIP before the right gutter. Use **single line + `CharacterEllipsis` + `ToolTip`**, and reserve the video glyph's width so adding/removing it does not reflow the row.

## 5. Typography, colour, motion

| Role | Cap height px | Est. DIP size | Weight | Colour |
|---|---:|---:|---|---|
| Month title | 22 | 16 | semibold | `#272727` |
| Weekday label | 17 | 12 | semibold | `#272727` |
| Day number | 16 | 11.5 | regular | `#272727` / `#BDBDBD` adjacent |
| Group header | 16 | 12 | semibold | `#272727` |
| Group date | 16 | 12 | regular | `#808080` |
| Event title | 16–17 | 12 | regular | `#272727` |
| Time | 16 | 11.5 | regular | `#808080` |
| Toolbar/nav icon | — | 12–15 | — | `#787878` / `#737373` |

Hierarchy is carried by **weight and colour**, not a wide size range: everything except the month title sits within ~1 DIP of 12. Copy that, but map it to Segoe UI Variable (Display for the month title, Text for the rest) and the Windows type ramp instead of scaling these cap heights literally.

| Token | Value | Contrast on white | Role |
|---|---|---:|---|
| Accent | `#0088FF` | 3.52:1 | selection outline, video glyph, blue event dots |
| Red | `#FB0054` | 4.02:1 | all-day bar, red event dots (calendar colour, not "all-day" semantics) |
| Primary text | `#272727` | 14.9:1 | titles, numbers |
| Secondary | `#808080` | 3.95:1 | time, group date — below 4.5:1 for small text; use the platform secondary token |
| Dimmed | `#BDBDBD` | 1.88:1 | adjacent month — fails for text; use ≈60% primary or `TextFillColorSecondaryBrush` |
| Divider | `#E6E6E6` | 1.25:1 | hairlines; non-text only |
| Tile | `#F6F6F6` | — | pressed/active icon background |

Dark/light: the source is light-only and **opaque**; the promo gradient is not translucency. Use Mica/acrylic per ADR 0003, keep a 1 DIP stroke and 8–12 DIP corners, and define tokens for light/dark/high-contrast. Selection, today, dots and the all-day bar must not rely on colour alone (add shape/text/`AutomationProperties`). Motion: a static image gives no evidence; use the platform flyout open/close with instant selection updates, and respect reduced-motion settings. No custom popover bounce or grid flip.

## 6. Copy vs adapt for Windows 11 / WinUI 3

**Copy:** the order and compact hierarchy (month title + nav → weekday → six rows → toolbar → agenda); agenda group header with right-aligned date and hairline; two-line event rows with a secondary time line; the icon gutter (dot/pill); the video glyph on meeting rows; the density goal (one day's agenda visible without scrolling).

**Adapt (deliberate changes):**
- **Geometry.** The source is 177 DIP wide; the target is 340–380 DIP. Keep 12–16 DIP type, do not double it. At 360 DIP wide: ≈328 DIP grid → 46–47 DIP columns, but **32–36 DIP row height**, so cells are wider than tall; six rows plus header ⇒ ≈240–270 DIP month block. Set an app-owned client height (≈560 DIP) and measure the month block against it.
- **Fonts.** Segoe UI Variable Display for the month title, Text for the rest; 12/14/16/20 DIP roles; 16/20/24 line heights. Do not imitate SF Pro weights.
- **Material and chrome.** System backdrop + border + rounded window; **drop the notch, drag handle, pin tile and month frame**; no custom drop shadow. Anchored tray flyout, light-dismiss on outside click/Escape.
- **Selection/today.** Replace the 19 DIP blue-stroke square with a Fluent selection state (accent fill with contrasting number, or accent stroke + tint) that passes 3:1 in both themes, and give today a **separate** marker (accent bar/dot or bold numeral).
- **Colour semantics.** Spec 0004 AC-3 says event dots/times/titles are neutral and accent is reserved for selection/today; the reference uses accent blue and red **calendar colours** for dots. Pick one: neutral dots per AC-3, or calendar colours with a neutral fallback and a non-colour cue. Add an "All day" label next to the red pill.
- **Keyboard/UIA.** Arrow keys move in the day grid (Home/End, PageUp/PageDown for months), Enter selects, Tab reaches the agenda, Escape closes; every control and day cell gets a UIA name (full date, "today", event count); the video glyph is non-interactive with an accessible description, while the expanded row exposes `Join meeting`.
- **DPI/theme.** Verify 100/150/200% and 340/360/380 DIP width; no hard-coded pixels; test light/dark/high contrast; keep the fixed flyout size when agenda/details change.

## 7. Implementation checklist (enforceable from screenshots)

- [ ] Flyout 360 ± 20 DIP wide, fixed client height recorded in the PR, no resize on agenda change.
- [ ] Month block (no top padding, header top → last row bottom) **≤ 50%**; report the basis and DIP value. Compare with the source's 48.4% (ink 47.2%, with top padding 51.9%).
- [ ] Six week rows always present; row height 32–36 DIP; weekday caps 8–9 DIP; day numerals 11–13 DIP.
- [ ] Selected and today are distinguishable in light/dark/HC; selection ≥3:1 against the card.
- [ ] Toolbar controls ≥32 × 32 DIP hit targets, 16–20 DIP icons, tooltips + UIA names; no bare dot.
- [ ] Group header ≤24 DIP; 1 DIP separator inset 10–16 DIP; event row ≤52 DIP; time secondary.
- [ ] A ≥40-character title ellipsizes on one line at 360 DIP with the video glyph still visible and a tooltip; row height unchanged with/without the glyph.
- [ ] Today + three events (one meeting, one all-day) visible without scrolling at 100% and 150% DPI; expanded details keep the action row visible (AC-1b/1c).
- [ ] Video glyph only on meeting rows; all-day row carries an "All day" label or equivalent.
- [ ] No animation beyond the system flyout/selection transitions; reduced motion respected.

Questions for the Lead:

1. **AC-1a basis:** should the 50% month budget include the top padding? The source is 48.4% without it and 51.9% with it — the threshold is tight either way.
2. **Height/width:** the source aspect (1 : 2.11) at 360 DIP would be ≈760 DIP tall. Do we fix ≈560 DIP and compress grid rows (recommended), or widen toward 380 and cap height?
3. **Event-dot colour:** neutral per AC-3, or source-style calendar colours? If colours, what is the neutral fallback and the non-colour cue?
4. **Today vs selected:** the reference conflates them (Sep 17). Define both treatments; should today stay neutral while selection owns accent?
5. **Mac-only affordances:** confirm dropping the notch, drag handle, pin tile and month frame, and replacing the bare today dot with a labelled button.

## References

- Source image: <https://www.mowglii.com/itsycal/itsycalbanner2@2x.png> (official ItsyCal banner, 2×).
- Upstream product: <https://github.com/sfsam/itsycal>.
- Windows material: [Acrylic](https://learn.microsoft.com/windows/apps/design/style/acrylic), [Mica](https://learn.microsoft.com/windows/apps/design/style/mica).
- Windows typography and input: [Segoe UI Variable](https://learn.microsoft.com/windows/apps/design/style/typography), [keyboard interactions](https://learn.microsoft.com/windows/apps/design/input/keyboard-interactions), [UIA names](https://learn.microsoft.com/windows/apps/design/accessibility/basic-accessibility-information).
