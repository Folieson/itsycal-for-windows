# Roadmap

Itsycal for Windows is moving to two surfaces (ADR 0003): a standalone WinUI 3 tray flyout
with the compact calendar, and a PowerToys Command Palette extension with the dock band and a
month flyout. This roadmap turns it from a single-source viewer into a dependable calendar
surface for both, while respecting the Command Palette host constraints (Adaptive Cards, host
theme, host window size) and the privacy promise (no telemetry, calendar data stays on the
device unless the user configures a provider that requires the network).

**Brand note (2026-11-08):** the product is currently published as Itsycal for Windows. It
rebrands at v2.0.0 under the chosen independent name **Calwren** (Cal + wren;
[ADR 0004](adr/0004-rebrand-independent-product.md),
[brand foundation](reference/brand-calwren.md)). The name is chosen; trademark clearance and
the package/Store identity migration are still pending, and the 1.x line stays as a bridge.
This roadmap keeps the current product name for shipped features until that migration.

**Provider decision (2026-11-08).** Microsoft Store publication is gated until the app is
fully functional for external calendar providers — Google/Gmail, Microsoft 365/Outlook/
Exchange Online, Yandex, CalDAV (iCloud/Fastmail/Nextcloud/mailbox.org) and ICS — per
[ADR 0005](adr/0005-external-calendar-providers-and-store-gate.md). The provider epic
is v1.3 and precedes the v2.0 Store submission; local, dev and (subject to a PO decision on
WinGet) trusted non-Store releases may continue before the gate.

## North star

- The fastest way to answer "what is on my calendar today?" from the keyboard.
- Any calendar the user already has (Outlook.com/Microsoft 365, Google Workspace, Yandex,
  iCloud/Fastmail/Nextcloud/mailbox.org CalDAV, exported `.ics`; Exchange on-premises is a
  documented follow-up) can feed the flyout.
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

### v1.2.3 — "compact, finished UI" (P0, PO decision 2026-11-08)

