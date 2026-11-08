# 0005 — ONB-101a: open calendar folder (Command Palette)

Status: revision 2 — the first independent spec review returned **request changes**; this revision
resolves the five findings below and a re-review verdict is pending. The implementation is in this
branch but is not accepted until that verdict is recorded.

Backlog: **ONB-101a**, the folder-command slice of ONB-101. The rest of ONB-101 (`.ics` import,
source status, empty-state call-to-action) is deliberately out of scope and listed below.

Related: [ADR 0002](../adr/0002-calendar-providers-and-network.md) (calendar providers and
network). The selected-day empty-state contract is owned by the calendar UI specs 0003 (compact
calendar, Command Palette surface) and 0004 (standalone flyout) on their own, unmerged branch; this
spec only adds first-run discoverability to the existing Command Palette extension and reuses that
contract unchanged.

## Context

The extension reads local `.ics` exports from `%LOCALAPPDATA%\ItsycalForWindows\calendars`
(`IcsEventSource.DefaultDirectory`). The folder is created lazily on the first calendar read, but a
user who installs the extension has no in-product way to find it: the folder path is documented only
in `windows/README.md` and in source comments. A first-run user with an empty calendar therefore has
to open File Explorer and type the path by hand, and gets no signal that dropping an `.ics` file
there is the intended workflow.

Current behaviour: the selected-day empty state (`CalendarPage`) renders one combined sentence that
mixes `No events on this day.` with an `.ics` instruction. UI specs 0003 (AC-5) and 0004 (AC-7)
already define a different, accepted shape: the exact sentence `No events on this day.` plus a
separate short calendars-folder pointer, retained until ONB-101 ships the full onboarding. This
change aligns the extension with that contract instead of keeping a competing sentence; specs
0003/0004 describe the pointer as one line, and the installed-build check below is what proves the
rendering rather than a unit test.

Proposal: one palette command, `Itsycal: open calendar folder`, exposed both as a top-level command
and in the calendar item's context menu. It creates the folder when missing and opens it in File
Explorer, and reports the outcome in a toast. The selected-day empty state becomes two separate
`TextBlock`s: the contract sentence and the folder pointer.

Constraints:

- The command must use the same folder the ICS provider reads, so there is a single source of truth
  (`IcsEventSource.DefaultDirectory` / `CalendarFolder.DefaultPath`).
- No network access or telemetry. The command only touches the local file system and
  `explorer.exe`; no event content is logged.
- The command runs across the extension's COM boundary: a failure must surface as a toast, never as
  an exception. The pure helpers report failure as `false`; `Invoke` has no throwing path.
- The empty state is a small, subtle block under the agenda. It must stay two short parts, not
  become a setup paragraph. Each part is a separate `TextBlock`; whether each part renders on one
  visual line is an installed-build manual check, not a unit-test claim.

## Acceptance criteria

- [ ] **AC-1 — command appears in the palette:** a command titled exactly
      `Itsycal: open calendar folder` is exposed as a top-level palette command with an icon and a
      subtitle, and is also reachable from the calendar item's `MoreCommands` (context menu). Both
      entries are present in Release builds, not only in DEBUG.
- [ ] **AC-2 — creates and opens the folder:** invoking the command creates
      `%LOCALAPPDATA%\ItsycalForWindows\calendars` when it is missing (idempotent when it already
      exists) and opens that folder in File Explorer.
- [ ] **AC-3 — same folder as the event source:** the command's folder is the folder
      `IcsEventSource` reads (`CalendarFolder.DefaultPath == IcsEventSource.DefaultDirectory`), so
      an `.ics` file dropped there is picked up without further configuration on a normal profile.
- [ ] **AC-4 — selected-day empty state:** the empty state is two separate `TextBlock`s, not one
      combined sentence:
      the first line is exactly `No events on this day.` and the second line is exactly
      `Calendars folder: %LOCALAPPDATA%\ItsycalForWindows\calendars`.
      Neither string contains a line break; both keep the existing small, subtle styling. The
      folder pointer stays until the rest of ONB-101 ships (it must not be removed by this slice).
      This spec does **not** claim that each part renders on one visual line: that is a manual
      check on an installed build (the host may wrap a `TextBlock`), and it is not asserted by
      unit tests.
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
      path; `Invoke` maps those results to toasts without a `try`/`catch` of its own.
