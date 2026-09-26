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

$websiteFiles = @(
    '01-encrypted-local-vault.png',
    '02-camera-and-google-qr.png',
    '03-biometric-quick-unlock.png',
    '04-swipe-manage-show-qr.png',
    '05-backup-and-languages.png'
)
foreach ($file in $websiteFiles) {
    $info = Get-PngInfo "packaging/android/marketing/en-US/$file"
    if ($info.Width -ne 1920 -or $info.Height -ne 1080 -or $info.BitDepth -ne 8 -or $info.ColorType -ne 2) {
        throw "Website campaign asset must be an opaque 24-bit 1920x1080 PNG: $file"
    }
}

$phoneFiles = @(
    '01-local-vault-1080x1920.png',
    '02-swipe-and-qr-1080x1920.png',
    '03-camera-google-import-1080x1920.png',
    '04-unlock-methods-1080x1920.png',
    '05-encrypted-backup-1080x1920.png',
    '06-four-languages-1080x1920.png'
)
foreach ($file in $phoneFiles) {
    $info = Get-PngInfo "fastlane/metadata/android/en-US/images/phoneScreenshots/$file"
    if ($info.Width -ne 1080 -or $info.Height -ne 1920 -or $info.BitDepth -ne 8 -or $info.ColorType -ne 2) {
        throw "Android store phone asset must be an opaque 24-bit 1080x1920 PNG: $file"
    }
}

$feature = Get-PngInfo 'fastlane/metadata/android/en-US/images/featureGraphic.png'
if ($feature.Width -ne 1024 -or $feature.Height -ne 500 -or $feature.BitDepth -ne 8 -or $feature.ColorType -ne 2) {
    throw 'The Android store feature graphic must be an opaque 24-bit 1024x500 PNG.'
}

$icon = Get-PngInfo 'fastlane/metadata/android/en-US/images/icon.png'
if ($icon.Width -ne 512 -or $icon.Height -ne 512 -or $icon.BitDepth -ne 8 -or $icon.ColorType -ne 6 -or $icon.Length -ge 1MB) {
    throw 'The Android store app icon must be a 32-bit 512x512 PNG with alpha below 1,024 KB.'
}

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

foreach ($source in @(
    '01-account-list.jpg',
    '02-swipe-actions.jpg',
    '02-swipe-delete.mp4',
    '03-account-qr.jpg',
    '04-camera-scanner.png',
    '05-language-settings.jpg',
    '06-security-settings.jpg',
    '07-import-export-settings.jpg',
    '08-lock-screen.jpg',
    '09-synthetic-qr-source.png'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot "packaging/android/marketing/source/captures/$source") -PathType Leaf)) {
        throw "Reviewed Android marketing source is missing: $source"
    }
}

Write-Output 'Android website and shared F-Droid/Google Play metadata meet the required content and image constraints.'
