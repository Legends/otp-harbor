<#
.SYNOPSIS
Synchronizes reviewed v3 Android marketing derivatives into the committed submission trees.

.DESCRIPTION
Copies the manifest-backed v3 campaign into packaging/android/marketing and the Play/Fastlane
assets into fastlane/metadata. Stale v2 images are removed and every copy is hash-verified.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$canonicalRoot = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot 'artifacts/marketing/store/android')).Path

function Sync-AssetSet {
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationDirectory,
        [Parameter(Mandatory)][string[]]$ExpectedFiles
    )

    $sourceRoot = [IO.Path]::GetFullPath($SourceDirectory)
    $destinationRoot = [IO.Path]::GetFullPath($DestinationDirectory)
    [IO.Directory]::CreateDirectory($destinationRoot) | Out-Null
    $expectedDestinations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($fileName in $ExpectedFiles) {
        $source = Join-Path $sourceRoot $fileName
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Generated v3 Android asset is missing: $source"
        }
        $destination = Join-Path $destinationRoot $fileName
        Copy-Item -LiteralPath $source -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
            throw "Android asset hash mismatch after copy: $destination"
        }
        [void]$expectedDestinations.Add([IO.Path]::GetFullPath($destination))
    }

    foreach ($existing in Get-ChildItem -LiteralPath $destinationRoot -File) {
        $resolvedExisting = [IO.Path]::GetFullPath($existing.FullName)
        if (-not $resolvedExisting.StartsWith(
            $destinationRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Android asset cleanup target escaped its root: $resolvedExisting"
        }
        if (-not $expectedDestinations.Contains($resolvedExisting)) {
            Remove-Item -LiteralPath $resolvedExisting -Force
        }
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
$phoneFiles = @(
    '01-dark-account-list-1080x1920.png',
    '02-dark-settings-1080x1920.png',
    '03-dark-scanner-1080x1920.png',
    '04-light-account-list-1080x1920.png',
    '05-light-settings-1080x1920.png',
    '06-light-scanner-1080x1920.png'
)

Sync-AssetSet `
    -SourceDirectory (Join-Path $canonicalRoot 'website-campaign/en-US') `
    -DestinationDirectory (Join-Path $repositoryRoot 'packaging/android/marketing/en-US') `
    -ExpectedFiles $websiteFiles
Sync-AssetSet `
    -SourceDirectory (Join-Path $canonicalRoot 'phone-screenshots') `
    -DestinationDirectory (Join-Path $repositoryRoot 'fastlane/metadata/android/en-US/images/phoneScreenshots') `
    -ExpectedFiles $phoneFiles
Sync-AssetSet `
    -SourceDirectory $canonicalRoot `
    -DestinationDirectory (Join-Path $repositoryRoot 'fastlane/metadata/android/en-US/images') `
    -ExpectedFiles @('featureGraphic.png', 'icon.png')

Write-Output 'Synchronized 8 v3 Android campaign images and 8 Play/Fastlane assets.'
