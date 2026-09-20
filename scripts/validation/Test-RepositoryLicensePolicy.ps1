<#
.SYNOPSIS
Fails closed when OTP Harbor's GPL-3.0-only licensing posture drifts.

.DESCRIPTION
Validates the normalized official GPLv3 text, public descriptions, immutable license history,
contribution notice, packaged legal files, build metadata, F-Droid agenda, and CI wiring.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path

function Read-RepositoryFile {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required licensing file is missing: $RelativePath"
    }

    return [IO.File]::ReadAllText($path)
}

$license = Read-RepositoryFile 'LICENSE.txt'
$normalizedLicense = $license.Replace("`r`n", "`n")
$licenseBytes = [Text.Encoding]::UTF8.GetBytes($normalizedLicense)
$licenseHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($licenseBytes)).ToLowerInvariant()
$expectedLicenseHash = '3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986'
if ($licenseHash -cne $expectedLicenseHash) {
    throw "LICENSE.txt differs from the reviewed official GNU GPL version 3 text. Expected $expectedLicenseHash; found $licenseHash."
}

foreach ($requiredText in @(
    'GNU GENERAL PUBLIC LICENSE',
    'Version 3, 29 June 2007',
    'END OF TERMS AND CONDITIONS'
)) {
    if (-not $license.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "LICENSE.txt is missing canonical GPLv3 text: $requiredText"
    }
}

$licensing = Read-RepositoryFile 'LICENSING.md'
foreach ($requiredText in @(
    'GNU General Public License version 3 only',
    '`GPL-3.0-only`',
    'GPLv3 permits use, modification, redistribution, and commercial activity',
    'does not grant permission to suggest sponsorship or affiliation',
    'revisions through commit `b058f8c` were published under the MIT License',
    'Commits `0bb0142` and `fedcace` were published under the PolyForm Noncommercial License 1.0.0',
    'terms apply to new material and changed files offered with revisions after',
    'corresponding source code for the exact released revision'
)) {
    if (-not $licensing.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "LICENSING.md is missing the required boundary: $requiredText"
    }
}

$readme = Read-RepositoryFile 'readme.md'
$buildMetadata = Read-RepositoryFile 'Directory.Build.props'
$contributing = Read-RepositoryFile 'CONTRIBUTING.md'
$thirdPartyNotices = Read-RepositoryFile 'THIRD_PARTY_NOTICES.md'
$website = Read-RepositoryFile 'site/index.html'
$androidWebsite = Read-RepositoryFile 'site/android/index.html'
$androidGuide = Read-RepositoryFile 'site/android/guide/index.html'
$storeListing = Read-RepositoryFile 'packaging/windows-store/STORE_LISTING.md'
$signPathRunbook = Read-RepositoryFile 'docs/security/SIGNPATH_FOUNDATION_ONBOARDING.md'
$productAgenda = Read-RepositoryFile 'docs/PRODUCT_AGENDA.md'
$workflow = Read-RepositoryFile '.github/workflows/build-and-test.yml'
$desktopProject = Read-RepositoryFile 'TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj'
$androidProject = Read-RepositoryFile 'TOTP.UI.Avalonia.Android/TOTP.UI.Avalonia.Android.csproj'

foreach ($requiredText in @(
    'GPLv3-licensed, open-source',
    'GNU General Public License version 3 only',
    '`GPL-3.0-only`',
    'permits commercial use and redistribution',
    'immutable license history of earlier revisions'
)) {
    if (-not $readme.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "README licensing disclosure is missing: $requiredText"
    }
}

foreach ($requiredText in @(
    '<PackageLicenseExpression>GPL-3.0-only</PackageLicenseExpression>',
    '<PackageRequireLicenseAcceptance>true</PackageRequireLicenseAcceptance>',
    'GPLv3-licensed, open-source'
)) {
    if (-not $buildMetadata.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "Build metadata is missing the GPLv3 marker: $requiredText"
    }
}

