# ADR 0001 — Ship Itsycal for Windows as a Command Palette dock-band extension

- Status: accepted
- Date: 2026-11-07
- Revision: 2026-11-07 — distribution corrected to signed MSIX after the discovery and
  WinGet research summarized in [Distribution evidence](#distribution-evidence)
- Deciders: project maintainer

## Context

ItsyCal is a macOS menu-bar calendar: a glanceable date in the menu bar, one click to a
month grid, and events from the system calendar. The fork must keep the macOS application
buildable, so the Windows product cannot be a port of the Objective-C sources.

PowerToys 0.102 ships **Command Palette**, whose **Dock** is a persistent toolbar at the
screen edge — the closest Windows surface to a menu bar widget. Its extension SDK
(`Microsoft.CommandPalette.Extensions`, 0.11) supports contributing dock bands and rich
flyouts.

## Decision

1. Add `windows/` to the fork; leave the macOS sources untouched.
2. Implement the Windows product as a Command Palette extension (C#, .NET 10), not a
   standalone tray application: the Dock gives the always-visible placement, plus theming,
   input, and discovery through PowerToys.
3. One dock band shows the current date (refreshed once a minute) and opens a month flyout:
   42-cell grid, ISO week numbers, configurable week start, today highlighted, month
   navigation, and events for the visible range.
4. Events are read-only from the local Windows appointment store
   (`Windows.ApplicationModel.Appointments`, restricted capability `appointmentsSystem`).
   No network, no telemetry; any access failure degrades to a calendar without events.
5. Distribution:
   - **Public releases and WinGet:** a signed, self-contained MSIX per architecture
     (`windows/scripts/Build-Release.ps1`), published as GitHub Release assets, plus a
     WinGet manifest with `InstallerType: msix`, `PackageFamilyName` and
     `SignatureSha256`. The package must be signed by a trusted certificate; see
     `docs/SIGNING.md`.
   - **Development:** MSIX package for local install via `windows/scripts/deploy-local.ps1`,
     signed with an ephemeral self-signed certificate.
6. The extension ships no autonomous updater; WinGet/`winget upgrade` owns updates, and the
   repository tags releases (`v*`) whose workflow builds, signs, and attaches the packages
   together with the prepared WinGet manifests.

## Distribution evidence

The MSIX-only route is not a preference; it is the only route that works:

- Command Palette discovers extensions only through the MSIX app-extension catalog
  (`AppExtensionCatalog.Open("com.microsoft.commandpalette")`). The official extension
  template's unpackaged/Inno Setup guidance is therefore non-functional — the CmdPal lead
  has no working example of an unpackaged extension, and the template's HKCU COM
  registration is not picked up by the host
  ([PowerToys #44442](https://github.com/microsoft/PowerToys/issues/44442)).
- Real Command Palette extensions in the WinGet community repository ship signed MSIX with
  `InstallerType: msix`, `PackageFamilyName`, `SignatureSha256`, and the
  `windows-commandpalette-extension` locale tag that Command Palette uses for discovery.
- The package is self-contained: the .NET runtime travels inside the MSIX and no
  Windows App Runtime package dependency is required.
- `SignatureSha256` is the SHA256 of the `AppxSignature.p7x` entry inside the MSIX, and
  `winget install` requires that signature to be trusted. The certificate subject must
  match the package `Publisher`; a self-signed package only installs after the user trusts
  its `.cer` and is not acceptable for WinGet.

## Alternatives considered

- **Standalone tray application** — no always-visible top-of-screen band, and it would
  duplicate settings, theming, and update distribution.
- **Win32 topmost floating bar** — looks like a second taskbar, breaks on multi-monitor and
  DPI changes, and needs its own autostart and update channel.
- **Port the Objective-C code** — EventKit/AppKit are macOS-only; the result would be a
  rewrite that also touches upstream files.
- **Writing and editing events in the first release** — requires write access and conflict
  handling; the read-only integration proves the product without becoming a calendar
  client first.
- **Unpackaged install with an Inno Setup installer and HKCU COM registration** — the host
  never discovers such an extension, so it cannot be supported; see [Distribution
  evidence](#distribution-evidence).

## Consequences

- Positive: one extension delivers date glanceability, a month grid, and events with no
  extra windows; upstream stays mergeable; the signed MSIX is discoverable by Command
  Palette and installable/upgradable through WinGet.
- Negative: releases now depend on a trusted code-signing certificate and the MSIX
  packaging tooling; the Publisher/identity and the package family name are
  release-sensitive, so a certificate with a different subject forces a deliberate
  identity change; a WinGet submission also needs publicly downloadable release assets.
- Neutral: the macOS app and the Windows extension share branding and intent, not code.

## Follow-ups

- [ ] Unit tests for the calendar grid, ISO weeks, day spans, and event ranges (in
      progress).
- [ ] Day selection with per-day event details and "open in Calendar".
- [ ] Obtain a trusted code-signing certificate, cut a signed release, and submit
      `Folieson.ItsycalForWindows` to `microsoft/winget-pkgs` using the manifests staged in
      `winget/` (see [docs/SIGNING.md](../SIGNING.md)).
- [ ] Verify the arm64 MSIX on Windows on ARM hardware.
- [ ] Localization beyond English.
- [ ] Event reminders/alerts (ItsyCal parity); needs write access, out of scope for now.

## Amendment 2026-11-07 — Windows-only repository

The macOS application sources, Xcode project, design files, macOS build scripts and the Apple signing workflow were removed from this repository. The product repository now contains only the Windows extension, its tests, documentation and release tooling. Upstream remains at https://github.com/sfsam/itsycal and the fork relation plus the MIT copyright in `LICENSE.txt` preserve attribution; the removed sources stay in the git history. Rationale: the macOS code is not buildable or relevant here, and a lean repository is easier to review, release and submit to package managers.
## Amendment 2026-11-08 — calendar providers and network policy

The read-only Windows appointment store is no longer the only event source. A composite
event source merges it with local ICS files today and with remote providers (Google
Calendar, Microsoft Graph/Exchange, CalDAV) later, each behind an explicit per-provider
opt-in. Network access is no longer forbidden outright: it is limited to self-update and
user-enabled calendar providers, with HTTPS, no telemetry, DPAPI-protected credentials and
no event content in logs. Details and the security follow-ups: [ADR 0002](0002-calendar-providers-and-network.md);
the product milestones are in [docs/ROADMAP.md](../ROADMAP.md).