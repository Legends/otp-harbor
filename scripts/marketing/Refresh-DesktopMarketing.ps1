<#
.SYNOPSIS
Refreshes desktop marketing derivatives from the canonical live-app captures.
.DESCRIPTION
Optionally makes the Settings corners transparent, regenerates website/Store assets,
validates them, then copies the localized Store campaign into the local submission tree.
Does not launch/control the app, publish the website or submit anything to a store.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 300)][int]$SettingsCornerRadius,
    [string]$PythonExecutable = 'python'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
& $PythonExecutable (Join-Path $PSScriptRoot 'marketing_environment.py')
if ($LASTEXITCODE -ne 0) { throw 'Use a Python environment with scripts/marketing/requirements.txt installed.' }

if ($PSBoundParameters.ContainsKey('SettingsCornerRadius')) {
    & $PythonExecutable (Join-Path $PSScriptRoot 'Mask-DesktopCaptureCorners.py') --screen settings --radius $SettingsCornerRadius --replace
    if ($LASTEXITCODE -ne 0) { throw 'Settings corner cleanup failed.' }
}
& $PythonExecutable (Join-Path $PSScriptRoot 'Record-MarketingCaptures.py')
if ($LASTEXITCODE -ne 0) { throw 'Capture provenance recording failed.' }
& $PythonExecutable (Join-Path $PSScriptRoot 'Generate-MarketingAssets.py') --platform desktop
if ($LASTEXITCODE -ne 0) { throw 'Desktop marketing generation failed.' }
& $PythonExecutable (Join-Path $PSScriptRoot 'Validate-MarketingAssets.py') --require-captures --platform desktop
if ($LASTEXITCODE -ne 0) { throw 'Marketing asset validation failed.' }
& (Join-Path $PSScriptRoot 'Sync-WindowsStoreSubmissionAssets.ps1')
Write-Output "Desktop marketing refreshed under $root/artifacts/marketing; publication review is still required."
