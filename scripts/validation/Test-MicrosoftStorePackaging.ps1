<#
.SYNOPSIS
Validates Microsoft Store packaging files and workflow integration.

.DESCRIPTION
Statically checks the Store manifest template, packager, workflows, version mapping, documentation, and listing for required identity placeholders, capabilities, languages, update policy, and release automation controls.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path

function Read-RequiredFile {
    param([Parameter(Mandatory)][string]$RelativePath)
    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Microsoft Store file is missing: $RelativePath"
    }
    return [IO.File]::ReadAllText($path)
}

$manifest = Read-RequiredFile 'packaging/windows-store/AppxManifest.xml.template'
$packager = Read-RequiredFile 'scripts/release/New-MicrosoftStoreMsix.ps1'
$workflow = Read-RequiredFile '.github/workflows/store-msix.yml'
$releaseWorkflow = Read-RequiredFile '.github/workflows/build-and-test.yml'
$versionResolver = Join-Path $repositoryRoot 'scripts/release/Get-MicrosoftStoreVersion.ps1'
$documentation = Read-RequiredFile 'docs/release/MICROSOFT_STORE.md'
$listing = Read-RequiredFile 'packaging/windows-store/STORE_LISTING.md'

function Assert-PngDimensions {
    param(
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][int]$ExpectedWidth,
        [Parameter(Mandatory)][int]$ExpectedHeight
    )

    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Microsoft Store image is missing: $RelativePath"
    }

    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 24 -or
        [Convert]::ToHexString($bytes[0..7]) -cne '89504E470D0A1A0A') {
        throw "Microsoft Store image is not a valid PNG: $RelativePath"
    }

    $width = [BitConverter]::ToInt32([byte[]]($bytes[19], $bytes[18], $bytes[17], $bytes[16]), 0)
    $height = [BitConverter]::ToInt32([byte[]]($bytes[23], $bytes[22], $bytes[21], $bytes[20]), 0)
    if ($width -ne $ExpectedWidth -or $height -ne $ExpectedHeight) {
        throw "Microsoft Store image has incorrect dimensions: $RelativePath is ${width}x${height}; expected ${ExpectedWidth}x${ExpectedHeight}."
    }
}

function Assert-PngMinimumDimensions {
    param(
        [Parameter(Mandatory)][string]$RelativePath,
        [int]$MinimumWidth = 1366,
        [int]$MinimumHeight = 768
    )

    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Microsoft Store screenshot is missing: $RelativePath"
    }

    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 24 -or [Convert]::ToHexString($bytes[0..7]) -cne '89504E470D0A1A0A') {
        throw "Microsoft Store screenshot is not a valid PNG: $RelativePath"
    }

    $width = [BitConverter]::ToInt32([byte[]]($bytes[19], $bytes[18], $bytes[17], $bytes[16]), 0)
    $height = [BitConverter]::ToInt32([byte[]]($bytes[23], $bytes[22], $bytes[21], $bytes[20]), 0)
    if ($width -lt $MinimumWidth -or $height -lt $MinimumHeight) {
        throw "Microsoft Store screenshot is too small: $RelativePath is ${width}x${height}."
    }
    if ((Get-Item -LiteralPath $path).Length -ge 50MB) {
        throw "Microsoft Store screenshot exceeds 50 MB: $RelativePath"
    }
}

function Assert-PngHasAlpha {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Join-Path $repositoryRoot $RelativePath
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 26 -or $bytes[25] -notin 4, 6) {
        throw "Microsoft Store PNG does not contain an alpha channel: $RelativePath"
    }
}

foreach ($placeholder in @(
    '__IDENTITY_NAME__',
    '__PUBLISHER__',
    '__PUBLISHER_DISPLAY_NAME__',
    '__VERSION__'
)) {
    if (-not $manifest.Contains($placeholder, [StringComparison]::Ordinal)) {
        throw "The Store manifest is missing the Partner Center placeholder: $placeholder"
    }
}

