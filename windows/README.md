# Itsycal for Windows РІР‚вЂќ extension source

This folder contains the Windows product: a PowerToys Command Palette extension written in
C#/.NET 10. It contributes a dock band with the current date and a flyout month calendar
with read-only events from the Windows appointment store.

## Prerequisites

| Tool | Purpose |
|---|---|
| .NET 10 SDK (10.0.x) | build, tests, packaging |
| Windows SDK 10.0.26100 | CsWinRT projection, MSIX packaging, `signtool` |
| PowerToys 0.102+ | Command Palette host |

A code-signing certificate is only needed to produce a trusted release package; published development
releases are signed with the persistent development certificate (`ItsycalForWindows-Dev`); a
local `Build-Release.ps1 -SelfSigned` build uses an ephemeral certificate, and a trusted
code-signing certificate is only required for a public trusted release. See `docs/SIGNING.md` at the repository
root.

## Build and test

```powershell
# from the repository root
dotnet build windows/ItsycalForWindows.sln -c Debug -p:Platform=x64
dotnet test  windows/tests/ItsycalForWindows.Tests.csproj -c Debug
```

The default configuration builds the **MSIX development package** (useful for local
installation and debugging).

## Release packages (signed MSIX)

Public releases are signed, self-contained MSIX packages РІР‚вЂќ the only form Command Palette
discovers. Build them with the release script from the repository root:

```powershell
# self-signed development packages (local testing only; users must trust the .cer)
.\windows\scripts\Build-Release.ps1 -Version 1.0.0 -SelfSigned

# trusted release build (the only kind acceptable for WinGet)
.\windows\scripts\Build-Release.ps1 -Version 1.0.0 `
  -CertificatePath .\itsycal-signing.pfx -CertificatePassword $env:TRUSTED_SIGNING_PFX_PASSWORD
```

The script builds each architecture (`x64`, `arm64` by default) with
`GenerateAppxPackageOnBuild=true` and `AppxPackageSigningEnabled=false`, signs the package
with `signtool`, verifies trusted signatures, and writes everything to
`windows/artifacts/release/<version>/`:

- `ItsycalForWindows_<package-version>_<arch>.msix` РІР‚вЂќ the signed package,
- `ItsycalForWindows_<package-version>_dev.cer` РІР‚вЂќ public development certificate
  (self-signed builds only),
- `SHA256SUMS.txt` РІР‚вЂќ hashes of every asset,
- `build-info.json` РІР‚вЂќ package identity and per-architecture `sha256`/`signatureSha256`
  values used to fill in the WinGet manifest.

`-Version` is the marketing version (`1.0.0` maps to the identity version `1.0.0.0`). The
script stamps `Package.appxmanifest` and the project metadata to the derived version before
packaging, so there is no separate manifest bump to forget. Certificate options, required
GitHub secrets, and the WinGet submission steps are in
[docs/SIGNING.md](../docs/SIGNING.md).

The release pipeline (`.github/workflows/release.yml`) runs this script on a `v*` tag. With
`TRUSTED_SIGNING_PFX_BASE64`/`TRUSTED_SIGNING_PFX_PASSWORD` set it publishes a signed release; without
them it publishes a self-signed development prerelease and skips the WinGet submission.

## Package versioning

The identity version is derived, never hand-maintained:

- **Release**: the tag is the source of truth. `v1.2.3` becomes package version `1.2.3.0`;
  the release workflow stamps the manifest, the project metadata and the WinGet manifests
  before packaging.
- **Development build**: `.github/workflows/windows-build.yml` stamps the revision from the
  GitHub run number (`<newest release X.Y.Z>.<run+1>`), so every artifact is newer than the previous one and
  `Add-AppxPackage` upgrades in place instead of failing with `0x80073CFB`.
- **Local**: `.\windows\scripts\Set-PackageVersion.ps1 -Version 1.2.3.0` (or
  `-Revision 42` to keep the current base) before building.

The committed manifest keeps the current release base; CI stamps a copy in the runner's
workspace and never commits it.

## Install a development build (MSIX)

MSIX sideloading trust is machine-wide, so the development installer must run elevated:

```powershell
# 1. download the artifact from the latest CI run
gh run download <run-id> -n itsycal-for-windows-dev -D .\pkg

# 2. install (elevated PowerShell)
.\windows\scripts\deploy-local.ps1 -MsixPath .\pkg\ItsycalForWindows_*.msix -CertPath .\pkg\devcert.cer
```

Stop **Command Palette** (and its extension process) before installing an upgrade: the running\nhost holds the package files and deployment fails with `0x80073D02`. Once stopped, a newer\nbuild installs over the previous one in place.\n\nThe development certificate is the repository's persistent development certificate; trust
its `.cer` once (see the root README) and then every CI build installs and upgrades in
place, because each build carries a higher package revision.

## Source map

| Path | Purpose |
|---|---|
| `src/ItsycalForWindows/Program.cs` | Out-of-process COM server host. Do not change the hosting pattern. |
| `src/ItsycalForWindows/ItsycalForWindows.cs` | `IExtension` implementation; carries the CLSID. |
| `src/ItsycalForWindows/ItsycalForWindowsCommandsProvider.cs` | Commands, dock band, cached page instance. |
| `src/ItsycalForWindows/Models/` | Calendar month grid, week rows, day/event models (pure logic). |
| `src/ItsycalForWindows/Pages/` | Dock item and Adaptive Card calendar flyout. |
| `src/ItsycalForWindows/Services/` | Appointment-store access and event range helpers. |
| `tests/` | Unit tests for the pure logic. |
| `scripts/` | Development tooling: MSIX install script, release build/sign script, icon generation. |
| `Package.appxmanifest` | MSIX identity, COM server, CmdPal app extension, `appointmentsSystem` capability. |

## Events and privacy

Events come from `Windows.ApplicationModel.Appointments` in read-only mode
(`AppointmentStoreAccessType.AllCalendarsReadOnly`). The extension never writes to the
calendar and logs no event content. Network access is limited to opt-in self-update checks and
future user-configured providers (ADR 0002). Without the capability, a
packaged run simply shows no events; the calendar still works.

## Troubleshooting

- **Band not visible** РІР‚вЂќ enable it in Command Palette РІвЂ вЂ™ Dock settings; the host persists the
  selection per user.
- **Extension not listed** РІР‚вЂќ restart Command Palette (or `Reload` РІвЂ вЂ™ "Reload Command Palette
  extensions"). If it is still missing, confirm the MSIX is installed
  (`Get-AppxPackage ItsycalForWindows`) and check the package's app-extension registration in
  `Package.appxmanifest`.
- **Install fails with a trust or signature error** РІР‚вЂќ the package was signed with a
  certificate this machine does not trust. Trust the matching `.cer`
  (`Import-Certificate -FilePath <dev>.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople`
  from an elevated shell), or install a release signed with a trusted certificate.
- **No events** РІР‚вЂќ the appointment store is empty, no calendar account is configured, or the
  extension is running outside its package identity (for example a plain `dotnet run`), in
  which case the `appointmentsSystem` capability is not granted and the calendar shows
  without events.
