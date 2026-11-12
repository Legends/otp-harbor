[CmdletBinding()]
param([string]$Device)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Testing.Common.ps1')
. (Resolve-OtpHarborTestingPath 'Windows/DevelopmentAccountSnapshot.Common.ps1')

$repositoryRoot = Get-OtpHarborRepositoryRoot -StartDirectory $PSScriptRoot
$kit = Join-Path $repositoryRoot 'artifacts/testing/v3-release-physical'
& (Resolve-OtpHarborTestingPath 'Windows/Prepare-V3ReleasePhysicalTestKit.ps1') | Out-Null
$connection = Resolve-AndroidDevice -Device $Device
$remoteRoot = '/sdcard/Download/OTP-Harbor-v3-release-test'
& $connection.Adb -s $connection.Device shell mkdir -p $remoteRoot
if ($LASTEXITCODE -ne 0) { throw 'Could not create the Android test-kit directory.' }

foreach ($source in Get-ChildItem -LiteralPath $kit -File) {
    $destination = "$remoteRoot/$($source.Name)"
    & $connection.Adb -s $connection.Device push $source.FullName $destination | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not copy $($source.Name) to Android." }
    $remoteOutput = (& $connection.Adb -s $connection.Device shell sha256sum $destination | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $remoteOutput -notmatch '^(?<Hash>[0-9a-fA-F]{64})\s') {
        throw "Could not verify $($source.Name) on Android."
    }
    if ($Matches.Hash.ToLowerInvariant() -ne (Get-Sha256 -Path $source.FullName)) {
        throw "Android checksum verification failed for $($source.Name)."
    }
}

Write-Output "Copied and verified the v3 physical-test kit on $($connection.Device): $remoteRoot"
