# ADR 0003 — Surfaces: hybrid standalone flyout + Command Palette extension

Status: accepted (PO decision 2026-11-08)
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
   page, dock band, commands) as an optional integration for CmdPal users. It follows the
   CmdPal host constraints; the compact UI spec does not force it beyond what the host allows.
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
extraction is a real refactor with regression risk; two MSIX identities to sign/update.

Neutral: the existing palette extension, its tests and CI remain; the standalone app adds a
new project/test target and a second artifact to the release.

## Migration plan

1. ADR accepted; backlog re-prioritised (ARCH-801/802, UI-101/102 primary on the flyout).
2. Technical spike: WinUI 3 tray + acrylic borderless flyout with a fake event list, verified
   on an installed MSIX (screenshots) before production work.
3. Extract `ItsycalForWindows.Core` incrementally behind tests; the CmdPal extension switches
   to the core first, then the standalone app consumes it.
4. Build the standalone flyout UI (spec 0004) with design review; keep CmdPal UI unchanged
   except shared-core compatibility.
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

- Host limits: `docs/specs/0003-compact-calendar-ui.md` (CmdPal surface), PowerToys
  `MainWindow.xaml` (800×480/640×420) and `TransientSurface.xaml` (host acrylic).
- Windows 11 widgets: https://learn.microsoft.com/en-us/samples/microsoft/windowsappsdk-samples/widgets/
- WinUI 3 tray pattern: https://github.com/mehdimyadi/systemtraywinui3
- PowerToys Dock band lifecycle: https://github.com/microsoft/PowerToys/pull/49739
