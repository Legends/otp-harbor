<#
.SYNOPSIS
Fails closed when OTP Harbor's noncommercial source-available license posture drifts.

.DESCRIPTION
Validates the normalized canonical license text, public descriptions, historical-license boundary,
contribution notice, package metadata, and CI wiring. Earlier MIT grants remain documented and are
not represented as revoked.
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

$licensePath = Join-Path $repositoryRoot 'LICENSE.txt'
$license = Read-RepositoryFile 'LICENSE.txt'
$normalizedLicense = $license.Replace("`r`n", "`n")
$licenseBytes = [Text.Encoding]::UTF8.GetBytes($normalizedLicense)
$licenseHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($licenseBytes)).ToLowerInvariant()
$expectedLicenseHash = '563a89df8ab4f1c69a5fe744e1aa83d9fabf206d6d7037eafb9dbdf9f8b0a1aa'
if ($licenseHash -cne $expectedLicenseHash) {
    throw "LICENSE.txt differs from the reviewed PolyForm Noncommercial text and required notice. Expected $expectedLicenseHash; found $licenseHash."
}

$licensing = Read-RepositoryFile 'LICENSING.md'
foreach ($requiredText in @(
    'PolyForm Noncommercial License 1.0.0',
    'does not grant permission',
    'for a commercial purpose',
    'revisions through commit `b058f8c` were published under the MIT License',
    'cannot be withdrawn',
    'source-available, not OSI-approved open-source software'
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
$workflow = Read-RepositoryFile '.github/workflows/build-and-test.yml'
$desktopProject = Read-RepositoryFile 'TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj'
$androidProject = Read-RepositoryFile 'TOTP.UI.Avalonia.Android/TOTP.UI.Avalonia.Android.csproj'

foreach ($requiredText in @(
    'source-available, noncommercial',
    'PolyForm Noncommercial License 1.0.0',
    'Commercial use is not granted',
    'immutable MIT status of earlier published revisions'
)) {
    if (-not $readme.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "README licensing disclosure is missing: $requiredText"
    }
}

foreach ($requiredText in @(
    '<PackageLicenseExpression>PolyForm-Noncommercial-1.0.0</PackageLicenseExpression>',
    '<PackageRequireLicenseAcceptance>true</PackageRequireLicenseAcceptance>',
    'Source-available, noncommercial'
)) {
    if (-not $buildMetadata.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "Build metadata is missing the noncommercial license marker: $requiredText"
    }
}

if (-not $contributing.Contains('you agree to license it under the', [StringComparison]::Ordinal)) {
    throw 'The contribution guide does not state the incoming contribution license.'
}
if (-not $thirdPartyNotices.Contains('Those licenses remain separate from OTP Harbor', [StringComparison]::Ordinal)) {
    throw 'Third-party notices do not distinguish dependency licenses from the project license.'
}
if (-not $signPathRunbook.Contains('source-available; not OSI-approved', [StringComparison]::Ordinal)) {
    throw 'The SignPath runbook still implies eligibility through an OSI-approved license.'
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
        'MIT-licensed',
        'MIT licensed',
        'distributed under [MIT]',
        'https://opensource.org/license/mit',
        'is an open-source',
        'is open-source',
        'Open source ·',
        'Open-source project',
        'quelloffen',
        'de código abierto'
    )) {
        if ($entry.Value.Contains($prohibitedText, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$($entry.Key) contains a stale or misleading license claim: $prohibitedText"
        }
    }
}

foreach ($requiredText in @(
    'https://polyformproject.org/licenses/noncommercial/1.0.0',
    'source-available, not OSI-approved open-source software'
)) {
    if (-not $website.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The public website is missing the current license disclosure: $requiredText"
    }
}
$androidWebsiteHasLicense = $androidWebsite.Contains('PolyForm Noncommercial 1.0.0', [StringComparison]::Ordinal)
$androidGuideHasLicense = $androidGuide.Contains('PolyForm Noncommercial 1.0.0', [StringComparison]::Ordinal)
if (-not $androidWebsiteHasLicense -or -not $androidGuideHasLicense) {
    throw 'The Android public pages are missing the current license disclosure.'
}
if (-not $workflow.Contains('./scripts/validation/Test-RepositoryLicensePolicy.ps1', [StringComparison]::Ordinal)) {
    throw 'The build workflow does not enforce the repository license policy.'
}

Write-Output 'PolyForm Noncommercial licensing, historical MIT boundaries, public claims, and CI enforcement are consistent.'
