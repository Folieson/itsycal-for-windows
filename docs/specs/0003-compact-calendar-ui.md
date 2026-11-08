# 0003 — Compact calendar — Command Palette surface (optional)

Status: proposed, **optional Command Palette surface only** — revision 3 (2026-11-08),
re-review pending. The primary compact UI is the standalone WinUI 3 flyout in
[spec 0004](0004-standalone-compact-flyout.md) per [ADR 0003](../adr/0003-surfaces-and-host-strategy.md);
this spec is deliberately limited to what the Command Palette host allows.
Backlog: [UI-101 / UI-102](../BACKLOG.md) (optional-surface scope)
Related: [spec 0004](0004-standalone-compact-flyout.md), [ADR 0003](../adr/0003-surfaces-and-host-strategy.md),
PR #33 (details/Join groundwork), PR #36 (v1.2.2), ADR 0002
Reference: original ItsyCal [product page](https://www.mowglii.com/itsycal/) and
[banner](https://www.mowglii.com/itsycal/itsycalbanner2@2x.png): tiny popover with a month
grid on top and the agenda + meeting actions below.

## Context

This is the **optional** Microsoft PowerToys Command Palette surface. The primary compact
experience (a ~360-DIP, acrylic, hotkey-opened tray flyout) moved to spec 0004 / ADR 0003 on
2026-11-08; the extension remains for CmdPal users, but this spec does not promise the primary
flyout's window, material control or accessibility.

The product reference is the original ItsyCal: the calendar is a **quick date-navigation
tool** and the agenda below is the primary content, with details and a meeting action for the
selected event. On this surface the host decides how much of that composition is possible.

**Host constraints (verified):** the extension renders inside the Command Palette
`ContentPage` (Adaptive Card 1.5). The host controls the window: 800×480 DIP default,
640×420 minimum; the extension cannot set the card/window width, and the repo screenshots are
~800×480 DIP. At the default size the card is ~448 DIP high and the measured ContentPage
viewport between the host top bar and the command bar is only ~340–350 DIP. A six-row month
block plus its title currently measures ~250 DIP on the reference build — roughly 72% of that
viewport. **This surface cannot meet the primary "month is not half the screen" compactness
goal**, so the spec uses an absolute month budget and allows scrolling for long details; the
no-scroll typical-day contract belongs to the primary flyout (spec 0004). If a criterion
below proves unachievable on this host, it is descoped in [Out of scope](#out-of-scope) and
recorded in the review, not silently claimed.

Other host constraints that shaped this spec: `FormContent` exposes only `TemplateJson` and
`SubmitForm`; the host handles Enter in TextBoxes and focuses the first focusable element;
Escape/GoBack is host-level and user-configurable. Adaptive Cards cannot nest actions inside
a row that already has a `selectAction`, so a meeting row shows a non-interactive video
**indicator** and the actual `Join meeting` button lives in the expanded details. Per-element
accessible names and arrow-key day navigation are not exposed by this surface; they are
tracked separately (see Out of scope). The current card already has month navigation, a
selected-day agenda, details and `Join meeting`/`Copy link`, but it appends details after the
whole agenda, uses accent for the time gutter and titles, and has no video indicator; those
are the gaps this revision specifies.

- **Light translucency:** the host already paints the palette surface with an acrylic backdrop
  (`TransientSurface.xaml` → `AlwaysActiveDesktopAcrylicBackdrop` in the installed Command
  Palette). The extension must let that material show through: no opaque full-card
  background, no fake semi-transparent colours (Adaptive Cards colour styles have no alpha),
  and readable contrast over the acrylic in both themes. If the host renders a solid surface
  (backdrop disabled or a remote session), the card must still look intentional as a solid
  surface; custom acrylic tint/opacity is host-owned.

## Acceptance criteria

- [ ] **AC-1 — compact composition (split into separately verifiable parts):** on an
      installed build at the default palette window (800×480 DIP, 100% scale) the card meets
      **all** of AC-1a–AC-1d. The parts are separate evidence; AC-1 is not a single
      no-scrollbar claim for long content.
  - [ ] **AC-1a — month block budget:** the month block (single-line title/navigation row,
        weekday header and six week rows) occupies **at most 224 DIP** — 50% of the host's
        448-DIP card. Measurement: numerator is the top edge of the title/navigation row to
        the bottom edge of the sixth week row, including row padding/borders and excluding the
        separator/events heading; denominator is the 448-DIP card height at the default
        window. The absolute 224-DIP budget is normative; the ~340–350-DIP ContentPage
        viewport is recorded as context only because it is host chrome that the extension
        cannot set. The reference screenshot (`docs/screenshots/calendar-flyout.png`, 824×504
        at 100%) measures ~250 DIP (~56% of the card), so this requires roughly a 26-DIP
        reduction; if it cannot be met without clipping, return to the analyst instead of
        relaxing the number. The window size is not changed by the extension.
  - [ ] **AC-1b — no scrolling for ordinary collapsed events:** with the deterministic
        ordinary fixture (three collapsed events, short titles/locations, one meeting link and
        two no-link events), the last event row and the bottom edge of the card are visible in
        the initial screenshot at 800×480, and scrolling to the end does not move the card
        content. Detection: capture the initial view, then scroll/send End and capture again;
        pass only if both show the last row and the card bottom in the same position and the
        initial view is not cut off. Never use scrollbar visibility as evidence — the host
        hides scrollbars.
  - [ ] **AC-1c — long expanded details may scroll, actions stay reachable:** with the long
        fixture event (long title/location/notes) expanded, the card may scroll, but a single
        scroll-to-end position shows the complete details action row (`Join meeting`,
        `Copy link`, `Close details` as applicable), fully inside the card and activatable.
        "Within one page" means one end-of-card position, not that the full details fit
        without scrolling.
  - [ ] **AC-1d — window unchanged:** acceptance screenshots capture the full host window
        (host top bar and command bar included); at 100% scale the host window is 800×480 DIP
        (a stored PNG may be ~824×504 with the drop shadow). The PR records the measured
        window bounds, and code review confirms the extension calls no window-resizing API.
- [ ] **AC-2 — quick date navigation:** month title, previous/next and Today controls form one
      compact row above the grid; clicking a day updates the selected-day agenda immediately;
      today and the selected day are visually distinct under the AC-3 accent rule; the
      optional week-number column is a fixed-width column and event text does not overlap it,
      wrap under it or get clipped at 800 and 640 DIP.
- [ ] **AC-3 — selected-day agenda and inline details:** the agenda shows only the selected
      day (one group header, e.g. `Today · Nov 8`; the `Today` prefix appears only for today).
      Each row shows a **neutral** event dot, time range (`All day` for all-day) and title
      (truncated to at most 2 lines / 60 characters, whichever is shorter, with an ellipsis),
      plus one location line (40 characters, ellipsis). Multi-day rows prefix the meta line
      with `Day N of M` before the location (`Day 1 of 3 · Location`); the prefix is never
      truncated and the location truncates independently. Rows with an `OnlineMeetingLink`
      show a non-interactive video glyph as an indicator. Selecting a row expands its details
      **directly below that row** (full when-line, location, truncated notes and the actions
      below); selecting another row collapses the previous one; no details block appears after
      the whole agenda.
      - Details actions: an event with a usable http(s) `OnlineMeetingLink` shows
        `Join meeting` (primary), `Copy link` and `Close details`; an event without one shows
        **only** `Close details` — no `Join meeting`, no `Copy link`. `Copy link` copies the
        exact normalized meeting URI and shows `Meeting link copied.` in the details block;
        clipboard failure shows `Could not copy the meeting link.` Use the exact labels
        `Join meeting` and `Copy link` (never bare `Join` or `Copy`) in UI and tests.
      - Notes are truncated to 480 characters with an ellipsis. A `Show more` expander is
        descoped on this surface (host toggle rendering is unverified); the full text remains
        in the source calendar app.
      - **Accent rule (normative, resolves the AC-3/AC-4 conflict):** accent is used only for
        the selected-day surface and today's day number. The event dot, time range, event
        title (selected or not), details title, location/notes and copy notice are neutral.
        The selected event is indicated by bolder title weight and its expanded details, not
        by accent. The host focus ring is host-owned and outside this rule; it is neither
        specified nor checked here. The current card's accent time gutter, selected title and
        details title must become neutral.
- [ ] **AC-4 — theme and clipping:** the same content renders correctly in the host's dark
      and light themes at 800×480 and at the 640×420 minimum without clipped time ranges,
      titles or `Join meeting`/`Copy link`/`Close details` labels, and without horizontal
      overflow; accent follows the AC-3 rule. The host focus ring is outside AC-4.
- [ ] **AC-5 — empty and loading states:** a day with no events shows one line,
      `No events on this day.`, with no calendars-folder path or setup paragraph repeated in
      the agenda; the setup/onboarding pointer is owned by ONB-101 and is not duplicated here.
      The loading state shows `Loading events…` only while the selected day is loading and no
      events are already cached (cached events render instead, so there is no loading flash).
      Loading is distinguishable from empty by its text. The loading state is verified by the
      AC-6 state-derivation unit tests; no loading screenshot is required. All-sources-failed/
      source-status is deferred to ONB-101 (needs a status contract that sources do not expose
      yet).
- [ ] **AC-6 — testable layout logic:** truncation limits, selected-row/action derivation,
      grouping, state derivation and multi-day/location composition live in pure helpers under
      `windows/src/ItsycalForWindows/Pages/AgendaLayout.cs`, with unit tests in
      `windows/tests/ItsycalForWindows.Tests/AgendaLayoutTests.cs`. The helper owns
      `ResolveState(events, isLoading)`, `BuildHeader(date, today)`, `BuildRow(event, date)`
      (truncated title/location, `TitleMaxLines = 2`, meta line, dot/glyph flags) and
      `BuildDetails(event)` (when-line, notes preview + `NotesTruncated`, exact action list).
      `ResolveState` also returns the exact empty/loading label (`No events on this day.` /
      `Loading events…`) that the renderer displays.
      The renderer (`CalendarPage`) only maps those values to Adaptive Card JSON; it must not
      slice strings, apply its own ellipsis or re-derive actions. Planned test names:
      `State_LoadingWithoutEvents_ReturnsLoading`,
      `State_LoadedWithoutEvents_ReturnsEmpty`,
      `State_LoadedWithEvents_ReturnsEvents`,
      `State_LoadingWithCachedEvents_ReturnsEvents`,
      `State_Loading_UsesLoadingEventsLabel`, `State_Empty_UsesNoEventsLabel`,
      `BuildHeader_Today_PrefixesToday`, `BuildHeader_OtherDay_OmitsToday`,
      `TruncateTitle_LongTitle_ReturnsSixtyCharsAndEllipsis`,
      `BuildRow_LongTitle_SetsMaxLinesTwo`,
      `TruncateLocation_LongLocation_ReturnsFortyCharsAndEllipsis`,
      `TruncateNotes_LongNotes_Returns480CharsAndEllipsis`,
      `Row_MultiDayPrefixPrecedesLocation_AndLocationTruncatesSeparately`,
      `Row_MeetingLink_FlagsVideoGlyph`, `Row_NoMeetingLink_HasNoVideoGlyph`,
      `Actions_MeetingLink_ReturnsJoinMeetingCopyLinkCloseDetails`,
      `Actions_NoMeetingLink_ReturnsCloseDetailsOnly`.
- [ ] **AC-7 — light translucency:** on an installed build the host acrylic is visible through
      the card (no opaque full-card panel fill and no fake alpha), text and controls keep
      readable contrast over the material in dark and light themes, and a solid-surface
      fallback (backdrop disabled) still renders intentionally. Verified with read-back
      screenshots; the material strength itself is host-owned and cannot be set by the
      extension.

## Deterministic fixture

No screenshot may depend on live calendar data. The implementation PR commits these files
(the table is normative; the fixture files must match it):

- `windows/tests/fixtures/compact-ui/ordinary.ics`
- `windows/tests/fixtures/compact-ui/long.ics`
- `windows/scripts/Seed-CompactFixture.ps1 -Fixture ordinary|long`

The script replaces the `{{TODAY}}` token with the local date (`yyyyMMdd`), writes exactly one
file to `%LOCALAPPDATA%\ItsycalForWindows\calendars\compact-fixture.ics`, removes previous
`compact-fixture*.ics` files, and fails if any `{{TODAY}}` remains. Fixture times are floating
local times (`DTSTART:{{TODAY}}T090000`), so they render at the stated wall-clock time on any
machine. No DEBUG build and no live calendar source is used. The capture profile must also
have no configured Windows appointment-store calendars and no other `.ics` files in the
calendars folder, so the fixture is the only event source; the PR records the source list.
If a clean profile cannot be provided, the run is not acceptance evidence.

**Ordinary fixture — three events today:**

| # | Time | Title | Location | Notes | Meeting link |
|---|---|---|---|---|---|
| 1 | 09:00–09:30 | `Design review` | `Google Meet` | none | `https://meet.google.com/abc-defg-hij` |
| 2 | 11:00–12:00 | `Lunch with the team` | `Cafeteria` | none | none |
| 3 | 15:00–15:30 | `Focus block` | none | none | none |

**Long fixture — one event today, expanded for AC-1c:**

| Field | Value |
|---|---|
| Time | 14:00–15:30 |
| Title | `Quarterly planning review with platform, design, operations and support` (71 characters) |
| Location | `Building 7, Conference Room 12, North Campus` (44 characters) |
| Notes | `Agenda: quarterly roadmap, platform dependencies, accessibility review results, and the remaining launch blockers. Decisions needed on the Windows 11 packaging, the tray flyout telemetry-free update path, the shared core extraction, and the federated provider roadmap. Please read the attached brief before the meeting and bring the open questions for the operations, support, and design representatives. This paragraph is intentionally longer than the 480-character preview so the expanded details exercise truncation and scrolling on the Command Palette surface.` (564 characters) |
| Meeting link | `https://teams.microsoft.com/l/meetup-join/19%3ameeting_compactfixture%40thread.v2/0` |

**Which event is expanded:**

| Check | Fixture | Expanded |
|---|---|---|
| AC-1b, AC-2 | ordinary | none |
| AC-1c | long | the long event |
| AC-3 | ordinary | event 1 (meeting), then event 2 (no link) |
| AC-4, AC-7 | ordinary | event 1 (meeting) |

## Test plan

| AC | Check | Kind |
|---|---|---|
| AC-1a | Installed build, 800×480/100%; full-window screenshot; crop to the card and measure DIP from the top of the title/navigation row to the bottom of the sixth week row; pass only if ≤224 DIP and ≤50% of the 448-DIP card; record numerator/denominator in the PR | manual + screenshot |
| AC-1b | Ordinary fixture, no event expanded; initial screenshot then scroll-to-end screenshot; pass only if both show the last row and card bottom in the same position with no cut-off; do not use scrollbar visibility | manual + screenshot |
| AC-1c | Long fixture expanded; screenshot, scroll once to the end, screenshot again; the full action row must be inside the card and every action visible/activatable in that one end position | manual + screenshot |
| AC-1d | Full-window capture includes host top/command bars; record window bounds (800×480 DIP at 100%); code review confirms no resize API call | manual + review |
| AC-2 | Prev/next/Today and day clicks; week-number on/off at 800×480 and 640×420; no overlap, wrap-under or clipping of event text | manual + screenshot |
| AC-3 | Ordinary fixture: select event 1 — details move directly below it, labels are `Join meeting`/`Copy link`/`Close details`, video glyph only on event 1; click `Copy link` and inspect the clipboard, then the `Meeting link copied.` notice; select event 2 — only `Close details` is present, no `Join meeting`/`Copy link`; previous details collapse; no trailing details block | manual + screenshot |
| AC-4 | Dark and light screenshots at 800×480 and 640×420; inspect clipping of time/title/action labels and horizontal overflow; accent only on selection/today | manual + screenshot |
| AC-5 | Empty-day screenshot shows only `No events on this day.`; loading state covered by `AgendaLayoutTests.cs` state tests, not a screenshot | screenshot + unit |
| AC-6 | `AgendaLayoutTests.cs` / the planned test names in AC-6; TDD red run recorded in the implementation PR | unit |
| AC-7 | Dark/light installed-build screenshots: host acrylic visible through the card, no opaque full-card fill/fake alpha, readable contrast; check the solid-surface fallback if the backdrop is unavailable | manual + screenshot |

The fixture is committed, not DEBUG-only, and deterministic: exactly three events today in the
ordinary fixture with fixed titles/times/locations/notes, one meeting link and two no-link
events; the long fixture supplies the long title/location/notes expansion. Every screenshot
above runs on that isolated source set; the PR records the source list. No row-level action is
claimed: the row's video glyph is an indicator; `Join meeting` is the primary button in the
expanded details (the host cannot nest actions in a selectable row).

## Out of scope

- The primary standalone flyout (window, acrylic/Mica strength, hotkey, tray, full keyboard/
  UIA): owned by [spec 0004](0004-standalone-compact-flyout.md).
- A 350-DIP/custom-width popover and a no-scroll contract for a typical day: the host fixes
  the window; those are primary-flyout requirements, not claims for this surface.
- No-scroll for long expanded details (AC-1c allows one scroll-to-end), notes `Show more`
  expander (host toggle rendering unverified; full text stays in the source app), custom
  acrylic tint/opacity (host-owned), two-month view.
- Arrow-key day navigation and per-element UIA names (host surface does not expose them;
  track as a host feature request; deferred). Renderer-supported keyboard (Today/prev/next,
  Enter on host-rendered action buttons) is expected from the host but is not a per-element
  contract here.
- Source-status/all-sources-failed UI, Calendars settings/onboarding (ONB-101); the empty-state
  setup pointer lives there, not in the agenda.
- Per-source event colours (no data source yet): the dot is neutral.
- Localization strings, Settings consolidation, dock-band redesign, providers.
- Event editing/creation/RSVP, animations, drag/drop.

## Review

Review record: **spec reviewer #2 — request changes (2026-11-08, round 2)**; revision 3
submitted 2026-11-08 for re-review — **verdict pending**; implementation starts only after an
`approve` or `approve with notes` verdict is recorded.

- Spec reviewer #1 (2026-11-08): request changes — impossible 340–400 DIP widths, unsupported
  arrow-focus/UIA names, source-status dependency, non-reproducible test evidence, ambiguous
  Join-icon semantics and scope overlap. Resolved by: host-defined 800/640 widths and
  composition-based compactness; a11y/keyboard/source-status moved out of scope; deterministic
  fixture and measured screenshot evidence; explicit truncation and action rules;
  indicator-vs-action distinction documented; AC set narrowed to layout/details/theme/states.
- Spec reviewer #2 (2026-11-08, re-review, independent of the author): request changes —
  AC-1 no-scrollbar conjunction unachievable at the measured ContentPage viewport, measurement
  crop undefined, loading fixture not deterministic; Copy/accent/link/roadmap contradictions.
  Revision 3 resolves them: AC-1 split into budget/no-scroll/long-details/window parts with a
  defined numerator, denominator and scroll-detection method; a required committed `.ics`
  fixture with a stamp-today script and exact values; loading verified by state unit tests with
  the loading screenshot dropped; exact `Join meeting`/`Copy link`/`Close details` semantics;
  accent restricted to selection/today with neutral dot/time/title; backlog/roadmap/spec links
  and `CHG-701` status corrected; month target lowered to 224 DIP (≤50% of the card) and the
  primary "not half the screen" requirement explicitly moved to spec 0004. Translucency is
  AC-7 (host acrylic).
- Design reviewer: required for the implementation PR (installed-build screenshots,
  dark/light, compact/expanded, host 800×480 and 640×420). Verdict recorded in the PR before
  code review.
- Code review: requested after the design verdict. The PR template holds the PR-level review
  record (reviewer, date, verdict, link); this section records the spec-level history.
