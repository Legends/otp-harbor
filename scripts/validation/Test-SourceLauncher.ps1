[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$launcherPath = Join-Path $repositoryRoot 'Start-OTP-Harbor.ps1'
if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw 'The source launcher is missing.'
}

. $launcherPath

function Assert-Plan {
    param(
        [Parameter(Mandatory)][string]$Expected,
        [Parameter(Mandatory)][string]$RepositoryState,
        [Parameter(Mandatory)][bool]$HasLocalChanges,
        [Parameter(Mandatory)][bool]$BuildIsCurrent
    )

    $actual = Get-OtpHarborLaunchPlan -RepositoryState $RepositoryState `
        -HasLocalChanges $HasLocalChanges -BuildIsCurrent $BuildIsCurrent
    if ($actual -cne $Expected) {
        throw "Expected launcher plan '$Expected', received '$actual'."
    }
}

Assert-Plan 'Run' 'Current' $false $true
Assert-Plan 'BuildAndRun' 'Current' $false $false
Assert-Plan 'UpdateBuildAndRun' 'Behind' $false $true
Assert-Plan 'UpdateBuildAndRun' 'Behind' $false $false
Assert-Plan 'BlockedLocalChanges' 'Current' $true $true
Assert-Plan 'BlockedLocalChanges' 'Behind' $true $false
Assert-Plan 'BlockedAhead' 'Ahead' $false $true
Assert-Plan 'BlockedDiverged' 'Diverged' $false $true

$launcherText = Get-Content -LiteralPath $launcherPath -Raw
foreach ($requiredGuard in @(
    'merge'', ''--ff-only''',
    'https://github.com/Legends/otp-harbor.git',
    '--no-build',
    '--no-restore'
)) {
    if (-not $launcherText.Contains($requiredGuard, [StringComparison]::Ordinal)) {
        throw "The source launcher is missing required guard: $requiredGuard"
    }
}

Write-Host 'Source launcher policy checks passed.'
