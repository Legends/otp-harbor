<#
.SYNOPSIS
Synchronizes the reviewed v3 Windows Store campaign into the committed submission tree.

.DESCRIPTION
Copies the deterministic localized campaign generated under artifacts/marketing into
packaging/windows-store/screenshots. Files not in the reviewed ten-image campaign are removed so
v2 screenshots cannot be submitted accidentally. Every copied file is hash-verified.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$sourceRoot = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot 'artifacts/marketing/store/windows/promotional')).Path
$destinationRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'packaging/windows-store/screenshots'))
$expectedFiles = @(
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
$cultures = @('en-US', 'de-DE', 'fr-FR', 'es-ES')

[IO.Directory]::CreateDirectory($destinationRoot) | Out-Null
$expectedDestinations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($culture in $cultures) {
    $sourceDirectory = Join-Path $sourceRoot $culture
    $destinationDirectory = [IO.Path]::GetFullPath((Join-Path $destinationRoot "$culture/marketing"))
    if (-not $destinationDirectory.StartsWith(
        $destinationRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Store screenshot destination escaped its root: $destinationDirectory"
    }
    [IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null

    foreach ($fileName in $expectedFiles) {
        $source = Join-Path $sourceDirectory $fileName
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Generated v3 Store campaign file is missing: $source"
        }
        $destination = Join-Path $destinationDirectory $fileName
        Copy-Item -LiteralPath $source -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
            throw "Store campaign hash mismatch after copy: $destination"
        }
        [void]$expectedDestinations.Add([IO.Path]::GetFullPath($destination))
    }
}

foreach ($existing in Get-ChildItem -LiteralPath $destinationRoot -Recurse -File) {
    $resolvedExisting = [IO.Path]::GetFullPath($existing.FullName)
    if (-not $resolvedExisting.StartsWith(
        $destinationRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Store screenshot cleanup target escaped its root: $resolvedExisting"
    }
    if (-not $expectedDestinations.Contains($resolvedExisting)) {
        Remove-Item -LiteralPath $resolvedExisting -Force
    }
}

Write-Output "Synchronized $($cultures.Count * $expectedFiles.Count) reviewed v3 Store campaign images."
