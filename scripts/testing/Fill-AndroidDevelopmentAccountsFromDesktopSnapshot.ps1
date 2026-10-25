[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$Snapshot,
    [string]$Device,
    [switch]$NoLaunch
)

. (Join-Path $PSScriptRoot 'DevelopmentAccountSnapshot.Common.ps1')

$source = Get-ValidatedSnapshot -Path $Snapshot
$connection = Resolve-AndroidDevice -Device $Device
Assert-AndroidDebugPackage -Connection $connection
if (-not $PSCmdlet.ShouldProcess("OTP Harbor Debug on $($connection.Device)", "Restore encrypted desktop account snapshot '$($source.Path)'")) { return }

$recovery = New-AndroidEncryptedSnapshot -Connection $connection -Kind android-recovery
Write-Host "Encrypted Android recovery snapshot: $recovery"
& $connection.Adb -s $connection.Device shell am force-stop $script:AndroidPackageName | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not stop the Android Debug app before restoring its vault.' }
Install-SnapshotOnAndroid -Connection $connection -Snapshot $source
Write-Host 'The Android Debug vault now contains the desktop snapshot accounts.'
Write-Warning 'Unlock Android with the desktop master password. Re-enroll biometric quick unlock if Android requests it.'

if (-not $NoLaunch) {
    & $connection.Adb -s $connection.Device shell monkey -p $script:AndroidPackageName -c android.intent.category.LAUNCHER 1 *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Restore succeeded, but launching the Android Debug app failed.' }
}
