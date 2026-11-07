# Roadmap

Itsycal for Windows is a PowerToys Command Palette extension: an always-visible dock band
with the date and a month flyout with read-only calendar events. This roadmap turns it from
a single-source viewer into a dependable calendar surface for the palette, while respecting
the constraints of the host (Adaptive Cards, host theme, host window size) and the privacy
promise (no telemetry, calendar data stays on the device unless the user configures a
provider that requires the network).

## North star

- The fastest way to answer "what is on my calendar today?" from the keyboard.
- Any calendar the user already has (Outlook/Microsoft 365, Google, Exchange on-prem,
  CalDAV, exported `.ics`) can feed the flyout.
- Small, quiet, fast, private: no account required for the local experience.

## Milestones

### v1.2 — "useful day-to-day" (in progress)

| Feature | Acceptance |
|---|---|
| Self-update | The extension checks GitHub releases (throttled, opt-in auto-check), shows the available version, downloads and verifies the MSIX against `SHA256SUMS.txt`, then installs it through a detached updater that stops the palette, runs `Add-AppxPackage`, restarts the palette and logs the result locally. Manual "Check for updates" command is always available. |
| Event details | Clicking an event row opens an inline details block: full title, when (all-day, multi-day), location, notes, and the meeting link. |
| Join meeting | A `Join meeting` action appears when the event carries a usable http(s) link (Teams, Zoom, Google Meet, KTalk, …); it opens the system browser. A copy-link fallback is provided. |
| ICS provider | `%LOCALAPPDATA%\ItsycalForWindows\calendars\*.ics` files are parsed (RFC 5545 subset, basic recurrence) and merged with the Windows appointment store via a composite source. Any calendar app that can export `.ics` becomes a data source: Google Calendar, Outlook/M365, Exchange, Apple Calendar, CalDAV bridges. |
| Dock band and settings | Dock subtitle shows the next event today (`13:00 Lunch`) instead of a bare count; update and calendar-source settings are reachable from the palette and documented. |

### v1.3 — "provider platform"

| Feature | Acceptance |
|---|---|
| Provider settings UX | A real settings surface lists every source with enable/disable, last sync, error state and a per-source color; changes apply without restarting the host. |
| `ICalendarProvider` contract | Remote providers implement a small contract (authenticate, list calendars, fetch range, subscribe to changes) shared with the local/ICS sources; the composite source no longer knows provider types. |
| Google Calendar | OAuth 2.0 for installed apps with PKCE and loopback redirect; user grants read-only calendar access; refresh token encrypted with DPAPI; events fetched for the visible range only; no event content stored or logged. |
| Microsoft Exchange / Outlook | Microsoft Graph `Calendars.Read` delegated OAuth (works for Microsoft 365 and Exchange Online); on-prem Exchange covered through Graph where available, otherwise documented as unsupported. |
| CalDAV | URL + app password flow for providers that expose CalDAV (Fastmail, Nextcloud, iCloud app password, some Exchange setups). |
| Sync and caching | Per-provider cache with a visible range window, exponential backoff on failures, offline-first rendering of the last good data. |

### v1.4 — "modern, finished UI"

- Richer flyout composition within Adaptive Cards 1.5: accent header band, grouped agenda,
  all-day chips, event categories/colors, compact/comfortable density setting.
- Light/dark parity check and screenshots for both themes; the host decides the theme, the
  extension must look intentional in either.
- Dock band options: compact date, next-event line, week-number style; per-user format
  preview in settings.
- Localization: strings through resource files, at least en-US and ru-RU, with date/time
  formatting following the Windows locale; accessibility names for every action.
- Keyboard-first navigation: an explicit ordered action model for the detail panel and
  settings, and focus restoration after the flyout re-renders.

### v2.0 — "distribute everywhere"

- Free open-source code-signing certificate (SignPath Foundation / Certum OSS) → trusted
  MSIX, non-prerelease channel, WinGet submission `Folieson.ItsycalForWindows`.
- Auto-update channels: stable and preview; update check respects the selected channel.
- ARM64 hardware verification and a second machine smoke test.
- Optional Microsoft Store listing (only if it does not compromise the update path).
- Publish an "ICS bridge" experiment: a tiny provider app that writes ICS/CalDAV data into
  the Windows appointment store so other Windows apps benefit from the same sources.

## Explicitly out of scope (for now)

- Editing/creating/deleting events: the extension stays read-only until the provider layer
  has write-safety design and tests.
- Sending invites or RSVPs.
- Telemetry of any kind, crash reporting to third parties, or storing event content.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Windows appointment store has no maintained first-party provider | Local sources (ICS now, remote providers next) are first-class; the store stays a compatibility source. |
| OAuth app registration/review delays and tenant admin consent for Exchange | Start with ICS/CalDAV that need no registration; document Graph scopes and add a graceful "not configured" state. |
| Host rendering constraints (Adaptive Cards only) | Design within the verified 1.5 subset; keep the UI verification loop (install → screenshot → read image) mandatory for every visual change. |
| Network/privacy expectations | Network is opt-in per feature/provider, HTTPS only, no telemetry; tokens in DPAPI; documented in the privacy section. |
| Update pipeline trust | MSIX must verify against SHA256SUMS and be signed by the repository certificate; Windows refuses untrusted packages. |
| Signing certificate cost/time | Self-signed dev channel continues to work; trusted channel is a funding/procurement task tracked in `docs/SIGNING.md`. |

## Sequenced backlog (next 10 tickets)

1. Wire the composite source (store + ICS) and verify with a real exported calendar.
2. Inline event details with join/copy-link and keyboard return path.
3. Update check command + settings page + optional auto-install.
4. Settings page: sources list, update settings, folder open button.
5. ICS provider: multiple files, timezone edge cases, recurring event cap.
6. Google Calendar provider (OAuth PKCE + DPAPI token store).
7. Microsoft Graph provider (Outlook/M365 and Exchange Online).
8. CalDAV provider with app-password flows.
9. Per-source colors/categories in the flyout and dock next-event line.
10. Trusted signing + WinGet submission + stable update channel.

## Definition of done for every roadmap item

- Behaviour covered by unit tests (pure logic) and, where visual, verified on an installed
  build with a captured screenshot that was read back.
- No secrets in git, no telemetry, no event content in logs.
- Docs updated: README features, `docs/` for procedures, `docs/adr/` for decisions.
