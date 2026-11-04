[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('light','dark')][string]$Theme,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Screen,
    [switch]$ConfirmedSyntheticData
)
$ErrorActionPreference = 'Stop'
if (-not $ConfirmedSyntheticData) { throw 'Inspect all visible accounts and pass -ConfirmedSyntheticData.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$process = @(Get-Process -Name 'TOTP.UI.Avalonia.Desktop' -ErrorAction SilentlyContinue)
if ($process.Count -ne 1 -or $process[0].Path -notlike (Join-Path $root 'artifacts/dev/avalonia-windows-debug/*')) {
    throw 'Exactly one published Debug desktop app must be running.'
}
$output = Join-Path $root "artifacts/marketing/source/desktop/$Theme/$Screen.png"
& (Join-Path $root 'scripts/assets/Capture-WindowsStoreScreenshot.ps1') -OutputPath $output -WindowWidth 1366 -WindowHeight 768 -Scale 1 -ConfirmSyntheticData
if (-not (Test-Path -LiteralPath $output -PathType Leaf)) { throw 'Desktop capture failed.' }
& python (Join-Path $PSScriptRoot 'Record-MarketingCaptures.py')
if ($LASTEXITCODE -ne 0) { throw 'Could not record desktop capture.' }
Write-Host "Captured genuine desktop app window: $output"
