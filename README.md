# Itsycal for Windows

A calendar for the **Windows PowerToys Command Palette Dock** вЂ” the closest Windows
equivalent of [ItsyCal](https://github.com/sfsam/itsycal)'s menu-bar calendar.

This repository contains the Windows application only. It is derived from the MIT-licensed
[ItsyCal](https://github.com/sfsam/itsycal) project by Sam Vermette and contributors; the
original macOS sources remain available upstream and in this repository's git history. The
fork relation is kept for attribution and history, not for shared code.

## Features

- **Date in the Dock** вЂ” an always-visible band at the screen edge showing today's date,
  refreshed automatically across midnight.
- **Month calendar** вЂ” one click opens a flyout with a 6Г—7 month grid, today highlighted,
  adjacent-month days dimmed, and previous/next/today navigation.
- **Day selection** вЂ” click a day to see that day's events; the selected day is highlighted.
- **Events** вЂ” read-only events from the local Windows calendar, with time ranges, location,
  all-day and multi-day handling; the calendar keeps working when no calendar is configured.
- **ISO week numbers** вЂ” optional week-number column (ISO-8601).
- **Settings** вЂ” dock date format, event indicator, first day of the week, week numbers,
  applied without restarting the extension.
- **Private by design** вЂ” no network calls, no telemetry, calendar data never leaves the
  machine.

## Requirements

| Requirement | Version |
|---|---|
| Windows | 10 2004 (build 19041) or newer |
| PowerToys | 0.102 or newer, with Command Palette enabled |

The MSIX package carries the .NET runtime it needs; no separate runtime installation is
required.

## Install

The current public build is the **1.0.0 development pre-release** вЂ” a signed MSIX for x64
and ARM64 that is self-contained (it carries its own .NET runtime, no separate installation
needed):

1. Download `ItsycalForWindows_1.0.0.0_x64.msix` (or the `_arm64.msix` file) and
   `ItsycalForWindows_1.0.0.0_dev.cer` from the
   [v1.0.0 release](https://github.com/Folieson/itsycal-for-windows/releases/tag/v1.0.0).
2. From an **elevated** PowerShell, trust the development certificate once and register the
   package:

   ```powershell
   Import-Certificate -FilePath .\ItsycalForWindows_1.0.0.0_dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   Add-AppxPackage -Path .\ItsycalForWindows_1.0.0.0_x64.msix
   ```

3. Open **Command Palette в†’ Dock settings** and enable the **Itsycal for Windows** band.

The build is a pre-release because it is signed with a development certificate. `winget
install Folieson.ItsycalForWindows` becomes the primary path once a trusted (free
open-source) code-signing certificate is in place; the staged WinGet manifests in
[`winget/`](winget) already carry the release URLs and hashes, and
[`docs/SIGNING.md`](docs/SIGNING.md) documents the signing options.

## Screenshots

<!-- Screenshots are added by the maintainer after the end-to-end UI check. -->

_Placeholder: dock band, month flyout and settings page screenshots land here._

## Build from source

Requires the .NET 10 SDK, the Windows SDK 10.0.26100 and Visual Studio Build Tools for MSIX
packaging.

```powershell
dotnet build windows/ItsycalForWindows.sln -c Debug -p:Platform=x64
dotnet test  windows/tests/ItsycalForWindows.Tests.csproj -c Debug
```

Release packaging and signing are described in [windows/README.md](windows/README.md) and
implemented by [windows/scripts/Build-Release.ps1](windows/scripts/Build-Release.ps1).

## Configuration

Open the extension's command in Command Palette and choose **Settings** to configure the
dock date format, event indicator, first day of the week and ISO week numbers. Settings are
stored by Command Palette.

## Privacy

The extension reads events through the read-only Windows appointment store
(`Windows.ApplicationModel.Appointments`). It never writes to the calendar, never sends data
anywhere, and logs no event content. Access is declared with the `appointmentsSystem`
capability in the package manifest.

## Repository layout

| Path | Purpose |
|---|---|
| `windows/` | Windows extension source, tests, build and release scripts. |
| `windows/src/ItsycalForWindows/` | The Command Palette extension (C#, .NET 10, MSIX). |
| `windows/tests/` | Unit tests for the pure calendar/event logic. |
| `winget/` | WinGet manifests for the MSIX release. |
| `docs/adr/` | Architecture decision records. |
| `docs/SIGNING.md` | Code-signing options and release secrets. |

## Contributing

Read [AGENTS.md](AGENTS.md) for repository rules (human and AI contributors alike) and
[CONTRIBUTING.md](CONTRIBUTING.md), then open a pull request using the template.

## License

MIT. See [LICENSE.txt](LICENSE.txt) вЂ” it retains the upstream ItsyCal copyright. The Windows
extension skeleton is derived from the Microsoft PowerToys template; see
[windows/THIRD-PARTY-NOTICES.md](windows/THIRD-PARTY-NOTICES.md) for details.
