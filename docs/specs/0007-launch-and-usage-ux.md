# 0007 — Launch and usage UX (end to end)

Status: proposed — **UX specification only** (2026-11-08). No code, workflow, packaging or behaviour change is part of this document; implementation needs separate slices (UX-101…UX-105) with tests first and the normal review gates.
Backlog: UX-101…UX-105 ([BACKLOG.md](../BACKLOG.md)). Related:
[spec 0004](0004-standalone-compact-flyout.md) (flyout geometry, material, keyboard, update apply
path), [spec 0003](0003-compact-calendar-ui.md) (optional CmdPal surface),
[spec 0005](0005-calendar-folder-onboarding.md) (open calendar folder),
[ADR 0003](../adr/0003-surfaces-and-host-strategy.md),
[ADR 0004](../adr/0004-rebrand-independent-product.md),
[ADR 0005](../adr/0005-external-calendar-providers-and-store-gate.md),
[tray checklist](../reference/winui3-tray-flyout.md).
Host: Windows 11, WinUI 3 standalone MSIX (primary) + optional PowerToys Command Palette extension.
Names: copy below uses the v2.0.0 name **Calwren**; 1.x bridge builds substitute "Itsycal for Windows" and must not ship Calwren-only claims before v2.0.0 (ADR 0004).

## Context

PO brief (2026-11-08): «проработать UX запуска и использования приложения» — define install, start, discovery, daily use, configuration, quit and migration, not only the calendar card. Specs 0003/0004 own the compact surface; this spec owns the layer that makes the product a calendar people use daily.

Current launch behaviour at master `6ee426b` (evidence, not assumptions):

- the flyout shell always creates and activates a window (`App.xaml.cs:30-34`): no tray-only start and no launch-path policy;
- the tray icon is spike-grade Win32: left-click toggles, right-click exits immediately, no `NIN_SELECT`/`NIN_KEYSELECT`/`WM_CONTEXTMENU`, no `TaskbarCreated` re-add (`TrayIcon.cs:141-162`);
- the hotkey is hardcoded Ctrl+Alt+C; a failed registration is only logged (`TrayIcon.cs:58-65`);
- placement is the bottom-right of the window's `DisplayArea` (`MainWindow.xaml.cs:246-254`), not the tray rect or cursor, and is not recomputed on every show;
- no single instance, no autostart, no package identity (`ItsycalForWindows.Flyout.csproj:13`): a second launch is a second process and tray icon;
- settings live only in Command Palette host pages (`SettingsManager.cs:58-91`); the flyout has no settings, hotkey, startup or About surface;
- update UX is palette commands plus `update.log`, and the updater restarts Command Palette, not the flyout (spec 0004 AC-6c);
- first run is only `No events on this day.` plus the folder pointer (`CalendarOnboarding.cs:11-29`), and there is no provider/account UX yet (ADR 0005).

## 1. Jobs to be done and principles

Users: Windows 11 knowledge workers in Outlook/Google/Yandex calendars who keep the app running and open it for seconds many times a day (the ItsyCal habit).

- **Glanceable day** — "What's on today?" answered in about two seconds, without a full app.
- **One hotkey** — the same gesture from any app, any monitor, even with the tray icon hidden.
- **Calm and quiet** — no badges or nags; the app speaks only when asked or when attention is needed.
- **No account required** — local calendars work on first launch, offline, forever.
- **Local-first** — remote providers add to local sources and never replace them.
- **Never lose data** — hide never quits, quit never destroys calendars, update/uninstall never silently deletes user files.
- **Host-appropriate** — native Windows 11 tray flyout; the Command Palette extension is optional.

Principles: P-1 hotkey and tray click do the same thing; P-2 the flyout is transient (hides, does not close) and quitting is explicit; P-3 quiet by default; P-4 a failing account never hides local events; P-5 destructive actions are confirmed or recoverable; P-6 failures explain themselves in place with one recovery action and never log event content; P-7 nothing runs at logon or steals focus unless asked; P-8 one place for settings.

## 2. Cold start and first run

Install is MSIX (sideload/WinGet now; Store only after ADR 0005 §8). After install nothing runs; the Start menu shows Calwren; the package declares a `windows.startupTask` **disabled by default** (spec 0004 AC-6b); the install/Store handoff does not auto-launch the app.

