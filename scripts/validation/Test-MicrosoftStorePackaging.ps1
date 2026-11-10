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
$currentSourcePackager = Read-RequiredFile 'scripts/release/New-CurrentSourceMicrosoftStoreMsix.ps1'
$workflow = Read-RequiredFile '.github/workflows/store-msix.yml'
$releaseWorkflow = Read-RequiredFile '.github/workflows/build-and-test.yml'
$versionResolver = Join-Path $repositoryRoot 'scripts/release/Get-MicrosoftStoreVersion.ps1'
$publicVersionUpdater = Join-Path $repositoryRoot 'scripts/release/Set-PublicMicrosoftStoreVersion.ps1'
$documentation = Read-RequiredFile 'docs/release/MICROSOFT_STORE.md'
$listing = Read-RequiredFile 'packaging/windows-store/STORE_LISTING.md'
$readme = Read-RequiredFile 'readme.md'
$publicVersionJson = Read-RequiredFile 'packaging/windows-store/public-version.json'
$publicVersion = $publicVersionJson | ConvertFrom-Json

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
    '$packageVersion.Build -eq 65535',
    '-p:AssemblyVersion=$assemblyVersion',
    '-p:IncludeSourceRevisionInInformationalVersion=false',
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
    "if: startsWith(github.ref, 'refs/tags/v') && !contains(github.ref_name, '-rc')",
    "-IdentityName 'Legends77.OTPHarbor'",
    "-Publisher 'CN=84095A7C-6458-436E-ABF2-DC02311E25F9'",
    'retention-days: 90',
    'needs: [package-avalonia-release, package-microsoft-store-msix]'
)) {
    if (-not $releaseWorkflow.Contains($control, [StringComparison]::Ordinal)) {
        throw "The release workflow is missing automatic Store-package control: $control"
    }
}

foreach ($control in @(
    "Where-Object { `$_ -match '^v\d+\.\d+\.\d+`$' }",
    'The source tree has uncommitted changes.',
    "-IdentityName 'Legends77.OTPHarbor'",
    "-Publisher 'CN=84095A7C-6458-436E-ABF2-DC02311E25F9'",
    "-PublisherDisplayName 'Legends77'",
    "-Version `$storeVersion",
    "-ProductVersion `$productVersion",
    "-OutputDirectory `$resolvedOutputDirectory",
    'The generated MSIX does not match store-package.json.'
)) {
    if (-not $currentSourcePackager.Contains($control, [StringComparison]::Ordinal)) {
        throw "The current-source Store wrapper is missing: $control"
    }
}

$stablePublicationMarker = '  publish-avalonia-release:'
$stablePublicationIndex = $releaseWorkflow.IndexOf(
    $stablePublicationMarker,
    [StringComparison]::Ordinal)
if ($stablePublicationIndex -lt 0) {
    throw 'The stable GitHub publication job is missing.'
}
$stablePublication = $releaseWorkflow.Substring($stablePublicationIndex)
foreach ($control in @(
    'name: OTP Harbor ${{ steps.versioning.outputs.release_version }}',
    'The stable desktop GitHub release must contain exactly the two Linux artifacts.',
    'No unsigned Windows executable is attached to this stable GitHub release.'
)) {
    if (-not $stablePublication.Contains($control, [StringComparison]::Ordinal)) {
        throw "The stable GitHub publication is missing: $control"
    }
}
if ($stablePublication.Contains(
    'release-assets/OTP-Harbor-windows-x64-${{ steps.versioning.outputs.release_version }}.zip',
    [StringComparison]::Ordinal)) {
    throw 'The stable GitHub publication must not attach a Windows ZIP.'
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
if (-not $documentation.Contains('Microsoft Store is the only packaged Windows distribution channel', [StringComparison]::Ordinal)) {
    throw 'The Store documentation does not identify the only packaged Windows distribution channel.'
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
if ($publicVersion.displayVersion -notmatch '^\d+\.\d+\.\d+$' -or
    $publicVersion.packageVersion -notmatch '^\d+\.\d+\.\d+\.0$' -or
    ([version]$publicVersion.displayVersion).Major -ne ([version]$publicVersion.packageVersion).Major) {
    throw 'The canonical public Microsoft Store version is invalid.'
}
& $publicVersionUpdater `
    -PackageVersion $publicVersion.packageVersion `
    -DisplayVersion $publicVersion.displayVersion `
    -Check

$versionUpdateFixture = Join-Path ([IO.Path]::GetTempPath()) (
    'otp-harbor-store-version-' + [Guid]::NewGuid().ToString('N'))
try {
    foreach ($relativePath in @(
        'readme.md',
        'site/index.html',
        'packaging/windows-store/STORE_LISTING.md',
        'packaging/windows-store/public-version.json'
    )) {
        $source = Join-Path $repositoryRoot $relativePath
        $destination = Join-Path $versionUpdateFixture $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
    & $publicVersionUpdater `
        -RepositoryRoot $versionUpdateFixture `
        -PackageVersion "$(([version]$publicVersion.packageVersion).Major + 1).2.65535.0"
    $updatedVersion = Get-Content `
        -LiteralPath (Join-Path $versionUpdateFixture 'packaging/windows-store/public-version.json') `
        -Raw | ConvertFrom-Json
    $updatedReadme = [IO.File]::ReadAllText((Join-Path $versionUpdateFixture 'readme.md'))
    $updatedWebsite = [IO.File]::ReadAllText((Join-Path $versionUpdateFixture 'site/index.html'))
    $expectedDisplay = "$(([version]$publicVersion.packageVersion).Major + 1).0.2"
    $expectedPackage = "$(([version]$publicVersion.packageVersion).Major + 1).2.65535.0"
    if ($updatedVersion.displayVersion -cne $expectedDisplay -or
        $updatedVersion.packageVersion -cne $expectedPackage -or
        -not $updatedReadme.Contains("Microsoft%20Store-$expectedPackage-", [StringComparison]::Ordinal) -or
        -not $updatedWebsite.Contains("data-release-channel=`"store`">$expectedPackage</span>", [StringComparison]::Ordinal)) {
        throw 'The Store publication updater did not synchronize the public package version.'
    }
}
finally {
    if (Test-Path -LiteralPath $versionUpdateFixture -PathType Container) {
        Remove-Item -LiteralPath $versionUpdateFixture -Recurse -Force
    }
}

