#requires -Version 7.0
param([Parameter(Mandatory)][string]$ReleaseTag, [Parameter(Mandatory)][string]$SourceCommit)
$ErrorActionPreference = 'Stop'
if ($ReleaseTag -cnotmatch '^(?:v|android-v)(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$') {
    throw 'New production candidates require canonical stable release tags.'
}
if ($SourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'A full immutable source commit is required.' }
$actual = & git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual -cne $SourceCommit) { throw 'Release source checkout mismatch.' }
if ($ReleaseTag.StartsWith('android-v', [StringComparison]::Ordinal)) {
    & (Join-Path $PSScriptRoot '../android/Get-AndroidReleaseVersion.ps1') -ReleaseTag $ReleaseTag | Out-Null
}
else { & (Join-Path $PSScriptRoot '../windows/Get-MicrosoftStoreVersion.ps1') -ReleaseTag $ReleaseTag | Out-Null }
