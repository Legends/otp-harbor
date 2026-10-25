[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$Snapshot,
    [string]$DesktopExecutable,
    [switch]$NoLaunch
)

. (Join-Path $PSScriptRoot 'DevelopmentAccountSnapshot.Common.ps1')

if (Get-Process -Name 'TOTP.UI.Avalonia.Desktop' -ErrorAction SilentlyContinue) {
    throw 'Close OTP Harbor desktop before restoring its encrypted development vault.'
}
$source = Get-ValidatedSnapshot -Path $Snapshot
$vaultPath = Join-Path $script:DesktopVaultRoot $script:VaultFileName
$envelopePath = Join-Path $script:DesktopVaultRoot $script:EnvelopeFileName
if (-not $PSCmdlet.ShouldProcess($script:DesktopVaultRoot, "Restore encrypted account snapshot '$($source.Path)'")) { return }

if ((Test-Path -LiteralPath $vaultPath -PathType Leaf) -and
    (Test-Path -LiteralPath $envelopePath -PathType Leaf)) {
    $recovery = New-EncryptedSnapshot -VaultPath $vaultPath -EnvelopePath $envelopePath -Kind desktop-recovery
    Write-Host "Encrypted desktop recovery snapshot: $recovery"
}

$temporaryVault = Join-Path $script:DesktopVaultRoot ('.master.vault.restore-' + [guid]::NewGuid().ToString('N'))
$temporaryEnvelope = Join-Path $script:DesktopVaultRoot ('.authorization-envelope.restore-' + [guid]::NewGuid().ToString('N'))
try {
    Copy-Item -LiteralPath $source.VaultPath -Destination $temporaryVault
    Copy-Item -LiteralPath $source.EnvelopePath -Destination $temporaryEnvelope
    Move-Item -LiteralPath $temporaryVault -Destination $vaultPath -Force
    Move-Item -LiteralPath $temporaryEnvelope -Destination $envelopePath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryVault -PathType Leaf) { Remove-Item -LiteralPath $temporaryVault -Force }
    if (Test-Path -LiteralPath $temporaryEnvelope -PathType Leaf) { Remove-Item -LiteralPath $temporaryEnvelope -Force }
}
if ((Get-Sha256 -Path $vaultPath) -ne $source.VaultHash -or
    (Get-Sha256 -Path $envelopePath) -ne $source.EnvelopeHash) {
    throw 'Desktop restore verification failed.'
}

Write-Host 'The desktop Debug vault now contains the accounts from the encrypted source snapshot.'
if (-not $NoLaunch) {
    if ([string]::IsNullOrWhiteSpace($DesktopExecutable)) {
        $DesktopExecutable = Get-ChildItem `
            -Path (Join-Path $PSScriptRoot '..\..\TOTP.UI.Avalonia.Desktop\bin\Debug') `
            -Filter 'TOTP.UI.Avalonia.Desktop.exe' `
            -File `
            -Recurse `
            -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if ([string]::IsNullOrWhiteSpace($DesktopExecutable) -or
        -not (Test-Path -LiteralPath $DesktopExecutable -PathType Leaf)) {
        throw 'Restore succeeded, but no Debug desktop executable was found. Start the app manually or pass -DesktopExecutable.'
    }
    Start-Process -FilePath $DesktopExecutable -WindowStyle Hidden
}
