[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param([string]$Device)

. (Join-Path $PSScriptRoot 'DevelopmentAccountSnapshot.Common.ps1')

$connection = Resolve-AndroidDevice -Device $Device
Assert-AndroidDebugPackage -Connection $connection
$snapshotPath = New-AndroidEncryptedSnapshot -Connection $connection -Kind android-recovery
$snapshotManifest = Get-Content -LiteralPath (Join-Path $snapshotPath 'manifest.json') -Raw | ConvertFrom-Json
if (-not $PSCmdlet.ShouldProcess("OTP Harbor Debug on $($connection.Device)", 'Delete every account')) {
    Write-Host "Encrypted Android recovery snapshot created without deleting accounts: $snapshotPath"
    return
}

$receiver = "$script:AndroidPackageName/$script:AndroidPackageName.DebugAccountDataReceiver"
$action = "$script:AndroidPackageName.action.DELETE_ALL_ACCOUNTS"
& $connection.Adb -s $connection.Device shell am broadcast -a $action -n $receiver | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The Android Debug delete request could not be delivered.' }

$deadline = [DateTime]::UtcNow.AddSeconds(20)
do {
    Start-Sleep -Milliseconds 350
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ('otp-harbor-verify-' + [guid]::NewGuid().ToString('N'))
    try {
        Copy-AndroidPrivateFileToHost -Connection $connection -RelativePath "$script:AndroidVaultRoot/$script:VaultFileName" -Destination $temporary
        if ((Get-Sha256 -Path $temporary) -ne [string]$snapshotManifest.vault.sha256) {
            Write-Host "All Android development accounts were deleted. Encrypted recovery snapshot: $snapshotPath"
            return
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force }
    }
} while ([DateTime]::UtcNow -lt $deadline)

throw "The Android vault did not change. Keep the Debug app open and unlocked, then try again. Recovery snapshot: $snapshotPath"
