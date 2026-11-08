# 0002 — Reliability hardening: recurrence correctness, versioning and updater safety

Status: proposed (v1.2.2 increment)
Related: PR #32 (merged), ADR 0001/0002, repository review 2026-11-08
(`docs/specs/README` not present; review findings are summarised in Context).

## Context

The 2026-11-08 repository review found four correctness/security defects that users can hit
today:

1. **ICS recurrence drifts across DST.** Occurrences are produced with
   `start.AddDays(stepDays * index)`, which preserves the original UTC offset. A weekly
   `DTSTART;TZID=Europe/Berlin:20260322T090000` event occurs at 10:00 Berlin after the
   spring transition instead of 09:00. No spring/fall test exists.
2. **`BYDAY` is ignored.** `FREQ=WEEKLY;BYDAY=MO,WE,FR` repeats only the DTSTART weekday,
   silently omitting occurrences. The parser comment claims unsupported rules degrade to the
   first occurrence.
3. **Development packages are versioned below the published release.** CI stamps
   `1.0.0.<run>` while releases are `1.2.1.0`; installing a CI dev MSIX over the released
   package is a downgrade and `Add-AppxPackage` rejects it. README/windows README claim
   in-place upgrades.
4. **Updater hazards.** The generated PowerShell updater is written as UTF-8 without BOM
   while it is launched with Windows PowerShell 5.1, which reads BOM-less scripts as ANSI, so
   a non-ASCII profile path corrupts the MSIX/log paths. The package SHA256 is not re-checked
   immediately before applying, and a failed `Add-AppxPackage` skips the palette-restart block,
   leaving Command Palette stopped.

The ICS folder is the only reliable source on current Windows builds (ADR 0002), so silent
recurrence errors directly hit the product promise.

## Acceptance criteria

- [ ] **AC-1**: A weekly event with an IANA `TZID` keeps its local wall-clock time across
      spring-forward and fall-back transitions (Europe/Berlin example: weekly 09:00 stays
      09:00 local; the UTC instants shift by the offset difference). Occurrence ends inherit
      the same corrected offset. Unit tests cover both transitions.
- [ ] **AC-2**: `FREQ=DAILY` and `FREQ=WEEKLY` honour `BYDAY` (including multi-day sets and
      `INTERVAL`). An unsupported recurrence part (`BYSETPOS`, `BYMONTHDAY`, `BYMONTH` for the
      supported frequencies) never silently duplicates a wrong weekday: the parser emits only
      the DTSTART occurrence and exposes a diagnostic count for that event/file. Unit tests
      cover `FREQ=WEEKLY;BYDAY=MO,WE,FR` and an unsupported part.
- [ ] **AC-3**: The development-package version stamped by CI is strictly greater than the
      latest published release version, so `Add-AppxPackage` upgrades in place. The version is
      derived from an explicit dev base (at least the newest release) plus the run number, not
      a hard-coded `1.0.0`; a script/unit test proves the ordering for the current release and
      is exercised by the build workflow. Releases keep deriving their identity from the tag.
- [ ] **AC-4**: The generated updater script is UTF-8 **with BOM**, so Windows PowerShell 5.1
      reads non-ASCII profile paths correctly. Apply re-verifies the package SHA256 against the
      release checksum immediately before staging the updater and records the verified hash in
      `update.log`. Command Palette is restarted in a `finally` block on both success and
      install failure. Unit tests assert the BOM, the hash re-check, and that the restart call
      exists outside the success-only path.
- [ ] **AC-5**: Documentation and commands that point at the test project use the real path
      `windows/tests/ItsycalForWindows.Tests/ItsycalForWindows.Tests.csproj`, and
      README/SECURITY/windows README no longer contradict the implemented network/certificate
      behaviour. A grep-based docs check or a documented manual review records this.

## Test plan (TDD order)

| AC | Test | Kind |
|---|---|---|
| AC-1 | `IcsParserTests.Parse_WeeklyTzidKeepsWallClockAcrossSpringDst`; `...AcrossFallDst` | unit |
| AC-2 | `IcsParserTests.Parse_WeeklyByDayExpandsAllDays`; `Parse_UnsupportedByPartFallsBackToSingleOccurrence` | unit |
| AC-3 | `VersioningTests.DevVersionIsGreaterThanLatestReleaseBase` (pure calculator used by CI/script) | unit |
| AC-4 | `UpdateLogicTests.UpdaterScriptStartsWithUtf8Bom`; `Apply_RehashesPackageBeforeStaging`; `UpdaterScriptRestartsHostOnFailure` | unit |
| AC-5 | grep/review checklist (docs only) + `dotnet test` command referenced in docs resolves to an existing path | manual/docs |

## Out of scope

- Onboarding (open calendar folder / `.ics` import), settings consolidation, dock-band
  screenshots, localization, light theme, accessibility — separate increments from the
  product review.
- Google/Microsoft/CalDAV providers (v1.3).
- Trusted signing/WinGet identity migration; release workflow test/ARM64 gates (own spec).
- Changing the Windows appointment store mapping (fixed in PR #33) beyond tests.

## Review

- Spec reviewer: pending (independent, read-only).
- Code review: requested on the implementation PR after tests are green.
