# 0005 — Calendar folder onboarding (Command Palette)

Status: proposed — first implementation in this branch; independent spec review pending.
Backlog: ONB-101.
Related: [ADR 0002](../adr/0002-calendar-providers-and-network.md) (calendar providers and
network). The selected-day empty-state slot is defined by the calendar UI specs 0003 (compact
calendar, Command Palette surface) and 0004 (standalone flyout) on their own, unmerged branch;
this spec only adds first-run discoverability to the existing Command Palette extension.

## Context

The extension reads local `.ics` exports from `%LOCALAPPDATA%\ItsycalForWindows\calendars`
(`IcsEventSource.DefaultDirectory`). The folder is created lazily on the first calendar read,
but a user who installs the extension has no in-product way to find it: the folder path is
documented only in `windows/README.md` and in source comments. A first-run user with an empty
calendar therefore has to open File Explorer and type the path by hand, and has no signal that
dropping an `.ics` file there is the intended workflow.

Current behaviour: the selected-day empty state (`CalendarPage.NoEventsLabel`) already names
the folder as a literal string, but it does not name any palette command, and no palette
command opens or creates the folder. The `Itsycal: check for updates` / `Itsycal: install
update` commands show the palette command pattern this change reuses.

Proposal: one palette command, `Itsycal: open calendar folder`, exposed both as a top-level
command and in the calendar item's context menu. It creates the folder when missing and opens
it in File Explorer, and reports the outcome in a toast. The selected-day empty state names
both the folder and the command in one quiet line.

Constraints:

- The command must use the same folder the ICS provider reads, so there is a single source of
  truth (`IcsEventSource.DefaultDirectory`).
- No network access or telemetry. The command only touches the local file system and
  `explorer.exe`; no event content is logged.
- The command runs across the extension's COM boundary: a failure must surface as a toast,
  never as an exception.
- The empty state is a small, subtle `TextBlock` under the agenda; it must stay a single line
  of guidance, not become a paragraph.

## Acceptance criteria

- [ ] **AC-1 — command appears in the palette:** a command titled exactly
      `Itsycal: open calendar folder` is exposed as a top-level palette command with an icon
      and a subtitle, and is also reachable from the calendar item's `MoreCommands`
      (context menu). Both entries are present in Release builds, not only in DEBUG.
- [ ] **AC-2 — creates and opens the folder:** invoking the command creates
      `%LOCALAPPDATA%\ItsycalForWindows\calendars` when it is missing (idempotent when it
      already exists) and opens that folder in File Explorer.
- [ ] **AC-3 — same folder as the event source:** the command's folder is the folder
      `IcsEventSource` reads (`CalendarFolder.DefaultPath == IcsEventSource.DefaultDirectory`),
      so an `.ics` file dropped there is picked up without further configuration.
- [ ] **AC-4 — selected-day empty state:** the empty state names the folder and the command,
      is a single line (no line breaks), is at most 160 characters, and is still rendered as
      the existing small, subtle `TextBlock` under the agenda heading.
- [ ] **AC-5 — no network or telemetry:** the command has no HTTP, telemetry or appointment
      store dependency, and adds no logging of event content; it only resolves and creates a
      local folder and launches File Explorer.
- [ ] **AC-6 — failure is a toast, never a crash:** a folder that cannot be created (for
      example the path exists as a file) produces a `CommandResult.ShowToast` result naming
      the path; a shell-launch failure produces a different toast. The folder helper reports
      failure as `false` instead of throwing, and `Invoke` catches path-resolution failures.

## Test plan

Tests in `windows/tests/ItsycalForWindows.Tests/CalendarFolderTests.cs`; they must fail before
the implementation and pass after it.

| AC | Test (file / name) | Kind |
|---|---|---|
| AC-1 | `CalendarFolderTests.CommandTitle_IsThePaletteCommandName`, `CommandSubtitle_IsASingleLine`; provider wiring inspected in review; search the installed palette for the command | unit + review + manual |
| AC-2 | `CalendarFolderTests.EnsureCreated_CreatesMissingFolder`, `EnsureCreated_IsIdempotentForAnExistingFolder`; Explorer launch observed on an installed build | unit + manual |
| AC-3 | `CalendarFolderTests.DefaultPath_IsTheFolderIcsEventSourceReads`, `DefaultPath_IsUnderLocalAppData`, `GetPath_ResolvesTheCalendarsFolderUnderLocalAppData`, `GetPath_RejectsAnEmptyRoot` | unit |
| AC-4 | `CalendarFolderTests.EmptyState_MentionsTheFolderAndTheCommandOnOneLine` | unit |
| AC-5 | static review: no `HttpClient`/URI/network call and no logging call in `CalendarFolder`, `CalendarFolderCommand`, `CalendarOnboarding`, and no new telemetry setting | review |
| AC-6 | `CalendarFolderTests.EnsureCreated_ReturnsFalseInsteadOfThrowingWhenThePathIsAFile`; `Invoke`'s catch and toast mapping inspected in review | unit + review |
| AC-2/AC-6 | `CalendarFolderTests.Toasts_NameTheFolder` (toast text stays path-specific) | unit |

Manual verification (pending, to be recorded by the independent tester on an installed build):
install the MSIX, search the palette for `calendar folder`, run it with the folder deleted
(the folder is created and Explorer opens), then delete the folder and observe the empty state
on the selected day. This was not run while writing the implementation; the unit tests above
are the local evidence, and the visual acceptance check remains open.

## Out of scope

- A file picker or `.ics` import UI, a watched-folder notification, or configuring an
  alternative calendar folder.
- Changes to the standalone flyout surface (spec 0004) or to the dock band's event count.
- Localization of the new strings; the extension ships English UI strings only.
- Changing the folder's location, permissions or cleanup policy.

## Review

- Spec reviewer: <!-- pending; the author must not fill this row -->
- Verdict: <!-- approve / approve with notes / request changes -->
- Date:
- Blocking findings resolved:

Implementation note: this change was implemented before the independent spec review, at the
lead's direction, so the review above is open and the acceptance criteria are not yet accepted.