foreach ($control in @(
    'EntryPoint="Windows.FullTrustApplication"',
    '<rescap:Capability Name="runFullTrust" />',
    '<DeviceCapability Name="webcam" />',
    '<Resource Language="en" />',
    '<Resource Language="de" />',
    '<Resource Language="fr" />',
    '<Resource Language="es" />'
)) {
    if (-not $manifest.Contains($control, [StringComparison]::Ordinal)) {
        throw "The Store manifest is missing a required desktop-app control: $control"
    }
}

foreach ($control in @(
    '-DistributionMode store',
    '-DisableUpdates',
    "distribution = 'microsoft-store-only'",
    'Do not distribute this unsigned MSIX directly.'
)) {
    if (-not $packager.Contains($control, [StringComparison]::Ordinal)) {
        throw "The Store packager is missing a required distribution control: $control"
    }
}

if (-not $workflow.Contains("if: `${{ github.event_name == 'workflow_dispatch' }}", [StringComparison]::Ordinal)) {
    throw 'The workflow must not upload PR smoke-test packages as distributable artifacts.'
}
foreach ($control in @(
    'package-microsoft-store-msix:',
    "-IdentityName 'Legends77.OTPHarbor'",
    "-Publisher 'CN=84095A7C-6458-436E-ABF2-DC02311E25F9'",
    'retention-days: 90',
    'needs: [package-avalonia-release, package-microsoft-store-msix]'
)) {
    if (-not $releaseWorkflow.Contains($control, [StringComparison]::Ordinal)) {
        throw "The release workflow is missing automatic Store-package control: $control"
    }
}

$versionCases = @(
    @{ Tag = 'v2.0.0-rc14'; Expected = '2.0.14.0' },
    @{ Tag = 'v2.0.0'; Expected = '2.0.65535.0' },
    @{ Tag = 'v2.0.1-rc1'; Expected = '2.1.1.0' },
    @{ Tag = 'v2.1.0-rc1'; Expected = '2.1000.1.0' }
)
foreach ($case in $versionCases) {
    $actual = & $versionResolver -ReleaseTag $case.Tag
    if ($actual -cne $case.Expected) {
        throw "Store version mapping failed for $($case.Tag): expected $($case.Expected), got $actual."
    }
}
if (-not $documentation.Contains('Microsoft Store is the primary Windows distribution channel', [StringComparison]::Ordinal)) {
    throw 'The Store documentation does not identify the primary Windows distribution channel.'
}
if (-not $documentation.Contains('must not be attached to a GitHub Release', [StringComparison]::Ordinal)) {
    throw 'The Store documentation does not prohibit direct distribution of the unsigned MSIX.'
}
foreach ($cultureHeading in @(
    '## English (en-US)',
    '## German (de-DE)',
    '## French (fr-FR)',
    '## Spanish (es-ES)'
)) {
    if (-not $listing.Contains($cultureHeading, [StringComparison]::Ordinal)) {
        throw "The Store listing draft is missing: $cultureHeading"
    }
}
if (-not $listing.Contains('Screenshots must use synthetic accounts only.', [StringComparison]::Ordinal)) {
    throw 'The Store listing draft does not enforce synthetic screenshot data.'
}

Assert-PngDimensions 'packaging/windows-store/assets/store-super-hero-1920x1080.png' 1920 1080
Assert-PngDimensions 'packaging/windows-store/assets/store-poster-art-720x1080.png' 720 1080
Assert-PngDimensions 'packaging/windows-store/assets/store-app-tile-300x300.png' 300 300
Assert-PngDimensions 'packaging/windows-store/assets/store-logo-150x150.png' 150 150
Assert-PngDimensions 'packaging/windows-store/assets/store-logo-71x71.png' 71 71

foreach ($screenshot in @(
    'packaging/windows-store/screenshots/en-US/01-account-dashboard.png',
    'packaging/windows-store/screenshots/en-US/02-search-accounts.png',
    'packaging/windows-store/screenshots/en-US/03-add-account.png',
    'packaging/windows-store/screenshots/en-US/04-quick-unlock-transparent.png'
)) {
    Assert-PngMinimumDimensions $screenshot
}

Assert-PngHasAlpha 'packaging/windows-store/screenshots/en-US/04-quick-unlock-transparent.png'

Write-Output 'Microsoft Store packaging controls are present.'
