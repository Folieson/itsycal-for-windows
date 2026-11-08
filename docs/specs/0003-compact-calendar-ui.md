# 0003 — Compact calendar flyout (ItsyCal-like)

Status: proposed (P0 product epic, PO decision 2026-11-08)
Backlog: [UI-101 / UI-102 / UI-103](../BACKLOG.md)
Related: PR #33 (details/Join groundwork), PR #36 (v1.2.2), ADR 0002
Reference: original ItsyCal [product page](https://www.mowglii.com/itsycal/) and
[banner](https://www.mowglii.com/itsycal/itsycalbanner2@2x.png) — a ~350-DIP-wide popover
with a month grid on top, compact toolbar, and agenda groups below (colour dot, time range,
video icon on meeting rows).

## Context

The current Command Palette flyout renders the 6×7 month grid and header so large that only
two event rows fit, and event details are appended after the whole agenda and below the
month navigation. A typical 3-event day therefore needs scrolling to see details or Join.
The original ItsyCal is the product reference: a **tiny** popover where the calendar is a
quick date-navigation tool and the agenda below is the primary content, with details and a
one-click Join action for the selected meeting.

The user (PO) explicitly prioritised this over the provider platform: the product must be
beautiful, modern, laconic and intuitive before adding OAuth/CalDAV sources.

## Design targets

- A compact single-card flyout; default content width ~360 DIP (acceptable 340–400), no
  oversized month headers or empty padding.
- Month grid is navigation-only: month title + prev/next arrows in one row, two-letter
  weekday header, 6 rows of cells; the grid block must not exceed ~55% of the card height.
- Below the grid: compact toolbar (Today; optional week numbers) and the agenda grouped by
  day (`Today · Nov 8`, `Sunday · Sep 20`). Each row: colour dot, time range, title,
  location (truncated); a video icon on rows that carry a meeting link.
- Selecting an event expands its details **inline under that row** (full when-line, location,
  notes, `Join` only for meeting links, `Copy link`); selecting another event collapses the
  previous one; the old separate details block at the end is removed.
- States: no events on the selected day (short message, no long setup text inside the
  agenda), loading, all-sources-failed; selection/today/focus are visually distinct with the
  accent reserved for them.
- Light and dark themes, readable contrast, no clipped labels at 340–400 DIP; accessible
  names on day cells and event rows; keyboard: Escape closes, arrows move focus between
  days, Today returns to the current month.
- The host page (`ContentPage` + Adaptive Card 1.5) remains the surface; no custom window.

## Acceptance criteria

- [ ] **AC-1**: At the default palette window, the calendar block (title + weekday header +
      6 week rows) occupies at most ~55% of the card height and the typical day (today with
      three events, one expanded) shows the agenda and inline details **without vertical
      scrolling**. Measured on an installed build with a read-back screenshot.
- [ ] **AC-2**: Month title and prev/next/today controls sit in one compact row; clicking a
      day updates the agenda immediately; the selected day is visually distinct; optional
      week numbers stay aligned without widening the card beyond 400 DIP.
- [ ] **AC-3**: Agenda rows show colour dot, time range, title and truncated location; rows
      with a meeting link show a video/Join icon. Selecting a row expands details directly
      below it (when-line, location, notes, Join for meeting links, Copy link); the previous
      selection collapses; no details block appears after the whole agenda.
- [ ] **AC-4**: Dark and light themes both render with readable contrast and no clipped text
      at 340, 360 and 400 DIP widths with `en-US` and `ru-RU` date formats; accent is used
      only for selection/today/focus; day cells and event rows expose accessible names.
- [ ] **AC-5**: Empty, loading and all-sources-failed states are distinguishable; the empty
      state does not repeat long onboarding text on every empty day; a visible status hint
      points to Calendars settings/folder.
- [ ] **AC-6**: Keyboard-only use reaches today, previous/next month, day selection and the
      selected event's Join action; Escape returns to the palette.

## Test plan

| AC | Check | Kind |
|---|---|---|
| AC-1 | Installed build, palette at default size: dark screenshot today + 3 events + one expanded; no scrollbar; measure grid/card ratio | manual + screenshot |
| AC-2 | Screenshots after prev month, day click, Today; week-number variant at 340/360/400 DIP | manual + screenshot |
| AC-3 | Two events selected in turn: details move under the selected row, previous collapses, Join icon only on meeting rows | manual + screenshot |
| AC-4 | Dark/light screenshots at 340/360/400 DIP, en-US and ru-RU; accessibility names checked via host automation/manual | manual + screenshot |
| AC-5 | Empty day, loading, all-sources-failed screenshots; folder/settings hint reachable | manual + screenshot |
| AC-6 | Keyboard-only run with palette hotkey; record focus order and Escape | manual |

Structural card unit tests are added where the layout logic is pure (row ordering, grouping,
truncation limits); visual claims require the screenshots above (AGENTS UI rule).

## Out of scope

- Settings redesign (BACKLOG SET-401), calendars onboarding (ONB-101), dock band redesign
  and screenshot (DOC-501), localization strings (LOC-601), providers (PRV-301).
- Custom WinUI window, animations, drag/drop, multi-month view.
- Editing events, creating events, RSVP.

## Review

- Spec reviewer: pending (independent, read-only) — do not implement before the verdict.
- Code review: requested on the implementation PR once the spec is accepted.
