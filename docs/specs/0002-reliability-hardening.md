# 0002 — ICS recurrence: DST wall-clock correctness and BYDAY

Status: proposed (v1.2.2 increment; narrowed after two independent spec reviews)
Dependency: PR #33 (open; parser/README changes) must merge first.
Related: ADR 0002, spec 0001. Follow-up work (not this increment): floating-zone DST
automation, recurrence diagnostics API/log, caps/window semantics, dev-versioning, updater
hardening, docs accuracy.

## Context

The ICS folder is the only reliable source on current Windows builds (ADR 0002). Two
recurrence defects corrupt what users see:

1. **DST drift.** Occurrences are built with `start.AddDays(stepDays * index)` on a
   `DateTimeOffset`, preserving the original UTC offset. A weekly
   `DTSTART;TZID=Europe/Berlin:20260322T090000` event is emitted at 10:00 Berlin after the
   spring transition instead of 09:00. No spring/fall test exists.
2. **`BYDAY` ignored.** `FREQ=WEEKLY;BYDAY=MO,WE,FR` repeats only the DTSTART weekday, and
   `INTERVAL=2` weeks are not aligned to the intended week.

The first review requested a diagnostics API and floating-zone automation; both are
not testable in CI (UTC runner) or would widen the change into a parser-API redesign. This
increment therefore fixes the user-visible defects only and records the rest as follow-ups.

## Policy (normative)

- **Supported RRULE parts:** `FREQ` ∈ {DAILY, WEEKLY}, `INTERVAL`, `COUNT`, `UNTIL`,
  `BYDAY` (plain weekday names, no ordinal prefixes).
- **BYDAY:** WEEKLY emits the listed weekdays inside each interval week; the interval week
  starts on the DTSTART weekday. DAILY filters allowed weekdays. Duplicate weekday values
  are de-duplicated. If DTSTART's weekday is not in BYDAY, the DTSTART occurrence is still
  emitted, then the selected weekdays follow.
- **COUNT** counts generated recurrence instances from DTSTART after BYDAY selection,
  before EXDATE/window filtering (preserves the existing COUNT+EXDATE behaviour). `UNTIL` is
  inclusive, compared in the event zone.
- **Unsupported input fails closed:** any other RRULE part (including `WKST` when
  `INTERVAL>1`, ordinal BYDAY, `BYSETPOS`, `BYMONTHDAY`, `BYMONTH`, `BYSECOND`, etc.) or any
  `FREQ` other than DAILY/WEEKLY emits exactly the DTSTART occurrence. `WKST` with
  `INTERVAL=1` is ignored because it cannot change the occurrence set. No diagnostics
  counter/log is added in this increment (deferred follow-up).
- **Wall-clock:** occurrence start is resolved as a wall-clock time in the event zone
  (TZID if present; floating values keep using the local zone, unchanged), producing the
  correct UTC instant per date. Occurrence end is `start wall-clock + duration` resolved in
  the same zone, i.e. a calendar-day duration, not an exact elapsed-duration arithmetic.
  Explicit example: Europe/Berlin 2026-03-29 01:30 + 2h wall-clock → 03:30 CEST
  (`2026-03-29T01:30:00Z`).
- **Unchanged:** all-day (`VALUE=DATE`) recurrence stays local-date based; the existing
  occurrence cap and window filtering stay as they are.

## Acceptance criteria

- [ ] **AC-1**: A weekly `TZID=Europe/Berlin` event at 09:00 stays 09:00 local across the
      2026-03-29 spring and 2026-10-25 fall transitions; each occurrence reports the correct
      offset and UTC instant (Berlin 09:00 → 07:00Z winter, 07:00Z summer). A
      transition-spanning event uses the wall-clock end rule with the explicit example above.
- [ ] **AC-2**: `FREQ=WEEKLY;BYDAY=MO,WE,FR` emits all three weekdays each week; DAILY
      filters weekdays; `INTERVAL=2` aligns weeks from DTSTART; duplicate BYDAY values are
      de-duplicated; DTSTART not in BYDAY still emits DTSTART and then the listed weekdays;
      COUNT counts generated instances before EXDATE (COUNT=3 + one EXDATE yields 2).
- [ ] **AC-3**: `FREQ=MONTHLY`, ordinal BYDAY, `WKST` with `INTERVAL>1` and other
      unsupported parts yield exactly the DTSTART occurrence (no crash, no extra events);
      `WKST` with `INTERVAL=1` keeps the full BYDAY series.
- [ ] **AC-4**: All-day recurrence remains date-based and unchanged (regression test over a
      DST boundary), and window filtering still returns only events overlapping the
      requested display-local range.

## Test plan (TDD order)

| AC | Test | Kind |
|---|---|---|
| AC-1 | `IcsParserTests.Parse_WeeklyTzidKeepsWallClockAcrossSpringDst`; `...AcrossFallDst`; `...DurationCrossingDstTransition` | unit |
| AC-2 | `...WeeklyByDayExpandsAllDays`; `...DailyByDayFiltersWeekdays`; `...IntervalTwoByDayAlignsWeeks`; `...DuplicateByDayIsDeduplicated`; `...StartNotInByDayStillEmitsStart`; `...CountCountsInstancesBeforeExDate` | unit |
| AC-3 | `...MonthlyFrequencyFallsBackToStartOnly`; `...OrdinalByDayFallsBackToStartOnly`; `...WkstWithIntervalOneKeepsSeries`; `...WkstWithIntervalGreaterThanOneFallsBack` | unit |
| AC-4 | `...AllDayRecurrenceUnchangedAcrossDst`; `...WindowFilteringUsesDisplayLocalDates` | unit |

All TZID tests assert both the zone-local hour/minute (via `TimeZoneInfo.ConvertTime`) and
the UTC instant, so they are independent of the CI machine zone.

## Out of scope

- Floating-zone DST automation (needs an injectable zone seam; documented manual check).
- Recurrence diagnostics API/log, occurrence-cap and window semantics changes.
- Versioning, updater, docs accuracy (separate follow-up specs/PRs).
- Providers/UI/onboarding/settings/accessibility/localization.

## Review

- Spec reviewer #1 (2026-11-08): request changes — bundled four behaviours, undefined
  diagnostics/versioning/updater ACs. Resolved by splitting scope.
- Spec reviewer #2 (2026-11-08): request changes — floating-DST test not executable on UTC
  CI, diagnostics contract contradictory, COUNT/WKST/end semantics undefined. Resolved by
  removing floating/diagnostics from scope, defining COUNT/WKST/end/all-day/window rules,
  and adding the exact transition example.
- Code review: requested on the implementation PR after PR #33 merges and both gates pass.
