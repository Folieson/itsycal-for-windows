# Stamps the package identity version in one place.
#
# The version is derived, not hand-maintained: CI stamps a unique revision for every
# development build, and the release workflow stamps the tag version before packaging, so a
# rebuild of the same source can never silently reuse an identity that is already installed.
#
# Usage:
#   ./windows/scripts/Set-PackageVersion.ps1 -Version 1.1.0.0
#   ./windows/scripts/Set-PackageVersion.ps1 -Revision 12345   # keeps the first three parts
[CmdletBinding(DefaultParameterSetName = 'Version')]
param(
    [Parameter(ParameterSetName = 'Version', Mandatory = $true)]
    [string]$Version,

    [Parameter(ParameterSetName = 'Revision', Mandatory = $true)]
    [ValidateRange(1, 65535)]
    [int]$Revision
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$manifestPath = Join-Path $repoRoot 'windows\src\ItsycalForWindows\Package.appxmanifest'
$projectPath = Join-Path $repoRoot 'windows\src\ItsycalForWindows\ItsycalForWindows.csproj'

foreach ($path in @($manifestPath, $projectPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Not found: $path"
    }
}

$manifestContent = Get-Content -LiteralPath $manifestPath -Raw
$identityMatch = [regex]::Match(
    $manifestContent,
    '(?s)<Identity\b.*?\bVersion="(?<version>\d+(?:\.\d+){3})"')
if (-not $identityMatch.Success) {
    throw "Package.appxmanifest has no Identity Version attribute."
}
$currentVersion = $identityMatch.Groups['version'].Value

if ($PSCmdlet.ParameterSetName -eq 'Revision') {
    $parts = $currentVersion.Split('.')
    $Version = '{0}.{1}.{2}.{3}' -f $parts[0], $parts[1], $parts[2], $Revision
}

if ($Version -notmatch '^(?<parts>\d{1,5}(?:\.\d{1,5}){3})$') {
    throw "Package version must have four numeric parts (for example 1.2.3.0), got '$Version'."
}

$parts = $Version.Split('.') | ForEach-Object { [int]$_ }
if (@($parts | Where-Object { $_ -gt 65535 }).Count -gt 0) {
    throw "Every MSIX identity version part must be <= 65535, got '$Version'."
}
if (@($parts | Where-Object { $_ -ne 0 }).Count -eq 0) {
    throw 'Package version 0.0.0.0 is not allowed.'
}

$stamped = $manifestContent.Replace(
    $identityMatch.Value,
    $identityMatch.Value.Replace("Version=`"$currentVersion`"", "Version=`"$Version`""))

if ($stamped -eq $manifestContent -and $currentVersion -ne $Version) {
    throw "Could not stamp the identity version in Package.appxmanifest."
}
Set-Content -LiteralPath $manifestPath -Value $stamped -NoNewline -Encoding utf8

# Keep the assembly metadata in step with the package identity. The project Version is the
# three-part marketing version; FileVersion carries the four-part identity version.
$projectContent = Get-Content -LiteralPath $projectPath -Raw
$threePart = ($Version.Split('.') | Select-Object -First 3) -join '.'
$projectContent = $projectContent -replace
    '<Version>\d+(?:\.\d+){1,3}</Version>',
    "<Version>$threePart</Version>"
$projectContent = $projectContent -replace
    '<FileVersion>\d+(?:\.\d+){1,3}</FileVersion>',
    "<FileVersion>$Version</FileVersion>"
Set-Content -LiteralPath $projectPath -Value $projectContent -NoNewline -Encoding utf8

Write-Host "Package version: $currentVersion -> $Version"