- [ ] **AC-7 — installed MSIX reads the folder the command opens (manual):** on an installed MSIX
      build, run `Itsycal: open calendar folder`, drop a deterministic `.ics` file into the folder
      File Explorer opened, refresh the calendar, and confirm the packaged app reads that file and
      shows its event. Until this is observed, the unit tests prove only the helper logic, not
      packaged-app file access.

## Test plan

Tests in `windows/tests/ItsycalForWindows.Tests/CalendarFolderTests.cs`; they must fail before the
implementation and pass after it. The revision-2 red probe against the pre-fix implementation failed
on both new contracts: `GetPath(" ")`/`EnsureCreated(" ")`/`TryOpenInExplorer(" ")` threw
`ArgumentException`, and `EmptyStateHint` still contained the old `Add an .ics file` sentence.
After the implementation the full suite passes 255/255.

| AC | Test (file / name) | Kind |
|---|---|---|
| AC-1 | `CalendarFolderTests.CommandTitle_IsThePaletteCommandName`, `CommandSubtitle_IsASingleLine`; provider wiring inspected in review; search the installed palette for the command | unit + review + manual |
| AC-2 | `CalendarFolderTests.EnsureCreated_CreatesMissingFolder`, `EnsureCreated_IsIdempotentForAnExistingFolder`; Explorer launch observed on an installed build | unit + manual |
| AC-3 | `CalendarFolderTests.DefaultPath_IsTheFolderIcsEventSourceReads`, `DefaultPath_IsUnderLocalAppData`, `GetPath_ResolvesTheCalendarsFolderUnderLocalAppData` | unit |
| AC-4 | `CalendarFolderTests.EmptyState_IsTheTwoPartContract`, `EmptyState_DoesNotUseTheOldCombinedSentence`; two-`TextBlock` wiring in `CalendarPage` inspected in review; installed-build empty-state screenshot | unit + review + manual |
| AC-5 | static review: no `HttpClient`/URI/network call and no logging call in `CalendarFolder`, `CalendarFolderCommand`, `CalendarOnboarding`, and no new telemetry setting | review |
| AC-6 | `CalendarFolderTests.EnsureCreated_ReturnsFalseInsteadOfThrowingWhenThePathIsAFile`, `EnsureCreated_ReturnsFalseWhenAParentIsAFile`, `EnsureCreated_ReturnsFalseForAnInvalidPath`, `TryOpenInExplorer_ReturnsFalseForAnInvalidPath`, `GetPath_ReturnsAnEmptyPathForAnInvalidRoot`, `TryGetDefaultPath_ResolvesTheProfileFolder`; `Invoke`'s toast mapping and the absent COM-boundary throw inspected in review | unit + review + manual |
| AC-2/AC-6 | `CalendarFolderTests.Toasts_NameTheFolder` (toast text stays path-specific) | unit |
| AC-7 | manual: install MSIX → run the command → drop a deterministic `.ics` into the opened folder → refresh → event visible; record both the Explorer path and the path the package read | manual |

### Manual verification (to be recorded by the independent tester on an installed build)

1. Install the signed MSIX, open Command Palette, search for `calendar folder`, and run
   `Itsycal: open calendar folder`. Confirm the folder is created, Explorer opens the folder shown
   in the toast, and the same command is present in the calendar item's context menu (AC-1, AC-2).
2. On a clean profile with no appointment-store sources and no other `.ics` files, with no events
   for the selected day, capture the empty state and read the screenshot back:
   `No events on this day.` on the first line and
   `Calendars folder: %LOCALAPPDATA%\ItsycalForWindows\calendars` on the second (AC-4). Record
   whether each part stays on one visual line at the installed palette width; if the host wraps it,
   record that instead of claiming a single line.
3. **MSIX `%LOCALAPPDATA%` virtualisation (must be verified).** MSIX can redirect
   `%LOCALAPPDATA%` writes into the package's virtualised store, so a file written by an external
   process may not be the file the packaged app reads. Create a deterministic
   `onboarding-check.ics` with one fixed event on the selected day, drop it into the folder File
   Explorer opened, refresh the calendar, and confirm the event appears (AC-7). Record both the
   Explorer path and the path the packaged app actually read. If they differ, the command/pointer
   is not acceptance evidence until the app reads the seeded path; do not "fix" the observation by
   seeding the virtualised path without documenting it.
