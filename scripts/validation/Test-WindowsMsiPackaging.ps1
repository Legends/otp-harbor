[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$packagerPath = Join-Path $repositoryRoot 'scripts/release/New-WindowsMsi.ps1'
$workflowPath = Join-Path $repositoryRoot '.github/workflows/build-and-test.yml'
$manifestPath = Join-Path $repositoryRoot 'scripts/release/New-ReleaseArtifactManifest.ps1'

$packager = Get-Content -LiteralPath $packagerPath -Raw
$workflow = Get-Content -LiteralPath $workflowPath -Raw
$manifest = Get-Content -LiteralPath $manifestPath -Raw

foreach ($control in @(
    'Scope="perMachine"',
    'AllowSameVersionUpgrades="yes"',
    '-DistributionMode package-manager',
    '-DisableUpdates',
    "Remove-Item -LiteralPath `$bundledUpdater",
    'ProgramFiles6432Folder',
    'Advertise="yes"',
    'msi validate -sice ICE61',
    'ProgramMenuFolder'
)) {
    if (-not $packager.Contains($control, [StringComparison]::Ordinal)) {
        throw "The Windows MSI packager is missing required control: $control"
    }
}

foreach ($control in @(
    'dotnet tool install --global wix --version 5.0.2',
    'New-WindowsMsi.ps1',
    'OTP-Harbor-windows-x64-${{ steps.versioning.outputs.release_version }}.msi',
    'exactly five Windows/Linux artifacts'
)) {
    if (-not $workflow.Contains($control, [StringComparison]::Ordinal)) {
        throw "The release workflow is missing MSI control: $control"
    }
}

if (-not $manifest.Contains('format = "msi"', [StringComparison]::Ordinal) -or
    -not $manifest.Contains('ownership = "windows-installer"', [StringComparison]::Ordinal)) {
    throw 'The release manifest does not model the MSI as a separate installer-owned artifact.'
}

Write-Host 'Windows MSI packaging controls are present.'
