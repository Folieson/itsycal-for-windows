# 0004 — Standalone compact flyout (primary surface)

Status: proposed (spike + spec, pending independent spec review)
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
times, details and one-click Join for meeting rows.

## Acceptance criteria (draft)

- [ ] **AC-1 — compact window:** the flyout opens at ~360 DIP wide (340–400 range), positioned
      near the tray or at the cursor; the month block occupies at most ~55% of the flyout
      height; the selected day's agenda and inline details are visible without scrolling for
      a typical day (3 events, one expanded), verified on the installed build.
- [ ] **AC-2 — native Windows 11 material/behaviour:** acrylic/Mica is visible and readable in
      dark/light/high-contrast themes; the window is borderless with rounded corners, opens on
      the correct monitor (tray monitor or cursor monitor), respects DPI, and light-dismisses
      on outside click/Escape without stealing focus permanently.
- [ ] **AC-3 — quick navigation + agenda + details:** month arrows/Today and day selection
      update the agenda immediately; agenda shows time/title/location, meeting rows expose a
      Join button/icon; selecting a row expands details under it with Join (meeting links
      only) and Copy; no details block after the whole agenda.
- [ ] **AC-4 — keyboard and accessibility:** global hotkey opens/focuses the flyout; arrows
      move day/event focus, Enter activates Join/Copy, Escape closes; every control exposes a
      UIA name and the focus order is deterministic.
- [ ] **AC-5 — shared core:** calendar model, Windows appointment store, ICS source, update
      service and settings come from `ItsycalForWindows.Core`; the standalone app contains no
      provider/update duplication. CmdPal extension consumes the same core.
- [ ] **AC-6 — packaging/lifecycle:** self-contained MSIX signed by the same certificate;
      single-instance tray app, autostart option, update through the existing verified update
      pipeline; uninstall removes the autostart entry and tray icon; no taskbar injection.

## Test plan (draft)

| AC | Test | Kind |
|---|---|---|
| AC-1 | Installed MSIX, deterministic fixture; screenshots at 360 DIP, measure window/month/agenda | manual + screenshot |
| AC-2 | Screenshots dark/light/high-contrast; multi-monitor (tray monitor vs cursor), 100/150% DPI; outside-click/Escape dismissal | manual + screenshot |
| AC-3 | Two events selected in turn; Join only on meeting rows; Copy; screenshot | manual + screenshot |
| AC-4 | Keyboard-only run: hotkey, arrows, Enter, Escape; UIA names via Inspect/manual | manual |
| AC-5 | Core unit tests run in the shared test project; both surfaces reference the same core; grep/build proves no duplicated provider code | unit + review |
| AC-6 | Install/upgrade/uninstall on a clean profile; autostart; update from v1.2.2 to the new package | manual |

## Out of scope (draft)

- Custom bottom dock/taskbar injection (rejected in ADR 0003).
- Windows 11 widget provider (deferred).
- Providers (Google/Graph/CalDAV), settings consolidation, localization.
- Replacing the CmdPal extension; it remains an optional surface.

## Review

- Spec reviewer: pending.
- Design reviewer: required for the spike and the implementation (installed-build screenshots,
  dark/light, 360 DIP, expanded details).
- Code review: after the spike/implementation; ADR 0003 accepted by the PO.
