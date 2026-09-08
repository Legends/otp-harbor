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

Write-Output 'Microsoft Store packaging controls are present.'