First launch (user-initiated from Start) shows the tray icon plus a **one-time welcome flyout** inside
the normal flyout window, fitting 448–600 DIP (spec 0004 AC-1) without scrolling: `Welcome to Calwren`
/ `Your day, one keystroke away.`; `Press Ctrl+Alt+C anywhere to open Calwren.` with `Change…`; primary
`Add calendars` (opens the calendars folder: "Drop exported .ics files here; Windows calendars are read
automatically" — same outcome as `Calwren: open calendar folder`, spec 0005); checkbox `Start Calwren
when I sign in` — **off** — applied immediately via `StartupTask.RequestEnableAsync`; `Connect a
calendar…` only once PRV-306 ships (never tease a provider the build cannot add); `Calwren sits in the
notification area.` with the tray glyph, naming the hotkey and Start menu as reliable paths because
overflow cannot be forced.

Rules: no sign-in, no account, no network request on first run; the welcome appears only for a
user-initiated first launch (a logon start never shows it); no wizard, no Next/Back; dismissal is
persisted, so later launches open the normal flyout and never show a tip again; an empty day then shows
`No events on this day.` plus the folder pointer (spec 0003 AC-5 / spec 0004 AC-7) until ONB-101 adds
`Add calendars…`, which replaces the pointer (never both).

| Behaviour | Default | Opt-in | Rationale |
|---|---|---|---|
| Start with Windows | Off | Welcome checkbox, Settings → General | Windows 11 startup expectations; nothing silent at logon |
| Welcome flyout | Once, first user launch | — | Teaches the one gesture and the local CTA at the moment of intent |
| Network providers | None configured | Settings → Accounts, per account | Local-first, no forced sign-in (ADR 0005 §8.3) |
| Update check / install | Off / ask | Settings → Updates | No surprise network (ADR 0002); never restart without consent |
| Server-side account | None exists | — | The product creates no cloud account (ADR 0005 §4.4) |

## 3. Launch paths matrix

| Path | What appears | Focus and dismissal | Notes / target |
|---|---|---|---|
| Tray left click | Flyout near the tray icon (or cursor monitor) | Selected-day focus; visible-but-unfocused activates, focused hides | Same result as the hotkey |
| Tray double click | Flyout once (no immediate open→hide) | As above | Debounce |
| Tray right click / Shift+F10 | Native menu: `Open Calwren`, `Settings…`, `Check for updates`, `About`, `Quit` | Menu owns focus; Escape closes only the menu | Never quit directly (today's behaviour is a hazard) |
| Tray keyboard (Win+B, arrows, Enter/Space) | Same as left click via `NIN_SELECT`/`NIN_KEYSELECT` | As above | Required accessibility path |
| Hotkey Ctrl+Alt+C | Flyout on the tray monitor (cursor monitor if configured) | Selected-day focus; pressing again hides | Works while the icon is in overflow |
| Start menu | Running: show/focus flyout; not running: process start then flyout (cold) | As above | The only explicit path that shows UI at start |
| Second launch while running | Redirected activation of the running instance | As above | One process, one tray icon |
| Logon with startup task | Tray icon only, no window (`ExtendedActivationKind.StartupTask`) | — | Hidden start; hotkey ready |
| Command Palette command/band (optional) | Palette page or band (spec 0003); `Open Calwren` launches the flyout | Host-controlled | No second copy of calendar state |
| Explorer restart | Tray icon re-added (`TaskbarCreated`) | — | Flyout stays hidden and reopens normally |
| Update handoff | Tray icon restored; flyout reopens only if it was open | As above | Welcome/first-run state not replayed |

Latency targets, measured on the reference machine and recorded (not a hardware promise): warm open
(process running) visible **≤150 ms typical, ≤300 ms p95**; cold open (Start menu) first visible frame
**≤2.0 s**; no white/black flash before the first frame; a show after a monitor/DPI change is positioned
before it is visible, with no jump. Five runs per path; a path fails if it exceeds twice its target,
flashes, or opens on the wrong monitor.

## 4. Daily use flows

Open → navigate → read → expand → act → dismiss:

1. **Open** (hotkey/tray): today selected, its agenda shown, focus on the selected day.
2. **Navigate**: arrows move day focus, Enter/Space selects, `PageUp`/`PageDown` change month, `Home` selects today; month arrows and `Today` work by mouse; selection updates the agenda at once.
3. **Read** each row: neutral dot, time (`All day`), title, location, `Day N of M`, video glyph (spec 0004 AC-3).
4. **Expand** with Enter or click: details appear directly below the row (spec 0004 AC-3).
5. **Act**: `Join meeting` hides the flyout first, then opens the default browser; `Copy link` shows `Meeting link copied.`; `Close details` collapses.
6. **Dismiss**: outside click or Escape hides; focus returns to the previous window (spec 0004 AC-2d); hiding never quits.

Launch-level keys (spec 0004 AC-4a stays normative for grid/agenda Tab order, arrows, Enter and the
two-stage Escape): `PageUp`/`PageDown` month, `Home` today, `Ctrl+,` Settings while focused, `Alt+F4`
hides (never quits), Tab/Shift+Tab stay inside the flyout. Pointer: left click only; right click inside
does nothing (no hidden quit); the wheel scrolls only an overflowing agenda. UIA: spec 0004 AC-4b names,
the tray button name from its tooltip, keyboard-navigable Settings, and Narrator landing on the selected
day rather than reading the agenda. Multi-monitor/DPI/theme: tray monitor by default with the
cursor-monitor setting (spec 0004 AC-2b), correct 100–200% layout and per-show repositioning (spec 0004
AC-2c), system theme with solid high-contrast fallback (spec 0004 AC-2a), and no animation when Windows
animations are off. Additions that extend spec 0004 (`PageUp`/`PageDown`/`Home`, `Ctrl+,`, `Alt+F4`,
Join-meeting hiding first) are recorded as 0004 amendments for UX-102; this spec is their UX source.

## 5. Provider and account UX

Entry points: Settings → Accounts (primary), the flyout aggregate `1 calendar needs attention` → Accounts, the Command Palette read-only status page with `Manage accounts` (ADR 0005 §5.5). Account setup never happens inside the compact flyout.

- **Add**: provider list — Google, Microsoft, Yandex, CalDAV (iCloud/Fastmail/Nextcloud/mailbox.org presets), ICS subscription — each with a one-line scope/limit note. Consent text states what is read (read-only), where it stays and how to revoke it **before** the browser opens (ADR 0005 §4.3).
- **Sign in**: system browser only, no embedded WebView, authorization code + PKCE on loopback (ADR 0005 §4.1); `Waiting for your browser…` with `Cancel`; a five-minute timeout closes the listener. App passwords are entered only in the CalDAV/Yandex form, with "use a dedicated app password" guidance and a provider deep link.
- **List**: label, provider, enable toggle, status, last sync, and `Sign in` / `Re-authenticate` / `Sign out` / `Remove` across the ADR 0005 §5.6 states; the flyout shows only an aggregate.
- **Sign out / remove**: confirmation states what is deleted locally and that the provider-side grant may persist, with the revocation path (ADR 0005 §4.4); local calendars are never touched.
- **No account required**: with zero accounts the app behaves exactly as a local app, with no upsell; Store/About copy says `No account required; online calendars are optional`, and no Google/Outlook/Yandex claim appears before REL-303. An unconfigured provider is normal, not an error.

## 6. Settings surface

One WinUI 3 **Settings window** (~720×560 DIP, resizable, own taskbar entry, normal chrome), hosted by the standalone package — not a compact page and not Command-Palette-only:

| Section | Contents |
|---|---|
| General | Language: `System language (default)` + LOC-602 list; `Start Calwren when I sign in` (with `Disabled by Windows` state linking to Windows Settings); hotkey `Ctrl+Alt+C` with `Change…`/`Reset`/`Disable`; theme follows Windows (no per-app theme in v1) |
| Calendars | Windows calendars (automatic, read-only row); local folder path with `Open folder` and `Add .ics…` (ONB-101); ICS subscriptions; week start/week numbers shared with the palette surface |
| Accounts | The ADR 0005 §5.5 account list and flows |
| Updates | Channel Stable/Preview; `Check automatically` (off) and `Install automatically` (off, requires check); `Check now`; current version, last check/result; `Open update log`; release-notes link |
| About | Name, version, build, architecture; MIT and upstream ItsyCal attribution with `not affiliated`; privacy statement link; `Copy diagnostics` (no event content, no tokens); license |

Entry points: tray menu `Settings…`, `Ctrl+,` while the flyout is focused, `Calwren: settings` from the
palette; Windows Settings → Installed apps does not open settings (OS-owned). No new flyout chrome is
added for Settings (spec 0004 owns the composition). The palette page mirrors status and launches this
window; if only the palette package is installed it stays the settings surface with install guidance.
Applied without restart: hotkey (re-registered immediately, failure shown in place), startup, accounts,
update preferences (next check), week start/week numbers. Language is the documented exception: apply on
restart with `Restart Calwren to apply` and a `Restart now` action (LOC-602 reload rule).

## 7. State and error matrix

Final English copy for the UX slice; it must move to `resw`/MRT with LOC-601, and nothing below may show a stack trace, event content, tokens or a bare Win32/HRESULT code.

| State | Where | Copy direction | Recovery | Must not |
|---|---|---|---|---|
| First run, no calendars | Welcome | `Welcome to Calwren` + hotkey + `Add calendars` + startup checkbox | Opens the folder | Sign-in prompt, network |
| Empty selected day | Agenda | `No events on this day.` + folder pointer, or `Add calendars…` after ONB-101 | Open folder / import | A second empty line or a spinner |
| Loading | Agenda | `Loading events…` only when nothing is cached | — | Flash over cached events |
| Local load failure | Agenda | `Couldn't load events.` + pointer | `Try again` | Hiding the folder pointer |
| Account synced, no events | Account row | `Synced — no events in this range.` | Change range / add a calendar | Treating it as an error |
| Offline with encrypted cache | Account row, flyout aggregate | `Offline — showing saved events` | Retry on reconnect | Hiding local events |
| Offline after restart (cache present) | Account row | `Offline — showing saved events` + last-sync time | `Retry` | Claiming the calendar is empty |
| Offline after restart (no cache yet) | Account row | `Not synced yet. Connect to the internet.` | `Retry` | Claiming the calendar is empty |
| Sign-in expired | Account row, aggregate | `Sign-in expired` + account label | `Sign in again` | Silent retry loop |
| Provider error | Account row | `Couldn't reach <provider>` + category | `Retry` / details | Taking over the flyout |
| Rate limited | Account row | `Rate limited. Retrying at <time>.` | Automatic | Modal dialog |
| Hotkey conflict | Flyout banner, Settings | `Ctrl+Alt+C is already in use.` + `Choose another hotkey` | Rebind / disable | Silent failure, blocking start |
| Tray registration failure | Flyout banner, About | `Calwren couldn't add its tray icon.` + `Use Ctrl+Alt+C or the Start menu.` | `Retry`; retry on `TaskbarCreated` | Exiting silently |
| Update available | Tray menu, Settings | `Version <x> is available.` | `Install` / `Later` / release notes | Installing without consent unless opted in |
| Update failed | Settings, post-install toast | `Update failed.` + log location | `Open log`, download from GitHub | Silent restart into a broken state |
| Corrupted ICS file | Settings → Calendars, per source | `Couldn't read <file> (line <n>).` | Replace the file; other sources unaffected | Dumping parser output or event text |

## 8. Exit, uninstall and migration

- **Hide versus quit:** X, Escape, outside click and deactivation hide; process, tray icon and hotkey stay; `Alt+F4` hides. `Quit` lives only in the tray menu (plus an optional `Calwren: quit` palette command): unregisters the hotkey, removes the icon, flushes settings and exits within about a second, confirming only during a sign-in (`Cancel sign-in and quit?`); shutdown/logoff exits without a prompt.
- **Tray unavailable:** if the icon cannot be registered (shell error, Explorer down), the hotkey and Start menu still work; show it once in the flyout banner and permanently in About; retry on `TaskbarCreated`; never exit silently and never open a window at logon because of it.
- **Single instance:** one process owns the tray icon and hotkey; a second launch redirects to it.
- **Uninstall:** removing the package removes the tray icon and startup entry and terminates a running app (MSIX); the update cache is cleaned on the next start; `%LOCALAPPDATA%\ItsycalForWindows\calendars` (the user's own `.ics` files) is kept and the README says so; settings/update-state retention is documented rather than implied.
- **1.x → v2.0.0 (Calwren):** new Publisher/package family name, so no in-place upgrade (ADR 0004); migration is uninstall 1.x, install 2.0. On first run the new package detects legacy `%LOCALAPPDATA%\ItsycalForWindows` data and offers `Use my existing calendars and settings` (default) or `Start fresh`, never deleting silently. If both packages run, a one-time note explains the two tray icons and documents quitting one; 1.x remains a bridge version.

## 9. Acceptance criteria

Each AC is observable on an installed MSIX and maps to §10; the UX slices (UX-101…UX-105) may implement them in phases.

- [ ] **AC-1 — first launch:** on a clean profile the first user-initiated launch shows the welcome exactly once (hotkey line, `Add calendars`, startup checkbox off, no sign-in, no network request) with the tray icon present; after dismissal and restart the normal flyout appears and the welcome does not; a logon start never shows it; the welcome fits 448–600 DIP without scrolling.
- [ ] **AC-2 — start policy and latency:** a logon start creates the tray icon and no window, while Start menu/hotkey/tray open the flyout; five recorded runs per path meet §3 (warm ≤300 ms p95, cold ≤2.0 s, no flash, no wrong-monitor show).
- [ ] **AC-3 — tray discovery:** the tooltip is `Calwren — calendar for Windows (Ctrl+Alt+C)` (≤128 chars, localized), the icon is legible on light and dark taskbars, overflow is named in welcome/About, the icon returns after an Explorer restart, and the hotkey works while it is hidden.
- [ ] **AC-4 — tray interactions:** left click toggles (visible-unfocused activates, focused hides); double click never opens-then-hides; right click / Shift+F10 opens the native menu with exactly `Open Calwren`, `Settings…`, `Check for updates`, `About`, `Quit` and never quits directly; the keyboard path (Win+B, arrows, Enter/Space) opens and dismisses menu and flyout.
- [ ] **AC-5 — hotkey:** Ctrl+Alt+C opens and focuses the flyout on the configured monitor and hides it when already focused; `Change…` rebinds with conflict detection and `Disable` keeps tray/Start paths; a registration failure produces the §7 banner, not just a log line; rebind/quit unregister the old chord and the choice persists.
- [ ] **AC-6 — single instance:** a second launch while running shows the existing flyout and creates no second process, tray icon or hotkey registration; after quitting, a launch starts normally.
- [ ] **AC-7 — placement, focus, dismissal:** every launch path opens on the tray monitor (cursor monitor when configured), clamped to the work area and repositioned on every show; focus lands on the selected day; outside click/Escape hide and return focus to the previous foreground window; no first-show flash at 100/125/150/200% or after a mixed-DPI move.
- [ ] **AC-8 — daily keyboard use:** arrows/Enter, `PageUp`/`PageDown`, `Home`, Tab/Shift+Tab order, Enter on a row and action, `Ctrl+,`, `Alt+F4` hides and the two-stage Escape behave as in §4; Tab never leaves the flyout; all of it works without a mouse.
- [ ] **AC-9 — pointer and actions:** day/row/action clicks match the keyboard results; `Join meeting` hides the flyout before the default browser opens; `Copy link` reports `Meeting link copied.`; outside click dismisses without quitting; right click inside is non-destructive.
- [ ] **AC-10 — accessibility on launch paths:** Narrator opens the flyout from the hotkey and the tray keyboard path, announces the product and lands on the selected day; every interactive element has its spec 0004 AC-4b name; Settings is fully keyboard-navigable with visible focus.
- [ ] **AC-11 — local empty state and CTA:** the welcome CTA and `Add calendars…` invoke the same outcome as `Calwren: open calendar folder`, creating the folder when missing and opening Explorer; no account prompt appears anywhere in a local-only run.
- [ ] **AC-12 — account connect:** consent precedes the browser; only the system browser is used; `Waiting for your browser…` with Cancel is shown; cancel or a five-minute timeout closes the loopback listener and adds nothing; `access_denied`, admin-policy/consent, bind and network failures each map to a §7 message; no token or code appears in UI or logs.
- [ ] **AC-13 — account list and status:** every ADR 0005 §5.6 state renders with label, last sync and the right action; enable/disable, re-authenticate, sign out and remove behave as §5; the flyout shows only an aggregate that opens Accounts; a failing account never hides local or other-account events; About/Store copy makes no provider claim before REL-303.
- [ ] **AC-14 — offline and encrypted cache:** with accounts configured, a lost connection shows `Offline — showing saved events` and keeps cached events; after restart the account shows the cached events with the last-sync time (or `Not synced yet` when no cache exists) and retries with backoff; the on-disk cache is encrypted at rest (DPAPI-wrapped key + authenticated encryption per account), contains no plaintext event content, and is purged on sign-out/removal; local sources stay visible throughout.
- [ ] **AC-15 — settings:** one Settings window contains the five §6 sections and opens from the tray menu, `Ctrl+,` while the flyout is focused, and the palette command; the palette page mirrors status and launches it (or shows install guidance); hotkey/startup/accounts/week settings apply without restart, language applies with the restart prompt, and no second surface duplicates the values.
- [ ] **AC-16 — updates:** an available update is visible in the tray menu and Settings, and never installs without consent unless auto-install is on; installing from the flyout restores the tray icon and reopens the flyout only if it was open; failure shows `Update failed.` with the log path and the old version keeps running; first-run state is not replayed.
- [ ] **AC-17 — error copy:** every §7 row is reachable and renders the stated copy and exactly one recovery action from resources (no hardcoded strings), with readable contrast in dark, light and all high-contrast themes; no stack trace, event content or bare HRESULT is shown.
- [ ] **AC-18 — exit semantics:** X/Escape/outside click/`Alt+F4` hide only; `Quit` is reachable only from the tray menu (or palette command), unregisters the hotkey, removes the icon and exits within about a second with no orphan process; quitting during a sign-in confirms first.
- [ ] **AC-19 — uninstall and retention:** uninstalling while the flyout runs terminates it, removes the tray icon and startup entry, keeps the user's `calendars` folder, cleans the update cache, and README's upgrade/uninstall section matches the observed behaviour.
- [ ] **AC-20 — 1.x → v2.0 migration:** installing v2.0.0 next to 1.x makes no in-place upgrade claim; first run offers reuse of legacy calendars/settings with a genuine `Start fresh` alternative and no silent deletion; the both-installed two-tray-icon state is detected once and documented.

## 10. Test plan

| AC | Test | Kind |
|---|---|---|
| AC-1 | `FirstRunPolicyTests` / `Welcome_ShownOnceForUserLaunch`; installed clean-profile launch + welcome screenshot read back; logon start | unit + manual + screenshot |
| AC-2 | Launch-timing runs (5×, logged timestamps); hidden logon start; no-flash capture sequence | manual + screenshot |
| AC-3 | Installed tray tooltip capture light/dark; Explorer restart; overflow case with the icon hidden | manual + screenshot |
| AC-4 | `TrayMenuPolicyTests` / menu items and click mapping; installed mouse/keyboard tray run | unit + manual |
| AC-5 | `HotkeyRegistrationTests` / conflict and rebind mapping; installed conflict run (second app owns Ctrl+Alt+C), rebind, disable | unit + manual |
| AC-6 | `SingleInstanceTests` / redirect decision; installed double-launch and post-quit launch | unit + manual |
| AC-7 | `LaunchPlacementTests` / monitor selection and clamp; tray and cursor-monitor screenshots at four DPI scales with a mixed-DPI move | unit + manual + screenshot |
| AC-8 | Keyboard-only run with logged key path and tab sequence; focus-state screenshots | manual + screenshot |
| AC-9 | Mouse-only run: join/copy/close, outside click, right click inside; clipboard value asserted | manual |
| AC-10 | Narrator + Inspect pass on the hotkey, tray and Settings paths | manual |
| AC-11 | `OnboardingActionTests` / CTA equals folder command; installed folder-create and empty-state screenshots | unit + manual + screenshot |
| AC-12 | `AccountFlowStateTests` / consent→browser→wait→cancel/timeout state machine and error mapping; recorded-fixture runs per provider (no live tokens in CI) | unit + manual |
| AC-13 | `AccountStatusCopyTests` per §5.6 state; installed Accounts screenshots per state; local-only regression run | unit + manual + screenshot |
| AC-14 | `OfflineStateTests` / cache and retry decisions; disk scan for event content; installed airplane-mode run | unit + manual |
| AC-15 | `SettingsApplicationTests` / live-vs-restart matrix; installed Settings screenshots; palette mirror run | unit + manual + screenshot |
| AC-16 | `UpdateSurfaceTests` / indicator and consent decisions; installed previous-build update run and failure path | unit + manual |
| AC-17 | `ErrorCopyTests` / one row per §7 state, resource keys, no raw error text; dark/light/contrast screenshots | unit + screenshot |
| AC-18 | Installed quit run with tray/hotkey/process checks; hide-vs-quit matrix; quit during sign-in | manual |
| AC-19 | Installed uninstall while running; folder and cache inspection; README diff review | manual + review |
| AC-20 | Installed 1.x + v2.0 profile: legacy detection, reuse, `Start fresh`; two-tray note; docs review | manual + review |

Screenshots follow the repository rule: captured from the installed MSIX, read back, and judged by an independent design review against this spec and spec 0004; unit tests cover pure decisions (first-run flag, launch path, placement, hotkey mapping, account state, copy selection) and are not visual evidence.

## 11. Out of scope

- Any implementation, code, workflow, package-manifest or CI change; UX-101…UX-105 are separate slices with their own specs/tests where behaviour changes.
- Flyout geometry, material, keyboard order and update apply path (spec 0004); Command Palette host limits (spec 0003); provider protocols, OAuth and security (ADR 0005, PRV-300…PRV-311).
- Store submission/certification (REL-303), iCloud or Exchange-on-prem support, write/edit events, Windows 11 widgets, bottom dock/taskbar injection.
- Full localization and RTL implementation (LOC-601…LOC-603); this spec only requires resources.
- Automatic uninstall of 1.x, cloud sync/backup of settings, telemetry or crash reporting.

## 12. Open PO decisions

Recommended option first; these gate the affected ACs and become PO decisions once answered.

| # | Decision | Recommendation | Why / alternative |
|---|---|---|---|
| Q1 | Autostart default | **Off**, offered once in the welcome and always in Settings | Windows 11 startup conventions and nothing silent at logon; the hotkey works only while running, so the welcome explains that and Start stays a reliable path |
| Q2 | First-run presentation | **One-time welcome flyout** on the first user launch; no balloon/toast, no wizard | Teaches hotkey, local CTA and startup choice in context; balloons are suppressed by Focus Assist and missed easily, while a silent tray-only first run hides the product |
| Q3 | Default hotkey and rebind | **Ctrl+Alt+C**, rebindable and disableable, conflict notice; pressing it again hides | Matches the current build and tooltip; Win+ chords are OS-reserved and unmodified chords collide; tray and Start menu remain the fallback |
| Q4 | Tray left click | **Toggle**: hide when already focused, otherwise show/focus; double click opens only; right click opens the menu | Keeps familiar tray behaviour without the current right-click-quit; the visible-but-unfocused branch avoids losing the flyout |
| Q5 | Settings location | **One standalone Settings window** in the flyout package plus a read-only palette mirror | Language, hotkey, startup, accounts, sources, updates and About do not fit the Adaptive Card host (ADR 0005 §5.5); a second full page would drift |
| Q6 | Account mandatory before Store | **No** — local-first, zero accounts, no sign-in prompt | ADR 0005 §8.3 already requires local-only parity; Store copy may say "no account required; online calendars are optional" |
| Q7 | Cache and offline default | **Encrypted disk cache** (PO decision 2026-11-08; supersedes the earlier in-memory recommendation in ADR 0005 Q7): cached events survive restart, are encrypted at rest with a DPAPI-wrapped key and authenticated encryption per account, are purged on sign-out/removal, and are never logged | Offline works after restart without exposing plaintext calendar data |

Pending elsewhere, not re-decided here: the package identity (one package with two applications vs two identities, spec 0004 AC-6a) and whether WinGet/trusted signing may precede the Store gate (ADR 0005 Q5); both change the install/first-run evidence for AC-1, AC-19 and AC-20.

## Review

- Spec reviewer: <!-- independent agent, not the author; pending -->
- Verdict: <!-- approve / approve with notes / request changes; pending -->
- Date: 2026-11-08 (draft); Blocking findings resolved: n/a

This is a UX/planning artifact: no implementation starts before an independent spec-review verdict, and each UX slice then follows the normal flow (developer tests first → tester → design review → code review).
