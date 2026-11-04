[CmdletBinding()]
param([ValidateSet('all','desktop','android')][string]$Platform='all')
$ErrorActionPreference='Stop'
& python (Join-Path $PSScriptRoot 'Generate-MarketingAssets.py') --platform $Platform
if ($LASTEXITCODE -ne 0) { throw 'Marketing generation failed.' }
if ($Platform -in @('all','android')) {
    & python (Join-Path $PSScriptRoot 'Generate-AndroidStoreAssets.py')
    if ($LASTEXITCODE -ne 0) { throw 'Android submission asset generation failed.' }
}
if ($Platform -eq 'all') {
    & (Join-Path $PSScriptRoot 'Validate-MarketingAssets.ps1') -RequireCaptures
} else {
    & (Join-Path $PSScriptRoot 'Validate-MarketingAssets.ps1')
}
if ($Platform -in @('all','desktop')) {
    & (Join-Path $PSScriptRoot 'Sync-WindowsStoreSubmissionAssets.ps1')
}
if ($Platform -in @('all','android')) {
    & (Join-Path $PSScriptRoot 'Sync-AndroidSubmissionAssets.ps1')
}