if (-not $contributing.Contains('you agree to license it under the', [StringComparison]::Ordinal)) {
    throw 'The contribution guide does not state the incoming contribution license.'
}
if (-not $thirdPartyNotices.Contains("separate from OTP Harbor's GPLv3 license", [StringComparison]::Ordinal)) {
    throw 'Third-party notices do not distinguish dependency licenses from the project license.'
}
if (-not $signPathRunbook.Contains('OSI-approved project license', [StringComparison]::Ordinal)) {
    throw 'The SignPath runbook does not identify the current OSI-approved project license.'
}
$storeListsSource = $storeListing.Contains('complete corresponding source and GPLv3 license', [StringComparison]::Ordinal)
$storeListsReleaseUrl = $storeListing.Contains('https://github.com/Legends/otp-harbor/releases', [StringComparison]::Ordinal)
if (-not $storeListsSource -or -not $storeListsReleaseUrl) {
    throw 'The Store listing does not direct binary recipients to corresponding source and GPLv3 terms.'
}
$agendaHasOfficialTarget = $productAgenda.Contains('Official F-Droid Android distribution — planned', [StringComparison]::Ordinal)
$agendaHasLicenseGate = $productAgenda.Contains('GPL-3.0-only satisfies', [StringComparison]::Ordinal)
if (-not $agendaHasOfficialTarget -or -not $agendaHasLicenseGate) {
    throw 'The product agenda does not reflect the GPLv3-compatible official F-Droid path.'
}

foreach ($requiredFile in @('LICENSE.txt', 'LICENSING.md', 'THIRD_PARTY_NOTICES.md')) {
    $desktopDeclaration = "Link=`"$requiredFile`" CopyToOutputDirectory=`"PreserveNewest`" CopyToPublishDirectory=`"PreserveNewest`""
    if (-not $desktopProject.Contains($desktopDeclaration, [StringComparison]::Ordinal)) {
        throw "Desktop packages do not include the required legal file: $requiredFile"
    }

    $androidDeclaration = "AndroidAsset Include=`"..\$requiredFile`" Link=`"$requiredFile`""
    if (-not $androidProject.Contains($androidDeclaration, [StringComparison]::Ordinal)) {
        throw "Android packages do not include the required legal file: $requiredFile"
    }
}

$publicFiles = [ordered]@{
    'readme.md' = $readme
    'site/index.html' = $website
    'site/android/index.html' = $androidWebsite
    'site/android/guide/index.html' = $androidGuide
    'packaging/windows-store/STORE_LISTING.md' = $storeListing
    'Directory.Build.props' = $buildMetadata
}
foreach ($entry in $publicFiles.GetEnumerator()) {
    foreach ($prohibitedText in @(
        'PolyForm',
        'source-available',
        'source available',
        'noncommercial',
        'non-commercial',
        'commercial use is not granted'
    )) {
        if ($entry.Value.Contains($prohibitedText, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$($entry.Key) contains a stale or misleading license claim: $prohibitedText"
        }
    }
}

foreach ($requiredText in @(
    'https://spdx.org/licenses/GPL-3.0-only.html',
    'GPLv3 permits use, modification, commercial redistribution, and forks'
)) {
    if (-not $website.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The public website is missing the GPLv3 disclosure: $requiredText"
    }
}
$androidWebsiteHasLicense = $androidWebsite.Contains('GPL-3.0-only', [StringComparison]::Ordinal)
$androidGuideHasLicense = $androidGuide.Contains('GPL-3.0-only', [StringComparison]::Ordinal)
if (-not $androidWebsiteHasLicense -or -not $androidGuideHasLicense) {
    throw 'The Android public pages are missing the current GPLv3 disclosure.'
}
if (-not $workflow.Contains('./scripts/validation/Test-RepositoryLicensePolicy.ps1', [StringComparison]::Ordinal)) {
    throw 'The build workflow does not enforce the repository license policy.'
}

Write-Output 'GPL-3.0-only licensing, immutable license history, packaged notices, public claims, and CI enforcement are consistent.'
