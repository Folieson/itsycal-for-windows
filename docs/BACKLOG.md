# Product backlog

Working plan for the next increments. The user is the **product owner**; decisions that
change scope or priority are recorded here as PO decisions. Specs live in `docs/specs/`.

## PO decision — 2026-11-08: compact, miniaturised UI first

The user (PO) directed that the interface must become **beautiful, modern, laconic and
intuitive**, modelled on the original ItsyCal: a small popover where the month calendar is
only for quick date navigation (it must not take half the screen), and below it the day's
meetings are visible with times, details and a one-click **Join** icon for the selected
event. This epic takes priority over the provider platform: a compact, finished UI is the
product's core value; OAuth providers add surface without it.

Reference: original ItsyCal [tiny menu bar calendar](https://www.mowglii.com/itsycal/)
(calendar + agenda below the menu-bar icon; clean readable design; up to two months; dark
mode; keyboard navigation) and its
[banner](https://www.mowglii.com/itsycal/itsycalbanner2@2x.png): a ~350-DIP-wide popover,
month grid on top, compact toolbar, agenda groups with colour dots, time ranges and a video
icon on meeting rows.

**Decision:** UI compaction is the next product epic after v1.2.2 reliability. Changelog
discipline is introduced at the same time (`CHANGELOG.md`, Keep a Changelog).

## Now / next / later

| ID | Priority | Item | Outcome / acceptance | Status |
|---|---|---|---|---|
| UI-101 | P0 | Compact calendar flyout | At the host-controlled 800×480 DIP window (min 640), the composition stays compact: month grid is quick-navigation only (≤55% of card height, single-line title, no oversized headers); selected-day agenda with inline details below. The extension cannot change the host width. Spec: [0003](specs/0003-compact-calendar-ui.md). | Spec written, review pending |
| UI-102 | P0 | Inline details + Join icon in agenda | Selecting an event expands its details under the row: full when-line, location, notes, `Join` (only for meeting links), `Copy link`; no separate details block after the whole agenda, no full-screen scroll for a typical 3-event day. | In UI-101 spec |
| UI-103 | P1 | Visual system: light/dark, typography, spacing, accessibility | Host acrylic translucency visible through the card (no opaque panel fill, no fake alpha); adaptive dark/light; readable contrast over the material; no clipped labels at 800×480 and the 640-DIP minimum; renderer-supported keyboard (Today/prev/next, Enter on actions). Arrow-key day focus and per-element UIA names are host-feature-dependent and deferred. | Planned |
| ONB-101 | P1 | Calendars onboarding | `Itsycal: open calendar folder` command, import `.ics…` file picker, source status (name, last read, parse errors), empty-state CTA. | Planned |
| REL-201 | P1 | ICS recurrence correctness | DST wall-clock expansion and BYDAY; fail-closed for unsupported RRULE parts. Spec [0002](specs/0002-reliability-hardening.md) (narrowed after two reviews). | Spec review in progress |
| REL-202 | P2 | Remaining reliability findings | Version/updater minors (TOCTOU, log encoding), release/CI gates (tests on release tags, ARM64, `winget validate`, branch protection), docs accuracy. | Planned |
| SET-401 | P2 | Settings consolidation | One Settings page: Calendar / Dock / Calendars / Updates; human labels, dock-format preview, update status. | Planned |
| DOC-501 | P2 | Dock band screenshot + release evidence | Enable PowerToys Dock, capture dark/light/narrow, attach to README; release checklist. | Planned |
| PRV-301 | P3 | Provider platform (Google/Graph/CalDAV) | OAuth/CalDAV, DPAPI tokens, per-provider settings; ADR 0002. Deferred until UI/onboarding are done. | Deferred |
| LOC-601 | P3 | Localization (en-US/ru-RU) | `.resw` strings, culture date/time formats; README updated. | Deferred |
| UI-104 | Done | Independent design-review gate | Read-only design reviewer judges installed-build screenshots against the accepted UI spec/reference after the tester and before code review; independence from author/tester/reviewers; verdict + screenshots recorded in the PR. | Added |
| CHG-701 | Done | Changelog discipline | `CHANGELOG.md` with sections for every release; contributors update `[Unreleased]`; release workflow uses the section. | Changelog added; workflow integration next |

## Dependencies and constraints

- UI work must be verified on an installed build with screenshots (host-rendered Adaptive
  Cards; JSON assertions are not evidence). Dock screenshot needs PowerToys Dock enabled.
- The compact layout must still fit the Command Palette host page (`ContentPage` → Adaptive
  Card) and the declared palette window size; no custom WinUI window in this epic.
- Providers wait for onboarding and the UI: the settings/status surface is the same one
  sources will use.
- Every user-visible change adds a `CHANGELOG.md` line under `[Unreleased]`; a release tag
  requires a matching `## [X.Y.Z]` section.
