# Builds the WinUI 3 tray-flyout spike for the agent sandbox.
#
# Why the TEMP/TMP redirect: PRI generation invokes MakePri.exe, which writes scratch files to
# %TEMP%. In this sandbox MakePri runs at low integrity and is denied those writes
# (MSB6006 / MakePri.exe exit code 0x80070005), which silently produces a build without
# resources.pri. The app then fails at startup loading XamlControlsResources:
#   XamlParseException: Cannot locate resource from
#   'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'
# Pointing TEMP/TMP at a project-local folder makes PRI generation succeed. Do not disable
# PRI generation with EnableCoreMrtTooling/AppxGeneratePriEnabled: that produces the crash.
param(
    [string]$Configuration = 'Debug',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'ItsycalForWindows.Flyout.csproj'
$tempDir = Join-Path $PSScriptRoot 'obj\.mrt-temp'
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# Prefer the user-local SDK; the machine-wide "C:\Program Files\dotnet" stub is not usable in
# the sandbox.
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnet = if ($dotnetCommand) { $dotnetCommand.Source } else { $null }
}
if (-not $dotnet) {
    throw "dotnet not found. Pass the SDK path or add it to PATH."
}

$env:TEMP = $tempDir
$env:TMP = $tempDir

& $dotnet build $project `
    -c $Configuration `
    -p:Platform=$Platform `
    -p:RuntimeIdentifier=$RuntimeIdentifier
exit $LASTEXITCODE
