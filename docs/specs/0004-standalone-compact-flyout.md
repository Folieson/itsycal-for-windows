# 0004 — Standalone compact flyout (primary surface)

Status: proposed — **primary compact surface**; draft revision 2 (2026-11-08), independent
spec review pending.
Backlog: ARCH-801/802, UI-101/102 (see [BACKLOG.md](../BACKLOG.md))
Related: [ADR 0003](../adr/0003-surfaces-and-host-strategy.md), spec
[0003](0003-compact-calendar-ui.md) (optional CmdPal surface; host constraints apply there
only).
Host: WinUI 3 / Windows App SDK 2.x, self-contained MSIX, Windows 11.

## Context

ADR 0003 makes a standalone WinUI 3 tray app the primary compact surface. Unlike the
Command Palette host, this surface owns the window: it can be ~360 DIP wide, use acrylic /
Mica, custom layout, keyboard and accessibility, and must feel like a native Windows 11
utility (tray flyout, hotkey, light-dismiss, multi-monitor/DPI). The CmdPal extension stays
as an optional secondary surface.

The goal is the same ItsyCal experience the PO described: a small flyout where the month is
for quick date navigation (not half the screen), the selected day's agenda is below with
times, details, a video glyph indicator on meeting rows and a `Join meeting` action in the
expanded details. This spec owns the compact, no-scroll-for-a-typical-day contract because the
flyout controls its own height; the optional CmdPal surface (spec 0003) may scroll and uses a
card-relative budget instead.

## Acceptance criteria (draft)

- [ ] **AC-1 — compact window and composition:** the flyout opens at ~360 DIP wide
      (340–380 range, per ADR 0003), positioned near the tray or at the cursor, and does not
      resize when the agenda or details change.
  - [ ] **AC-1a — month budget:** the month block (month header/navigation + weekday header +
        six week rows) occupies at most 50% of the flyout client height, measured from the top
        of the month header to the bottom of the last week row. The flyout client height is
        app-owned; the ratio is normative and the absolute DIP value is recorded in the PR.
  - [ ] **AC-1b — no scrolling for ordinary collapsed events:** with today + three ordinary
        collapsed events (short titles/locations, one meeting link and two no-link events), the
        last event row is visible without scrolling. Detect by capturing the initial view and
        a scroll-to-end view: pass only if the content does not move and the last row is
        visible in the initial capture; do not infer scrolling from a scrollbar.
  - [ ] **AC-1c — expanded details:** one expanded ordinary event keeps its details and action
        row visible without scrolling. The long fixture may scroll, but one scroll-to-end
        position must show the complete action row (`Join meeting`, `Copy link`,
        `Close details` as applicable), fully visible and activatable.
- [ ] **AC-2 — native Windows 11 material/behaviour:** acrylic/Mica is visible and readable in
      dark/light/high-contrast themes; the window is borderless with rounded corners, opens on
      the correct monitor (tray monitor or cursor monitor), respects DPI, and light-dismisses
      on outside click/Escape without stealing focus permanently.
- [ ] **AC-3 — quick navigation + agenda + details:** month arrows/Today and day selection
      update the agenda immediately; agenda rows show a neutral dot, time, title and location,
      with a non-interactive video glyph on meeting rows; selecting a row expands details under
      it with `Join meeting` (meeting links only), `Copy link` and `Close details`; a no-link
      event shows `Close details` only; no details block after the whole agenda. `Copy link`
      copies the exact normalized meeting URI. Accent is reserved for selection/today; event
      dots, times and titles are neutral.
- [ ] **AC-4 — keyboard and accessibility:** global hotkey opens/focuses the flyout; arrows
      move day/event focus, Enter activates the focused action (`Join meeting`/`Copy link`/
      `Close details`), Escape closes; every control exposes a UIA name and the focus order is
      deterministic. This full contract is required here; the optional CmdPal surface only
      promises renderer-supported keyboard.
- [ ] **AC-5 — shared core:** calendar model, Windows appointment store, ICS source, update
      service and settings come from `ItsycalForWindows.Core`; the standalone app contains no
      provider/update duplication. CmdPal extension consumes the same core.
- [ ] **AC-6 — packaging/lifecycle:** self-contained MSIX signed by the same certificate;
      single-instance tray app, autostart option, update through the existing verified update
      pipeline; uninstall removes the autostart entry and tray icon; no taskbar injection.

## Test plan (draft)

| AC | Test | Kind |
|---|---|---|
| AC-1a | Installed MSIX, deterministic fixture (`Seed-CompactFixture`); screenshot at 360 DIP; measure month block ≤50% of client height and record the DIP values | manual + screenshot |
| AC-1b | Ordinary fixture, no expansion; initial and scroll-to-end screenshots; last row visible and content does not move | manual + screenshot |
| AC-1c | Ordinary fixture, one event expanded; long fixture expanded; one scroll-to-end shows the complete action row | manual + screenshot |
| AC-2 | Screenshots dark/light/high-contrast; multi-monitor (tray monitor vs cursor), 100/150% DPI; outside-click/Escape dismissal | manual + screenshot |
| AC-3 | Ordinary fixture: select the meeting event — details under the row, `Join meeting`/`Copy link`/`Close details`, clipboard checked; select the no-link event — `Close details` only; video glyph only on the meeting row; screenshot | manual + screenshot |
| AC-4 | Keyboard-only run: hotkey, arrows, Enter, Escape; UIA names via Inspect/manual | manual |
| AC-5 | Core unit tests run in the shared test project; both surfaces reference the same core; grep/build proves no duplicated provider code | unit + review |
| AC-6 | Install/upgrade/uninstall on a clean profile; autostart; update from v1.2.2 to the new package | manual |

The fixture is the committed `.ics` + `Seed-CompactFixture.ps1` set defined in spec 0003, so
the same records are used for both surfaces. The clean-profile/source-isolation rule in spec
0003 applies here too: no appointment-store calendars and no other `.ics` files during
screenshots. Screenshots are read back; a JSON assertion is not visual evidence.

## Out of scope (draft)

- Custom bottom dock/taskbar injection (rejected in ADR 0003).
- Windows 11 widget provider (deferred).
- Providers (Google/Graph/CalDAV), settings consolidation, localization.
- Replacing the CmdPal extension; it remains an optional surface.

## Review

Review record: **spec reviewer — pending**; draft revision 2 submitted 2026-11-08. No
implementation or spike work starts before an `approve` or `approve with notes` verdict.

- Spec reviewer: pending (independent, not the author).
- Design reviewer: required for the spike and the implementation (installed-build screenshots,
  dark/light, 360 DIP, expanded details).
- Code review: after the spike/implementation; ADR 0003 accepted by the PO.
