# AGENTS.md

Contributor rules for AI coding agents working in this repository. This is the Windows-only
repository of **Itsycal for Windows**, a PowerToys Command Palette extension; it is derived
from the MIT-licensed upstream [ItsyCal](https://github.com/sfsam/itsycal) macOS project,
which is not part of this repository anymore.

## Layout

- `windows/` — the Windows extension: C#/.NET 10, Command Palette dock band + calendar.
- `windows/src/ItsycalForWindows/` — extension source (`Program.cs`, provider, `Models/`,
  `Pages/`, `Services/`, package assets).
- `windows/tests/` — unit tests for the pure logic (calendar grid, event ranges).
- `windows/scripts/` — development tooling: `deploy-local.ps1` (MSIX dev install),
  `Build-Release.ps1` (signed release assets), `New-BrandAssets.ps1`.
- `winget/` — WinGet manifests for the MSIX release (`InstallerType: msix`).
- `docs/` — architecture decision records and `docs/SIGNING.md`.

## Hard rules

- All behaviour changes go in `windows/`. Do not reintroduce macOS sources; refer to
  upstream by link when behaviour parity matters.
- The extension is an out-of-process COM server. Keep `Program.cs`'s
  `-RegisterProcessAsComServer` branch intact; the CLSID must stay identical in
  `ItsycalForWindows.cs` and `Package.appxmanifest` (`com:Class Id`, `CreateInstance
  ClassId`).
- Never commit `bin/`, `obj/`, generated packages, certificates, `*.msix`, `publish/`, or
  `windows/artifacts/` output.
- Do not add telemetry. Network access is limited to the self-update check and to calendar
  providers the user explicitly enables, as decided in `docs/adr/0002-calendar-providers-and-network.md`.
  Calendar data never leaves the machine except through a provider the user configured, and
  event content is never logged.
- Ask before changing the MSIX identity (`Name`, `Publisher`, version), the WinGet package
  identifier, or the signing story — all are release-sensitive.
- Verify before reporting: a build/`dotnet test` exit code is evidence; "should work" is
  not. For UI claims, state what was actually observed.

## Spec-first and TDD

Behaviour changes start from a spec, not from code:

1. Write or update `docs/specs/NNNN-<slug>.md` (template: `docs/specs/TEMPLATE.md`): context,
   acceptance criteria, and the test plan that maps every criterion to a test or a documented
   manual check. Cross-cutting decisions still go in `docs/adr/`.
2. Write the failing tests for the acceptance criteria first. When the local toolchain allows,
   attach the red run; when it does not (offline restore, CI-only environment), CI is the
   first run and the PR says why. A change is not ready while a criterion has no test.
3. Keep pure decision logic in testable helpers; network/UI edges get a structural test or a
   manual verification recorded in the PR/checkpoint.
4. CI is the gate: build, the full test suite with coverage, the coverage floor and MSIX
   packaging must be green before merge. Quote the CI run in the PR.

## Roles and flow

Behaviour changes follow the team role model:

`product owner` (scope/priority) → `analyst` (spec in `docs/specs/`) → **independent
spec review** → `developer` (tests first) → **independent tester verification** →
**independent code review** → `lead` merges.

- The user is the product owner; scope or priority changes need the user's decision, not
  the agent's.
- The analyst authors the spec; the spec-reviewer is a different agent. The developer
  implements only an accepted spec. The tester is read-only and is a different agent from
  the developer and both reviewers. The code-reviewer checks the diff against the accepted
  spec and the CI evidence.
- Bug fixes and docs-only changes may skip the PO/analyst artifacts, but a behaviour
  change still needs a regression test and the independent code review.

## Independent reviews

Behaviour changes pass two independent, read-only reviews. The author never reviews their own
change, and the two roles are separate agents/sessions:

1. **Spec review** — after the spec is written and before implementation. The
   `spec-reviewer` role audits acceptance criteria, their test mapping, edge/security/privacy
   cases and scope; implementation starts only after `approve` or `approve with notes`.
2. **Code review** — before merge. The `code-reviewer` role checks the diff against the
   accepted spec, the tests and the CI run/coverage. It must not be the author and must not be
   the agent that reviewed the spec.

Record both verdicts (reviewer, date, verdict, link or quoted summary) in the PR under
"Independent reviews". Every blocking finding is resolved before merge or explicitly deferred
with the user's agreement. The Lead merges only with both verdicts and green checks.

## Build and verify

```powershell
dotnet build windows/ItsycalForWindows.sln -c Debug -p:Platform=x64
dotnet test windows/tests/ItsycalForWindows.Tests.csproj -c Debug --collect:"XPlat Code Coverage" --settings windows/tests/coverlet.runsettings
```

CI (`.github/workflows/windows-build.yml`) runs the same test command, enforces a line
coverage floor from the Cobertura report and uploads the report as an artifact; MSIX packaging runs before the tests and signing runs after them.

Public releases are signed MSIX packages built per architecture by
`windows/scripts/Build-Release.ps1`. Command Palette discovers extensions only through the
MSIX app-extension catalog, so there is no unpackaged build — see `windows/README.md` and
`docs/SIGNING.md`. The release pipeline is `.github/workflows/release.yml` (tag `v*`); CI
builds, packages and tests every PR.

## Branching

- Start every branch from a freshly fetched `master`: `git switch master && git pull --ff-only && git switch -c <type>/<slug>`. Never branch from another feature branch.
- Before opening a PR, run `git diff master...HEAD --stat` and confirm it contains only this change. If another PR's commits appear, the branch is stacked: recreate it from `master` and cherry-pick only its own commits (`git rebase --onto master <other-branch> <your-branch>`), then re-verify.
- Never stack branches.

## Changelog

Every user-visible change adds a line under `[Unreleased]` in `CHANGELOG.md` (Keep a
Changelog format). A release tag requires a `## [X.Y.Z]` section; the release workflow fails
without it and uses the section as the GitHub release "What's changed" body.

## Commits and PRs

Conventional Commits (`feat(windows): ...`, `fix(windows): ...`, `docs: ...`), one logical
change per PR, PR description with the exact verification commands and results plus the spec
link. Keep `docs/adr/` up to date when a decision changes, and `docs/specs/` when behaviour
changes.

## Decision records

Architecture decisions live in `docs/adr/` (`NNNN-kebab-title.md`).
