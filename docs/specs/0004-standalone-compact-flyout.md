# 0004 — Standalone compact flyout (primary surface)

Status: proposed — **primary compact surface**; draft revision 3 (2026-11-08). Independent
spec review round 3 returned request changes; revision 3 resolves them and awaits re-review.
Backlog: ARCH-801/802, UI-101/102 (see [BACKLOG.md](../BACKLOG.md))
Related: [ADR 0003](../adr/0003-surfaces-and-host-strategy.md), spec
[0003](0003-compact-calendar-ui.md) (optional CmdPal surface; the host constraints and the
scrolling allowance apply there only).
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
viewport-relative budget instead.

**Core status (2026-11-08).** `ItsycalForWindows.Core` exists on master (PR #39) with the
calendar models and the Windows appointment-store, ICS and composite providers; the CmdPal
extension already consumes it. `UpdateService` and settings are still in the CmdPal app
(`windows/src/ItsycalForWindows/`), so the flyout must consume the core and reuse the update
download/verify logic instead of forking it; ARCH-802 tracks the remaining extraction.

**Feasibility spike.** A spike on branch `feat/flyout-spike` (commit `958ea10`) demonstrates
that a WinUI 3 borderless acrylic window, DWM rounded corners, a Win32 tray icon and a hotkey
can run. It is **feasibility evidence only and cannot be used as acceptance evidence**: it is
unpackaged (`WindowsPackageType=None`, so `Package.Current`/`StartupTask` are unavailable),
tray registration could not be exercised in the sandbox used for the spike and packaged
behaviour was not verified, its details panel is a fixed panel rather than inline under the
row, and its labels are bare `Join`/`Close` instead of `Join meeting`/`Close details`. Its gap list is
[winui3-tray-flyout.md](../reference/winui3-tray-flyout.md). Nothing in the spike satisfies an
AC below; production implementation still starts only after an approved spec verdict.

The design study in [itsycal-visual-reference.md](../reference/itsycal-visual-reference.md) is
input, not an acceptance source: where its estimates differ from this spec (for example a
240–270 DIP month block or an ≈560 DIP client height), the numbers here are normative.

## Acceptance criteria

- [ ] **AC-1 — window geometry and composition:** the flyout is 340–380 DIP wide (~360 target).
      Client height is content-hugging, clamped to **448–600 DIP** (the client area excludes the
      DWM frame and shadow), so the window never changes width and changes height only within
      that range when the agenda or details change.
  - [ ] **AC-1a — month budget:** the month block (month header/navigation row + weekday header
        + six week rows) occupies **at most 224 DIP**, measured from the top of the month header
        to the bottom of the last week row. With the 448-DIP minimum client height this is
        exactly 50%; at the 480-DIP collapsed target it is 46.7%, and at the 600-DIP maximum
        37.3%. The 448-DIP floor is twice the month budget, so the ≤50% rule holds at every
        allowed client height. The PR records the measured month DIP and the client height in
        DIP at both captured sizes.
  - [ ] **AC-1b — collapsed height and no scrolling:** with the ordinary fixture (five collapsed
        rows: one multi-day all-day row, one single all-day row and three timed rows) and no
        expansion, the measured client height is **480 ±16 DIP** and the last row is visible
        without scrolling. Detect by capturing the initial view and a scroll-to-end view: pass
        only if the content does not move and the last row is visible in the initial capture; do
        not infer scrolling from a scrollbar. Record the client height (`GetClientRect` or
        `AppWindow` size, physical pixels divided by the window DPI scale) and both screenshots.
  - [ ] **AC-1c — expanded details grow, then scroll:** expanding one ordinary event grows the
        flyout, up to the **600-DIP maximum**, so that the details and the complete action row
        (`Join meeting`, `Copy link`, `Close details` as applicable) remain visible without
        scrolling. If the content would still exceed 600 DIP, the window stays at 600 and the
        agenda pane scrolls one page; one scroll-to-end position must show the complete action
        row, fully visible and activatable. The long fixture follows the same grow-then-scroll
        rule. Each state records its client height and a screenshot.
- [ ] **AC-2 — native material, positioning, DPI and dismissal** (separate evidence per part):
  - [ ] **AC-2a — material and theme:** the acrylic backdrop is visible in dark and light themes;
        in high contrast (all four contrast themes) acrylic is replaced by a **solid,
        non-acrylic** fallback with system colours, and text/controls stay readable; no
        hardcoded colours. The same solid fallback applies when transparency effects are off or
        Battery Saver disables acrylic. Verified by read-back screenshots.
  - [ ] **AC-2b — positioning and the correct monitor:** the window is borderless with rounded
        corners and opens on the **tray monitor** — the monitor that contains the tray icon when
        `Shell_NotifyIconGetRect` returns a rect — and falls back to the monitor under the cursor
        when the icon is in overflow or the rect is unavailable. A setting may switch the
        preference to the cursor monitor. The window is clamped to the monitor work area and
        repositioned on every show, not only at startup. The PR records the monitor device name,
        work area and DPI for both cases.
  - [ ] **AC-2c — DPI matrix:** at **100%, 125%, 150% and 200%** the width stays 340–380 DIP,
        the client height follows AC-1, and text/controls are not clipped; moving between
        monitors with different DPI re-lays out correctly. Each DPI is captured and the physical
        size is recorded.
  - [ ] **AC-2d — light-dismiss and focus return:** an outside click and Escape hide the flyout
        (they do not close it), and focus returns to the window that was foreground before the
        flyout opened; opening does not permanently steal focus and there is no first-show flash.
        Verified with the keyboard-only and mouse runs.
- [ ] **AC-3 — quick navigation, agenda and inline details:** month arrows/Today and day
      selection update the agenda immediately; agenda rows show a neutral dot, time (`All day`
      for all-day), title and location, with a non-interactive video glyph on meeting rows; a
      multi-day row prefixes its meta line with `Day N of M` (`Day 2 of 3 · Building 7`).
      Selecting a row expands details directly below it with the full when-line (including
      `All day` / `Day N of M` relative to the selected date), location, truncated notes and the
      actions; a no-link event shows `Close details` only; no details block appears after the
      whole agenda. `Copy link` copies the normalized `uri.AbsoluteUri` (http/https only; a
      `ftp:`/`mailto:`/relative link is treated as no link) and shows `Meeting link copied.`;
      clipboard failure shows `Could not copy the meeting link.` The clipboard is an injected
      adapter shared with spec 0003, so both messages are unit-testable. Accent is reserved for
      selection/today; dots, times and titles are neutral.
- [ ] **AC-4 — keyboard and accessibility** (separate evidence per part):
  - [ ] **AC-4a — hotkey and focus model:** the global hotkey opens and focuses the flyout with
        focus on the selected day cell. Arrow keys move day focus (left/right by day, up/down by
        week); Enter/Space selects the focused day. Tab/Shift+Tab cycle in one deterministic
        order: month navigation (`Previous month`, `Today`, `Next month`) → calendar grid
        (roving focus) → agenda rows → expanded-details actions; Tab stays inside the flyout.
        Enter on an agenda row expands it; Enter on an action activates it. Escape collapses the
        expanded details and returns focus to its row; a second Escape closes the flyout. A
        hotkey that cannot be registered surfaces a visible message (rebinding belongs to the
        settings surface).
  - [ ] **AC-4b — UIA names:** every interactive control has an accessible name: `Previous
        month`, `Next month`, `Today`, `<weekday>, <month> <day>, <year>` for day cells,
        `<time range>, <title>, <location>` for agenda rows (plus `has video meeting` when the
        glyph is present), and the action labels `Join meeting`, `Copy link`, `Close details`.
        The video glyph is decorative with help text, not an action. The tab order matches
        AC-4a; verified with Inspect and a Narrator pass.
- [ ] **AC-5 — shared core:** calendar models and providers (Windows appointment store, ICS,
      composite) come from `ItsycalForWindows.Core` (merged, PR #39), and both surfaces reference
      the same assembly; the flyout contains no provider or parsing duplication. `UpdateService`
      and settings are still app-side; ARCH-802 tracks their extraction, and until it lands the
      flyout must reuse the existing download/SHA-256/verify logic (shared source, not a copy).
- [ ] **AC-6 — packaging, lifecycle and update** (separate evidence per part):
  - [ ] **AC-6a — packaging and identity:** the flyout ships as a self-contained MSIX signed by
        the same certificate, with a package identity that supports `Package.Current` and
        `StartupTask`; it installs and launches on a clean profile without PowerToys. The
        identity choice (one package with two applications vs a second package identity) is a
        PO decision recorded before implementation; an unpackaged build is not acceptance
        evidence.
  - [ ] **AC-6b — lifecycle:** single instance (a second launch redirects to the running app and
        creates no second tray icon), opt-in autostart through the `windows.startupTask` manifest
        extension (not an HKCU Run value), uninstall removes the tray icon and autostart entry
        and terminates a running flyout, and the app does not inject into the taskbar.
  - [ ] **AC-6c — update path:** the flyout has its own package/apply path even when it shares a
        package identity with the extension. It reuses the download and SHA-256 verification and
        relies on MSIX signature trust at apply time, but the updater stops the flyout
        package/process, applies the package with `Add-AppxPackage`, and restarts the flyout only
        if it was running. It must not hardcode `Microsoft.CommandPalette` or restart the
        palette. Test: install the previous signed build of the same package identity, update to
        the candidate, then verify single instance, autostart and uninstall still work. This
        tests the flyout only; upgrading the CmdPal extension is not evidence for the flyout, and
        vice versa.
- [ ] **AC-7 — empty, loading and error states:** a day with no events shows exactly one line,
      `No events on this day.`, plus the one-line calendars-folder pointer until ONB-101 ships.
      `Loading events…` appears only while the selected day is loading and no events are cached
      (cached events render instead, so there is no loading flash). A failed load shows
      `Couldn't load events.` with the same folder pointer, no stack trace and no event content;
      it is visibly distinct from the empty state (this is a load failure, not a per-source
      status; per-source status remains ONB-101). State derivation lives in a pure helper with
      unit tests; empty and error states get read-back screenshots, and loading is verified by
      its unit test and a manual run with a deliberately delayed source.

## Test plan (draft)

| AC | Test | Kind |
|---|---|---|
| AC-1a | Installed MSIX, ordinary fixture; screenshot at ~360 DIP; measure the month block and the client height with `GetClientRect`/`AppWindow`; pass only if month ≤224 DIP and ≤50% at 448/480/600; record both DIP values | manual + screenshot |
| AC-1b | Ordinary fixture, no expansion; initial and scroll-to-end screenshots; client height 480 ±16 DIP; last row visible, content does not move | manual + screenshot |
| AC-1c | Ordinary fixture, one event expanded; long fixture expanded; client height ≤600 DIP; details/action row visible without scrolling, or one scroll-to-end shows the complete action row; record heights | manual + screenshot |
| AC-2a | Screenshots in dark/light and all four high-contrast themes; solid fallback with no acrylic in HC; transparency-off/Battery Saver fallback | manual + screenshot |
| AC-2b | Tray-monitor and cursor-monitor cases; work-area clamp; reposition on every show; record monitor device name, work area and DPI | manual + screenshot |
| AC-2c | 100/125/150/200% DPI screenshots; width 340–380 DIP; no clipping; mixed-DPI move | manual + screenshot |
| AC-2d | Outside click and Escape; focus returns to the previous foreground window; no first-show flash | manual |
| AC-3 | Ordinary fixture: select event 2 (meeting) — details directly below, `Join meeting`/`Copy link`/`Close details`, clipboard equals `https://meet.google.com/abc-defg-hij`; select event 3 — `Close details` only; all-day row shows `All day`; multi-day row shows `Day 2 of 3`; video glyph only on event 2; no trailing block | manual + screenshot |
| AC-4a | Keyboard-only: hotkey, day/row arrows, Tab order, Enter, Escape twice; record the tab sequence | manual |
| AC-4b | Inspect/Narrator: every name from AC-4b, glyph help text, tab order matches AC-4a | manual |
| AC-5 | Core tests run in the shared test project; both surfaces reference `ItsycalForWindows.Core`; grep/build proves no duplicated provider code | unit + review |
| AC-6a | Clean-profile install; `Package.Current` and `StartupTask` work; launch with PowerToys absent; identity choice recorded | manual |
| AC-6b | Second launch redirects; autostart enable/disable at logon; uninstall removes tray/autostart while running | manual |
| AC-6c | Install the previous signed build of the same package identity, update to the candidate; verify restart, no palette restart, single instance, autostart, uninstall | manual |
| AC-7 | State-helper unit tests; empty and error screenshots; loading with a delayed source | unit + manual |

The fixture is the planned `.ics` + `Seed-CompactFixture.ps1` set defined in spec 0003 (the
implementation PR commits it; it is not in the repository at head), so both surfaces use the same
records. The clean-profile/source-isolation rule and the MSIX `%LOCALAPPDATA%` virtualisation
check in spec 0003 apply here too: no appointment-store calendars and no other `.ics` files
during screenshots. Screenshots are read back; a JSON assertion is not visual evidence.

## Out of scope (draft)

- Custom bottom dock/taskbar injection (rejected in ADR 0003).
- Windows 11 widget provider (deferred).
- Providers (Google/Graph/CalDAV), settings consolidation, localization.
- Replacing the CmdPal extension; it remains an optional surface. A per-source status UI is
  ONB-101.

## Review

Review record: **round 3 independent spec review (2026-11-08): request changes**; revision 3
submitted 2026-11-08 for re-review — **verdict pending**. Production implementation starts only
after an `approve` or `approve with notes` verdict is recorded; the existing feasibility spike is
not implementation of this spec and cannot substitute for the verdict.

- Round 3 (2026-11-08, independent of the author): request changes — AC-1 had no client-height
  contract; AC-2/AC-4/AC-6 bundled unrelated claims; the AC-6 update premise was false (the
  current `UpdateService` is CmdPal-specific: it hardcodes `Microsoft.CommandPalette` and
  restarts the palette); AC-5 claimed update/settings were already in the core; there was no
  empty/loading/error AC. Revision 3 resolves them: a 448–600 DIP content-hugging height with a
  480 ±16 DIP collapsed target and a 600-DIP grow-then-scroll policy; AC-2/AC-4/AC-6 split into
  measurable parts; the flyout-specific apply/restart path separated from the CmdPal updater;
  AC-5 names the merged core and the remaining ARCH-802 work; AC-7 adds the three states.
- Spec reviewer: round 3 verdict above; re-review pending (independent of the author).
- Design reviewer: required for the implementation (installed-build screenshots, dark/light,
  ~360 DIP, expanded details); not applicable to the spike.
- Code review: after the implementation; ADR 0003 accepted by the PO.
