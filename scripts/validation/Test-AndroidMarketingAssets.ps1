<#
.SYNOPSIS
Validates the reproducible Android website and shared F-Droid/Google Play store-listing assets.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path

function Get-PngInfo([string]$RelativePath) {
    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Android marketing asset is missing: $RelativePath"
    }
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 26 -or [Convert]::ToHexString($bytes[0..7]) -cne '89504E470D0A1A0A') {
        throw "Android marketing asset is not a valid PNG: $RelativePath"
    }
    return [pscustomobject]@{
        Width = [BitConverter]::ToUInt32([byte[]]($bytes[19], $bytes[18], $bytes[17], $bytes[16]), 0)
        Height = [BitConverter]::ToUInt32([byte[]]($bytes[23], $bytes[22], $bytes[21], $bytes[20]), 0)
        BitDepth = $bytes[24]
        ColorType = $bytes[25]
        Length = $bytes.Length
    }
}

function Read-RequiredText([string]$RelativePath) {
    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Android store metadata is missing: $RelativePath"
    }
    return [IO.File]::ReadAllText($path)
}

function Assert-CanonicalMirror([string]$CanonicalRelativePath, [string]$MirrorRelativePath) {
    $canonicalPath = Join-Path $repositoryRoot $CanonicalRelativePath
    $mirrorPath = Join-Path $repositoryRoot $MirrorRelativePath
    if (-not (Test-Path -LiteralPath $canonicalPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $mirrorPath -PathType Leaf)) {
        throw "Android marketing mirror or canonical source is missing: $MirrorRelativePath"
    }
    if ((Get-FileHash -LiteralPath $canonicalPath -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath $mirrorPath -Algorithm SHA256).Hash) {
        throw "Android marketing mirror differs from its reviewed canonical asset: $MirrorRelativePath"
    }
}

$websiteFiles = @(
    '01-dark-account-list.png',
    '02-dark-settings.png',
    '03-dark-qr-scanning.png',
    '04-dark-backup-restore.png',
    '05-light-account-list.png',
    '06-light-settings.png',
    '07-light-qr-scanning.png',
    '08-light-backup-restore.png'
)
foreach ($file in $websiteFiles) {
    $info = Get-PngInfo "packaging/android/marketing/en-US/$file"
    if ($info.Width -ne 1920 -or $info.Height -ne 1080 -or $info.BitDepth -ne 8 -or $info.ColorType -ne 2) {
        throw "Website campaign asset must be an opaque 24-bit 1920x1080 PNG: $file"
    }
    Assert-CanonicalMirror `
        "artifacts/marketing/store/android/website-campaign/en-US/$file" `
        "packaging/android/marketing/en-US/$file"
}
$actualWebsiteFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'packaging/android/marketing/en-US') -File |
    Select-Object -ExpandProperty Name | Sort-Object)
if (Compare-Object ($websiteFiles | Sort-Object) $actualWebsiteFiles) {
    throw 'The committed Android campaign contains stale or missing files.'
}

$phoneFiles = @(
    '01-dark-account-list-1080x1920.png',
    '02-dark-settings-1080x1920.png',
    '03-dark-scanner-1080x1920.png',
    '04-light-account-list-1080x1920.png',
    '05-light-settings-1080x1920.png',
    '06-light-scanner-1080x1920.png'
)
foreach ($file in $phoneFiles) {
    $info = Get-PngInfo "fastlane/metadata/android/en-US/images/phoneScreenshots/$file"
    if ($info.Width -ne 1080 -or $info.Height -ne 1920 -or $info.BitDepth -ne 8 -or $info.ColorType -ne 2) {
        throw "Android store phone asset must be an opaque 24-bit 1080x1920 PNG: $file"
    }
    Assert-CanonicalMirror `
        "artifacts/marketing/store/android/phone-screenshots/$file" `
        "fastlane/metadata/android/en-US/images/phoneScreenshots/$file"
}
$actualPhoneFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'fastlane/metadata/android/en-US/images/phoneScreenshots') -File |
    Select-Object -ExpandProperty Name | Sort-Object)
if (Compare-Object ($phoneFiles | Sort-Object) $actualPhoneFiles) {
    throw 'The committed Android phone screenshots contain stale or missing files.'
}

$feature = Get-PngInfo 'fastlane/metadata/android/en-US/images/featureGraphic.png'
if ($feature.Width -ne 1024 -or $feature.Height -ne 500 -or $feature.BitDepth -ne 8 -or $feature.ColorType -ne 2) {
    throw 'The Android store feature graphic must be an opaque 24-bit 1024x500 PNG.'
}
Assert-CanonicalMirror 'artifacts/marketing/store/android/featureGraphic.png' 'fastlane/metadata/android/en-US/images/featureGraphic.png'

$icon = Get-PngInfo 'fastlane/metadata/android/en-US/images/icon.png'
if ($icon.Width -ne 512 -or $icon.Height -ne 512 -or $icon.BitDepth -ne 8 -or $icon.ColorType -ne 6 -or $icon.Length -ge 1MB) {
    throw 'The Android store app icon must be a 32-bit 512x512 PNG with alpha below 1,024 KB.'
}
Assert-CanonicalMirror 'artifacts/marketing/store/android/icon.png' 'fastlane/metadata/android/en-US/images/icon.png'

foreach ($locale in @('en-US', 'de-DE', 'fr-FR', 'es-ES')) {
    $metadataRoot = "fastlane/metadata/android/$locale"
    $title = (Read-RequiredText "$metadataRoot/title.txt").Trim()
    $summary = (Read-RequiredText "$metadataRoot/short_description.txt").Trim()
    $description = (Read-RequiredText "$metadataRoot/full_description.txt").Trim()
    if ($title -cne 'OTP Harbor') {
        throw "Android store title is inconsistent for locale '$locale'."
    }
    if ($summary.Length -eq 0 -or $summary.Length -gt 80) {
        throw "Android store short description must contain 1-80 characters for locale '$locale'."
    }
    foreach ($claim in @('TOTP', 'otpauth', '2FAS', 'Android Keystore')) {
        if (-not $description.Contains($claim, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Android store full description for '$locale' is missing reviewed claim '$claim'."
        }
    }
}

if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'scripts/assets/New-AndroidMarketingImages.ps1')) {
    throw 'The obsolete v2 Android marketing generator must not be restored.'
}
$obsoleteCaptureRoot = Join-Path $repositoryRoot 'packaging/android/marketing/source/captures'
if ((Test-Path -LiteralPath $obsoleteCaptureRoot) -and
    @(Get-ChildItem -LiteralPath $obsoleteCaptureRoot -Recurse -File).Count -ne 0) {
    throw 'Obsolete v2 Android marketing captures remain in the packaging source tree.'
}

Write-Output 'Android v3 website and shared F-Droid/Google Play metadata meet the required content and image constraints.'
