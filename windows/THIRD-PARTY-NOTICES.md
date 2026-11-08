# Third-party notices

This project is derived from [ItsyCal](https://github.com/sfsam/itsycal) by Sam Vermette and
contributors, licensed under the MIT License. The upstream macOS sources were removed from
this Windows-only repository; the upstream copyright is retained in `LICENSE.txt` and the
original sources remain available upstream and in this repository's git history.

The Windows port reuses material from Microsoft PowerToys:

- The extension project skeleton (`Program.cs`, `app.manifest`, `Package.appxmanifest`,
  project files) is derived from the `TemplateCmdPalExtension` in
  [microsoft/PowerToys](https://github.com/microsoft/PowerToys), MIT License,
  Copyright (c) Microsoft Corporation.
- The brand assets in `windows/src/ItsycalForWindows/Assets/` are generated from the upstream
  ItsyCal app icon (MIT) by `windows/scripts/New-BrandAssets.ps1`; the source image is kept
  as `Assets/SourceIcon.png`.

NuGet dependencies are declared centrally in `windows/Directory.Packages.props`; add new
packages there and record them here.
### NuGet packages used by the test harness

- `coverlet.collector` 6.0.4 — MIT License, Copyright (c) 2018 Toni Solarin-Sodara. Collects
  XPlat code coverage for the CI coverage gate (`windows/tests/coverlet.runsettings`).
