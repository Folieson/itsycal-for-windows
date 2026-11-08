# 0002 — ICS recurrence correctness: DST and BYDAY

Status: proposed (v1.2.2 increment, spec revised after independent spec review)
Related: PR #33 (open; parser/README changes that this spec builds on), PR #35 (spec),
ADR 0002, [0001 update-download-fallback](0001-update-download-fallback.md).
Follow-up specs (not in this increment): `0003-dev-package-versioning.md`,
`0004-updater-hardening.md`, and a docs-accuracy change.

## Context

The ICS folder is the only reliable calendar source on current Windows builds (ADR 0002),
and two recurrence defects silently corrupt what users see:

1. **DST drift.** Occurrences are produced with `start.AddDays(stepDays * index)` on a
   `DateTimeOffset`, so the original UTC offset is preserved. A weekly
   `DTSTART;TZID=Europe/Berlin:20260322T090000` event occurs at 10:00 Berlin after the
   spring transition instead of 09:00. No spring/fall test exists.
2. **`BYDAY` ignored.** `FREQ=WEEKLY;BYDAY=MO,WE,FR` repeats only the DTSTART weekday,
   silently omitting occurrences; `INTERVAL` groups are not aligned to the intended week.
   Unsupported RRULE parts are not reported anywhere.

This spec covers only recurrence. Versioning, updater hardening and documentation accuracy
are separate specs listed above; bundling them into one increment made acceptance criteria
unverifiable (review finding).

## Recurrence policy (normative)

- **Supported RRULE parts:** `FREQ` (DAILY, WEEKLY), `INTERVAL`, `COUNT`, `UNTIL`, `BYDAY`
  (weekday names, no ordinal prefixes). `DTSTART` is always the first occurrence.
- **`BYDAY` semantics:** for WEEKLY, occurrences fall on the listed weekdays inside each
  interval week; the interval week starts on the weekday of `DTSTART` (WKST is not
  honoured, see below). For DAILY, `BYDAY` filters the allowed weekdays.
  Duplicate weekday values are de-duplicated. If `DTSTART`'s weekday is not in `BYDAY`, the
  `DTSTART` occurrence is still emitted, then the selected weekdays follow.
- **`COUNT`** counts emitted occurrences (including the DTSTART occurrence), not candidate
  steps. **`UNTIL`** is inclusive and compared as an instant.
- **Unsupported parts** — any RRULE part outside the supported set, including `WKST`,
  ordinal `BYDAY` (e.g. `2MO`), `BYSETPOS`, `BYMONTHDAY`, `BYWEEKNO`, `BYYEARDAY`,
  `BYMONTH`, `BYHOUR`, `BYMINUTE`, `BYSECOND` — make the recurrence **fail closed**: only
  the `DTSTART` occurrence is emitted and one diagnostic is counted for that event.
  `WKST` is deliberately treated as unsupported rather than silently ignored.
- **Wall-clock correctness:** every occurrence start is resolved as a wall-clock time in the
  event's zone (TZID zone if present, otherwise floating/local), producing the correct UTC
  instant for that date, including DST transitions. Occurrence end is `start wall-clock +
  duration` resolved in the same zone; a duration crossing a transition therefore shifts the
  UTC end correctly. `DTEND`/`DURATION` semantics for the base event are unchanged.
- **Diagnostics API:** add `IcsParseResult ParseWithDiagnostics(string ics)` returning the
  existing event list plus `UnsupportedRecurrenceCount` (per event) and
  `UnsupportedRecurrenceEvents` (event count, not titles/UIDs). `Parse` keeps its current
  signature and delegates to it. The count is consumed by tests and by a DEBUG-only,
  content-free log line (`Recurrence: N unsupported rule(s), M event(s)`); nothing else logs
  event data. Malformed input still yields an empty list, never throws.
- **Caps:** the existing occurrence cap applies to emitted occurrences, not candidate steps;
  windowed parsing (`from`/`to`) emits only occurrences intersecting the window.

## Acceptance criteria

