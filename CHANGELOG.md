# Changelog

All notable changes to Itsycal for Windows are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). Each release must have a `## [X.Y.Z]` section;
the release workflow reads it to build the GitHub release notes.

## [Unreleased]

### Added

- ONB-101a: `Itsycal: open calendar folder` creates and opens the local calendars
  folder from Command Palette; the empty day shows the folder pointer as a separate
  `TextBlock`.
- Backlog and the compact calendar UI specs: spec 0003 for the optional Command Palette
  surface and spec 0004 for the primary standalone flyout (`docs/BACKLOG.md`,
  `docs/specs/`).
- PO decision (ADR 0003): hybrid surfaces — standalone WinUI 3 tray flyout becomes the primary compact UI, CmdPal extension remains optional, custom bottom dock rejected; specs 0004 (primary) and 0003 (optional) in independent review.
- Changelog discipline for releases.
- Independent design-review gate for UI changes (installed-build screenshots, host-aware
  compactness/typography/states, host acrylic translucency without opaque panels or fake alpha).

### Changed

- Revised both compact-UI specs after the round-3 independent spec review. Spec 0003 now scopes
  the optional CmdPal surface honestly: the impossible no-scroll criterion is deleted, the month
  budget is measured against the visible ContentPage viewport, Copy uses the normalized
  `AbsoluteUri` through an injectable clipboard helper, and the planned fixture covers
  all-day/multi-day rows (with MSIX `%LOCALAPPDATA%` virtualisation to verify). Spec 0004 now
  defines a 448–600 DIP content-hugging client height (480 ±16 DIP collapsed, grow-to-600 then
  scroll), splits material/positioning/DPI/keyboard/packaging/update criteria, separates the
  flyout update path from the CmdPal updater, records the merged shared core and the remaining
  ARCH-802 work, adds empty/loading/error states, and marks the feasibility spike as
  non-acceptance evidence.
- Backlog: UI-105 added for optional CmdPal surface polish; ARCH-802 and the UI-101/102 status
  reflect the merged `ItsycalForWindows.Core` (models + providers) and spec 0004 revision 3.

## [1.2.2] - 2026-11-08

### Added

- First-run guidance: the empty day message names the calendars folder.
- ICS meeting links from `DESCRIPTION`/`LOCATION` and `X-GOOGLE-CONFERENCE` (Google Meet,
  Zoom, Teams, Webex, KTalk), with RFC 5545 unescaping and a strict host allowlist.
- The Windows appointment store fetches `OnlineMeetingLink` and `Details`, so Join/Copy and
  notes work for store events.

### Changed

- Automatic update checks are now opt-in (off by default), matching the privacy policy.
- Development builds derive their package version from the newest release tag
  (`X.Y.Z.<run+1>`), so a dev MSIX is newer than the published release.
- The updater script is written as UTF-8 with BOM, re-verifies the SHA256 immediately
  before applying, and restarts Command Palette in a `finally` block on success or failure.
- README/install/upgrade/uninstall/certificate/data documentation rewritten and corrected.

### Fixed

- The checksum file is written atomically; a failed checksum download leaves no partial
  `SHA256SUMS.txt`.
- Update downloads fall back to the GitHub API asset endpoint on transport failures, 5xx,
  429 and 403; user cancellation never triggers a fallback.
- Trusted signing: `SIGNING.md` names `TRUSTED_SIGNING_PFX_*`, and the release workflow
  fails instead of silently producing a dev-signed build when trusted mode is requested.

## [1.2.1] - 2026-11-07

### Added

- GitHub API asset endpoint as a fallback for update downloads.
- Spec-first/TDD process: specs in `docs/specs/`, coverage floor in CI (30%),
  independent spec/code reviews.
- Unit tests for the update download pipeline (fallback, SHA256, asset ids).

### Fixed

- Update downloads no longer fail when the browser download URL is blocked but the API
  endpoint works.

## [1.2.0] - 2026-11-07

### Added

- Inline event details: full when-line, location, notes, Join meeting and Copy link.
- Dock band subtitle with the next event today.
- ICS folder provider and merged local calendar source (Windows appointment store + ICS).
- Self-update: check GitHub releases, SHA256 verification, detached updater that restarts
  Command Palette.
- Roadmap, ADR 0002 and the event-details screenshot.

[Unreleased]: https://github.com/Folieson/itsycal-for-windows/compare/v1.2.2...HEAD
[1.2.2]: https://github.com/Folieson/itsycal-for-windows/releases/tag/v1.2.2
[1.2.1]: https://github.com/Folieson/itsycal-for-windows/releases/tag/v1.2.1
[1.2.0]: https://github.com/Folieson/itsycal-for-windows/releases/tag/v1.2.0