$storeBadge = @($readme -split '\r?\n' | Where-Object { $_.StartsWith('[![Microsoft Store version]', [StringComparison]::Ordinal) })
if ($storeBadge.Count -ne 1 -or
    -not $storeBadge[0].Contains('https://apps.microsoft.com/detail/9P31KH5L924P', [StringComparison]::Ordinal) -or
    $storeBadge[0].Contains('img.shields.io/github/v/release/', [StringComparison]::Ordinal) -or
    -not $listing.Contains("## Version $($publicVersion.displayVersion) release notes", [StringComparison]::Ordinal) -or
    -not $readme.Contains("Microsoft%20Store-$($publicVersion.packageVersion)-", [StringComparison]::Ordinal) -or
    -not $readme.Contains("OTP Harbor package ``$($publicVersion.packageVersion)`` is publicly available", [StringComparison]::Ordinal)) {
    throw "The public Microsoft Store package version is not reported consistently as $($publicVersion.packageVersion)."
}
foreach ($control in @(
    'mark_as_published:',
    'display_version:',
    'Set-PublicMicrosoftStoreVersion.ps1',
    'mark-public-version:',
    'gh pr create'
)) {
    if (-not $workflow.Contains($control, [StringComparison]::Ordinal)) {
        throw "The Store workflow is missing the publication-version control: $control"
    }
}

Assert-PngDimensions 'packaging/windows-store/assets/store-super-hero-1920x1080.png' 1920 1080
Assert-PngDimensions 'packaging/windows-store/assets/store-poster-art-720x1080.png' 720 1080
Assert-PngDimensions 'packaging/windows-store/assets/store-box-art-1080x1080.png' 1080 1080
Assert-PngDimensions 'packaging/windows-store/assets/store-app-tile-300x300.png' 300 300
Assert-PngDimensions 'packaging/windows-store/assets/store-logo-150x150.png' 150 150
Assert-PngDimensions 'packaging/windows-store/assets/store-logo-71x71.png' 71 71
Assert-PngDimensions 'packaging/windows-store/assets/source/store-abstract-background.png' 1672 941
Assert-PngDimensions 'packaging/windows-store/assets/source/store-marketing-screenshot-background.png' 1672 941

$expectedV3Campaign = @(
    '02-dark-account-list.png',
    '03-light-dark-themes.png',
    '04-appearance-settings.png',
    '05-account-actions.png',
    '06-quick-unlock.png',
    '07-qr-import.png',
    '08-groups-favourites.png',
    '09-icon-pack.png',
    '10-backup-restore.png',
    '11-import-formats.png'
)
foreach ($culture in @('en-US', 'de-DE', 'fr-FR', 'es-ES')) {
    $submissionDirectory = Join-Path $repositoryRoot "packaging/windows-store/screenshots/$culture/marketing"
    $actualCampaign = @(Get-ChildItem -LiteralPath $submissionDirectory -Filter '*.png' -File |
        Select-Object -ExpandProperty Name | Sort-Object)
    $expectedSorted = @($expectedV3Campaign | Sort-Object)
    if ([string]::Join('|', $actualCampaign) -cne [string]::Join('|', $expectedSorted)) {
        throw "The committed $culture Store campaign does not contain exactly the reviewed v3 files."
    }
    foreach ($marketingScreenshot in $expectedV3Campaign) {
        $relativeSubmission = "packaging/windows-store/screenshots/$culture/marketing/$marketingScreenshot"
        $generated = Join-Path $repositoryRoot "artifacts/marketing/store/windows/promotional/$culture/$marketingScreenshot"
        Assert-PngDimensions $relativeSubmission 1920 1080
        if (-not (Test-Path -LiteralPath $generated -PathType Leaf)) {
            throw "Canonical generated Store campaign file is missing: $generated"
        }
        $submission = Join-Path $repositoryRoot $relativeSubmission
        if ((Get-FileHash -LiteralPath $submission -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath $generated -Algorithm SHA256).Hash) {
            throw "Committed Store campaign differs from the canonical generated file: $relativeSubmission"
        }
    }
}

foreach ($obsoleteSource in @(
    'import-export-settings.png',
    'import-qr-camera.png',
    'quick-unlock-screen.png',
    'windows-hello-prompt.png',
    'windows-hello-quick-unlock.png'
)) {
    if (Test-Path -LiteralPath (Join-Path $repositoryRoot "packaging/windows-store/assets/source/$obsoleteSource")) {
        throw "Obsolete v2 Store UI source remains: $obsoleteSource"
    }
}
if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'scripts/assets/New-WindowsStoreMarketingScreenshots.ps1')) {
    throw 'The obsolete v2 Store screenshot generator must not remain in the release tree.'
}
if (-not $listing.Contains('### Draft version 3.0.0 release notes', [StringComparison]::Ordinal)) {
    throw 'The Store listing is missing localized draft v3.0.0 release notes.'
}

Write-Output 'Microsoft Store packaging controls are present.'