- [ ] **AC-1**: Weekly TZID events keep their local wall-clock time across spring-forward
      and fall-back (Europe/Berlin weekly 09:00 stays 09:00 local; UTC instants shift by the
      offset difference). Assertions convert to the event zone and also assert the UTC
      instant (CI runs in UTC). A transition-spanning event resolves its end in the event
      zone. Tests cover spring, fall, and a duration crossing the transition, using a
      windowed parse.
- [ ] **AC-2**: `FREQ=WEEKLY;BYDAY=MO,WE,FR` emits Monday, Wednesday and Friday in every
      interval week; `FREQ=DAILY;BYDAY=MO,WE,FR` filters to those weekdays;
      `INTERVAL=2` with `BYDAY` aligns to the interval week from `DTSTART`. `COUNT` counts
      emitted occurrences. `DTSTART` not in `BYDAY` is handled per policy. Duplicate
      weekdays are de-duplicated.
- [ ] **AC-3**: `WKST`, ordinal `BYDAY`, `BYSETPOS`, `BYMONTHDAY`, `BYMONTH` and
      `BYHOUR`/`BYMINUTE`/`BYSECOND` produce exactly the `DTSTART` occurrence and increment
      `UnsupportedRecurrenceCount` once per event; the DEBUG log line contains only counts.
- [ ] **AC-4**: Floating recurrence (no TZID) also uses wall-clock expansion in the local
      zone across a DST transition; `UNTIL` is inclusive of occurrences whose start equals
      the UNTIL instant in the event zone.
- [ ] **AC-5**: Occurrence-window and cap behaviour: a windowed parse does not emit
      occurrences outside `[from, to)`; the occurrence cap counts emitted occurrences; a
      recurrence with more candidates than the cap still emits at most the cap.

## Test plan (TDD order)

| AC | Test | Kind |
|---|---|---|
| AC-1 | `IcsParserTests.Parse_WeeklyTzidKeepsWallClockAcrossSpringDst`; `...AcrossFallDst`; `...DurationCrossingDstTransition` | unit |
| AC-2 | `...WeeklyByDayExpandsAllDays`; `...DailyByDayFiltersWeekdays`; `...IntervalTwoByDayAlignsWeeks`; `...CountCountsEmittedOccurrences`; `...StartNotInByDayStillEmitsStart` | unit |
| AC-3 | `...UnsupportedByPartFallsBackToStartOnly`; `...WkstIsReportedUnsupported`; `...UnsupportedRecurrenceCountsEvents` | unit |
| AC-4 | `...FloatingWeeklyKeepsWallClockAcrossDst`; `...UntilIsInclusiveInEventZone` | unit |
| AC-5 | `...WindowedParseSkipsOccurrencesOutsideRange`; `...OccurrenceCapCountsEmittedOccurrences` | unit |

All tests use an explicit Europe/Berlin zone where a TZID is present and assert both the
zone-local hour/minute and the UTC instant, so they are independent of the CI machine zone.

## Out of scope

- Versioning, updater and documentation-accuracy defects found in the same repository
  review (separate specs `0003`, `0004`, docs change).
- Calendar providers, UI/onboarding, settings, accessibility, localization.
- Ordinal `BYDAY`/complex RRULE support; those fail closed by policy.
- Changing the appointment-store mapping (handled by PR #33).

## Review

- Spec reviewer (independent, read-only): 2026-11-08, verdict **request changes**.
  Resolved by this revision: split from four behaviours to recurrence only (AC-3/4/5 of the
  old spec moved to `0003-dev-package-versioning.md`, `0004-updater-hardening.md` and a
  docs change); defined the diagnostics API and consumer; defined the exact supported RRULE
  set, BYDAY/COUNT/DTSTART/WKST policies; specified wall-clock/end resolution with a
  transition-spanning test; defined the caps/window semantics; mapped every AC to tests;
  added a manual evidence location.
- Code review: requested on the implementation PR after the spec re-review and after
  PR #33 is merged (this spec builds on its parser changes).