Per ADR 0003 the primary compact UI moves to a standalone WinUI 3 tray flyout (~360 DIP,
acrylic, hotkey): the month is only for quick date navigation; meeting rows show a video glyph
indicator; the agenda below shows times/locations/inline details with a `Join meeting` button
in the expanded details. The PowerToys Command Palette extension remains an optional secondary
surface, but **it adapts to the flyout's design language** wherever the Adaptive Cards 1.5 host
allows ([spec 0003](specs/0003-compact-calendar-ui.md) rev 5, UI-105/UI-106): the same composition
and agenda → inline-details hierarchy, neutral row anatomy, accent discipline, typography/spacing
tokens and acrylic-forward surfaces. The host still fixes the window, material strength and
button metrics and the surface may scroll (including for an ordinary day), so it cannot match
the primary compactness; those are documented host limits, not permission to diverge, and an
achievable visual/interaction difference is a defect. CmdPal parity follows the flyout's
accepted visual system as its reference and runs in parallel with the provider platform; it is
P1 but not a provider/Store-gate blocker. Renderer-supported keyboard (Today/prev/next, Enter on
actions) is expected on both surfaces; per-element UIA names and arrow-key day focus are deferred
on the CmdPal surface and are implemented in the primary flyout (spec 0004).
Plan: [BACKLOG.md](BACKLOG.md). ADR: [0003](adr/0003-surfaces-and-host-strategy.md).
Spec: [0004-standalone-compact-flyout.md](specs/0004-standalone-compact-flyout.md); CmdPal
parity: [0003-compact-calendar-ui.md](specs/0003-compact-calendar-ui.md).
Shared core: models and providers are already in `ItsycalForWindows.Core` (PR #39); `UpdateService`
and settings extraction is the remaining ARCH-802 work. The feasibility spike on
`feat/flyout-spike` is evidence only — packaging, tray lifecycle and the flyout-specific update
path are proven by the implementation, not by the spike.

### v1.3 — "provider platform" (P0, Store gate)

Per the PO decision of 2026-11-08 ([ADR 0005](adr/0005-external-calendar-providers-and-store-gate.md)),
the app must read the calendars people actually use before it can be submitted to the
Microsoft Store. The epic is read-only, opt-in per account, and keeps the no-telemetry and
no-event-content-logging promise. Provider logic moves into a new
`ItsycalForWindows.Providers` library shared by both surfaces; the tracked rows are
[PRV-300 … PRV-311 and REL-303](BACKLOG.md#provider-epic--store-gate-adr-0005).

| Feature | Acceptance |
|---|---|
| Google Calendar (Gmail/Google Workspace) | OAuth 2.0 authorization code + PKCE on a loopback redirect, `calendar.readonly`, `syncToken` incremental with full resync; installed-build verification with a personal account. |
| Microsoft Graph (Outlook.com/M365/Exchange Online) | Delegated `Calendars.Read`, `calendarView` + delta; Outlook.com and a Microsoft 365 tenant verified; tenant admin-consent case handled; on-prem Exchange documented out of v1 (EWS follow-up, PO Q1). |
| Yandex Calendar + generic CalDAV | Yandex preset at `caldav.yandex.ru` with app password and 2FA guidance; CalDAV discovery/time-range query validated on iCloud, Fastmail, Nextcloud and mailbox.org; self-hosted URLs supported with TLS validation. |
| ICS subscriptions (P1) | HTTPS conditional GET (`ETag`/`If-Modified-Since`), refresh interval, no `user:pass@`; existing local `.ics` folder stays as the zero-setup path. |
| Settings/auth UX | Per-account enable/disable, sign-in/re-authenticate/sign-out/remove, status/last sync/errors; full account window on the primary surface plus a Command Palette status page. |
| Token storage and sync | Windows Credential Locker/DPAPI per account, lazy refresh, on-demand + in-process background sync, encrypted on-disk cache (Q7), backoff and offline states. |
| Verification, privacy, security | Recorded-fixture tests, live-account matrix, privacy policy and Store answers, independent security review; no telemetry. |

**Store gate.** No Microsoft Store submission of a Calwren package before the ADR 0005 §8
criteria are met: all P0 provider rows accepted on an installed signed MSIX, the independent
security review clear, the privacy/compliance pack live, certification test accounts and PO
sign-off. Store *preparation* (identity, policy, screenshots, age rating) may proceed;
*submission* waits. The gate is on Store publication, not on local/dev builds (see
[What can ship before the Store gate](#what-can-ship-before-the-store-gate)).

### v1.4 — "modern, finished UI"

- Richer composition on the primary standalone flyout (the optional CmdPal surface stays
  within Adaptive Cards 1.5): a restrained header band (accent remains reserved for
  selection/today), grouped agenda, all-day chips, event categories/colors, and a
  compact/comfortable density setting.
- Light/dark parity check and screenshots for both surfaces; the standalone flyout follows the
  Windows theme and the CmdPal host decides its own; the UI must look intentional in either.
- Dock band options: compact date, next-event line, week-number style; per-user format
  preview in settings.
- Localization: strings through resource files, at least en-US and ru-RU, with date/time
  formatting following the Windows locale; accessibility names for every action.
- Keyboard-first navigation: an explicit ordered action model for the detail panel and
  settings, and focus restoration after the flyout re-renders.

### v2.0 — "Calwren brand + trusted/Store distribution"

- v2.0.0 carries the **Calwren** brand: the chosen name, original icon/branding and a new MSIX
  Publisher and package family name, with a new WinGet and Store identity
  ([ADR 0004](adr/0004-rebrand-independent-product.md),
  [brand foundation](reference/brand-calwren.md)). Name clearance (US/EU classes 9/42),
  domains and registry IDs are pending (BRAND-902), and the identity/package migration is not
  implemented in code yet. 1.x releases remain bridge versions and there is no in-place
  upgrade: migration is uninstall old and install new.
- Free open-source code-signing certificate (SignPath Foundation / Certum OSS) under the new
  identity → trusted MSIX, non-prerelease channel, WinGet submission under the new
  PackageIdentifier. Trusted signing and WinGet may proceed before the Store gate only with
  the PO's decision (ADR 0005 Q5); Store submission does not.
- Auto-update channels: stable and preview; update check respects the selected channel.
- ARM64 hardware verification and a second machine smoke test.
- Microsoft Store listing under the new identity is **gated by ADR 0005**: submitted only
  after the v1.3 provider epic and its Store-gate criteria are met, with the privacy policy,
  certification test accounts and security review in place.
- Publish an "ICS bridge" experiment: a tiny provider app that writes ICS/CalDAV data into
  the Windows appointment store so other Windows apps benefit from the same sources.

## What can ship before the Store gate

- **Local app:** the appointment store, local `.ics` folder, onboarding, compact UI (specs
  0003/0004), updater and settings continue as today.
- **v2.0.0 brand/identity migration:** the Calwren name, icon, Publisher/PFN change and
  trusted signing, plus a WinGet/sideload release if the PO allows (ADR 0005 Q5). The
  identity migration is not repeated for the Store later.
- **Store preparation that is not submission:** Partner Center account/reservation, privacy
  policy, screenshots, age rating, certification notes and test accounts.
- **Not before the gate:** the Store listing, and any README/WinGet/Store claim that
  Google/Outlook/Yandex calendars work.

## Explicitly out of scope (for now)

- Editing/creating/deleting events: the app stays read-only until the provider layer has
  write-safety design and tests.
- Sending invites or RSVPs.
- Exchange on-premises in v1: Graph is unavailable there; an EWS follow-up is documented in
  [ADR 0005](adr/0005-external-calendar-providers-and-store-gate.md) and is taken only on a
  real requirement (Q1). Do not claim on-prem support.
- Telemetry of any kind, crash reporting to third parties, or storing event content.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Windows appointment store has no maintained first-party provider | Local sources (ICS now, remote providers next) are first-class; the store stays a compatibility source. |
| Google OAuth verification and Entra app registration/tenant consent delay the Store gate | Start registrations at PRV-301/302 kickoff, keep scopes minimal, prepare the demo video and privacy policy from the installed build; graceful "not configured"/admin-consent states; local/WinGet may continue while the Store waits (ADR 0005 Q5). |
| Store gate slips and the provider epic grows without bound | Gate is an explicit checklist (ADR 0005 §8) with PO sign-off; P0 scope is fixed to Google, Graph, Yandex and generic CalDAV + cross-cutting rows; ICS subscriptions are P1 and do not block the gate. |
| On-prem Exchange users are excluded | Documented out-of-scope with an EWS follow-up (ADR 0005 Q1); no false "Exchange" claim in README/WinGet/Store text. |
| Host rendering constraints on the optional CmdPal surface (Adaptive Cards only) | Design within the verified 1.5 subset for that surface but keep the primary flyout's design language as the target (spec 0003 rev 5); host limits are documented, not permission to diverge. The primary flyout owns its renderer, and the installed-build screenshot loop stays mandatory for every visual change. |
| Network/privacy expectations | Network is opt-in per feature/provider, HTTPS only, no telemetry; tokens in the Windows Credential Locker/DPAPI (ADR 0005 §4.2); documented in the privacy section. |
| Update pipeline trust | MSIX must verify against SHA256SUMS and be signed by the repository certificate; Windows refuses untrusted packages. |
| Signing certificate cost/time | Self-signed dev channel continues to work; trusted channel is a funding/procurement task tracked in `docs/SIGNING.md`. |
| Rebrand at v2.0.0 loses name recognition and cannot upgrade in place | [ADR 0004](adr/0004-rebrand-independent-product.md): 1.x bridge releases, uninstall/reinstall migration docs and repository redirects; the rebrand shares the identity break trusted signing already requires. |

## Sequenced backlog (next 11 tickets)

1. Finish the compact flyout implementation (spec 0004) and its installed-build evidence.
2. **CmdPal parity (UI-105/UI-106, spec 0003 rev 5)** — after the flyout's visual system is
   accepted; run it in parallel with the provider library (item 3), not before or as a blocker
   of the Store gate. Implementation starts only after the independent spec review.
3. Shared provider library, registry and contracts (PRV-300); token/secret storage (PRV-307).
4. Account/settings shell and sync scheduler (PRV-306/PRV-308).
5. Google Calendar provider (PRV-301) and its OAuth verification submission.
6. Microsoft Graph provider (PRV-302) for Outlook.com/M365/Exchange Online.
7. Generic CalDAV engine with Yandex, iCloud, Fastmail, Nextcloud and mailbox.org presets
   (PRV-304/PRV-303).
8. ICS subscriptions (PRV-305) and recurrence hardening (REL-201).
9. Verification harness and privacy/compliance pack (PRV-309/PRV-310).
10. Independent security review (PRV-311).
11. Store gate and certification (REL-303) after the v2.0 identity is ready; trusted signing
    and WinGet follow the PO's decision on ADR 0005 Q5.

## Definition of done for every roadmap item

- Behaviour covered by unit tests (pure logic) and, where visual, verified on an installed
  build with a captured screenshot that was read back.
- No secrets in git, no telemetry, no event content in logs.
- Docs updated: README features, `docs/` for procedures, `docs/adr/` for decisions.
