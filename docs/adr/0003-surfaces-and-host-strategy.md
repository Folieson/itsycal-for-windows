# ADR 0003 — Surfaces: hybrid standalone flyout + Command Palette extension

Status: accepted (PO decision 2026-11-08)
Surface status (2026-11-08): primary = spec 0004 (revision 4, re-review pending); optional CmdPal
surface = spec 0003 (revision 5, parity mandate, independent review pending).
Supersedes: nothing. Amends the surface plan in `docs/ROADMAP.md` and spec 0003.

## Context

The product was built as a PowerToys Command Palette extension. The UI review and the PO's
compact-ItsyCal direction exposed host limits: the Command Palette fixes the window
(800×480 DIP default, 640 minimum), renders `ContentPage` as an Adaptive Card 1.5, and does
not let the extension set the card width, acrylic/backdrop material, per-element accessible
names or arrow-key grid focus. A compact ~360-DIP popover with real translucency and custom
layout is not achievable there.

Windows 11 does not offer a supported way to host a third-party band inside the taskbar:
deskbands/toolbars are gone, and a custom always-on-top "bottom dock" conflicts with the
taskbar, Snap, fullscreen games, DPI and multi-monitor setups. The supported surfaces are:
the notification-area tray (with an app-owned popup), Windows 11 widgets (Widgets board,
not the taskbar), and the PowerToys Command Palette Dock band.

The PO asked whether to decouple from PowerToys and integrate with a bottom dock, and chose
the hybrid option: keep the CmdPal extension as an optional surface, and build a standalone
WinUI 3 tray application as the primary compact UI.

## Decision

1. **Primary compact surface:** a standalone WinUI 3 (Windows App SDK) desktop app, packaged
   as a self-contained MSIX, with a notification-area tray icon and an acrylic, borderless,
   compact flyout (~340–380 DIP wide) anchored near the tray or at the cursor. Global hotkey,
   light-dismiss, multi-monitor/DPI aware, keyboard-accessible. No taskbar injection, no
   custom bottom dock.
2. **Secondary surface:** keep the existing PowerToys Command Palette extension (calendar
   page, dock band, commands) as an optional integration for CmdPal users. Optional in
   distribution does not mean divergent UI: the card follows the primary flyout's design
   language and interaction hierarchy — month header/navigation → six week rows → compact
   agenda header → agenda rows → inline details under the selected row, neutral row anatomy,
   accent discipline, typography/spacing tokens and acrylic-forward surfaces — as far as the
   Adaptive Cards 1.5 host allows. Spec 0003 revision 5 carries the parity mandate. Only
   host-owned limits are descoped (window width/position, material strength, no-scroll long
   details, nested row actions, per-element UIA names and full keyboard/arrow focus); a visual
   or interaction difference the card can express is a defect. The compact, no-scroll primary
   experience is owned by spec 0004, and scrolling is accepted on the CmdPal surface.
3. **Shared core:** extract calendar domain/providers/update/settings into a shared library
   (`ItsycalForWindows.Core`) used by both surfaces. No provider logic is duplicated. The
   Windows appointment store, ICS folder, update service and settings stay in the core.
4. **Distribution/signing:** both surfaces are MSIX packages signed by the same certificate;
   self-update logic is reused or wrapped per package. WinGet manifests target the standalone
   package first (the CmdPal extension remains an add-on).
5. **Bottom dock:** rejected. Do not build a window that pretends to be the taskbar dock.
6. **Widgets:** deferred. A Windows 11 widget provider is a possible later surface, not a
   replacement for the flyout.

## Consequences

Positive: full control over compactness, acrylic/Mica, typography, animation and
accessibility; no PowerToys dependency for the core experience; CmdPal users keep the band.

Negative: two shells to build/test/release; standalone needs tray icon, hotkey, autostart,
single-instance and multi-monitor handling, plus its own install/upgrade UX; the shared-core
extraction is a real refactor with regression risk; two MSIX identities to sign/update. The
optional CmdPal surface cannot match the primary flyout's window geometry, material control or
compactness; those are documented host limits, but the CmdPal card still follows the flyout's
design language and interaction hierarchy, so "optional" is not an excuse for divergent UI.

Neutral: the existing palette extension, its tests and CI remain; the standalone app adds a
new project/test target and a second artifact to the release.

## Migration plan

1. ADR accepted; backlog re-prioritised (ARCH-801/802, UI-101/102 primary on the flyout).
2. Technical spike: WinUI 3 tray + acrylic borderless flyout with a fake event list, verified
   on an installed MSIX (screenshots) before production work.
3. Extract `ItsycalForWindows.Core` incrementally behind tests; the CmdPal extension switches
   to the core first, then the standalone app consumes it.
4. Build the standalone flyout UI (spec 0004) with design review, then implement the CmdPal
   parity contract (spec 0003 revision 5, UI-105/UI-106) against that accepted visual language so
   both surfaces share one design. Keep the extension working and shared-core compatible in the
   meantime. CmdPal parity is P1 and follows the flyout's visuals; it is not optional polish,
   but it is also not on the provider/Store critical path.
5. Package/sign/release both; update WinGet and README; changelog per surface.

## Alternatives considered

- **CmdPal-only:** rejected — cannot deliver the compact, translucent, keyboard-first
  ItsyCal experience the PO asked for.
- **Standalone-only / drop PowerToys:** rejected for now — loses CmdPal discovery/band with
  no user benefit; the extension is already working.
- **Custom bottom dock:** rejected — no supported taskbar hosting; poor Windows 11 UX.
- **Windows 11 widget only:** rejected — Widgets board is not a quick-glance flyout and
  cannot show event details/Join interactively.

## References

- Host limits and the parity contract: `docs/specs/0003-compact-calendar-ui.md`
  (parity-mandated CmdPal surface; host limits are listed there), PowerToys `MainWindow.xaml`
  (800×480/640×420) and `TransientSurface.xaml` (host acrylic).
- Primary surface: `docs/specs/0004-standalone-compact-flyout.md` (WinUI 3 tray flyout).
- Windows 11 widgets: https://learn.microsoft.com/en-us/samples/microsoft/windowsappsdk-samples/widgets/
- WinUI 3 tray pattern: https://github.com/mehdimyadi/systemtraywinui3
- PowerToys Dock band lifecycle: https://github.com/microsoft/PowerToys/pull/49739
