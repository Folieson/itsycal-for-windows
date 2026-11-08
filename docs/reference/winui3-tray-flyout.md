# WinUI 3 tray flyout — production checklist
Applies to ADR 0003 (`docs/adr/0003-surfaces-and-host-strategy.md`) / spec 0004
(`docs/specs/0004-standalone-compact-flyout.md`): standalone WinUI 3 (Windows App SDK 2.6.x,
.NET 10) tray app on Windows 11, shipped as MSIX. Baseline: the
spike in `windows/src/ItsycalForWindows.Flyout/` (unpackaged self-contained, borderless topmost, acrylic,
Win32 tray icon, `RegisterHotKey`, `DisplayArea`, DWM corners). Working checklist, not a spec.
`[verify]` = not yet proven on an installed package.
## 1. Tray icon lifecycle
**Replace the spike's tray code; do not subclass the WinUI window proc.** It subclasses the XAML HWND
(`TrayIcon.cs:43`) and only handles `WM_LBUTTONUP`/`WM_RBUTTONUP` (`:98-104`), so keyboard activation and
`WM_CONTEXTMENU` are missing.
Options, in order:
1. **WinUIEx `TrayIcon`** (MIT): hidden top-level WinUI window, `TaskbarCreated`, `NIM_SETVERSION` 4,
   `NIN_SELECT`/`WM_CONTEXTMENU`, `Shell_NotifyIconGetRect` flyout anchor, hide on `WM_ACTIVATE` loss
   ([source](https://github.com/dotMorten/WinUIEx/blob/main/src/WinUIEx/TrayIcon.cs),
   [usage](https://github.com/dotMorten/WinUIEx/blob/main/docs/concepts/TrayIcon.md)).
2. **H.NotifyIcon** (MIT): hidden top-level message window, `TaskbarCreated`, version 4,
   `NIN_SELECT`/`NIN_KEYSELECT`, native popup menus, `NIM_SETFOCUS`
   ([source](https://github.com/HavenDV/H.NotifyIcon/blob/master/src/libs/H.NotifyIcon/Core/MessageWindow.cs)).
3. **Own wrapper** if neither compiles against WinAppSDK 2.6.4/net10 `[verify]`; same features, ~150–250 lines.

Lifecycle rules:
- Add with `NIF_MESSAGE | NIF_ICON | NIF_TIP` and a stable id (GUID via `NIF_GUID` recommended), then
  `NIM_SETVERSION` with `NOTIFYICON_VERSION_4` after every `NIM_ADD`; re-add on the `TaskbarCreated`
  registered message after Explorer restarts. Use a hidden top-level window, not `HWND_MESSAGE`-only:
  message-only windows do not receive broadcasts `[verify]`
  ([Shell_NotifyIcon](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw),
  [NOTIFYICONDATA](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)).
- Handle `NIN_SELECT` (mouse and keyboard select), `NIN_KEYSELECT` (Enter/Space), `WM_CONTEXTMENU`
  (right-click/Shift+F10), and `NIM_SETFOCUS` on Escape menu dismissal. This is the accessibility path;
  mouse-only wiring is not enough.
- Context menu: `Settings…`, `Check for updates`, `About`, `Quit`; use native `TrackPopupMenu` (foreground
  window + `PostMessage(WM_NULL)`,
  [docs](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenu)) or the
  library popup. `Quit` disposes the icon and unregisters the hotkey. Double-click = single-select action.
- Tooltip (≤128 chars) = app name + hotkey. Screen readers derive the tray button name from it `[verify]`
  (Narrator/Inspect).
- Icon: multi-size `.ico` as `ApplicationIcon`, loaded at the small-icon metric with
  `LoadIconMetric`/`LoadImage`; package logos are not the tray icon. Test light/dark taskbars. Windows 11
  controls overflow and the app cannot force pinning `[verify]`; the hotkey must work while hidden.
## 2. Window / host behavior
- **AppWindow vs HWND.** `AppWindow` for presenter, visibility, position, size and `IsShownInSwitchers`;
  HWND (`WindowNative.GetWindowHandle`) for DWM, `SetForegroundWindow`, `RegisterHotKey`, the tray host and
  `GetDpiForWindow`. `AppWindow.Show()` shows *and activates*; `Hide()` keeps the object alive
  ([AppWindow](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow?view=windows-app-sdk-2.0)).
- **Borderless + corners.** `OverlappedPresenter.SetBorderAndTitleBar(false, false)`, `IsResizable=false`,
  `IsAlwaysOnTop=true`. Borderless/no-resize removes DWM rounding; set
  `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND` (33/2); PowerToys does this for the same reason
  ([source](https://github.com/microsoft/PowerToys/blob/main/src/modules/launcher/PowerLauncher/MainWindow.xaml.cs)).
  Use `DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE` (0xFFFFFFFE) to suppress the frame, or a 1px XAML stroke;
  borderless shadow is not automatic `[verify]`
  ([DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)).
- **Acrylic, not Mica.** Acrylic is the documented material for transient/light-dismiss surfaces; Mica is for
  the app foundation. Set `Window.SystemBackdrop = new DesktopAcrylicBackdrop()`, keep the XAML root
  transparent, and do not stack an in-app `AcrylicBrush` on it (double tint). Acrylic becomes solid in High
  Contrast, with Transparency effects off, and in Battery Saver; supply an opaque fallback
  ([Acrylic](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic),
  [system backdrops](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)).
- **Positioning.** Anchor to `Shell_NotifyIconGetRect`; on failure (icon in overflow) `[verify]` fall back to
  `GetCursorPos` + `DisplayArea.GetFromPoint(..., Nearest)`. Clamp to `DisplayArea.WorkArea` (already
  excludes the taskbar); handle bottom/top/left/right taskbars and auto-hide `[verify]`. Re-position on every
  show, not only in the constructor (spike: `MainWindow.xaml.cs:107-115`).
- **DPI.** XAML is DIPs; `AppWindow` screen coordinates are physical pixels in a PerMonitorV2 process.
  Convert with `GetDpiForWindow`/`XamlRoot.RasterizationScale` before `Resize`/`MoveAndResize`; move, resize,
  then re-check after `WM_DPICHANGED`. PowerToys documents a three-step move hack for PerMonitorV2; test
  mixed-DPI monitors.
- **Show/hide/focus.** Create hidden (no `Activate` at startup), then `Show()`+`Activate()` on tray/hotkey;
  verify no first-show flash `[verify]`. Hide on `Window.Activated` → `WindowActivationState.Deactivated`
  (light-dismiss), Escape, or the tray toggle; do not `Close()` (a WinUI `Window` cannot be reopened). Ignore
  a false `Deactivated` while a `MenuFlyout`/`Flyout` owns focus `[verify]`. After show, focus the selected
  day (`Focus(FocusState.Keyboard)`); arrows move day/agenda, Enter activates, Escape closes, Tab stays in
  the flyout
  ([Window.Activated](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.window.activated?view=windows-app-sdk-2.0)).
- **Foreground activation.** `Activate()` alone is unreliable from a hotkey/tray click; PowerToys Run sends
  an empty `SendInput` mouse event first
  ([PR #1282](https://github.com/microsoft/PowerToys/pull/1282),
  [issue #4270](https://github.com/microsoft/PowerToys/issues/4270)). Implement `Activate()` +
  `SetForegroundWindow` with that fallback and test both entry points; honor the
  [SetForegroundWindow restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow).
- **Single instance.** Define `DISABLE_XAML_GENERATED_MAIN`; in `Main` call
  `AppInstance.FindOrRegisterForKey("ItsycalFlyout")`; if not current, `RedirectActivationToAsync(args)` and
  exit without a window; handle `AppInstance.Activated` to show the flyout. Follow the documented STA-safe
  redirect and guard circular redirects
  ([guide](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance)).
- **Hotkey.** Prefer `RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'C')`; handle
  `WM_HOTKEY` on the owning thread. It fails when the chord is taken, F12 is reserved, Win+key chords are
  OS-reserved, and it cannot target a window on another thread
  ([docs](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)). Surface
  "hotkey unavailable", offer rebind, re-register on change, unregister on exit. Do not use a low-level
  `WH_KEYBOARD_LL` hook for Ctrl+Alt+C; hooks are a shared system resource and must keep pumping messages
  ([SetWindowsHookEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw)).
## 3. Packaging / distribution
- **Production must be packaged MSIX.** The spike sets `WindowsPackageType=None`
  (`ItsycalForWindows.Flyout.csproj:12-14`): no identity, so `Package.Current` fails, `StartupTask` is
  unavailable and the update path cannot be exercised. Remove it, enable `EnableMsixTooling`, add a
  `Package.appxmanifest`, and keep `WindowsAppSDKSelfContained=true` plus .NET self-contained.
- **Self-contained vs framework-dependent.** Self-contained copies WinAppSDK and .NET into the package
  (larger, no runtime prerequisite, updates ship the runtime); it is supported for packaged apps.
  Framework-dependent is smaller but needs Windows App Runtime 2.6 installed and matching. No single-file
  EXE: WinUI 3 native dependencies stay separate
  ([guide](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps),
  [samples](https://github.com/microsoft/WindowsAppSDK-Samples/tree/main/Samples/SelfContainedDeployment)).
- **PRI/resources.** Packaged apps use the generated `resources.pri`; declare languages in the manifest
  `<Resources>` and use `.resw` + `x:Uid`. Unpackaged builds need a manual PRI and resolve language
  differently, so unpackaged screenshots are not localization evidence
  ([MRT Core](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/mrtcore-overview),
  [localize strings](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings)).
- **Identity choice — decide before the build.** The repo has one identity (`ItsycalForWindows`,
  `CN=ItsycalForWindows-Dev`) and one WinGet package. Recommended default: **one package with two
  `<Application>` entries** (CmdPal + flyout): one identity, certificate, update flow and WinGet package;
  each app keeps its Start entry. It raises the package minimum OS to 26100, dropping Windows 10 — confirm
  that is acceptable (CmdPal is Windows 11 anyway). Verify the build can put both EXEs in the MSIX `[verify]`.
  Alternative: a second package identity with its own WinGet identifier/manifest and update asset; ADR 0003
  anticipated two identities, so the PO must choose.
- **Signing.** Same certificate as the Publisher. `Build-Release.ps1` builds only `ItsycalForWindows.csproj`
  and names assets `ItsycalForWindows_<ver>_<arch>.msix` (`:209,300-324`); extend it to package the flyout
  with distinct asset names. See [SIGNING.md](../SIGNING.md).
- **Autostart.** Use the `windows.startupTask` manifest extension (`TaskId`, `DisplayName`, `Executable`,
  `EntryPoint="Windows.FullTrustApplication"`, `Enabled="false"` for opt-in) with
  `StartupTask.GetAsync`/`RequestEnableAsync`. Windows shows it in Settings > Startup and Task Manager; a
  user-disabled task cannot be re-enabled by the app (`DisabledByUser`) — link to Settings. At logon,
  `ExtendedActivationKind.StartupTask` means start hidden. Avoid HKCU Run / Startup folder: outside MSIX
  cleanup and not surfaced in Settings
  ([StartupTask](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask?view=winrt-26100),
  [packaging extension](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions)).
- **Uninstall cleanup.** A package-declared startup task disappears with the package (no app-authored Run
  value) `[verify]`; package data is removed, but `%LOCALAPPDATA%\ItsycalForWindows\updates`
  (`UpdateService.cs:282-284`) survives — clean stale folders on start or document them. Verify MSIX
  uninstall while the tray app runs `[verify]`.
- **Update pipeline gaps.** `UpdateService.GetCurrentVersion` reads `Package.Current` (packaged only,
  `UpdateService.cs:815-833`). The updater stops `Microsoft.CmdPal.UI` and `ItsycalForWindows` only
  (`UpdateService.cs:949-953`) and always restarts Command Palette (`:988-995`): add the flyout process and
  restart it if it was running. `SelectMsixAsset` matches a `_x64.msix` *suffix* (`:723-737`), so two
  packages in one release are ambiguous — use a surface-specific prefix. One package/two apps avoids most of
  this: stop both, install, restart both.
- **WinGet / Start menu.** One package: update version, hashes and `MinimumOSVersion` in
  `winget/Folieson.ItsycalForWindows`; each `<Application>` gets a Start entry (MSIX cannot hide it
  `[verify]`) — keep one per surface. Two packages: add a second identifier/manifest; do not put two MSIXs in
  one `InstallerType: msix` installer. Keep `PackageFamilyName`/`SignatureSha256` from the signed package
  ([SIGNING.md](../SIGNING.md)).
## 4. Accessibility and quality
- **UIA.** Explicit `AutomationProperties.Name` on every day cell, agenda row and action; include localized
  date/time, title, location and "has video meeting"; give the video glyph `HelpText` (decorative, not an
  action). Use `AutomationProperties.PositionInSet`/`SizeOfSet` for the 6×7 grid `[verify]`; verify a
  deterministic tab order and that focus lands on the selected day after open
  ([accessibility overview](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview)).
- **High contrast.** No hardcoded colors. The spike's `Foreground="White"` on Join (`MainWindow.xaml:137`) and
  fixed accent/transparent brushes must come from theme resources; use `SystemColor*` resources and
  `HighContrastAdjustment=None` when system colors are correct. Add a 2px contrast-theme flyout border and
  test Aquatic/Desert/Dusk/Night sky; acrylic becomes solid
  ([contrast themes](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes)).
- **Reduced motion.** Gate storyboards/transitions on `UISettings.AnimationsEnabled`; no open animation when
  false
  ([UISettings](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.animationsenabled?view=winrt-26100)).
- **Localization readiness.** All strings in `.resw` + `x:Uid`; no hardcoded "Today", month names or weekday
  abbreviations. First day of week from culture/setting (spike hardcodes Monday, `MainWindow.xaml.cs:194`);
  time format from the user's clock setting, not `HH:mm` (`:354`). Set `FlowDirection` from UI language and
  mirror arrow-key semantics in RTL
  ([FlowDirection](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.flowdirection?view=windows-app-sdk-2.0)).
  Test `ru-RU` (long strings) and `ar-SA`/`he-IL`.
- **Testing matrix** (manual, screenshots per AGENTS.md): DPI 100/125/150/200%; primary/secondary monitors,
  mixed DPI, secondary taskbar, taskbar top/left, auto-hide; light/dark/all four contrast themes; en-US,
  ru-RU, ar-SA or he-IL (RTL), 12h/24h clock; mouse, keyboard-only, Narrator, hotkey conflict, icon in
  overflow; first run, startup task disabled, Explorer restart, upgrade, uninstall.

## 5. What the spike still needs (ordered)
**Blocking**
1. Package as signed MSIX and verify an installed run: identity, `Package.Current`, `StartupTask`, no
   `WindowsPackageType=None`.
2. Replace the tray host: `TaskbarCreated`, `NIM_SETVERSION` 4, `NIN_SELECT`/`NIN_KEYSELECT`/
   `WM_CONTEXTMENU`, `NIM_SETFOCUS`; no WndProc subclassing of the XAML window.
3. Tray context menu and `Quit`, plus double-click policy.
4. Single instance + activation redirect; hidden start on `StartupTask`; hotkey conflict UI and rebind.
5. Foreground activation fallback and deactivation guard (no first-show flash, no hiding under popups).
6. Positioning from tray icon rect/cursor on every show; taskbar edge and mixed DPI.
7. Release/update: build both surfaces, surface-specific asset names, updater stops/restarts the flyout;
   decide one package vs two with the PO; update WinGet.
8. High-contrast and hardcoded-color cleanup; acrylic fallback.
9. UIA names + keyboard-only + Narrator pass.
10. Localization groundwork: resw/x:Uid, first day, time format, RTL.
11. Reduced motion + theme/backdrop change handling.
12. Uninstall/upgrade and update-cache cleanup verification.

**Polish**
13. DWM shadow/border, multi-size tray icon for light/dark taskbars.
14. Settings surface (hotkey, autostart, theme) and About; Start menu/tray discoverability text.
15. Always-on-top behavior over fullscreen apps/games `[verify]`.

## Open questions
- One MSIX package with two applications, or two package identities? Packaging feasibility and the Windows 10
  drop need a spike/PO decision.
- Do WinUIEx 2.9.3 (`Microsoft.WindowsAppSDK.WinUI` 1.8) and H.NotifyIcon.WinUI 2.4.1 (`Microsoft.WindowsAppSDK`
  1.6) build and run against WinAppSDK 2.6.4 + net10?
- Does `Shell_NotifyIconGetRect` return a usable rect when the icon is in the Windows 11 overflow?
- Does `Activate()` succeed for a tray click and for a `RegisterHotKey` hotkey without the `SendInput`
  fallback on the target machine?
- Does the flyout `Deactivated` event fire when a XAML `MenuFlyout` opens, and is the package-declared
  `StartupTask` removed on uninstall / does uninstall terminate the running app?
- Does the tray icon keep its pinned/overflow state across an MSIX upgrade (GUID is tied to the binary path,
  which moves under `WindowsApps`)?
