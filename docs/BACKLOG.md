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

## Now / next / later

| ID | Priority | Item | Outcome / acceptance | Status |
|---|---|---|---|---|
| ARCH-801 | P0 | Hybrid surfaces: standalone WinUI 3 tray flyout + optional CmdPal extension | ADR 0003 accepted: standalone WinUI 3 tray app is the primary compact surface (acrylic, ~360 DIP, hotkey); CmdPal extension stays optional; shared `ItsycalForWindows.Core`; no taskbar/bottom-dock injection. Feasibility spike exists on `feat/flyout-spike` (evidence only, not acceptance evidence). | Decided; spec 0004 rev 3 in review |
| ARCH-802 | P0 | Extract `ItsycalForWindows.Core` | Calendar models and the appointment/ICS/composite providers are in `ItsycalForWindows.Core` on master (PR #39); both surfaces consume it. `UpdateService` and settings extraction remains, with no duplicated provider/update logic in either surface. | In progress — models + providers merged (#39) |
| UI-101 | P0 | Compact calendar flyout (standalone primary) | Primary surface is the standalone WinUI 3 flyout (~360 DIP, acrylic, hotkey; ADR 0003): month grid is quick-navigation only, selected-day agenda with inline details below, a video glyph indicator on meeting rows and `Join meeting` in the expanded details. Client height is content-hugging 448–600 DIP with a 480 ±16 DIP collapsed target; the optional CmdPal surface keeps its host constraints and may scroll ([spec 0003](specs/0003-compact-calendar-ui.md)). Spec: [0004](specs/0004-standalone-compact-flyout.md). | Spec 0004 rev 3 re-review pending |
| UI-102 | P0 | Inline details and meeting actions in agenda | Selecting an event expands its details under the row: full when-line, location, notes, `Join meeting` (only for meeting links), `Copy link` and `Close details`; no-link events show `Close details` only. A video glyph on meeting rows is a non-interactive indicator, not an action; no separate details block after the whole agenda. The optional CmdPal surface may scroll for long details ([spec 0003](specs/0003-compact-calendar-ui.md)); the primary flyout owns the no-scroll typical-day contract ([spec 0004](specs/0004-standalone-compact-flyout.md)). | In spec 0004 rev 3 / optional in 0003 rev 4 |
| UI-103 | P1 | Visual system: light/dark, typography, spacing, accessibility | Optional CmdPal surface: host acrylic translucency visible through the card (no opaque panel fill, no fake alpha); adaptive dark/light; readable contrast over the material; no clipped labels at 800×480 and 640×420; renderer-supported keyboard (Today/prev/next, Enter on actions). Arrow-key day focus and per-element UIA names are host-feature-dependent and deferred there. The primary flyout follows spec 0004 (full keyboard/UIA contract). | Planned |
| UI-105 | P2 | Optional CmdPal compact-surface polish | Implement [spec 0003](specs/0003-compact-calendar-ui.md) on the optional Command Palette surface after the primary flyout ships: 224-DIP month budget measured against the visible ContentPage viewport, inline details with `Join meeting`/`Copy link`, theme/translucency, scrolling allowed for any day. Host-limited by design; explicitly **not** parity with the primary flyout and not on the critical path. | Spec 0003 rev 4 in review |
| ONB-101 | P1 | Calendars onboarding | `Itsycal: open calendar folder` command, import `.ics…` file picker, source status (name, last read, parse errors), empty-state CTA. | Planned |
| REL-201 | P1 | ICS recurrence correctness | DST wall-clock expansion and BYDAY; fail-closed for unsupported RRULE parts. Spec [0002](https://github.com/Folieson/itsycal-for-windows/blob/c92c31d/docs/specs/0002-reliability-hardening.md) (reliability hardening; narrowed after two independent reviews; pinned to the latest reviewed commit on `spec/v1.2.2-reliability` until it merges). | Spec review in progress |
| REL-202 | P2 | Remaining reliability findings | Version/updater minors (TOCTOU, log encoding), release/CI gates (tests on release tags, ARM64, `winget validate`, branch protection), docs accuracy. | Planned |
| SET-401 | P2 | Settings consolidation | One Settings page: Calendar / Dock / Calendars / Updates; human labels, dock-format preview, update status. | Planned |
| DOC-501 | P2 | Dock band screenshot + release evidence | Enable PowerToys Dock, capture dark/light/narrow, attach to README; release checklist. | Planned |
| PRV-301 | P3 | Provider platform (Google/Graph/CalDAV) | OAuth/CalDAV, DPAPI tokens, per-provider settings; ADR 0002. Deferred until UI/onboarding are done. | Deferred |
| LOC-601 | P3 | Localization (en-US/ru-RU) | `.resw` strings, culture date/time formats; README updated. | Deferred |
| UI-104 | Done | Independent design-review gate | Read-only design reviewer judges installed-build screenshots against the accepted UI spec/reference after the tester and before code review; independence from author/tester/reviewers; verdict + screenshots recorded in the PR. | Added |
| CHG-701 | Done | Changelog discipline | `CHANGELOG.md` with sections for every release; contributors update `[Unreleased]`; the release workflow extracts the tagged `## [X.Y.Z]` section into the GitHub release notes and fails when it is missing. | Done — `release.yml` section extraction verified |

## Dependencies and constraints

- UI work must be verified on an installed build with screenshots that are read back
  (host-rendered Adaptive Cards; JSON assertions are not evidence). Dock screenshot needs
  PowerToys Dock enabled.
- `ItsycalForWindows.Core` (models + providers) is on master (PR #39). `UpdateService` and
  settings remain in the CmdPal app until ARCH-802 completes, so both surfaces must reuse the
  existing download/verify code instead of forking it.
- The optional CmdPal surface must still fit the Command Palette host page (`ContentPage` →
  Adaptive Card) and the declared palette window size. The primary standalone flyout owns a
  WinUI 3 window (spec 0004); it is no longer constrained to the palette card. The CmdPal
  surface may scroll; the no-scroll contract belongs to the flyout only.
- Providers wait for onboarding and the UI: the settings/status surface is the same one
  sources will use.
- Every user-visible change adds a `CHANGELOG.md` line under `[Unreleased]`; a release tag
  requires a matching `## [X.Y.Z]` section.
