<#
.SYNOPSIS
Validates the Android distribution-channel signing and migration policy.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

function Read-RequiredFile([string]$RelativePath) {
    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Android distribution-policy file is missing: $RelativePath"
    }
    return [IO.File]::ReadAllText($path)
}

$policy = Read-RequiredFile 'docs\android\FDROID_SIGNING_AND_UPDATES.md'
foreach ($required in @(
    'not yet available from the official F-Droid repository',
    'F-Droid-managed signing',
    'will not be uploaded or disclosed to F-Droid',
    'Android will reject an in-place install from a differently signed channel',
    'Export a password-protected encrypted `.totp` backup',
    'Android system backup is disabled',
    'cross-channel in-place updates remain unsupported')) {
    if (-not $policy.Contains($required, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Android distribution policy is missing required boundary: $required"
    }
}

$project = Read-RequiredFile 'TOTP.UI.Avalonia.Android\TOTP.UI.Avalonia.Android.csproj'
if (-not $project.Contains(
        '<AndroidProductionApplicationId>io.github.legends.otpharbor</AndroidProductionApplicationId>',
        [StringComparison]::Ordinal)) {
    throw 'Android distribution channels must retain the permanent production application ID.'
}

$manifest = Read-RequiredFile 'TOTP.UI.Avalonia.Android\Properties\AndroidManifest.xml'
if (-not $manifest.Contains('android:allowBackup="false"', [StringComparison]::Ordinal) -or
    -not $manifest.Contains('android:fullBackupContent="false"', [StringComparison]::Ordinal)) {
    throw 'Android distribution migration policy requires system backup to remain disabled.'
}

$listingFiles = Get-ChildItem (Join-Path $repositoryRoot 'fastlane\metadata\android') `
    -Recurse -File -Filter '*.txt'
foreach ($listingFile in $listingFiles) {
    $text = [IO.File]::ReadAllText($listingFile.FullName)
    if ($text.Contains('F-Droid', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Android listing prematurely claims or discusses F-Droid availability: $($listingFile.FullName)"
    }
}

$workflow = Read-RequiredFile '.github\workflows\build-and-test.yml'
if (-not $workflow.Contains(
        './scripts/validation/Test-AndroidDistributionChannels.ps1',
        [StringComparison]::Ordinal)) {
    throw 'Android distribution-channel policy is not enforced by CI.'
}

Write-Output 'Android distribution signatures, update ownership, and encrypted cross-channel migration remain explicit.'
