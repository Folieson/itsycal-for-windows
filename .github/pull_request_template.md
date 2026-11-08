## What and why

<!-- One paragraph. Link the issue and the spec in docs/specs/ if there is one. -->

## Spec and tests (TDD)

- Spec: <!-- docs/specs/NNNN-... or "no behaviour change: ..." -->
- [ ] Every acceptance criterion in the spec maps to a test (or a documented manual check).
- [ ] The new tests cover the acceptance criteria; when the local toolchain allows, a red run is attached, otherwise the PR says why CI-only was used.
- [ ] CI coverage floor passes and coverage did not decrease in the touched files.

## Roles and independent reviews

- PO / analyst: <!-- who owned scope and authored the spec -->
- Developer: <!-- implementer -->
- Tester verification: <!-- role/agent, date, verdict, evidence -->
- Design review (UI changes): <!-- role/agent, date, verdict, screenshot paths -->
- Spec review: <!-- reviewer/agent, date, verdict, link -->
- Code review: <!-- reviewer/agent, date, verdict, link -->
- [ ] The tester, design reviewer (UI) and both reviewers were different agents from the author and from each other.
- [ ] Every blocking finding was resolved or explicitly deferred with the user's agreement.
## How verified

<!-- Exact commands and results, not "it builds". -->
- `dotnet build windows/ItsycalForWindows.sln -c Debug -p:Platform=x64` — exit code:
- `dotnet test windows/tests/ItsycalForWindows.Tests.csproj -c Debug --collect:"XPlat Code Coverage" --settings windows/tests/coverlet.runsettings` — exit code / line coverage:
- Other checks (installer, Command Palette behaviour):

## Risk and rollback

- Risk:
- Rollback:

## Checklist

- [ ] Branch is based on current `master`; the PR diff contains only this change (no stacked branches).
- [ ] Changes stay inside `windows/` (or the matching docs); no macOS sources reintroduced.
- [ ] CLSID is unchanged in `ItsycalForWindows.cs` and `Package.appxmanifest`.
- [ ] No telemetry; network usage matches `docs/adr/0002-calendar-providers-and-network.md`; event content is not logged.
- [ ] New NuGet packages are recorded in `windows/THIRD-PARTY-NOTICES.md`.
- [ ] No `bin/`, `obj/`, `publish/`, certificates or packages in the diff.
- [ ] README/ADR/spec updated when behaviour or a decision changed; user-visible changes have a CHANGELOG [Unreleased] entry.
