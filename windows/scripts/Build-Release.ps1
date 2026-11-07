#Requires -Version 5.1
<#
.SYNOPSIS
Builds, signs, and verifies the Itsycal for Windows release MSIX packages.

.DESCRIPTION
Builds the self-contained MSIX once per architecture (x64 and arm64 by default),
signs each package with signtool, verifies the result, and copies the release
assets plus SHA256SUMS.txt and build-info.json into
windows/artifacts/release/<version>/.

Signing modes:
  -CertificatePath  Sign with a trusted code-signing certificate stored in a .pfx.
                    This is the only mode acceptable for WinGet: the manifest's
                    SignatureSha256 is the SHA256 of the package signature, and
                    `winget install` requires a certificate Windows trusts.
  -SelfSigned       Generate an ephemeral development certificate whose subject
                    matches the package Publisher, sign with it, and export the
                    public .cer next to the packages. Users must trust that .cer
                    before they can install the package.

.PARAMETER Version
Marketing version, for example 1.0.0. The package identity version is derived from it
(1.0.0 maps to 1.0.0.0). The script stamps Package.appxmanifest and the project metadata
to the derived version before packaging, so no manual manifest bump is required; the
release workflow passes the version parsed from the git tag.

.PARAMETER CertificatePath
Path to the .pfx code-signing certificate. Its subject must match the Publisher in
Package.appxmanifest, otherwise Windows rejects the package at install time.

.PARAMETER CertificatePassword
Password for -CertificatePath. The script never prints it; in CI pass it through
the TRUSTED_SIGNING_PFX_PASSWORD secret.

.PARAMETER SelfSigned
Generate an ephemeral self-signed development certificate. Cannot be combined
with -CertificatePath.

.PARAMETER Architectures
Architectures to build. Default: x64, arm64.

.PARAMETER Configuration
Build configuration. Default: Release.

.EXAMPLE
./windows/scripts/Build-Release.ps1 -Version 1.0.0 -SelfSigned

