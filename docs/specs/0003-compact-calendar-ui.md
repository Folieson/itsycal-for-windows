# 0003 — Compact calendar flyout (ItsyCal-like composition)

Status: proposed (P0 UI epic, PO decision 2026-11-08; revised after independent spec review)
Backlog: [UI-101 / UI-102](../BACKLOG.md)
Related: PR #33 (details/Join groundwork), PR #36 (v1.2.2), ADR 0002
Reference: original ItsyCal [product page](https://www.mowglii.com/itsycal/) and
[banner](https://www.mowglii.com/itsycal/itsycalbanner2@2x.png): tiny popover with a month
grid on top and the agenda + meeting actions below.

## Context

The current Command Palette flyout lets the month grid and header dominate the card; event
details are appended after the whole agenda and below the month navigation, so a typical
3-event day scrolls. The product reference is the original ItsyCal: the calendar is a
**quick date-navigation tool** and the agenda below is the primary content, with details and
a Join action for the selected meeting.

**Host constraint (verified):** the extension renders inside the Command Palette `ContentPage`
(Adaptive Card 1.5). The host controls the window: 800×480 DIP default, 640×420 minimum; the
extension cannot set the card/window width, and the repo screenshots are ~800×480 DIP. The
ItsyCal inspiration is therefore adapted as a **compact composition inside the host width**,
not as a 350-DIP window. Original ItsyCal's narrow popover is a host-window feature request,
not something this extension can reproduce today.

Other host constraints that shaped this spec: `FormContent` exposes only `TemplateJson` and
`SubmitForm`; the host handles Enter in TextBoxes and focuses the first focusable element;
Escape/GoBack is host-level and user-configurable. Adaptive Cards cannot nest actions inside
a row that already has a `selectAction`, so a row shows a video **indicator** and the actual
Join button lives in the expanded details. Per-element accessible names and arrow-key day
navigation are not exposed by this surface; they are tracked separately (see Out of scope).

## Acceptance criteria

- [ ] **AC-1 — compact composition:** On an installed build at the default palette window
      (800×480 DIP, 100% scale) the month block (single-line title + weekday header + six
      week rows) occupies **at most 55% of the card height**, and a deterministic fixture
      with today + three events (one expanded, including a long title/location/notes) renders
      the agenda and the inline details **without a vertical scrollbar**. Measured from a
      read-back screenshot; the window size is not changed by the extension.
- [ ] **AC-2 — quick date navigation:** month title, previous/next and Today controls form one
      compact row above the grid; clicking a day updates the selected-day agenda immediately;
      today and the selected day are visually distinct; the optional week-number column stays
      aligned without pushing event text off the card.
- [ ] **AC-3 — selected-day agenda and inline details:** the agenda shows only the selected
      day (one group header, e.g. `Today · Nov 8`). Each row shows a neutral/accent event dot,
      time range and title (title truncated to 2 lines / 60 chars with ellipsis), plus one
      line of location (40 chars, ellipsis). Rows with an `OnlineMeetingLink` show a video
      glyph as an indicator. Selecting a row expands its details **directly below that row**
      (full when-line, location, notes truncated to 480 chars with "Show more" if needed,
      `Join` only for meeting links, `Copy link`); selecting another row collapses the
      previous one; no details block appears after the whole agenda.
- [ ] **AC-4 — theme and clipping:** the same content renders correctly in the host's dark
      and light themes at 800×480 and at the 640-DIP minimum width without clipped time
      ranges, title or Join/Copy labels; accent is reserved for selection/today/focus.
- [ ] **AC-5 — empty and loading states:** a day with no events shows a short message (no
      long setup text inside the agenda); the loading state is distinguishable from empty.
      All-sources-failed/source-status is deferred to ONB-101 (needs a status contract that
      sources do not expose yet).
- [ ] **AC-6 — testable layout logic:** truncation limits, selected-row/action derivation and
      day grouping live in pure helpers with unit tests; card-level claims are verified with
      the deterministic fixture and screenshots above.

## Test plan

| AC | Check | Kind |
|---|---|---|
| AC-1 | Deterministic fixture: today + 3 events (one with meeting link, long title/location/notes, one without a link); installed build, 800×480/100%; dark + light screenshots; measure grid-height/card-height ratio from the PNG; assert no scrollbar | manual + screenshot |
| AC-2 | Screenshots after prev month, day click and Today; week-number variant at 800 and 640 DIP | manual + screenshot |
| AC-3 | Select two rows in turn: details move under the selected row, previous collapses, video glyph only on meeting rows, Join absent on the no-link event, Copy works | manual + screenshot |
| AC-4 | Dark/light screenshots at 800×480 and 640 DIP; inspect clipping of time/title/Join/Copy labels | manual + screenshot |
| AC-5 | Empty day and loading screenshots; no long onboarding text repeated on empty days | manual + screenshot |
| AC-6 | Unit tests for truncation, grouping and action derivation in a pure layout helper | unit |

The fixture is DEBUG-only (or an `.ics` file) and deterministic: exactly three events today,
fixed titles/times/locations/notes, one `https://meet.google.com/...` link, one event without
a link. No claim of "one-click Join" is made: the row's video glyph is an indicator; Join is
the primary button in the expanded details (host cannot nest actions in a selectable row).

## Out of scope

- Custom window width / a 350-DIP popover (host feature request), two-month view.
- Arrow-key day navigation and per-element UIA names (host surface does not expose them;
  track as a host feature request; deferred).
- Source-status/all-sources-failed UI, Calendars settings/onboarding (ONB-101).
- Per-source event colours (no data source yet): the dot uses the neutral/accent colour.
- Localization strings, Settings consolidation, dock-band redesign, providers.
- Event editing/creation/RSVP, animations, drag/drop.

## Review

- Spec reviewer #1 (2026-11-08): request changes — impossible 340–400 DIP widths, unsupported
  arrow-focus/UIA names, source-status dependency, non-reproducible test evidence, ambiguous
  Join-icon semantics and scope overlap. Resolved by: host-defined 800/640 widths and
  composition-based compactness; a11y/keyboard/source-status moved out of scope; deterministic
  DEBUG/ICS fixture and measured screenshot evidence; explicit truncation and action rules;
  indicator-vs-action distinction documented; AC set narrowed to layout/details/theme/states.
- Design reviewer: required for the implementation PR (installed-build screenshots, dark/light,
  compact/expanded, host 800×480 and 640 DIP). Verdict recorded in the PR before code review.
- Code review: requested after the design verdict.