4. Folder-creation failure: create a file named `calendars` at
   `%LOCALAPPDATA%\ItsycalForWindows\calendars` (or make a parent a file), run the command, and
   confirm the create-failed toast names the path; restore the folder afterwards (AC-6).
5. Explorer launch failure: on a normal desktop this cannot be induced safely. The pure half is
   covered by `TryOpenInExplorer_ReturnsFalseForAnInvalidPath`; the shell failure is covered by
   code review of the `catch` and the distinct toast. If a session without Explorer is available
   (for example Windows Server Core), run the command there and record the open-failed toast.
   Mark this check "not run" on a normal desktop rather than claiming it.
6. Missing `%LOCALAPPDATA%`: not reproducible on a normal profile. `GetPath`/`EnsureCreated`/
   `TryOpenInExplorer` invalid-input tests and code review of `TryGetDefaultPath` cover the
   defensive branch; record the check as not reproducible instead of claiming a manual pass.

## Out of scope

This change is **ONB-101a only**. The remaining ONB-101 work is explicitly deferred to follow-up
changes:

- the `.ics` import file picker / drag-and-drop workflow and any import UI;
- per-source status (source name, last read, parse errors);
- an empty-state call-to-action beyond the folder pointer (for example a button that runs the
  command);
- a watched-folder notification or configuring an alternative calendar folder;
- changes to the standalone flyout surface (spec 0004) or to the dock band's event count;
- localization of the new strings; the extension ships English UI strings only;
- changing the folder's location, permissions or cleanup policy.

Release notes: this branch intentionally adds no `CHANGELOG.md` entry. The changelog is introduced
by docs PR #37, which is not merged yet; after #37 merges, rebase this branch and add the
`[Unreleased]` row in the same change that lands ONB-101a. Until then the branch contains no
conflicting changelog file.

## Review

- Spec reviewer round 1 (2026-11-08, independent of the author): **request changes** — (1) the
  empty-state contract conflicted with specs 0003 AC-5 / 0004 AC-7; (2) MSIX
  `%LOCALAPPDATA%` virtualisation was unproven; (3) the scope overstated ONB-101; (4) failure paths
  were under-specified; (5) the CHANGELOG dependency was unrecorded.
- Resolution of round-1 findings, revision 2:
  1. **Empty state:** AC-4 and the code now use two separate `TextBlock`s with the exact 0003/0004
     wording (`No events on this day.` plus `Calendars folder: %LOCALAPPDATA%\ItsycalForWindows\calendars`);
     `EmptyState_IsTheTwoPartContract` and `EmptyState_DoesNotUseTheOldCombinedSentence` assert the
     two parts and the absence of the old combined sentence. No single-line claim is made; the
     one-line rendering check is manual (AC-4, manual step 2).
  2. **MSIX virtualisation:** new manual AC-7 and manual step 3 require an installed-MSBX run with a
     deterministic `.ics`, and record the virtualised-path risk instead of assuming external writes
     are visible to the packaged app.
  3. **Scope:** the spec is narrowed to ONB-101a and the remaining ONB-101 work is listed in Out of
     scope; the PR wording is updated to the slice.
  4. **Failure paths:** AC-6 now names folder-creation, shell-launch and missing/invalid-profile
     failures; invalid input returns `false`/empty (tests added), and the manual plan covers the
     folder-as-file failure and documents why shell failure and missing `%LOCALAPPDATA%` are not
     reproducible on a normal desktop.
  5. **CHANGELOG:** the spec and PR state that the `[Unreleased]` entry lands after docs PR #37
     merges; no conflicting changelog file is added and the branch stays ready to rebase.
- Verdict: **pending re-review** — the round-1 reviewer or a different independent reviewer must
  record `approve` / `approve with notes` before merge. This row is not an author approval.
- Code review: pending (independent of the author, after the tester).
- Design review: required for the empty-state screenshot above, after the functional check and
  before code review.
