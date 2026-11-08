# Product backlog

Working plan for the next increments. The user is the **product owner**; decisions that
change scope or priority are recorded here as PO decisions. Specs live in `docs/specs/`.

## PO decision — 2026-11-08: compact, miniaturised UI first

The user (PO) directed that the interface must become **beautiful, modern, laconic and
intuitive**, modelled on the original ItsyCal: a small popover where the month calendar is
only for quick date navigation (it must not take half the screen), and below it the day's
meetings are visible with times, details and a `Join meeting` action for the selected
event. This epic takes priority over the provider platform: a compact, finished UI is the
product's core value; OAuth providers add surface without it.

Reference: original ItsyCal [tiny menu bar calendar](https://www.mowglii.com/itsycal/)
(calendar + agenda below the menu-bar icon; clean readable design; up to two months; dark
mode; keyboard navigation) and its
[banner](https://www.mowglii.com/itsycal/itsycalbanner2@2x.png): a ~350-DIP-wide popover,
month grid on top, compact toolbar, agenda groups with colour dots, time ranges and a video
icon on meeting rows.

**Decision:** UI compaction is the next product epic after v1.2.2 reliability; the PO chose the hybrid surface strategy on 2026-11-08 (ADR 0003): a standalone WinUI 3 tray flyout becomes the primary compact UI, the CmdPal extension remains optional, and a custom bottom dock is rejected. Changelog
discipline is introduced at the same time (`CHANGELOG.md`, Keep a Changelog).

## PO decision — 2026-11-08: the CmdPal surface follows the primary flyout

The user (PO) decided that the optional PowerToys Command Palette surface must **adapt to the
primary standalone flyout's visual and interaction language**, not merely be "polished" within
its host limits. The same composition (month header/navigation → weekday header → six week rows
→ compact agenda header → agenda rows → inline details directly under the selected row), neutral
row anatomy, accent discipline (selected day/today plus the `Join meeting` primary action only),
typographic/spacing rhythm and acrylic-forward surface treatment apply as far as the Adaptive
Cards 1.5 host allows.

The surface remains optional in distribution and may scroll; host-owned constraints (window
size, material strength, action-button metrics, per-element UIA names, full keyboard parity) are
recorded as limits, **not** as permission to diverge. A visual or interaction difference the
card can express is a defect. Spec [0003](specs/0003-compact-calendar-ui.md) revision 5 carries
the mandate; UI-105 tracks the contract and UI-106 the implementation. ADR 0003 records the
decision.

## PO decision — 2026-11-08: independent product brand at v2.0.0

The user (PO) decided that v2.0.0 ships under an independent product identity: a new product
name, original branding and icon, a new MSIX `Publisher` and package family name, and a new
WinGet/Store identity. The product has outgrown its ItsyCal-port framing — a standalone WinUI
3 tray flyout, an optional Command Palette extension and a shared core with its own acrylic
UI and roadmap — while the MIT license grants code rights, not the ItsyCal name or brand.
Trusted signing already forces a one-time Publisher/PFN identity migration, so v2.0.0
combines it with the rebrand. The full decision is
[ADR 0004](adr/0004-rebrand-independent-product.md).

The product name is **Calwren** (Cal + wren), chosen by the PO on 2026-11-08
([brand foundation](reference/brand-calwren.md)): a warm, calm, minimalist wren silhouette
perched on a calendar row, a terracotta/amber accent on neutral, the lowercase `calwren`
wordmark and the descriptor `Calwren — calendar for Windows`. This is a naming and design
decision only: the **public rename happens at v2.0.0**, when the package becomes a clean
identity with no in-place MSIX upgrade; until then the current 1.x line stays the bridge under
the old identity, and migration is documented as uninstall 1.x and install 2.0. Trademark
clearance (US/EU classes 9/42), domains (`calwren.com`, `calwren.app`, `calwren.dev`),
GitHub/npm/NuGet IDs, WinGet/Store identity and the final icon/wordmark remain pending
(BRAND-902).

**v2.0.0 milestone:** independent brand + trusted/Store distribution — trusted signing, the
WinGet listing and any Store listing ship under the new name and Publisher, while the 1.x
line stays a bridge.

## PO decision — 2026-11-08: Store publication gated on external providers

The user (PO) decided that Microsoft Store publication — and the trusted-signing/Store
submission that goes with it — is **gated** until the app is fully functional for the
calendars people actually use: Exchange/Outlook, Gmail, Yandex Mail/Calendar and the other
major/popular sources. A local-only build (Windows appointment store plus exported `.ics`)
would be judged broken in Store review and by first-run users. The provider epic therefore
moves ahead of Store submission; it does not change the read-only or privacy promises. The
decision, provider matrix, security model, architecture and gate criteria are
[ADR 0005](adr/0005-external-calendar-providers-and-store-gate.md); the rows are
[PRV-300 … PRV-311 and REL-303](#provider-epic--store-gate-adr-0005). Open PO questions
(scope, registrations, WinGet timing) are listed in the ADR.

## Now / next / later

| ID | Priority | Item | Outcome / acceptance | Status |
|---|---|---|---|---|
| ARCH-801 | P0 | Hybrid surfaces: standalone WinUI 3 tray flyout + optional CmdPal extension | ADR 0003 accepted: standalone WinUI 3 tray app is the primary compact surface (acrylic, ~360 DIP, hotkey); CmdPal extension stays optional; shared `ItsycalForWindows.Core`; no taskbar/bottom-dock injection. Feasibility spike exists on `feat/flyout-spike` (evidence only, not acceptance evidence). | Decided; spec 0004 rev 4 (rev 3 approved with notes; height amendment re-review pending) |
| ARCH-802 | P0 | Extract `ItsycalForWindows.Core` | Calendar models and the appointment/ICS/composite providers are in `ItsycalForWindows.Core` on master (PR #39); both surfaces consume it. `UpdateService` and settings extraction remains, with no duplicated provider/update logic in either surface. | In progress — models + providers merged (#39) |
| UI-101 | P0 | Compact calendar flyout (standalone primary) | Primary surface is the standalone WinUI 3 flyout (~360 DIP, acrylic, hotkey; ADR 0003): month grid is quick-navigation only, selected-day agenda with inline details below, a video glyph indicator on meeting rows and `Join meeting` in the expanded details. Client height is content-hugging 448–600 DIP with a 544 ±16 DIP collapsed target (preliminary 547 DIP on the unpackaged v3 build with an in-memory replica of the five-row ordinary fixture: 302 + 244 = 546 layout + 1-DIP headroom; supersedes the 480 estimate; acceptance re-measures on the installed MSIX with the committed fixture). The optional CmdPal surface follows the flyout's design language within its host constraints and may scroll ([spec 0003](specs/0003-compact-calendar-ui.md) rev 5). Spec: [0004](specs/0004-standalone-compact-flyout.md). | Spec 0004 rev 4; rev 3 approved with notes, height amendment re-review pending; CmdPal parity spec 0003 rev 5 in review (UI-105/UI-106) |
| UI-102 | P0 | Inline details and meeting actions in agenda | Selecting an event expands its details under the row: full when-line, location, notes, `Join meeting` (only for meeting links), `Copy link` and `Close details`; no-link events show `Close details` only. A video glyph on meeting rows is a non-interactive indicator, not an action; no separate details block after the whole agenda. The optional CmdPal surface follows the same inline-details contract within host limits ([spec 0003](specs/0003-compact-calendar-ui.md) rev 5) and may scroll for long details; the primary flyout owns the no-scroll typical-day contract ([spec 0004](specs/0004-standalone-compact-flyout.md)). | In spec 0004 rev 4 / parity contract in 0003 rev 5 |
| BRAND-901 | P0 | v2.0.0 Calwren rebrand and package-identity migration | Ship v2.0.0 under the chosen **Calwren** name and original icon/branding; new MSIX `Publisher` and package family name; new WinGet and Store identity; trusted/Store signing under the new identity; README/release migration docs (uninstall 1.x, install 2.0) and a "not affiliated with ItsyCal" credit line; 1.x releases stay bridge versions. Remaining work: US/EU class 9/42 clearance, domains `calwren.com`/`.app`/`.dev`, GitHub org, npm/NuGet IDs, icon/wordmark approval, and the Publisher/PFN/WinGet/Store migration. | In progress — name Calwren selected; clearance, IDs, icon and identity migration pending |
| BRAND-902 | P1 | Calwren name research, trademark clearance and identifier reservations | Name research is **done**: Calwren selected by the PO (2026-11-08). Remaining: trademark clearance in the US/EU in Nice classes 9 and 42; availability and reservation of domains (`calwren.com`, `calwren.app`, `calwren.dev`), the GitHub organization, npm/NuGet package IDs, WinGet and the Store. Keep candidate names and artwork private until cleared. | Name research done; clearance + reservations pending |
| UI-103 | P1 | Visual system: light/dark, typography, spacing, accessibility | Shared visual system for the primary flyout and the CmdPal surface: light/dark/accent roles, typography and spacing tokens, accessibility and contrast. CmdPal side: host acrylic translucency visible through the card (no opaque panel fill, no fake alpha); adaptive dark/light; readable contrast over the material; no clipped labels at 800×480 and 640×420; the flyout's token map normative in [spec 0003](specs/0003-compact-calendar-ui.md) rev 5 AC-8 (host-mapped metrics are not defects); renderer-supported keyboard (Today/prev/next, Enter on actions). Arrow-key day focus and per-element UIA names are host-feature-dependent and deferred there. The primary flyout follows spec 0004 (full keyboard/UIA contract). | Planned — parity contract in 0003 rev 5 |
| UI-105 | P1 | CmdPal parity with the primary flyout | Adapt [spec 0003](specs/0003-compact-calendar-ui.md) rev 5 to the primary flyout's design language (the parity contract): month header/navigation and a six-row month block measured against the visible ContentPage viewport, compact agenda header, neutral row anatomy (marker/time/title and a non-interactive video glyph), inline details directly under the selected row with `Join meeting`/`Copy link`/`Close details` (no-link = `Close details` only), accent only for selected day/today plus the Join primary action, typography/spacing token parity, light/dark over host acrylic, and scrolling with actions reachable at scroll-end. Host limits are not defects; an achievable visual/interaction difference is a defect. Independent spec review before implementation. | Spec 0003 rev 5 in review |
| UI-106 | P1 | CmdPal parity implementation | Implement the accepted [spec 0003](specs/0003-compact-calendar-ui.md) rev 5: inline details next to the selected row, neutral markers/time/titles + video glyph, month header with inline navigation and compact agenda header, unfilled month block, typography/spacing token map, light/dark + host acrylic; pure Adaptive-Card JSON tests plus installed-build screenshots at 800×480/640 and light/dark, scroll-to-end long details, and the design review. | Blocked on spec 0003 rev 5 approval (UI-105) |
| ONB-101 | P1 | Calendars onboarding | Import `.ics…` file picker, per-source status (name, last read, parse errors), empty-state CTA beyond the folder pointer. The folder command is sliced out as ONB-101a. | Planned — ONB-101a in review (PR #42); rest deferred |
| ONB-101a | P1 | Open calendar folder command (slice of ONB-101) | `Itsycal: open calendar folder` creates and opens `%LOCALAPPDATA%\ItsycalForWindows\calendars` from the palette and the calendar item's context menu; the selected-day empty state shows the 0003/0004 pointer as a separate `TextBlock`. Spec [0005](specs/0005-calendar-folder-onboarding.md). | In review — PR #42, spec re-review pending |
| REL-201 | P1 | ICS recurrence correctness | DST wall-clock expansion and BYDAY; fail-closed for unsupported RRULE parts. Spec [0002](https://github.com/Folieson/itsycal-for-windows/blob/c92c31d/docs/specs/0002-reliability-hardening.md) (reliability hardening; narrowed after two independent reviews; pinned to the latest reviewed commit on `spec/v1.2.2-reliability` until it merges). | Spec review in progress |
| REL-202 | P2 | Remaining reliability findings | Version/updater minors (TOCTOU, log encoding), release/CI gates (tests on release tags, ARM64, `winget validate`, branch protection), docs accuracy. | Planned |
| SET-401 | P2 | Settings consolidation | One Settings page: Calendar / Dock / Calendars / Updates; human labels, dock-format preview, update status. | Planned |
| DOC-501 | P2 | Dock band screenshot + release evidence | Enable PowerToys Dock, capture dark/light/narrow, attach to README; release checklist. | Planned |
| LOC-601 | P1 | Localization platform + Russian and English | Externalize **every** user-visible string (Command Palette commands/pages/toasts, standalone flyout, settings, errors, empty/loading states, About, package display name, README/Store listing metadata) into a resource system (`.resw`/PRI or an equivalent typed catalog) with culture-aware date/time/number/plural formatting. Ship `en-US` as source and `ru-RU` as the first complete translation; add pseudo-localization for CI/layout checks. Default language follows the Windows display language (`ApplicationLanguages`), with no hardcoded English UI strings left in code. | Planned — P1 pre-Store |
| LOC-602 | P1 | Language switcher and popular-language set | Settings language override with **System language (default)** plus an explicit list; the choice applies consistently to the palette extension and the standalone flyout (reload/restart behavior documented) and is persisted in app settings. Tier 1 target: `ru-RU`, `de-DE`, `fr-FR`, `es-ES`, `pt-BR`, `it-IT`, `pl-PL`, `tr-TR`, `ja-JP`, `ko-KR`, `zh-Hans`, `zh-Hant`, `uk-UA`; every shipped language must have complete UI coverage, not partial machine translation. Store listing/WinGet descriptions localized for the shipped set. | Planned — depends on LOC-601 |
| LOC-603 | P1 | Localized UI adaptation and verification | Verify layouts do not break with longer strings (German/Russian/Polish/Turkish), CJK glyphs/font fallback, plural forms, and RTL readiness if an RTL language is added. Capture installed-build screenshots per shipped language for the palette and flyout (month/weekday/date formats, agenda, long event titles, details/actions, empty/error states), run design review per language, and keep a pseudo-locale CI check for clipping/truncation. Date/time formats must follow the selected culture (including the current `MMM d` hardcode). | Planned — depends on LOC-601/602 |
| UI-104 | Done | Independent design-review gate | Read-only design reviewer judges installed-build screenshots against the accepted UI spec/reference after the tester and before code review; independence from author/tester/reviewers; verdict + screenshots recorded in the PR. | Added |
| CHG-701 | Done | Changelog discipline | `CHANGELOG.md` with sections for every release; contributors update `[Unreleased]`; the release workflow extracts the tagged `## [X.Y.Z]` section into the GitHub release notes and fails when it is missing. | Done — `release.yml` section extraction verified |

Deferred ONB-101 items after ONB-101a (PR #42): `.ics` import file picker / drag-and-drop,
per-source status (name, last read, parse errors), and an empty-state call-to-action beyond the
folder pointer. [Spec 0005](specs/0005-calendar-folder-onboarding.md) lists the same set under
Out of scope.

## Launch and usage UX (spec 0007)

The end-to-end launch and usage UX (install → first run → every launch path → daily use →
providers/accounts → settings → errors → quit/uninstall/migration) is specified in
[spec 0007](specs/0007-launch-and-usage-ux.md). It is **UX only**: no code, packaging or behaviour
change ships with it; each row needs its own implementation slice (tests first) and the normal review
gates.

| ID | Priority | Item | Acceptance / outcome | Depends on | Status |
|---|---|---|---|---|---|
| UX-101 | P1 | Launch and first run | Tray-only start (hidden at logon, visible for a user launch), one-time welcome with hotkey + Add calendars + start-with-Windows offer, tray overflow/tooltip discovery, single instance, tray-registration and hotkey-conflict fallbacks (spec 0007 AC-1…AC-7). | Spec 0004 AC-6a/6b (package identity, startup task), MSIX-packaged flyout | Planned — spec 0007 in review |
| UX-102 | P1 | Daily use and keyboard | Open → navigate → read → inline details → Join/Copy/Close → dismiss on every launch path; launch-level keys (`PageUp`/`PageDown`, `Home`, `Ctrl+,`, `Alt+F4` hides), pointer model, UIA/Narrator launch paths, per-show positioning and DPI/theme/reduced-motion behaviour (AC-8…AC-10). | UX-101; spec 0004 AC-2/AC-4 (geometry, keyboard, UIA) | Planned |
| UX-103 | P1 | Provider and account setup UX | Accounts window flows: provider list, consent before browser, OAuth handoff/wait/cancel/timeout, app-password guidance, account list with status/last sync/re-auth, sign-out/remove, aggregate "needs attention", offline/cache states; local-only path unchanged (AC-11…AC-14). | PRV-306/PRV-307/PRV-308 (account model, secrets, sync); ADR 0005 §5.5 | Planned |
| UX-104 | P1 | Settings, states and errors | One Settings window (General/Calendars/Accounts/Updates/About) with tray, in-flyout shortcut and CmdPal entry points and documented live-vs-restart behaviour; the state/error copy matrix with one recovery action per row; update available/failed UX (AC-15…AC-17). | UX-101, UX-103; LOC-601/LOC-602 (resources, language) | Planned |
| UX-105 | P2 | Exit, uninstall and migration | Hide-vs-quit semantics and the single Quit path, tray-unavailable fallback, uninstall retention (calendars kept, tray/startup removed), 1.x→v2.0 Calwren identity migration note and legacy-data reuse offer (AC-18…AC-20). | UX-101; BRAND-901 (v2.0 identity) | Planned |

## Provider epic — Store gate (ADR 0005)

The provider epic is P0 because the Store listing is blocked until it is done. The full
decision, provider matrix and gate criteria are
[ADR 0005](adr/0005-external-calendar-providers-and-store-gate.md). PRV-301 was previously
the deferred P3 provider-platform row; this plan reassigns it to Google Calendar and makes
**PRV-300** the umbrella. Sizes are indicative (S ≤ ~1 week, M ~2–3 weeks, L ~4–6 weeks of
one engineer including tests), not schedule promises.

| ID | Priority | Item | Acceptance / outcome | Size | Depends on | Status |
|---|---|---|---|---|---|---|
| PRV-300 | P0 | Provider epic, registry and shared contracts | `ItsycalForWindows.Providers` framework + ICS subscriptions first slice is in PR #52 (registry/contracts, DPAPI secret delivery, sync, ICS, thin `IEventSource` adapter); both-surface composition and provider UI remain PRV-306/PRV-308, so the app reference was removed until wiring. ADR 0005 accepted; scope questions resolved or deferred in ADR 0005 §10. | M | — | In review — PR #52 |
| PRV-301 | P0 | Google Calendar (Gmail/Workspace) | Google Calendar API v3, OAuth 2.0 authorization code + PKCE on loopback, `calendar.readonly`, `syncToken` incremental with full resync on `410`, Workspace admin-policy error handled; verified on an installed signed MSIX with a personal and a Google Workspace account. | L | PRV-300, PRV-307 | Planned |
| PRV-302 | P0 | Microsoft Graph (Outlook.com/M365/Exchange Online) | Graph `me/calendars` + `me/calendarView` + delta, OAuth 2.0 public client + PKCE, `Calendars.Read` delegated; Outlook.com and a Microsoft 365 tenant verified; tenant admin-consent-required handled; on-prem Exchange explicitly out of v1 (EWS follow-up, PO Q1). | L | PRV-300, PRV-307 | Planned |
| PRV-303 | P0 | Yandex Calendar | CalDAV preset at `caldav.yandex.ru` with app password (2FA guidance + deep link), no public OAuth assumed; verified with a 2FA-enabled account; app-password revocation instructions. | S | PRV-304, PRV-307 | Planned |
| PRV-304 | P0 | Generic CalDAV engine + presets | RFC 4791/6764 principal and calendar-home discovery, time-range `REPORT`, ETag/`sync-token` incremental with full-resync fallback; presets validated on iCloud, Fastmail, Nextcloud and mailbox.org; TLS validation never disabled. | L | PRV-300, PRV-307 | Planned |
| PRV-305 | P1 | ICS subscriptions (HTTP(S) + local) | `ETag`/`If-Modified-Since` and `304`, configurable refresh with jitter, size cap, existing parser reused; no `user:pass@` credentials; token-in-path feeds treated as secrets; not a Store-gate blocker (local `.ics` already works). | M | PRV-300, PRV-307 | Planned |
| PRV-306 | P0 | Settings and authentication UX | Shared account model; add/enable/disable/sign-in/re-authenticate/sign-out/remove; status/last sync/errors; full `AccountsWindow` + CmdPal status page **including the `ProviderEventSource`/composite wiring into both surfaces**; **owns the `settings.json` account-metadata layout** per ADR 0005 §4.2 (depends on PRV-307 for the secret layout); installed-build screenshots. | L | PRV-300, PRV-307 | Planned |
| PRV-307 | P0 | Token and secret storage | Refresh tokens in the Windows Credential Locker, app passwords/token URLs in DPAPI per account, atomic rotation, no plaintext, redaction tests, deletion on sign-out/remove; 20-entry locker cap; **owns the final `providers\` secret layout and the migration from the framework's per-account `secrets.dat`**. | M | PRV-300 | Planned |
| PRV-308 | P0 | Sync scheduler, cache and status | On-demand visible-range fetch plus in-process background refresh (no service/task); **encrypted on-disk event cache (PO decision; DPAPI-wrapped key, TTL/purge, no plaintext)**, backoff/jitter, `429 Retry-After`, offline and partial-failure states; local sources never hidden. The framework's in-memory TTL cache is a placeholder. | M | PRV-300, first adapter | Planned |
| PRV-309 | P0 | Verification harness and provider test matrix | Recorded-fixture contract tests for Google/Graph/CalDAV/ICS, parser fuzz corpus, live-account manual matrix documented; CI never stores real tokens/accounts. | M | PRV-301, PRV-302, PRV-304 | Planned |
| PRV-310 | P0 | Privacy/compliance pack | Privacy policy at a project-owned URL, Store privacy answers, revocation/deletion instructions per provider, capability justification (`internetClient`, possibly `privateNetworkClientServer`), no-telemetry network-capture evidence. | M | PRV-306 | Planned |
| PRV-311 | P0 | Independent security review | Read-only review of OAuth/loopback, token storage, redirects, SSRF/ICS handling and logging; no blocking findings, or deferred with PO agreement; re-run after material changes. | M | PRV-301, PRV-302, PRV-304, PRV-307 | Planned |
| REL-303 | P0 | Store gate and certification pack | No Store submission before ADR 0005 §8 is met; dedicated certification test accounts/app passwords, screenshots from the installed build, age rating, capability/support notes, v2.0 identity + trusted certificate; explicit PO sign-off. | S | All P0 + PRV-311 | Gated — ADR 0005 §8 |

**Order:** PRV-300/PRV-307/PRV-306 start first (contracts, secrets, settings shell);
PRV-301 and PRV-302 run in parallel; PRV-304 unlocks PRV-303 and the iCloud/Fastmail/
Nextcloud/mailbox.org presets; PRV-305 runs alongside; PRV-308 lands with the first adapter;
PRV-309/PRV-310 run alongside; PRV-311 precedes the gate; REL-303 is last. Local builds,
the existing local app and non-Store work continue while the gate is closed. On-prem
Exchange (Q1), write providers (Q2) and an encrypted offline cache (Q7) are explicitly
deferred in the ADR.

## Dependencies and constraints

- UI work must be verified on an installed build with screenshots that are read back
  (host-rendered Adaptive Cards; JSON assertions are not evidence). Dock screenshot needs
  PowerToys Dock enabled.
- `ItsycalForWindows.Core` (models + providers) is on master (PR #39). `UpdateService` and
  settings remain in the CmdPal app until ARCH-802 completes, so both surfaces must reuse the
  existing download/verify code instead of forking it.
- The optional CmdPal surface must still fit the Command Palette host page (`ContentPage` →
  Adaptive Card) and the declared palette window size, and it follows the primary flyout's
  design language where the host allows ([spec 0003](specs/0003-compact-calendar-ui.md) rev 5,
  UI-105/UI-106). The primary standalone flyout owns a WinUI 3 window (spec 0004); it is no longer
  constrained to the palette card. Host-owned limits are recorded, not used to justify divergent
  UI; the CmdPal surface may scroll; the no-scroll contract belongs to the flyout only.
- CmdPal parity depends on the flyout's accepted visual system (spec 0004 plus the design
  review) as the reference. It is P1 after the flyout hardening and can run in parallel with the
  provider platform (PRV-300); it must not block the provider/Store gate, and the provider epic
  must not block it.
- Providers depend on the settings/status surface (PRV-306) and the shared provider library
  (PRV-300); they are now a Store-gate item ([ADR 0005](adr/0005-external-calendar-providers-and-store-gate.md)),
  not a deferred P3. `IEventSource` stays the data contract, provider logic lives in
  `ItsycalForWindows.Providers`, and neither app may fork it.
- Microsoft Store submission is blocked until ADR 0005 §8 is met (the P0 provider rows,
  PRV-311 and REL-303). Trusted signing, the v2.0 package identity and WinGet may proceed
  only with the PO's decision on ADR 0005 Q5; no Store package is submitted early.
- Provider v1 is read-only and opt-in per account: no write scopes, no telemetry, no event
  content on disk or in logs, tokens per ADR 0005 §4.2. On-prem Exchange, write support and an
  encrypted offline cache are out of v1 (ADR 0005 Q1/Q2/Q7).
- Every user-visible change adds a `CHANGELOG.md` line under `[Unreleased]`; a release tag
  requires a matching `## [X.Y.Z]` section.
- Rebranding is a package-identity change ([ADR 0004](adr/0004-rebrand-independent-product.md)):
  the 1.x and 2.0 packages have different family names, so there is no in-place MSIX upgrade.
  Migration docs must instruct uninstall/reinstall, and trusted/Store signing waits for the new
  Publisher/PFN (BRAND-901/BRAND-902). The name Calwren is chosen; clearance and identifier
  reservations are still pending, and no rename ships before v2.0.0. The Store listing also
  waits for the provider gate (ADR 0005).
