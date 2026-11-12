[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param()

. (Join-Path $PSScriptRoot 'DevelopmentAccountSnapshot.Common.ps1')

$vaultPath = Join-Path $script:DesktopVaultRoot $script:VaultFileName
$envelopePath = Join-Path $script:DesktopVaultRoot $script:EnvelopeFileName
$processes = @(Get-Process -Name 'TOTP.UI.Avalonia.Desktop' -ErrorAction SilentlyContinue)
if ($processes.Count -ne 1) {
    throw 'Exactly one running OTP Harbor Debug desktop process is required. Start and unlock it first.'
}
$executable = $processes[0].Path
if ([string]::IsNullOrWhiteSpace($executable) -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'The running OTP Harbor desktop executable could not be resolved.'
}

$snapshotPath = New-EncryptedSnapshot `
    -VaultPath $vaultPath `
    -EnvelopePath $envelopePath `
    -Kind desktop-source `
    -SetAsDesktopSource
$beforeHash = Get-Sha256 -Path $vaultPath
if (-not $PSCmdlet.ShouldProcess('the unlocked OTP Harbor Debug desktop vault', 'Delete every account')) {
    Write-Host "Encrypted source snapshot created without deleting accounts: $snapshotPath"
    return
}

Start-Process -FilePath $executable -ArgumentList '--debug-delete-all-accounts' -WindowStyle Hidden
$deadline = [DateTime]::UtcNow.AddSeconds(20)
do {
    Start-Sleep -Milliseconds 250
    if ((Get-Sha256 -Path $vaultPath) -ne $beforeHash) {
        Write-Host "All desktop development accounts were deleted. Encrypted source snapshot: $snapshotPath"
        return
    }
} while ([DateTime]::UtcNow -lt $deadline)

throw "The desktop vault did not change. Ensure the running Debug app is unlocked and try again. Snapshot: $snapshotPath"
