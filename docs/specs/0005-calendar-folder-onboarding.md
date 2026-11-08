# 0005 — ONB-101a: open calendar folder (Command Palette)

Status: revision 3 — the second independent spec review returned **request changes** on
documentation and review-record accuracy; the code is unchanged and consistent with the
accepted scope. This revision resolves B1 and M1–M9 plus the minor notes below and awaits a
re-review verdict. The implementation is in this branch but is not accepted until that verdict
is recorded.

Backlog: [ONB-101a](../BACKLOG.md), the folder-command slice of ONB-101. The rest of ONB-101
(`.ics` import, source status, empty-state call-to-action) is deliberately out of scope, listed
under [Out of scope](#out-of-scope) and recorded in the backlog.

Related: [ADR 0002](../adr/0002-calendar-providers-and-network.md) (calendar providers and
network). The selected-day empty-state contract is owned by the calendar UI specs
[0003](0003-compact-calendar-ui.md) (AC-5, compact calendar / Command Palette surface) and
[0004](0004-standalone-compact-flyout.md) (AC-7, standalone flyout), both merged into master;
those specs have their own re-review pending. This change adds the folder command and aligns
the empty state with that contract; it does not add import, source status or a broader
onboarding flow.

Links: [PR #42](https://github.com/Folieson/itsycal-for-windows/pull/42), CI
[run 42487140544](https://github.com/Folieson/itsycal-for-windows/actions/runs/42487140544)
(`windows-build`, commit `4d189db`), [backlog entry](../BACKLOG.md), and the
[review record](https://github.com/Folieson/itsycal-for-windows/pull/42#independent-reviews)
(also summarized under [Review](#review)).

## Context

The extension reads local `.ics` exports from
`%LOCALAPPDATA%\ItsycalForWindows\calendars` (`IcsEventSource.DefaultDirectory`). The folder is
created lazily on the first calendar read. Its location is documented in the root
[README](../../README.md) (Features, and Install step 4) and was named in the old empty-state
label, so the path itself was not hidden; the missing piece was an in-product action. A
first-run user had to open File Explorer and retype the path from the README or the label, and
the old label mixed the `No events on this day.` sentence with the `.ics` instruction in one
combined string.

Current behaviour: the selected-day empty state (`CalendarPage`) renders that one combined
sentence. UI specs 0003 (AC-5) and 0004 (AC-7) define a different shape: the exact sentence
`No events on this day.` plus a **one-line** calendars-folder pointer, retained until ONB-101
ships the full onboarding. This change aligns the extension with that contract instead of
keeping a competing sentence; the installed-build check below is what verifies the rendering,
not a unit test.

Proposal: one palette command, `Itsycal: open calendar folder`, exposed both as a top-level
command and in the calendar item's context menu. It creates the folder when missing and opens
it in File Explorer, and reports the outcome in a toast. The selected-day empty state becomes
two separate `TextBlock` elements: the contract sentence and the folder pointer.

Constraints:

- The command must resolve the same folder the ICS provider reads:
  `CalendarFolder.DefaultPath` is required to equal `IcsEventSource.DefaultDirectory`, enforced
  by `CalendarFolderTests.DefaultPath_IsTheFolderIcsEventSourceReads`. The two paths are still
  derived separately, so the equality test — not a shared constant — is what prevents drift.
- No network access or telemetry. The command only touches the local file system and
  `explorer.exe`; no event content is logged.
- The command runs across the extension's COM boundary: a failure must surface as a toast, never
  as an exception. The pure helpers report failure as `false`; `Invoke` has no throwing path.
- The empty state is a small, subtle block under the agenda. It must stay two short parts, not
  become a setup paragraph. Each part is a separate `TextBlock`; whether each part renders on one
  visual line is determined by the installed-build check in AC-4, not by a unit-test claim.

## Acceptance criteria

- [ ] **AC-1 — command appears in the palette:** a command titled exactly
      `Itsycal: open calendar folder` is exposed as a top-level palette command with a folder
      icon and the subtitle `Create the calendar folder and open it in File Explorer`, and is
      also reachable from the calendar item's `MoreCommands` (context menu, with the same icon).
      Both entries are present in Release builds, not only in DEBUG. The title and subtitle
      strings are unit-tested; the icon/subtitle presence is confirmed on the installed build in
      manual step 1.
- [ ] **AC-2 — creates and opens the folder:** invoking the command creates
      `%LOCALAPPDATA%\ItsycalForWindows\calendars` when it is missing (idempotent when it already
      exists) and opens that folder in File Explorer.
- [ ] **AC-3 — same folder as the event source:** the command's folder is the folder
      `IcsEventSource` reads (`CalendarFolder.DefaultPath == IcsEventSource.DefaultDirectory`), so
      an `.ics` file dropped there is picked up without further configuration on a normal profile.
      The equality is unit-tested; the packaged-read half is AC-7.
- [ ] **AC-4 — selected-day empty state follows the 0003/0004 contract:** the empty state is two
      separate `TextBlock` elements, not one combined string: the first is exactly
      `No events on this day.` and the second is exactly
      `Calendars folder: %LOCALAPPDATA%\ItsycalForWindows\calendars`. Neither string contains a
      line break; both keep the existing small, subtle styling; the folder pointer stays until the
      rest of ONB-101 ships (it must not be removed by this slice).
      [0003 AC-5](0003-compact-calendar-ui.md) requires one line, `No events on this day.`, plus a
      **one-line** calendars-folder pointer; [0004 AC-7](0004-standalone-compact-flyout.md)
      requires exactly one line plus the same pointer. The installed-build
      check (manual step 2) must confirm each part renders on one visual line at the installed
      palette width. If either part wraps, AC-4 **fails**, and the wrap is a finding against
      0003 AC-5 / 0004 AC-7 (their one-line requirement is unmet), recorded against those specs
      rather than accepted as a rendering note. The two-`TextBlock` structure is unit-tested; the
      one-line rendering is manual.
- [ ] **AC-5 — no network or telemetry:** the command has no HTTP, telemetry or appointment-store
      dependency, and adds no logging of event content; it only resolves and creates a local folder
      and launches File Explorer.
- [ ] **AC-6 — failure is a toast, never a crash:** every failure path returns a
      `CommandResult.ShowToast` and never lets an exception cross the COM boundary:
      - a folder that cannot be created (for example the path exists as a file, or a parent is a
        file) produces the create-failed toast naming the path;
      - a shell launch failure produces a different open-failed toast;
      - an unavailable or invalid profile path produces the unavailable-folder toast.
      `GetPath` returns `string.Empty`, `EnsureCreated`/`TryOpenInExplorer` return `false`, and
      `TryGetDefaultPath` returns `false` instead of throwing on an invalid or unavailable profile
      path; `Invoke` maps those results to toasts without a `try`/`catch` of its own. The
      `TryGetDefaultPath` false branch has no unit test (the profile root is not injectable) and is
      mapped to review and manual step 6; no test coverage is claimed for it.
- [ ] **AC-7 — installed MSIX reads the folder the command opens (manual):** install the signed
      MSIX from CI run
      [42487140544](https://github.com/Folieson/itsycal-for-windows/actions/runs/42487140544)
      (workflow `windows-build`, commit `4d189db`). Download the `itsycal-for-windows-dev`
      artifact from a checkout of this repository, locate
      `ItsycalForWindows_1.2.2.111_x64_Debug.msix` and `devcert.cer` in the extracted directory
      (the artifact's `package-path.txt` records the CI runner path, not a local path), trust the
      certificate, and install:

      ```powershell
      gh run download 42487140544 -R Folieson/itsycal-for-windows -n itsycal-for-windows-dev -D <dir>
      Import-Certificate -FilePath <dir>\devcert.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
      Add-AppxPackage -Path <msix>
      ```

      Use `LocalMachine\TrustedPeople` from an elevated shell as the README documents; if the
      session is not elevated, trust the certificate in `Cert:\CurrentUser\TrustedPeople` instead
      and record the store used.
      If the CI artifact cannot be installed, build a dev MSIX locally with the persistent
      development certificate per `docs/SIGNING.md` and `windows/scripts/deploy-local.ps1`, and
      record the artifact path and SHA-256.
      Run `Itsycal: open calendar folder`, select 2026-11-08, and in the Explorer window it opens
      create exactly `onboarding-check.ics` with this content:

      ```ics
      BEGIN:VCALENDAR
      VERSION:2.0
      PRODID:-//ItsycalForWindows//ONB-101a manual check//EN
      BEGIN:VEVENT
      UID:onb-101a-packaged-read-20261108@itsycal-for-windows
      DTSTAMP:20261107T000000Z
      DTSTART:20261108T120000
      DTEND:20261108T130000
      SUMMARY:ONB-101a packaged read check
      END:VEVENT
      END:VCALENDAR
      ```

      Refresh by restarting Command Palette, or by closing and reopening the calendar page more
      than 30 s after the last load (`CalendarPage.EventsRefreshInterval` is 30 s; a restart
      always reloads). Record `openedPath` (Explorer address bar and the opened toast),
      `documentedPath` (`%LOCALAPPDATA%\ItsycalForWindows\calendars` expanded), `writtenPath`
      (`Get-Item` on the dropped file), and `readPath` (the app-visible path: set it to
      `writtenPath` only after the exact event appears, otherwise record `readPath: unknown`; the
      DEBUG `Itsycal: probe calendar access` command probes the appointment store only and is not
      evidence here).
      **Pass** only if the exact event appears after the refresh and `openedPath`,
      `documentedPath`, `writtenPath` and `readPath` are the same path (case-insensitive,
      normalized). **Fail** otherwise. If MSIX `%LOCALAPPDATA%` virtualisation makes the package
      read a different path, AC-7 fails and the pointer/implementation must change to the path the
      package actually reads; recording the virtualized path as the documented path is not a pass.

## Test plan

Tests live in `windows/tests/ItsycalForWindows.Tests/CalendarFolderTests.cs`. The test file is
written against the post-fix API. Against the pre-fix tree it did not compile (`CS0117` for the
new `TryGetDefaultPath`, `EmptyStateTitle` and `EmptyStatePointer` members), so the compile red
is recorded separately from the behavioral red probe and is not treated as a red assertion.

Behavioral red probe against the pre-fix implementation (recorded separately from the compile
red): `GetPath(" ")` threw `ArgumentException` (the old `EnsureCreated`/`TryOpenInExplorer` had
the same guard), and the old `EmptyStateHint` still contained `Add an .ics file`. After the
implementation the suite passed: 255 tests total, 0 failed, 0 skipped.

| AC | Test (file / name) | Kind |
|---|---|---|
| AC-1 | `CalendarFolderTests.CommandTitle_IsThePaletteCommandName`, `CommandSubtitle_IsASingleLine`; provider wiring inspected in review; installed palette: title, icon, subtitle and context-menu icon in manual step 1 | unit + review + manual |
| AC-2 | `CalendarFolderTests.EnsureCreated_CreatesMissingFolder`, `EnsureCreated_IsIdempotentForAnExistingFolder`; Explorer launch observed on an installed build | unit + manual |
| AC-3 | `CalendarFolderTests.DefaultPath_IsTheFolderIcsEventSourceReads`, `DefaultPath_IsUnderLocalAppData`, `GetPath_ResolvesTheCalendarsFolderUnderLocalAppData` | unit |
| AC-4 | `CalendarFolderTests.EmptyState_IsTheTwoPartContract`, `EmptyState_DoesNotUseTheOldCombinedSentence` (the old-hint regression guard; the pre-fix member was `EmptyStateHint` and the test keeps its existing name — renaming is a code change outside this docs-only revision); two-`TextBlock` wiring in `CalendarPage` inspected in review; installed-build empty-state screenshot and one-line check | unit + review + manual |
| AC-5 | static review: no `HttpClient`/URI/network call and no logging call in `CalendarFolder`, `CalendarFolderCommand`, `CalendarOnboarding`, and no new telemetry setting | review |
| AC-6 | `CalendarFolderTests.EnsureCreated_ReturnsFalseInsteadOfThrowingWhenThePathIsAFile`, `EnsureCreated_ReturnsFalseWhenAParentIsAFile`, `EnsureCreated_ReturnsFalseForAnInvalidPath`, `TryOpenInExplorer_ReturnsFalseForAnInvalidPath`, `GetPath_ReturnsAnEmptyPathForAnInvalidRoot`, `TryGetDefaultPath_ResolvesTheProfileFolder` (true branch only); `Invoke`'s toast mapping and the absent COM-boundary throw inspected in review | unit + review + manual |
| AC-6 (`TryGetDefaultPath` false branch) | no unit test: the profile root is not injectable; mapped to review of `CalendarFolder.TryGetDefaultPath` and manual step 6 | review + manual (not reproducible) |
| AC-2/AC-6 | `CalendarFolderTests.Toasts_NameTheFolder` (toast text stays path-specific) | unit |
| AC-7 | manual: install the AC-7 artifact → run the command → drop the exact AC-7 `.ics` into the opened folder → refresh (restart, or >30 s) → event visible; record `openedPath`/`documentedPath`/`writtenPath`/`readPath` | manual |

### Manual verification (to be recorded by the independent tester on an installed build)

1. Install the signed MSIX from AC-7, open Command Palette, search for `calendar folder`, and run
   `Itsycal: open calendar folder`. Confirm the folder is created, Explorer opens the folder named
   in the toast, and the same command is present in the calendar item's context menu (AC-1,
   AC-2). Record the top-level folder icon and subtitle and the context-menu icon; a blank or
   missing icon fails AC-1.
2. On a clean profile with no appointment-store sources and no other `.ics` files, select a day
   with no events, capture the empty state and read the screenshot back: `No events on this day.`
   in the first element and `Calendars folder: %LOCALAPPDATA%\ItsycalForWindows\calendars` in the
   second (AC-4). Record whether each part stays on one visual line at the installed palette
   width. If either wraps, record **AC-4 fail** and file the finding against 0003 AC-5 / 0004
   AC-7; do not accept the wrap as a rendering note.
3. **MSIX `%LOCALAPPDATA%` virtualisation and packaged read (must be verified; AC-7).** Use the
   AC-7 artifact, select 2026-11-08, run the folder command, and in the Explorer window it opens
   create exactly `onboarding-check.ics` from AC-7 (UID
   `onb-101a-packaged-read-20261108@itsycal-for-windows`, summary `ONB-101a packaged read check`,
   `DTSTART:20261108T120000`, `DTEND:20261108T130000` local time). Record `openedPath`,
   `documentedPath`, `writtenPath` and `readPath` as defined in AC-7. Refresh by restarting
   Command Palette, or by closing and reopening the calendar page more than 30 s after the last
   load (`CalendarPage.EventsRefreshInterval`), and confirm the exact event appears. AC-7 passes
   only when the event appears and all four paths are the same (case-insensitive, normalized);
   otherwise AC-7 fails and the pointer/implementation must change. Do not record a virtualized
   path as the documented path.
4. Folder-creation failure: if a `calendars` folder already exists, remove or rename it first —
   otherwise a file of that name cannot be created. Then create a file named `calendars` at
   `%LOCALAPPDATA%\ItsycalForWindows\calendars` (or make a parent a file), run the command, and
   confirm the create-failed toast names the path; restore the folder afterwards (AC-6).
5. Explorer launch failure: on a normal desktop this cannot be induced safely. The pure half is
   covered by `TryOpenInExplorer_ReturnsFalseForAnInvalidPath`; the shell failure is covered by
   code review of the `catch` in `CalendarFolder.TryOpenInExplorer` (the command itself has no
   `try`/`catch`) and the distinct toast. If a session without Explorer is available (for example
   Windows Server Core), run the command there and record the open-failed toast. Mark this check
   "not run" on a normal desktop rather than claiming it.
6. Missing `%LOCALAPPDATA%`: not reproducible on a normal profile. `GetPath`/`EnsureCreated`/
   `TryOpenInExplorer` invalid-input tests and code review of `CalendarFolder.TryGetDefaultPath`
   cover the defensive branch; `TryGetDefaultPath`'s false branch has no unit test (the profile
   root is not injectable), so record this check as review + not reproducible instead of claiming
   a manual pass.

### Tester evidence (independent tester, installed MSIX)

- **Tester/date:** independent tester, 2026-11-08 (Europe/Moscow). MSIX
  `ItsycalForWindows_1.2.2.111_x64_Debug.msix`, SHA-256
  `F554A4472E2559EDAE61CE64679DADB302884EB44730D2F42D2D141FCB0A8106`, from CI run 42487140544
  (head `4d189db`, `itsycal-for-windows-dev`); signed `CN=ItsycalForWindows-Dev`
  (thumbprint `C4CC57FC…`, trusted in `LocalMachine\TrustedPeople`). Installed with
  `gh run download … -n itsycal-for-windows-dev` + `Add-AppxPackage` over `1.2.2.0`; host
  `Microsoft.CommandPalette_0.13.13096.0`.
- **AC-1 — PASS:** exact-title search returns `Itsycal: open calendar folder` as row 1 with
  folder icon + subtitle; the calendar item's Ctrl+K menu lists it. Screenshots
  `AC1-exact-title-search.png`, `AC1-calendar-item-morecommands.png`. Release presence is
  source-reviewed only because CI packaged a Debug build.
- **AC-2 — PASS:** missing folder was created at
  `%LOCALAPPDATA%\ItsycalForWindows\calendars`, Explorer opened on it, toast named the exact
  path; a second invocation re-opened it without changing the creation time or contents.
  `AC2-explorer-opened.png`, `AC2-opened-toast.png`, `AC2-idempotent-*`.
- **AC-4 — PASS:** two separate lines, `No events on this day.` and
  `Calendars folder: %LOCALAPPDATA%\ItsycalForWindows\calendars`; neither wraps at 800×480
  (`AC4-empty-state.png`). Captured on an event-free day (14 Sept 2026) because the current
  day had demo/appointment events; the "clean profile" wording was not reproduced.
- **AC-6 — PASS (folder-as-file):** replacing the folder with a file produced
  `Could not create the calendar folder …\calendars`; no crash or Application Error.
  Explorer-launch failure and missing `%LOCALAPPDATA%` were **not run** (not reproducible on
  this desktop).
- **AC-7 — PASS:** deterministic `onboarding-check.ics` seeded in the real
  `%LOCALAPPDATA%\ItsycalForWindows\calendars` was shown after a host restart
  (`AC7-ics-event.png`, 08:00–08:30). `openedPath` = `documentedPath` = `writtenPath` =
  `readPath` = `C:\Users\ponam\AppData\Local\ItsycalForWindows\calendars` (case-insensitive,
  normalized); no `LocalCache` virtualized copy exists, so MSIX `%LOCALAPPDATA%`
  virtualization does not block the external-write workflow.
- **Not reproducible:** Explorer-launch failure and missing `%LOCALAPPDATA%` (manual step 6).
- **Design review:** pending (empty-state screenshots above); the code review is recorded
  separately in the Review section.

## Out of scope

This change is **ONB-101a only**. The remaining ONB-101 work is explicitly deferred to follow-up
changes and recorded in [docs/BACKLOG.md](../BACKLOG.md):

- the `.ics` import file picker / drag-and-drop workflow and any import UI;
- per-source status (source name, last read, parse errors);
- an empty-state call-to-action beyond the folder pointer (for example a button that runs the
  command);
- a watched-folder notification or configuring an alternative calendar folder;
- changes to the standalone flyout surface (spec 0004) or to the dock band's event count;
- localization of the new strings; the extension ships English UI strings only;
- changing the folder's location, permissions or cleanup policy.

## Release notes

`CHANGELOG.md` reached this branch through a merge of `origin/master` (`04a525f`), not a rebase.
The ONB-101a `[Unreleased]` entry was added in `4d189db`; this revision moves it under
`### Added`, removes the stray blank line, wraps it to 100 columns, and says the empty day shows
the folder pointer as a separate `TextBlock` (not a separate visual line). It is not left to the
lead, and no conflicting changelog file was added by this branch:

```markdown
- ONB-101a: `Itsycal: open calendar folder` creates and opens the local calendars
  folder from Command Palette; the empty day shows the folder pointer as a separate
  `TextBlock`.
```

## Review

- Spec review round 1 (2026-11-08, independent of the author): **request changes** — (1) the
  empty-state contract conflicted with specs 0003 AC-5 / 0004 AC-7; (2) MSIX
  `%LOCALAPPDATA%` virtualisation was unproven; (3) the scope overstated ONB-101; (4) failure paths
  were under-specified; (5) the CHANGELOG dependency was unrecorded.
- Resolution of round-1 findings, revision 2:
  1. **Empty state:** AC-4 and the code use two separate `TextBlock`s with the exact 0003/0004
     wording; `EmptyState_IsTheTwoPartContract` and `EmptyState_DoesNotUseTheOldCombinedSentence`
     assert the two parts and the absence of the old combined sentence.
  2. **MSIX virtualisation:** manual AC-7 and manual step 3 require an installed-MSIX run with a
     deterministic `.ics` and record the virtualised-path risk instead of assuming external
     writes are visible to the packaged app.
  3. **Scope:** the spec is narrowed to ONB-101a and the remaining ONB-101 work is listed in Out
     of scope; the PR wording is updated to the slice.
  4. **Failure paths:** AC-6 names folder-creation, shell-launch and missing/invalid-profile
     failures; invalid input returns `false`/empty (tests added), and the manual plan covers the
     folder-as-file failure and documents why shell failure and missing `%LOCALAPPDATA%` are not
     reproducible on a normal desktop.
  5. **CHANGELOG:** docs PR #37 has merged into master; master was **merged** into this branch
     (`04a525f`), not rebased. The required ONB-101a `[Unreleased]` entry is present
     (`4d189db`) and revision 3 moves it under `### Added` and wraps it; it is not left to the
     lead. No conflicting changelog file was added.
- Spec review round 2 (2026-11-08, independent of the author): **request changes** —
  documentation/review-record accuracy; the code was consistent. Findings: **B1** release notes
  and review item 5 contradicted HEAD (entry present, branch merged not rebased); **M1** context
  overclaimed that the path was documented only in `windows/README.md` and that the user gets no
  signal; **M2** `TryGetDefaultPath`'s false branch was implied to be tested; **M3** no backlog row
  for ONB-101a; **M4** the 0003/0004 one-line pointer contract was treated as an observation
  rather than a finding; **M5** the behavioral red probe was conflated with the compile red;
  **M6** manual steps 3/4 were not executable (no deterministic VEVENT; the folder is not removed
  first); **M7** missing links to 0003/0004, the backlog, PR/CI and the review record, and the
  gates were unnamed; **M8** "single source of truth" was an overclaim; **M9** scope wording said
  "only adds discoverability". Minor: old-hint test naming, AC-1 icon/subtitle check,
  `installed-MSBX` typo, gate order (tester → design → code), PR test-count phrasing, manual
  step 5 catch location, and the missing icon check.
- Resolution of round-2 findings, revision 3:
  - **B1:** Release notes and round-1 item 5 now state that the entry is present and the branch
    merged master (not rebased); the entry is under `### Added`, without the stray blank line,
    wrapped to 100 columns, and says "separate `TextBlock`". The PR body was corrected in the
    same revision.
  - **M1:** Context now names the root README and the old label as the places that documented the
    path, and states the real gaps: no in-product action, the combined sentence, and retyping the
    path.
  - **M2:** AC-6 and the test table map `TryGetDefaultPath`'s false branch to review + manual (not
    reproducible) and claim no unit-test coverage; the true branch is the only
    `TryGetDefaultPath` test.
  - **M3:** [docs/BACKLOG.md](../BACKLOG.md) gains an ONB-101a row under ONB-101 and records the
    deferred ONB-101 items.
  - **M4:** AC-4 quotes the 0003 AC-5 / 0004 AC-7 one-line requirement and makes a wrap an AC-4
    failure and a finding against those specs.
  - **M5:** the test plan separates the behavioral red probe (invalid-path throw; old
    `EmptyStateHint` text) from the compile red (`CS0117` new members absent) and does not call a
    compile failure a red assertion.
  - **M6:** manual step 3 has the exact VEVENT (UID/summary/time) and the refresh rule; step 4
    removes the existing `calendars` folder before creating a file of that name; AC-7 states
    pass/fail explicitly.
  - **M7:** Links point to 0003/0004, the backlog, PR #42, CI run 42487140544 and the review
    record; the gates are named in order (independent tester → independent design review →
    independent code review) and marked pending.
  - **M8:** the constraint now says "same folder, enforced by the equality test" instead of
    "single source of truth".
  - **M9:** scope says the change "adds the folder command and aligns the empty state", not "only
    adds discoverability".
  - **Minor:** the old-hint regression is referenced by its exact test name
    `EmptyState_DoesNotUseTheOldCombinedSentence` (renaming it is a code change outside this
    docs-only revision); AC-1 and manual step 1 require the icon and subtitle check;
    "installed-MSBX" is fixed; the gates are ordered tester → design → code; the test count is
    phrased "255 total, 0 failed, 0 skipped"; manual step 5 names the `catch` in
    `CalendarFolder.TryOpenInExplorer`; the icon check is explicit.
- Tester (2026-11-08, independent, installed MSIX from CI run 42487140544): **PASS** on all
  testable ACs; AC-5 by static review; Explorer-launch failure and missing `%LOCALAPPDATA%`
  not run. Evidence is recorded in "Tester evidence" above.
- Design review: **pending** (empty-state screenshots from the tester; the flyout design gate
  is separate).
- Code review (2026-11-08, independent): **approve with notes** on the implementation before
  this docs-only revision — no blocking/major defects; notes on DefaultPath duplication, an
  empty-state test asserting only part of the path, untested toast mapping (review/manual),
  and README wording. None of the notes changed code; the code diff is unchanged by this
  revision.
- Verdict: **pending final spec re-review** — a reviewer independent of the author must
  record `approve` / `approve with notes` before merge. This row is not an author approval.
- Gates (in order): independent tester **PASS**; independent design review **pending**;
  independent code review **approve with notes**; final spec re-review **pending**.
