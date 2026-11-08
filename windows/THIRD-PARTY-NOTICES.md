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

## System.Security.Cryptography.ProtectedData

`System.Security.Cryptography.ProtectedData` 10.0.0 is a .NET BCL extension package from
the [dotnet/runtime](https://github.com/dotnet/runtime) project. It is used for the DPAPI
encryption of provider secrets (`windows/src/ItsycalForWindows.Providers/Accounts`).

MIT License

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

The package's license text is also shipped in the NuGet package
(`System.Security.Cryptography.ProtectedData` → `LICENSE.TXT`) and is available at
<https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>.
