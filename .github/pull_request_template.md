## What and why

<!-- One paragraph. Link the issue and the spec in docs/specs/ if there is one. -->

## Spec and tests (TDD)

- Spec: <!-- docs/specs/NNNN-... or "no behaviour change: ..." -->
- [ ] Every acceptance criterion in the spec maps to a test (or a documented manual check).
- [ ] The new tests failed before the implementation (show the red run locally or in a draft PR).
- [ ] CI coverage floor passes and coverage did not decrease in the touched files.

## Independent reviews

- Spec review: <!-- reviewer/agent, date, verdict, link -->
- Code review: <!-- reviewer/agent, date, verdict, link -->
- [ ] Neither reviewer was the author; spec and code were reviewed by separate agents.
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

- [ ] Changes stay inside `windows/` (or the matching docs); no macOS sources reintroduced.
- [ ] CLSID is unchanged in `ItsycalForWindows.cs` and `Package.appxmanifest`.
- [ ] No telemetry; network usage matches `docs/adr/0002-calendar-providers-and-network.md`; event content is not logged.
- [ ] New NuGet packages are recorded in `windows/THIRD-PARTY-NOTICES.md`.
- [ ] No `bin/`, `obj/`, `publish/`, certificates or packages in the diff.
- [ ] README/ADR/spec updated when behaviour or a decision changed.
