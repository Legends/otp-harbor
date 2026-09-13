<#
.SYNOPSIS
Validates the reproducible Android website and Google Play marketing asset sets.
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
    $info = Get-PngInfo "packaging/android/google-play/en-US/$file"
    if ($info.Width -ne 1080 -or $info.Height -ne 1920 -or $info.BitDepth -ne 8 -or $info.ColorType -ne 2) {
        throw "Google Play phone asset must be an opaque 24-bit 1080x1920 PNG: $file"
    }
}

$feature = Get-PngInfo 'packaging/android/google-play/en-US/feature-graphic-1024x500.png'
if ($feature.Width -ne 1024 -or $feature.Height -ne 500 -or $feature.BitDepth -ne 8 -or $feature.ColorType -ne 2) {
    throw 'The Google Play feature graphic must be an opaque 24-bit 1024x500 PNG.'
}

$icon = Get-PngInfo 'packaging/android/google-play/en-US/app-icon-512x512.png'
if ($icon.Width -ne 512 -or $icon.Height -ne 512 -or $icon.BitDepth -ne 8 -or $icon.ColorType -ne 6 -or $icon.Length -ge 1MB) {
    throw 'The Google Play app icon must be a 32-bit 512x512 PNG with alpha below 1,024 KB.'
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

Write-Output 'Android website and Google Play marketing assets meet the required formats and dimensions.'
