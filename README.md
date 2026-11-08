# Itsycal for Windows

A calendar for the **Windows PowerToys Command Palette Dock** — the closest Windows
equivalent of [ItsyCal](https://github.com/sfsam/itsycal)'s menu-bar calendar.

This repository contains the Windows application only. It is derived from the MIT-licensed
[ItsyCal](https://github.com/sfsam/itsycal) project by Sam Vermette and contributors; the
original macOS sources remain available upstream and in this repository's git history. The
fork relation is kept for attribution and history, not for shared code.

## Features

- **Date in the Dock** — an always-visible band at the screen edge showing today's date and
  the next event today, refreshed automatically across midnight.
- **Month calendar** — one click opens a flyout with a 6x7 month grid, today highlighted,
  adjacent-month days dimmed, event dots, and previous/next/today navigation.
- **Day selection** — click a day to see that day's events; the selected day is highlighted.
- **Event details and joining** — click an event to see its full when-line (all-day and
  multi-day aware), location, notes and a **Join meeting** action for Teams/Zoom/Google
  Meet/KTalk-style links (validated `http(s)` only, opened in the default browser), plus a
  copy-link fallback.
- **Calendar sources** — events are merged from the local Windows appointment store and from
  `.ics` files in `%LOCALAPPDATA%\ItsycalForWindows\calendars\`. The ICS folder is the
  reliable source on current Windows builds, where the system appointment store is often
  empty; it reads manual exports (no live sync yet) and currently expands daily/weekly
  recurrences only. Native Google/Microsoft/CalDAV providers are on the roadmap.
- **Self-update** — checks the project's GitHub releases when you opt in (24-hour throttle),
  shows the available version, downloads the MSIX, verifies it against `SHA256SUMS.txt` and
  installs it through a detached updater that restarts the palette. Manual
  `Itsycal: check for updates` / `Itsycal: install update` commands are always available.
- **ISO week numbers** — optional week-number column (ISO-8601).
- **Settings** — dock date format, event indicator, first day of the week, week numbers and
  update behaviour (auto-check/auto-install), applied without restarting the extension.
- **Private by design** — no telemetry and no event content in logs. Network access is
  limited to self-update checks you enable and, in future, remote calendar providers you
  configure. Automatic update checks are **off** by default; the first check happens when
  you run the check command, or on the next launch after enabling the setting.

## Requirements

| Requirement | Version |
|---|---|
| Windows | 10 2004 (build 19041) or newer |
| PowerToys | 0.102 or newer, with Command Palette enabled |

The MSIX package carries the .NET runtime it needs; no separate runtime installation is
required.

## Install

The current public build is a **development pre-release** — a signed MSIX for x64 and ARM64
that is self-contained:

1. Open the [releases page](https://github.com/Folieson/itsycal-for-windows/releases) and
   download the newest x64 (or arm64) MSIX together with its matching `_dev.cer`.
2. From an **elevated** PowerShell, trust the development certificate once and register the
   package (the certificate subject must match the package publisher,
   `CN=ItsycalForWindows-Dev`; the release also attaches the `_dev.cer` that signed the package — verify it with
   `Get-AuthenticodeSignature .\ItsycalForWindows_<version>_x64.msix`):

   ```powershell
   Import-Certificate -FilePath .\ItsycalForWindows_<version>_dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   Add-AppxPackage -Path .\ItsycalForWindows_<version>_x64.msix
   ```

3. Open **Command Palette → Dock settings**, add the **Itsycal for Windows** band.
4. Add events: drop exported `.ics` files into
   `%LOCALAPPDATA%\ItsycalForWindows\calendars\` (the folder is created on first read; open it in
   Explorer).

The build is a pre-release because it is signed with a development certificate.
`winget install Folieson.ItsycalForWindows` becomes the primary path once a trusted
(free open-source) code-signing certificate is in place; staged manifests are in
[`winget/`](winget), and [`docs/SIGNING.md`](docs/SIGNING.md) documents the signing options.

## Upgrade and uninstall

- **Upgrade**: close Command Palette first (the running host holds package files), install the
  newer MSIX with `Add-AppxPackage`, then open Command Palette again. Each build stamps a
  higher package revision, so no uninstall is needed. If Windows reports `0x80073D02`
  (files in use), close the palette and the extension process and retry.
- **Uninstall**: `Get-AppxPackage ItsycalForWindows | Remove-AppxPackage`. Settings, update state and logs stay under `%LOCALAPPDATA%\ItsycalForWindows`;
  the `calendars` folder contains your own exported `.ics` files — do not delete it unless you
  have copies elsewhere. If you no longer trust the development certificate, remove
  it with `Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -like '*ItsycalForWindows-Dev*' | Remove-Item`.

## Data and network

- Data: `%LOCALAPPDATA%\ItsycalForWindows\` — `settings.json`, `update-settings.json`,
  `calendars\*.ics`, `updates\`, `update.log`.
- Network: only `api.github.com` / GitHub release downloads for self-update when you opt in;
  no telemetry, no calendar data is uploaded, event titles are not logged.
- Update failures are written to `%LOCALAPPDATA%\ItsycalForWindows\update.log`; the release
  notes are not displayed in-app yet.

## Screenshots

| Command Palette | Calendar flyout |
|---|---|
| ![Itsycal for Windows in Command Palette search](docs/screenshots/palette-search.png) | ![Month calendar with event dots and the selected day events](docs/screenshots/calendar-flyout.png) |

| Event details | |
|---|---|
| ![Inline event details with the selected event expanded below the agenda](docs/screenshots/event-details.png) | Clicking an event expands its details under the agenda: full when-line, location, notes and the Join meeting / Copy link actions for events that carry a meeting URL. |

Captured from the installed 1.2.x development builds with demo data, dark theme and a Russian
locale. The extension UI strings are currently English-only while dates follow the Windows
display language and first-day-of-week setting. The calendar marks days that have events with
a dot; the demo-events commands exist only in DEBUG builds. The dock-band screenshot is still
pending because PowerToys Dock is not enabled on the verification machine.

## Changelog

Release notes: [CHANGELOG.md](CHANGELOG.md).

## Build from source

Requires the .NET 10 SDK, the Windows SDK 10.0.26100 and Visual Studio Build Tools for MSIX
packaging.

```powershell
dotnet build windows/ItsycalForWindows.sln -c Debug -p:Platform=x64
dotnet test windows/tests/ItsycalForWindows.Tests.csproj -c Debug
```

Release packaging and signing: `windows/scripts/Build-Release.ps1`. CI runs build, tests with
a coverage floor and MSIX packaging on every PR (`.github/workflows/windows-build.yml`).

## License

MIT. See [LICENSE.txt](LICENSE.txt) — it retains the upstream ItsyCal copyright. The Windows
port reuses material from Microsoft PowerToys; see
[windows/THIRD-PARTY-NOTICES.md](windows/THIRD-PARTY-NOTICES.md).
## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

- **Authors / committers:** [@Folieson](https://github.com/Folieson).
- **Reviewers:** [@Folieson](https://github.com/Folieson) reviews every change proposed by
  people who are not committers before it is merged.
- **Approvers:** [@Folieson](https://github.com/Folieson) approves every signing request.
  Each release is signed only after a manual approval.
- **What is signed:** only binaries built from this repository's source by the public GitHub
  Actions pipeline (the standalone WinUI 3 flyout and its MSIX installer; the optional
  PowerToys Command Palette extension MSIX). Upstream MIT components are not submitted for
  signing. The development certificate used for local/test builds is not a trusted public
  signing identity.
- **Privacy:** this program will not transfer any information to other networked systems
  unless specifically requested by the user or the person installing or operating it. There
  is no telemetry and no calendar/event content is logged or uploaded; the only built-in
  network use is the opt-in self-update check against GitHub Releases (GitHub's own privacy
  policy applies to that request), and future calendar providers only after the user
  configures them. See [SECURITY.md](SECURITY.md) for the data-handling details.
- **Installation / uninstallation:** MSIX install and `Remove-AppxPackage` uninstall are
  documented above; no background services or system configuration changes are made beyond
  the user-created app data folder and the optional autostart setting. Remove the app data
  folder manually if desired.
- **Reproducibility:** every release artifact is produced by GitHub Actions from the tagged
  commit; the workflow, test suite and packaging scripts are in this repository.

## Independence

ItsyCal for Windows is an independent project. It is not affiliated with or endorsed by the
ItsyCal macOS app or its author; the project was inspired by ItsyCal and retains the upstream MIT
attribution in [LICENSE.txt](LICENSE.txt). The planned v2.0.0 rebrand will ship under the chosen
name **Calwren**, recorded in
[docs/adr/0004-rebrand-independent-product.md](docs/adr/0004-rebrand-independent-product.md).
