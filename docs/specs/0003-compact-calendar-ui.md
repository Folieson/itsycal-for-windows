# 0003 — Compact calendar — Command Palette surface (parity)

Status: proposed — **optional Command Palette surface with a visual/interaction parity mandate** —
revision 5 (2026-11-08), independent spec review pending; implementation starts only after an
`approve` or `approve with notes` verdict. The surface is optional in distribution, not in design:
it follows the primary standalone WinUI 3 flyout's design language and interaction hierarchy
([spec 0004](0004-standalone-compact-flyout.md), [ADR 0003](../adr/0003-surfaces-and-host-strategy.md))
within the verified host constraints below. Scrolling is an accepted host limit here; the
no-scroll contract belongs to spec 0004.
Backlog: [UI-105](../BACKLOG.md) (parity contract, P1), [UI-106](../BACKLOG.md) (parity
implementation, P1), UI-102 (shared details semantics), UI-103 (visual system)
Related: [spec 0004](0004-standalone-compact-flyout.md), [ADR 0003](../adr/0003-surfaces-and-host-strategy.md),
[ADR 0002](../adr/0002-calendar-providers-and-network.md),
PR #33 (details/Join groundwork), PR #36 (v1.2.2)
Design target: `windows/src/ItsycalForWindows.Flyout/MainWindow.xaml` and `.xaml.cs` (month
header, agenda rows, inline details), spec 0004, and the installed-build evidence in PR #46.
Screenshots: [calendar-flyout.png](../screenshots/calendar-flyout.png) (current CmdPal card),
[event-details.png](../screenshots/event-details.png) (current trailing details block).
Reference: original ItsyCal [product page](https://www.mowglii.com/itsycal/) and
[banner](https://www.mowglii.com/itsycal/itsycalbanner2@2x.png): tiny popover with a month
grid on top and the agenda + meeting actions below.

## Context

This is the **optional** Microsoft PowerToys Command Palette surface. The primary compact
experience (a ~360-DIP, acrylic, hotkey-opened tray flyout) is [spec 0004](0004-standalone-compact-flyout.md)
/ [ADR 0003](../adr/0003-surfaces-and-host-strategy.md). The **PO decision of 2026-11-08** makes
this surface's **visual and interaction parity with the flyout** a P1 requirement
([UI-105](../BACKLOG.md)/[UI-106](../BACKLOG.md)): optional in distribution does not mean
divergent UI.

**Parity mandate.** The card reproduces the flyout's composition and hierarchy — month
header/navigation → weekday header → six week rows → compact agenda header → agenda rows →
inline details directly under the selected row — together with its neutral row anatomy, accent
discipline, typographic/spacing rhythm and acrylic-forward surface treatment, as far as the
Adaptive Cards 1.5 host renderer allows. The selected day moves from the current oversized
accent header above the grid into the flyout-style compact agenda header (`Today`/weekday left,
`MMM d` right); month navigation moves into the month header row beside `MMMM yyyy`. A visual or
interaction difference that this host can express is a **defect**; a difference owned by the
host is a documented limit, not a defect.

The product reference is the original ItsyCal: the calendar is a **quick date-navigation tool**
and the agenda below is the primary content, with details and a meeting action for the selected
event. On this surface the host decides how much of that composition is possible.

**Host constraints (verified):** the extension renders inside the Command Palette
`ContentPage` (Adaptive Card 1.5). The host controls the window: 800×480 DIP default, 640×420
minimum; the extension cannot set the card/window width, and the repo screenshots are
~800×480 DIP. At the default size the card is ~448 DIP high and the measured ContentPage
viewport between the host top bar and the command bar is only ~340–350 DIP. A six-row month
block plus its title currently measures ~250 DIP on the reference build — roughly 72% of that
viewport. **This surface cannot meet the primary "month is not half the screen" compactness
goal** (the month block is still ~64–66% of the visible viewport after the month budget), so
the spec uses an absolute month budget and **allows scrolling for any day whose agenda exceeds
the viewport**, including the ordinary fixture day. The no-scroll requirement exists only in
the primary-flyout spec 0004. Revision 4 deleted the former AC-1b (no scrolling for ordinary
collapsed events) as impossible on this host: even a 224-DIP month block plus three ordinary
collapsed rows cannot fit the ~340–350-DIP viewport. That is a documented host limit, not a
regression, and no criterion below claims otherwise. If a criterion below proves unachievable
on this host, it is descoped in [Out of scope](#out-of-scope) and recorded in the review, not
silently claimed.

Other host constraints that shaped this spec: `FormContent` exposes only `TemplateJson` and
`SubmitForm`; the host handles Enter in TextBoxes and focuses the first focusable element;
Escape/GoBack is host-level and user-configurable. Adaptive Cards 1.5 has **no font-family or
icon-font control, no per-element alpha, no row borders and no nested actions inside a row that
already has a `selectAction`**; text sizes/weights and spacing are renderer-mapped tokens, not
DIP values. So a meeting row shows a non-interactive video **indicator** and the actual
`Join meeting` button lives in the inline details. Per-element accessible names and arrow-key
day navigation are not exposed by this surface; they are tracked separately (see Out of scope).
The current card already has month navigation, a selected-day agenda, details and
`Join meeting`/`Copy link`, but it appends details after the whole agenda, uses accent for the
time gutter and titles, has no per-row neutral marker or video indicator, shows an oversized
accent day number above the grid, puts the month navigation in the host bottom action bar and
fills the month panel; those are the concrete gaps this revision specifies (see
[calendar-flyout.png](../screenshots/calendar-flyout.png) and
[event-details.png](../screenshots/event-details.png)).

**Host-controlled, not defects:** the window size/position and the host top/command bars; the
card margin, radius, padding and stroke; the acrylic/backdrop material and its strength (and the
solid fallback); the exact font metric mapped to each Adaptive Card size token; action-button
height/paint; hover/pressed states and the focus ring; the scrollbar; the renderer's inability
to nest actions in a selectable row; Enter/Escape/focus handling; per-element UIA names and
arrow-key grid focus; and scrolling itself. **A difference is a defect only when the card can
express it and the installed host renders it** — for example an accent time gutter, an accent
row/details title, a trailing details block, an oversized day number, a filled month panel, an
`Events · …` caption, extra separator rules, bold row titles, or text larger than `medium`.

- **Light translucency:** the host paints the palette surface with an acrylic backdrop
  (`TransientSurface.xaml` → `AlwaysActiveDesktopAcrylicBackdrop` in the installed Command
  Palette). The extension must let that material show through: no opaque full-card background,
  no fake semi-transparent colours (Adaptive Cards colour styles have no alpha), and readable
  contrast over the acrylic in both themes. The flyout draws its month grid directly on its
  acrylic card, so the card must not paint a filled panel behind the month block either; the
  selected-row surface and the inline details are the only filled sub-surfaces. If the host
  renders a solid surface
  (backdrop disabled or a remote session), the card must still look intentional as a solid
  surface; custom acrylic tint/opacity is host-owned.

## Acceptance criteria

- [ ] **AC-1 — compact composition and hierarchy (split into separately verifiable parts):** on
      an installed build at the default palette window (800×480 DIP, 100% scale) the card meets
      **all** of AC-1a–AC-1c. The card's vertical order is the primary flyout's: month
      header/navigation, weekday header, six week rows, compact agenda header, agenda rows and
      inline details directly under the selected row; no other section or panel sits between the
      month block and the agenda. The parts are separate evidence; this surface may scroll and no
      criterion here claims otherwise. The former AC-1b (no scrolling for ordinary collapsed
      events) was deleted in revision 4 as impossible on this host: even at the 224-DIP month
      budget, the month block plus three collapsed rows cannot fit the ~340–350-DIP viewport.
      Scrolling is accepted on this surface; the no-scroll contract is
      [spec 0004](0004-standalone-compact-flyout.md).
  - [ ] **AC-1a — month block budget, measured against the visible viewport:** the month block
        (single-line month header row with `MMMM yyyy`, plus inline previous/next/Today
        navigation unless the AC-2 fallback applies; weekday header and six week rows) occupies
        **at most 224 DIP**, and at most **two-thirds of the visible viewport**. Measurement: the
        numerator is the distance from the top edge of the month header row to the bottom edge of
        the sixth week row, including row padding/borders and excluding the compact agenda header
        (≤20 DIP, see AC-2) and any separator between the month block and the agenda; the
        denominator is the **visible ContentPage viewport** between the host top bar and the
        command bar measured in the same full-window screenshot (~340–350 DIP at 800×480/100%),
        not the 448-DIP card. At that viewport two-thirds is ~227–233 DIP, so the absolute 224-DIP
        budget binds; a smaller measured viewport tightens the effective budget instead of being
        ignored. The absolute 224-DIP budget is normative; the PR records numerator, denominator
        and the resulting ratio (~64–66%), which is **not** 50%: the ≤50% compactness target
        belongs to the flyout (spec 0004) and is not claimed here. The block must always show six
        week rows. The flyout's density intent is a 220-DIP block with 27-DIP day rows and a
        32-DIP month header; on this host a workable split is a 28–30-DIP month header, a 16-DIP
        weekday header and six 28–30-DIP week rows (six rows are never reduced or collapsed to
        make the budget; for example 28 + 16 + 6×30 = 224, or 30 + 16 + 6×28 = 214). The
        reference screenshot (`docs/screenshots/calendar-flyout.png`, 824×504 at 100%) measures
        ~250 DIP (~71–74% of the viewport), so this requires roughly a 26-DIP reduction;
        32–36-DIP week rows would exceed the cap, so the implementation must use the denser row
        and record the actual measurement. There is no oversized selected-day number or
        selected-day header above the grid: the selected day is carried by the compact agenda
        header (AC-2), which is agenda chrome and not part of this numerator. A ~50-DIP
        selected-day header remains disallowed. If the budget cannot be met without clipping,
        return to the analyst instead of relaxing the number. The window size is not changed by
        the extension.
  - [ ] **AC-1b — long expanded details may scroll, actions stay reachable:** with the long
        fixture event (long title/location/notes) expanded inline under its row, the card may
        scroll, but a single scroll-to-end position shows the complete inline details action row
        (`Join meeting`, `Copy link`, `Close details` as applicable), fully inside the card and
        activatable. "Within one page" means one end-of-card position, not that the full details
        fit without scrolling. An ordinary collapsed day may also scroll; this spec makes no
        no-scroll claim for any day.
  - [ ] **AC-1c — window unchanged:** acceptance screenshots capture the full host window
        (host top bar and command bar included); at 100% scale the host window is 800×480 DIP
        (a stored PNG may be ~824×504 with the drop shadow). The PR records the measured
        window bounds, and code review confirms the extension calls no window-resizing API.
- [ ] **AC-2 — quick date navigation, agenda header and day-cell parity:** month title
      (`MMMM yyyy`), previous/next and Today form one compact row above the weekday header, in the
      flyout's control order (previous, Today, next); the controls are an inline `ActionSet`
      in the month header row. Only if the installed renderer cannot place the action set inline
      does the host bottom action bar become the recorded fallback. Clicking a day updates the
      selected-day agenda and the agenda header immediately. The agenda header is one compact row
      (≤20 DIP, outside the AC-1a numerator): `Today` when the selected day is today, else the
      weekday name, on the left; `MMM d` on the right; the selected day is not appended to the
      month title. Today and the selected day are visually distinct using the flyout's two cues —
      today's accent number/marker and the selected day's accent selection surface — with no
      third accent state in the month grid; a day with events shows a neutral dot (never accent);
      adjacent-month days are subtle. The optional week-number column is a fixed-width column and
      event text
      does not overlap it, wrap under it or get clipped at 800 and 640 DIP.
- [ ] **AC-3 — selected-day agenda, inline details and accent rule** (separately verifiable
      parts):
  - [ ] **AC-3a — agenda rows:** the agenda shows only the selected day and starts directly with
        its rows under the compact agenda header — there is **no** `Events` caption and no
        additional selected-day/group header row beyond that compact agenda header. Each row
        shows a **neutral** marker (secondary,
        never accent), a **neutral** time range (`All day` for all-day) and a **neutral** title
        truncated to at most 2 lines / 60 characters (whichever is shorter, with an ellipsis),
        plus one location line (40 characters, ellipsis). Rows use the flyout's row anatomy: the
        marker at the start of the row, the title in the primary text colour at **regular
        weight**, and the secondary time/meta line; there is no accent time gutter and no bold
        title. Multi-day rows prefix the meta line with `Day N of M` before the location
        (`Day 2 of 3 · Building 7`); the prefix is never truncated and the location truncates
        independently. Rows with a usable `OnlineMeetingLink` show a **non-interactive video
        glyph** as an indicator; rows without one show no glyph. The selected row is marked
        without accent by the host's selection surface (an emphasis container) and its inline
        details (AC-3b); if the installed renderer cannot paint the row surface, the details
        directly below are the indicator and the PR records the limitation.
  - [ ] **AC-3b — inline details directly under the selected row:** selecting a row expands its
        details **directly below that row**, as the next element in the card body, inside a
        filled details surface (the flyout's details card equivalent: `Container style: emphasis`
        + rounded corners; the host owns padding/stroke). The details show the full title, the
        when-line computed **relative to the selected date** (including `All day` and
        `Day N of M`), the location, truncated notes and the actions below. Selecting another row
        collapses the previous one; selecting the same row collapses it; `Close details`
        collapses it; **no details block appears after the whole agenda**.
        - Details actions: an event with a usable http(s) `OnlineMeetingLink` shows
          `Join meeting` (primary), `Copy link` and `Close details`; an event without one shows
          **only** `Close details` — no `Join meeting`, no `Copy link`. A link is usable only
          when `Uri.TryCreate(raw, UriKind.Absolute, out var uri)` succeeds and the scheme is
          `http` or `https`. `Copy link` copies `uri.AbsoluteUri` (canonical form: lower-cased
          scheme/host, default port removed, spaces percent-encoded; percent-encoded delimiters
          such as `%3A` are preserved) and shows `Meeting link copied.` in the details block;
          clipboard failure shows `Could not copy the meeting link.` Expected fixture values:
          the ordinary event 2 link `https://meet.google.com/abc-defg-hij` is copied unchanged,
          and the long fixture link
          `https://teams.microsoft.com/l/meetup-join/19%3ameeting_compactfixture%40thread.v2/0`
          is copied unchanged (`uri.AbsoluteUri` preserves the lowercase percent escapes). An
          `ftp:`/`mailto:`/relative link is treated as no usable link (no `Join meeting`, no
          `Copy link`). The renderer calls an **injectable** clipboard adapter
          (`IMeetingLinkClipboard.TryCopyAsync(string)`); the pure helper decides the URI and
          the notice, so success and failure are unit-tested without the real clipboard. Use the
          exact labels `Join meeting` and `Copy link` (never bare `Join` or `Copy`) in UI and
          tests.
        - Notes are truncated to 480 characters with an ellipsis and render at most 4 lines
          (matching the flyout's notes cap); a `Show more` expander is descoped on this surface
          (host toggle rendering is unverified); the full text remains in the source calendar
          app.
  - [ ] **AC-3c — accent and emphasis rule (normative):** accent, or an action's primary
        emphasis, is permitted only on the selected-day surface, today's day number/marker and
        the `Join meeting` action. `Join meeting` is the card's only primary-emphasised action;
        the host's primary/positive action paint is host-owned and may or may not be the accent
        colour. The event marker, time range, event title, details title, location, notes and
        copy notice are neutral. The selected
        event is indicated by the host selection surface and its expanded details, not by accent
        and not by a heavier title (row titles stay regular weight). The host focus ring is
        host-owned and outside this rule; it is neither specified nor checked here. The current
        card's accent time gutter, accent selected title, accent details title and oversized
        accent day number must be removed; `large`/`extraLarge` text is not used.
- [ ] **AC-4 — theme and clipping:** the same content renders correctly in the host's dark
      and light themes at 800×480 and at the 640×420 minimum without clipped time ranges,
      titles or `Join meeting`/`Copy link`/`Close details` labels, and without horizontal
      overflow; the neutral/accent hierarchy, the selected-row surface and the details surface
      stay legible over the host acrylic in both themes; the month header title and navigation
      stay on one row at 640 DIP; accent follows the AC-3c rule. The host focus ring is outside
      AC-4.
- [ ] **AC-5 — empty and loading states:** a day with no events shows one line,
      `No events on this day.`, plus a **one-line** calendars-folder pointer
      (`Calendars folder: …`) until ONB-101 ships; the compact agenda header above the message
      is day context and does not change this two-line empty-state contract. The message is not
      a setup paragraph, and the folder pointer is not removed before ONB-101 provides the
      replacement (removing it now would regress the v1.2.2 first-run guidance). The loading
      state shows `Loading events…` only while the selected day is loading and no events are
      already cached (cached events render instead, so there is no loading flash). Loading is distinguishable from empty by its text. The
      loading state is verified by the AC-6 state-derivation unit tests; no loading screenshot
      is required. All-sources-failed/source-status is deferred to ONB-101 (needs a status
      contract that sources do not expose yet).
- [ ] **AC-6 — testable layout logic and pure card JSON:** truncation limits, selected-row/
      action derivation, grouping, state derivation, multi-day/location composition and the
      **Adaptive Card JSON** live in pure helpers under
      `windows/src/ItsycalForWindows/Pages/AgendaLayout.cs` and `.../CalendarCard.cs`, with unit
      tests in `windows/tests/ItsycalForWindows.Tests/AgendaLayoutTests.cs` and
      `.../CalendarCardTests.cs`. The helpers own `ResolveState(events, isLoading)`,
      `BuildHeader(date, today)`, `BuildRow(event, date)` (truncated title/location,
      `TitleMaxLines = 2`, marker/meta line, video-glyph flag), `BuildDetails(event, date)`
      (when-line computed **relative to the selected date**, including `All day` and
      `Day N of M`; notes preview + `NotesTruncated`; exact action list),
      `TryGetNormalizedMeetingUri(string?)` (http/https plus `uri.AbsoluteUri`) and
      `BuildCopyNotice(bool success)` (exact `Meeting link copied.` /
      `Could not copy the meeting link.`).
      `CalendarCard.BuildJson(month, events, selectedDate, selectedEvent, isLoading, showWeekNumbers, today)`
      returns the complete card JSON; the renderer (`CalendarPage`) only publishes that string
      and handles the submit payloads. It must not slice strings, apply its own ellipsis,
      re-derive actions, reorder body elements or paint colours outside the builder, so element
      order and styles are testable without the host. The renderer calls an injected
      `IMeetingLinkClipboard`; no test touches the real clipboard. `ResolveState` also returns
      the exact empty/loading label (`No events on this day.` / `Loading events…`) that the
      renderer displays.
      Planned data-test names:
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
      `Row_MultiDayAllDay_DayTwoOfThree_PrefixesMetaLine`,
      `Row_AllDay_TimeRangeIsAllDay`,
      `BuildDetails_MultiDay_UsesSelectedDateForDayNOfM`,
      `BuildDetails_AllDay_WhenLineIsAllDay`,
      `Link_UppercaseSchemeAndHost_ReturnsLowercaseAbsoluteUri`,
      `Link_SpaceInPath_PercentEncodesInAbsoluteUri`,
      `Link_NonHttpScheme_ReturnsNoUsableLink`,
      `Copy_ClipboardSuccess_ReturnsMeetingLinkCopiedNotice`,
      `Copy_ClipboardFailure_ReturnsCouldNotCopyNotice`,
      `Row_MeetingLink_FlagsVideoGlyph`, `Row_NoMeetingLink_HasNoVideoGlyph`,
      `Actions_MeetingLink_ReturnsJoinMeetingCopyLinkCloseDetails`,
      `Actions_NoMeetingLink_ReturnsCloseDetailsOnly`.
      Planned JSON-test names: `Card_Body_StartsWithMonthHeader`,
      `Card_MonthGrid_AlwaysHasSixWeekRows`, `Card_NavActions_AreInMonthHeaderRow`,
      `Card_MonthBlock_HasNoFilledPanel`,
      `Card_AgendaHeader_ShowsTodayAndShortDate`,
      `Card_Details_IsNextElementAfterSelectedRow`, `Card_Details_IsNotAfterLastRow`,
      `Card_Details_UsesEmphasisSurface`,
      `Card_Row_MarkerTimeAndTitleAreNeutral`,
      `Card_SelectedRow_UsesSelectionSurface_NotAccentText`,
      `Card_MeetingRow_HasNonInteractiveVideoGlyph`,
      `Card_NoLinkRow_HasOnlyCloseDetails`,
      `Card_MeetingRow_HasJoinCopyCloseWithExactLabels`,
      `Card_DetailsNotes_UsesMaxLinesFour`,
      `Card_JoinAction_IsOnlyPrimaryAction`, `Card_NoLargeOrExtraLargeText`,
      `Card_NoSeparatorRulesInAgenda`, `Card_Spacing_UsesMappedTokens`.
- [ ] **AC-7 — light translucency:** on an installed build the host acrylic is visible through
      the card (no opaque full-card panel fill and no fake alpha), text and controls keep
      readable contrast over the material in dark and light themes, the month block is unfilled
      (no emphasis panel) so the material reads through it, the selected-row surface and the
      inline details are the only filled sub-surfaces, and a solid-surface fallback (backdrop
      disabled) still renders intentionally. Verified with read-back screenshots; the material
      strength itself is host-owned and cannot be set by the extension.
- [ ] **AC-8 — typography, spacing and surface roles match the flyout within the renderer:**
      the card uses only the token map below; the month title and details title are the largest
      text (`medium`, `bolder`); the agenda header is `default`, `bolder`; row titles are
      `default` at regular weight (no `weight`); weekday labels, times, meta, dates, notes and
      notices are `small`, with `isSubtle` for the secondary roles; `large`/`extraLarge` and
      `lighter` are not used. Spacing uses only `none`/`small`/`medium` (host-mapped); there are
      no separator rules between the month block, the agenda header, the rows or the details
      (the details surface replaces the separator); the month block has no filled panel; the
      selected row and the inline details are the only filled sub-surfaces (`emphasis`), plus
      the permitted selected-day accent surface. Verified by the AC-6 JSON tests and by
      installed-build dark/light screenshots at
      800×480 and 640×420, judged by the design review against the flyout reference. The exact
      font DIP mapped to a token, action-button height/paint, container padding/stroke and the
      scrollbar are host-controlled and **not** defects; the drift examples listed in the
      Context are.

| Role | Flyout target (DIP) | CmdPal token (normative) |
|---|---|---|
| Month title | 16 semibold | `size: medium`, `weight: bolder` — largest text with the details title |
| Month navigation | 32×32 controls, 12 glyph | inline `ActionSet`; `Action.Submit` titles `‹`, `Today`, `›`; tooltips `Previous month`/`Next month` |
| Weekday labels | 11 semibold secondary | `size: small`, `weight: bolder`, `isSubtle: true` |
| Day number | 12 regular; selected/today stronger | `size: small`; `weight: bolder` for selected/today; `isSubtle` for adjacent-month days |
| Agenda header | 13 semibold + 12 secondary | left `size: default`, `weight: bolder`; right `size: small`, `isSubtle: true`, `horizontalAlignment: right` |
| Event marker | 4-DIP neutral dot | neutral `•` `TextBlock`, `isSubtle: true`, no `color` |
| Event title | 13 regular, 2 lines | `size: default`, no `weight`, `maxLines: 2`; 60-character preview |
| Event time/meta | 12 secondary | `size: small`, `isSubtle: true`; time right-aligned |
| Video glyph | 14 neutral | non-interactive neutral `TextBlock` glyph, meeting rows only |
| Details title | 16 semibold | `size: medium`, `weight: bolder`, neutral |
| Details when/location | 12 secondary | `size: small`, `isSubtle: true` |
| Details notes | 12 primary, ≤4 lines | `size: small`, no `isSubtle`, `maxLines: 4` + 480-character preview |
| Details actions | 32 DIP; Join primary, Copy/Close default | `ActionSet`; Join `style: positive`; Copy/Close default |
| Details surface | 1-DIP stroke, 8 radius, secondary fill | `Container style: emphasis`, `roundedCorners: true` |
| Card | 12 margin, 12 radius, 1-DIP stroke, acrylic | host card; the extension paints no card background |

## Deterministic fixture

No screenshot may depend on live calendar data. The fixture files and seed script are
**planned, not committed at head**; the implementation PR will commit them (the paths and the
tables below are normative):

- `windows/tests/fixtures/compact-ui/ordinary.ics`
- `windows/tests/fixtures/compact-ui/long.ics`
- `windows/scripts/Seed-CompactFixture.ps1 -Fixture ordinary|long`

The script replaces `{{TODAY}}`, `{{TODAY-1}}` and `{{TODAY+2}}` with the corresponding local
dates (`yyyyMMdd`), writes exactly one file to
`%LOCALAPPDATA%\ItsycalForWindows\calendars\compact-fixture.ics`, removes only its own previous
`compact-fixture*.ics` files, and fails if any `{{…}}` placeholder remains. It must **fail**
when the calendars folder contains any other `.ics` file (it never deletes unknown files):
the capture profile must contain only the fixture, otherwise the run is not acceptance
evidence. Fixture times are floating local times (`DTSTART:{{TODAY}}T090000`), so they render
at the stated wall-clock time on any machine. No DEBUG build and no live calendar source is
used. The capture profile must also have no configured Windows appointment-store calendars;
the PR records the source list.

**MSIX `%LOCALAPPDATA%` virtualisation (must be verified).** MSIX can redirect
`%LOCALAPPDATA%` writes into the package's virtualised store, so a file written by an external
script may not be the file the packaged app reads. The implementation PR must verify that the
packaged app sees the seeded file (or seed through the path the package actually reads) and
record the observed path; until that is verified, the fixture is not acceptance evidence.

**Ordinary fixture — five rows today:**

| # | Time | Title | Location | Notes | Meeting link |
|---|---|---|---|---|---|
| 1 | `All day`, `{{TODAY-1}}`–`{{TODAY+2}}` (today is `Day 2 of 3`) | `Release freeze` | `Building 7` | none | none |
| 2 | 09:00–09:30 | `Design review` | `Google Meet` | none | `https://meet.google.com/abc-defg-hij` |
| 3 | 11:00–12:00 | `Lunch with the team` | `Cafeteria` | none | none |
| 4 | 15:00–15:30 | `Focus block` | none | none | none |
| 5 | `All day`, `{{TODAY}}` | `Company offsite` | `Campus` | none | none |

**Long fixture — one event today, expanded for AC-1b/AC-3b:**

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
| AC-1a, AC-2 | ordinary | none |
| AC-1b | long | the long event |
| AC-3a | ordinary | event 2 (meeting), then event 3 (no link); events 1 and 5 cover the multi-day/all-day rows |
| AC-3b | ordinary | event 2 (meeting), then event 3 (no link); the long fixture for the scroll-to-end check |
| AC-3c | ordinary | event 2 (meeting) |
| AC-4, AC-7, AC-8 | ordinary | event 2 (meeting) |

## Test plan

| AC | Check | Kind |
|---|---|---|
| AC-1a | Installed build, 800×480/100%; full-window screenshot; crop the month header row through the sixth week row and the visible ContentPage viewport; pass only if the numerator ≤224 DIP **and** ≤ two-thirds of the measured viewport; six week rows present; no oversized selected-day header; record the numerator, denominator, ratio (~64–66%), month-header height, week-row height and agenda-header height | manual + screenshot |
| AC-1b | Long fixture expanded inline; screenshot, scroll once to the end, screenshot again; the complete inline action row must be inside the card and every action visible/activatable in that one end position; an ordinary collapsed day is allowed to scroll | manual + screenshot |
| AC-1c | Full-window capture includes host top/command bars; record window bounds (800×480 DIP at 100%); code review confirms no resize API call | manual + review |
| AC-2 | Prev/next/Today and day clicks; month title + inline navigation in one row; agenda header shows `Today`/weekday + `MMM d` and updates immediately; today/selected/event-dot cues; week-number on/off at 800×480 and 640×420; no overlap, wrap-under or clipping of event text | manual + screenshot |
| AC-3a | Ordinary fixture: event 2 has a neutral marker, neutral time and regular-weight title (no accent, no bold); video glyph only on event 2; event 1 shows `All day` and `Day 2 of 3 · Building 7`; no `Events` caption, no accent gutter, no separator rules | manual + screenshot |
| AC-3b | Ordinary fixture: select event 2 — details are the next element directly under the row, labels `Join meeting`/`Copy link`/`Close details`; click `Copy link` and compare the clipboard with `https://meet.google.com/abc-defg-hij` and the neutral `Meeting link copied.` notice; select event 3 — only `Close details`, no `Join meeting`/`Copy link`; selecting another row collapses the previous; clicking the selected row or `Close details` collapses; no details block after the last row; long fixture scrolls as AC-1b | manual + screenshot |
| AC-3c | Ordinary fixture: accent appears only on the selected day's surface and today's number/marker; the Join action is the only primary action; row time/title, details title, notes and notice are neutral; no `large`/`extraLarge` text | manual + unit |
| AC-4 | Dark and light screenshots at 800×480 and 640×420; inspect clipping of time/title/action labels and horizontal overflow; the month header stays one row at 640; accent only per AC-3c | manual + screenshot |
| AC-5 | Empty-day screenshot shows `No events on this day.` plus the one-line calendars-folder pointer; loading state covered by `AgendaLayoutTests.cs` state tests, not a screenshot | screenshot + unit |
| AC-6 | `AgendaLayoutTests.cs` / `CalendarCardTests.cs` with the planned names in AC-6 (data plus JSON order/inline/surface/neutral/labels/no-link/video/token tests); TDD red run recorded in the implementation PR | unit |
| AC-7 | Dark/light installed-build screenshots: host acrylic visible through the card, no opaque full-card fill/fake alpha, no filled month panel, readable contrast; check the solid-surface fallback if the backdrop is unavailable | manual + screenshot |
| AC-8 | `CalendarCardTests.cs` token tests plus installed-build dark/light screenshots at 800×480 and 640×420 compared with the flyout reference; hierarchy, density, surfaces and spacing judged by the design review; no `large`/`extraLarge`, no separators, no bold row titles; record host-mapped differences (font DIP, button metrics, padding) as NOT defects | unit + screenshot + design review |

The fixture is deterministic and planned for the implementation PR, not DEBUG-only: exactly five
rows today in the ordinary fixture (two all-day rows, one of them a three-day event where today
is `Day 2 of 3`; three timed rows) with fixed titles/times/locations/notes, one meeting link and
four no-link rows; the long fixture supplies the long title/location/notes expansion. Every
screenshot above runs on that isolated source set; the PR records the source list and the MSIX
`%LOCALAPPDATA%` path it verified. No row-level action is claimed: the row's video glyph is an
indicator; `Join meeting` is the primary button in the inline details (the host cannot nest
actions in a selectable row). The **host-controlled items listed in the Context are NOT
defects**; the PR records them when they differ from the flyout. The design review required by
`AGENTS.md` runs after the tester and before code review on these installed-build screenshots,
against this spec, [spec 0004](0004-standalone-compact-flyout.md) and the flyout reference.

## Out of scope

- The primary standalone flyout (window, acrylic/Mica strength, hotkey, tray, full keyboard/
  UIA): owned by [spec 0004](0004-standalone-compact-flyout.md).
- **Pixel-for-pixel copy of the flyout:** the target is its design language and interaction
  hierarchy, not identical pixels. Host-mapped font metrics, action-button height/paint,
  container padding/stroke, card radius/margin, the scrollbar, the material strength and
  hover/pressed states are host-owned and are not defects (see the Context list).
- Host window geometry/position, window animation, backdrop/acrylic ownership and the host
  top/command bars: the extension cannot set them; they are outside this spec.
- A 350-DIP/custom-width popover and a no-scroll contract for a typical day: the host fixes
  the window and this surface scrolls even for an ordinary day; those are primary-flyout
  requirements, not claims for this surface.
- No-scroll for long expanded details (AC-1b allows one scroll-to-end), notes `Show more`
  expander (host toggle rendering unverified; full text stays in the source app), custom
  acrylic tint/opacity (host-owned), two-month view.
- **Nested row actions / a `Join meeting` button inside the selectable row:** Adaptive Cards
  1.5 cannot nest an action inside a row that already has a `selectAction`; the video glyph is
  a non-interactive indicator and `Join meeting` is the primary action in the inline details.
- **Custom per-element UIA names and full keyboard parity** (arrow-key day focus, a
  deterministic tab order, Escape semantics): the host surface does not expose them; track as
  a host feature request; deferred. Renderer-supported keyboard (Today/prev/next, Enter on
  host-rendered action buttons) is expected from the host but is not a per-element contract
  here.
- Source-status/all-sources-failed UI, Calendars settings/onboarding (ONB-101). The agenda's
  one-line calendars-folder pointer stays until ONB-101 ships and replaces it.
- Per-source event colours (no data source yet): the marker is neutral.
- Custom fonts/icon fonts: Adaptive Cards 1.5 has no font-family or icon-font control; the
  video glyph is a text glyph and the exact glyph rendering is reviewed on the installed build.
- Localization strings, Settings consolidation, dock-band redesign, providers.
- Event editing/creation/RSVP, animations, drag/drop.

## Review

Review record: **revision 5 submitted 2026-11-08 for independent spec review — verdict
pending**; implementation starts only after an `approve` or `approve with notes` verdict is
recorded. Revision 5 supersedes revision 4 before any revision-4 verdict was recorded; the
independent review is requested against revision 5, not against revision 4.

- Revision 5 (2026-11-08, PO decision: CmdPal parity): the surface is re-framed from
  "optional polish, explicitly not parity" to a **parity mandate** — optional in distribution,
  not in design. Changes: AC-1a now measures a month header (title + inline navigation) and
  moves the selected day into a compact ≤20-DIP agenda header (a ~50-DIP selected-day header
  stays disallowed); AC-2 adds the agenda header and day-cell/nav parity; the former AC-3 is
  split into AC-3a (neutral rows/video glyph), AC-3b (inline details directly under the row,
  filled details surface) and AC-3c (accent only for the selected day/today plus the Join
  primary action); AC-6 adds the pure `CalendarCard.BuildJson` builder and Adaptive-Card JSON
  tests; AC-7 clarifies the unfilled month block; AC-8 is new (typography/spacing/surface token
  map). AC-4 and AC-5 keep their numbers (spec 0005 references 0003 AC-5); AC-4 gains the
  theme/hierarchy check and AC-5 is unchanged. Host-owned limits are listed as NOT defects in
  the Context and the test plan. No implementation has started; independent review pending.
- Round 3 (2026-11-08, independent of the author): request changes — AC-1b (no scrolling for
  ordinary collapsed events) is impossible at the measured ContentPage viewport; AC-1a's
  denominator and "50% of the card" claim are dishonest; Copy semantics are undefined and
  `BuildDetails` has no selected date or all-day/multi-day coverage; the fixture/script are not
  committed and MSIX `%LOCALAPPDATA%` virtualisation is unverified; the empty-state folder
  pointer would be removed before ONB-101 exists; backlog ownership conflates this optional
  surface with the primary flyout. Revision 4 resolved them: AC-1b deleted and scrolling
  accepted, AC-1a measured against the visible viewport, normalized-URI + injectable-clipboard
  semantics, `BuildDetails(event, date)` and all-day/multi-day fixture rows/tests, fixture and
  script marked planned with a virtualisation check and a foreign-`.ics` failure, a one-line
  folder pointer retained until ONB-101, and [UI-105](../BACKLOG.md) added for this surface.
  Revision 4's re-review did not complete; revision 5 carries the parity mandate and keeps
  these resolutions.
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
  defined numerator, denominator and scroll-detection method; a required `.ics` fixture with a
  stamp-today script and exact values; loading verified by state unit tests with
  the loading screenshot dropped; exact `Join meeting`/`Copy link`/`Close details` semantics;
  accent restricted to selection/today with neutral dot/time/title; backlog/roadmap/spec links
  and `CHG-701` status corrected; month target lowered to 224 DIP and the primary "not half the
  screen" requirement explicitly moved to spec 0004 (the "≤50% of the card" wording was
  superseded by round 3's viewport denominator). Translucency is AC-7 (host acrylic).
- Design reviewer: required for the implementation PR (installed-build screenshots,
  dark/light, compact/expanded, host 800×480 and 640×420). Verdict recorded in the PR before
  code review.
- Code review: requested after the design verdict. The PR template holds the PR-level review
  record (reviewer, date, verdict, link); this section records the spec-level history.