.EXAMPLE
./windows/scripts/Build-Release.ps1 -Version 1.0.0 `
  -CertificatePath ./itsycal-signing.pfx -CertificatePassword $env:TRUSTED_SIGNING_PFX_PASSWORD
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,

    [string]$CertificatePath,

    [string]$CertificatePassword,

    [switch]$SelfSigned,

    # Development releases are signed with a persistent development certificate that the
    # runner does not trust; skip the Authenticode verification in that mode.
    [switch]$SkipSignatureVerification,

    [ValidateSet('x64', 'arm64')]
    [string[]]$Architectures = @('x64', 'arm64'),

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-FourPartVersion {
    param([Parameter(Mandatory = $true)][string]$Value)

    $parts = @($Value.Trim().Split('.'))
    if ($parts.Count -gt 4) {
        throw "Version '$Value' has more than four parts."
    }
    foreach ($part in $parts) {
        if ($part -notmatch '^\d+$') {
            throw "Version '$Value' is not numeric."
        }
    }
    while ($parts.Count -lt 4) {
        $parts += '0'
    }
    return ($parts -join '.')
}

function Get-ManifestIdentity {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Package manifest not found: $Path"
    }
    [xml]$manifest = Get-Content -LiteralPath $Path -Raw
    $identity = $manifest.Package.Identity
    if (-not $identity) {
        throw "The package manifest has no <Identity> element: $Path"
    }
    return [pscustomobject]@{
        Name      = [string]$identity.Name
        Publisher = [string]$identity.Publisher
        Version   = [string]$identity.Version
    }
}

function Find-SignTool {
    $candidates = @()
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitsRoot) {
        $candidates = @(Get-ChildItem -Path $kitsRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' })
    }
    if ($candidates.Count -eq 0) {
        $onPath = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
        if ($onPath) {
            return $onPath.Source
        }
        throw 'signtool.exe not found. Install the Windows SDK (10.0.26100) or the MSIX packaging tools of Visual Studio.'
    }
    return ($candidates | Sort-Object FullName -Descending | Select-Object -First 1).FullName
}

function Get-ZipEntryBytes {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$EntryName
    )

    if (-not ('System.IO.Compression.ZipFile' -as [type])) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
    }
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq $EntryName } | Select-Object -First 1
        if (-not $entry) {
            throw "Package $Path has no $EntryName entry; it was not signed."
        }
        $stream = $entry.Open()
        try {
            $memory = [System.IO.MemoryStream]::new()
            $stream.CopyTo($memory)
            return $memory.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Get-SignatureSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $signature = Get-ZipEntryBytes -Path $Path -EntryName 'AppxSignature.p7x'
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($signature)
    }
    finally {
        $sha.Dispose()
    }
    return ([System.BitConverter]::ToString($hash) -replace '-', '')
}

function Invoke-SignTool {
    param(
        [Parameter(Mandatory = $true)][string]$SignTool,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $SignTool @Arguments | Out-Host
    $exitCode = $LASTEXITCODE
    return [int]$exitCode
}

# --- inputs and identity -----------------------------------------------------

if ($SelfSigned -and $CertificatePath) {
    throw 'Pass either -CertificatePath or -SelfSigned, not both.'
}
if (-not $SelfSigned -and -not $CertificatePath) {
    throw 'Pass -CertificatePath (trusted signing certificate) or -SelfSigned (development build).'
}
if ($SelfSigned -and $CertificatePassword) {
    throw '-SelfSigned generates its own throw-away password; do not pass -CertificatePassword.'
}
if ($Architectures.Count -eq 0) {
    throw 'Pass at least one architecture.'
}
if (-not $SelfSigned) {
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
        throw "Signing certificate not found: $CertificatePath"
    }
    $CertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'windows\src\ItsycalForWindows\ItsycalForWindows.csproj'
$manifestPath = Join-Path $repoRoot 'windows\src\ItsycalForWindows\Package.appxmanifest'
$artifactsRoot = Join-Path $repoRoot 'windows\artifacts'
$releaseDir = Join-Path $artifactsRoot (Join-Path 'release' $Version)
$buildRoot = Join-Path $artifactsRoot 'msix'

$fourPartVersion = ConvertTo-FourPartVersion -Value $Version
$identity = Get-ManifestIdentity -Path $manifestPath
$manifestVersion = ConvertTo-FourPartVersion -Value $identity.Version
if ($manifestVersion -ne $fourPartVersion) {
    # The version comes from the tag (release) or the caller; stamp it instead of asking a
    # human to edit the manifest and forget the bump.
    & (Join-Path $PSScriptRoot 'Set-PackageVersion.ps1') -Version $fourPartVersion
    $identity = Get-ManifestIdentity -Path $manifestPath
    $manifestVersion = ConvertTo-FourPartVersion -Value $identity.Version
    if ($manifestVersion -ne $fourPartVersion) {
        throw "Could not stamp Package.appxmanifest Identity Version to $fourPartVersion."
    }
}
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Extension project not found: $project"
}

# --- signing material -------------------------------------------------------

$signTool = Find-SignTool
$pfxPath = $null
$pfxPassword = $null
$devCertificate = $null
$temporaryFiles = @()

try {
    if ($SelfSigned) {
        Write-Host "Generating an ephemeral development certificate for publisher '$($identity.Publisher)'."
        $pfxPassword = [guid]::NewGuid().ToString('N')
        $securePassword = ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText
        $devCertificate = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $identity.Publisher `
            -KeyUsage DigitalSignature `
            -FriendlyName "Itsycal for Windows $Version development signing" `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        $pfxPath = Join-Path ([System.IO.Path]::GetTempPath()) ("itsycal-release-{0}.pfx" -f [guid]::NewGuid().ToString('N'))
        Export-PfxCertificate -Cert $devCertificate -FilePath $pfxPath -Password $securePassword | Out-Null
        $temporaryFiles += $pfxPath
    }
    else {
        $pfxPassword = $CertificatePassword
        $certificate = $null
        try {
            if ([string]::IsNullOrEmpty($CertificatePassword)) {
                $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
            }
            else {
                $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath, $CertificatePassword)
            }
        }
        catch {
            Write-Warning "Could not read the .pfx to compare its subject with the package Publisher: $($_.Exception.Message)"
        }
        if ($certificate) {
            $publisherCn = [regex]::Match($identity.Publisher, 'CN=([^,]+)')
            $certificateCn = @($certificate.Subject.Split(',') | Where-Object { $_ -match '^\s*CN=' }) | Select-Object -First 1
            if ($publisherCn.Success -and $certificateCn -and ($certificateCn -notmatch [regex]::Escape($publisherCn.Groups[1].Value.Trim()))) {
                Write-Warning ("The certificate subject ($($certificate.Subject)) does not match the package Publisher " +
                    "($($identity.Publisher)). Windows rejects a package signed by a certificate for another publisher; " +
                    'update Package.appxmanifest Identity Publisher, or use a certificate for the current publisher.')
            }
        }
    }

    # --- build, sign, verify -------------------------------------------------

    if (Test-Path -LiteralPath $buildRoot) {
        Remove-Item -LiteralPath $buildRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

    $assets = @()
    foreach ($architecture in $Architectures) {
        $platform = if ($architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
        $packageDir = Join-Path $buildRoot $architecture
        New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
        $appxPackageDirArgument = '-p:AppxPackageDir=' + $packageDir + [System.IO.Path]::DirectorySeparatorChar

        Write-Host "Building $architecture MSIX ($Configuration, Platform $platform)."
        $buildArguments = @(
            'build', $project,
            '--configuration', $Configuration,
            '--nologo',
            "-p:Platform=$platform",
            '-p:GenerateAppxPackageOnBuild=true',
            "-p:RuntimeIdentifier=win-$architecture",
            '-p:SelfContained=true',
            '-p:AppxPackageSigningEnabled=false',
            '-p:AppxBundle=Never',
            $appxPackageDirArgument
        )
        & dotnet @buildArguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for $architecture (exit code $LASTEXITCODE)."
        }

        $package = Get-ChildItem -Path $packageDir -Recurse -Filter '*.msix' -File |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if (-not $package) {
            throw "The $architecture build produced no .msix package under $packageDir."
        }

        $assetName = 'ItsycalForWindows_{0}_{1}.msix' -f $fourPartVersion, $architecture
        $assetPath = Join-Path $releaseDir $assetName
        Copy-Item -LiteralPath $package.FullName -Destination $assetPath -Force

        $signArguments = @('sign', '/fd', 'SHA256', '/f', $pfxPath)
        if (-not [string]::IsNullOrEmpty($pfxPassword)) {
            $signArguments += @('/p', $pfxPassword)
        }
        if (-not $SelfSigned) {
            $signArguments += @('/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')
        }
        $signArguments += $assetPath

        Write-Host "Signing $assetName."
        $signExitCode = Invoke-SignTool -SignTool $signTool -Arguments $signArguments
        if ($signExitCode -ne 0 -and -not $SelfSigned) {
            Write-Warning 'Timestamping failed; signing again without an RFC 3161 timestamp. The signature stays valid while the certificate does.'
            $retryArguments = @($signArguments | Where-Object { $_ -notin @('/tr', 'http://timestamp.digicert.com', '/td', 'SHA256') })
            $signExitCode = Invoke-SignTool -SignTool $signTool -Arguments $retryArguments
        }
        if ($signExitCode -ne 0) {
            throw "signtool sign failed for $assetName (exit code $signExitCode)."
        }

        if ($SelfSigned -or $SkipSignatureVerification) {
            Write-Host 'Development package: signature block present, Authenticode verification skipped because the certificate is not trusted.'
        }
        else {
            Write-Host "Verifying $assetName."
            & $signTool verify /pa /fd SHA256 $assetPath
            if ($LASTEXITCODE -ne 0) {
                throw "signtool verify failed for $assetName (exit code $LASTEXITCODE)."
            }
        }

        $assets += [pscustomobject]@{
            Architecture    = $architecture
            File            = $assetName
            Sha256          = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
            SignatureSha256 = Get-SignatureSha256 -Path $assetPath
        }
    }

    if ($SelfSigned) {
        $certificateAsset = 'ItsycalForWindows_{0}_dev.cer' -f $fourPartVersion
        Export-Certificate -Cert $devCertificate -FilePath (Join-Path $releaseDir $certificateAsset) -Force | Out-Null
        Write-Host "Exported the development certificate: $certificateAsset. Users must trust it before installing the packages."
    }

    # --- release metadata ----------------------------------------------------

    $releaseFiles = @(Get-ChildItem -LiteralPath $releaseDir -File |
        Where-Object { $_.Name -notin @('SHA256SUMS.txt', 'build-info.json') } |
        Sort-Object Name)
    $checksumLines = @($releaseFiles | ForEach-Object {
            '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
        })
    Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Value $checksumLines -Encoding ascii

    $buildInfo = [ordered]@{
        version        = $Version
        packageVersion = $fourPartVersion
        configuration  = $Configuration
        selfSigned     = [bool]$SelfSigned
        identityName   = $identity.Name
        publisher      = $identity.Publisher
        architectures  = @($Architectures)
        artifacts      = @($assets | ForEach-Object {
                [ordered]@{
                    architecture    = $_.Architecture
                    file            = $_.File
                    sha256          = $_.Sha256
                    signatureSha256 = $_.SignatureSha256
                }
            })
    }
    $buildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseDir 'build-info.json') -Encoding utf8

    Write-Host ''
    Write-Host "Release assets written to $releaseDir"
    foreach ($asset in $assets) {
        Write-Host ('  {0}  sha256={1}  signature={2}' -f $asset.File, $asset.Sha256, $asset.SignatureSha256)
    }
    Write-Host '  SHA256SUMS.txt'
    Write-Host '  build-info.json'
    if ($SelfSigned) {
        Write-Host 'Development build: self-signed. Do not submit these packages to WinGet; WinGet MSIX installers must be signed by a trusted certificate.'
    }
}
finally {
    if ($devCertificate) {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $devCertificate.Thumbprint) -Force -ErrorAction SilentlyContinue
    }
    foreach ($file in $temporaryFiles) {
        if (Test-Path -LiteralPath $file) {
            Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
        }
    }
}
