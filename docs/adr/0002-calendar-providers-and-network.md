# Calendar providers and the network policy

Status: implemented (accepting ICS) / proposed (remote providers)
Date: 2026-11-08

## Context

The extension started as a read-only view of the Windows appointment store
(`Windows.ApplicationModel.Appointments`), which was the right first source but is
effectively empty on current Windows 11: the only first-party provider (Mail and Calendar)
was retired, the new Outlook does not populate the store, and the taskbar calendar is a
shell integration with Outlook rather than a public API. A calendar surface that depends on
this store cannot answer "what is on my calendar" for most users.

At the same time the project rule said "no network calls", which blocks every real provider
(Google Calendar, Microsoft Graph/Exchange, CalDAV) and self-update.

## Decision

1. **`IEventSource` is the provider boundary.** The extension always renders through a
   composite source; local sources (Windows store, ICS folder) and future remote providers
   are interchangeable, isolated from each other, and merged with de-duplication by
   `(subject, start, end)`.
2. **ICS folders are the first non-store source.** `%LOCALAPPDATA%\ItsycalForWindows\calendars\*.ics`
   is parsed locally (RFC 5545 subset, simple recurrence), needs no account, no network and
   no OAuth review, and covers exported Google/Outlook/Exchange/Apple calendars today.
3. **Network access is allowed only for explicit, user-facing features, opt-in and
   documented.** Two categories:
   - *self-update*: GET GitHub releases, download the signed MSIX and `SHA256SUMS.txt`,
     verify the hash, install after stopping the host;
   - *remote calendar providers*: user authorizes a provider (OAuth/CalDAV), and the
     extension talks only to that provider's API over HTTPS.
   There is no telemetry, no analytics, no third-party crash reporting. Calendar content is
   never sent anywhere except to the provider the user configured, and event content is
   never written to logs.
4. **Remote provider credentials** are stored per provider, encrypted with Windows DPAPI
   (`ProtectedData`, current user), under `%LOCALAPPDATA%`, and never logged or put in git.
   Tokens must be revocable from the settings page; refresh tokens are refreshed lazily.
5. **Remote providers are gated by an explicit settings toggle per provider** and render a
   clear "not configured" state; no background network traffic happens for a provider that
   the user has not enabled.
6. **Update installs require signature trust.** The updater downloads assets from the
   project's GitHub releases, verifies the SHA256 from `SHA256SUMS.txt` and relies on
   Windows MSIX signature/publisher checks. It stops `Microsoft.CmdPal.UI` and the extension
   process first, installs with `Add-AppxPackage`, restarts the palette and logs the result
   locally (`update.log`). There is no silent install without a user-visible update check.

## Alternatives considered

- **Stay store-only** — rejected: no maintained provider, the calendar is empty on clean
  Windows 11 (verified locally: one store calendar, zero events with Outlook and
  OneCalendar installed).
- **Graph-only** — rejected: locks out Google Calendar and privacy-conscious users, needs
  tenant/admin consent for many Exchange setups.
- **Store-only plus "add manually"** — rejected: users expect their existing calendars.
- **No network at all** — rejected: no self-update and no real provider integration.
- **Silent automatic updates without hash verification** — rejected: an update path without
  integrity checks is a remote-code-execution risk.

## Consequences

- Positive: exported/exportable calendars work today; the provider abstraction makes
  Google/Microsoft/CalDAV additive work rather than rewrites; updates can ship safely.
- Negative: the extension now makes network requests in explicitly enabled features, so the
  privacy section, README and ADR must be kept accurate; OAuth flows add attack surface and
  require careful token handling.
- Neutral: ICS files remain the zero-setup path; the Windows store remains a compatibility
  source when present.

## Follow-ups

- [ ] Settings surface: per-provider enable/disable, connection state, last sync, sign out.
- [ ] Google Calendar OAuth (PKCE, loopback redirect, DPAPI token store).
- [ ] Microsoft Graph OAuth (`Calendars.Read`) for Microsoft 365/Exchange Online.
- [ ] CalDAV (URL + app password).
- [ ] Update channel setting (stable/preview) and update check throttling UX.
- [ ] Security review of the OAuth flows and the updater script before a stable release.
- [ ] Privacy policy / README privacy section updated with the network matrix.

## References

- `docs/adr/0001-windows-command-palette-extension.md` (original extension decisions)
- `docs/ROADMAP.md` (milestones and backlog)
- `docs/SIGNING.md` (trusted signing options for public releases)
- `windows/src/ItsycalForWindows/Services/` (event sources and composite)
- `windows/src/ItsycalForWindows/Services/UpdateService.cs` (self-update, once merged)
