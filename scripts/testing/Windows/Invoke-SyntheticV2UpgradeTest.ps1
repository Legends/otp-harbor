<#
.SYNOPSIS
Seeds encrypted synthetic v2 data for migration by the real installed v3 app.
.DESCRIPTION
Seed preserves existing data directories beside their originals, then installs only fictional
encrypted v2 data. Verify is read-only against application files. No v2 installer is required.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Seed', 'Verify')][string]$Phase)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. ([IO.Path]::Combine($PSScriptRoot, 'StoreUpgrade.Preflight.ps1'))
Invoke-UpgradePreflight -RequireStore -VerifierPath ([IO.Path]::Combine($PSScriptRoot, 'verifier', 'StoreUpgradeVerifier.exe'))
. (Join-Path $PSScriptRoot 'StoreUpgrade.Testing.ps1')
$kit = [IO.Path]::GetFullPath($PSScriptRoot)
Assert-UpgradeRegularPath $kit
$verifier = Join-Path $kit 'verifier/StoreUpgradeVerifier.exe'
if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) { throw 'Run this script from the prepared test kit.' }
Assert-UpgradeAppClosed
$package = Get-UpgradeStorePackage
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$statePath = Join-Path $kit 'synthetic-upgrade-state.json'
$directories = @(Get-UpgradeDataDirectories -PackageFamilyName $package.PackageFamilyName | ForEach-Object { [IO.Path]::GetFullPath($_) })

if ($Phase -eq 'Seed') {
    if (Test-Path -LiteralPath $statePath) { throw 'Synthetic data was already prepared. Use VerifySyntheticV3.cmd after opening/unlocking v3.' }
    foreach ($directory in $directories) { Assert-UpgradeRegularPath $directory }
    $local = [Environment]::GetFolderPath('LocalApplicationData')
    $legacy = [IO.Path]::GetFullPath((Join-Path (Join-Path (Join-Path $local 'Packages') $package.PackageFamilyName) 'LocalCache/Roaming/TOTP-Manager'))
    if ($directories -cnotcontains $legacy) { throw 'Unexpected legacy destination.' }
    Protect-UpgradeKitDirectory $kit
    $source = Join-Path $kit "synthetic-v2-$([Guid]::NewGuid().ToString('N'))"
    & $verifier seed-v2 $source
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic v2 generation failed; application data was not changed.' }
    $hashes = [ordered]@{}
    foreach ($name in @('master.totp', 'authorization-envelope.bin', 'master.totp.bak1')) {
        $hashes[$name] = Get-UpgradeHash (Join-Path $source $name)
    }
    $state = [ordered]@{
        Schema = 1; Kind = 'SyntheticV2VaultMigration'; Stage = 'Prepared'
        Sid = $sid; Machine = [Environment]::MachineName; PackageFamilyName = $package.PackageFamilyName
        PackageBefore = $package.PackageFullName; Source = $source; Hashes = $hashes
        PreservedDirectories = @(); Verifications = @()
    }
    Save-UpgradeState $state $statePath
    foreach ($directory in $directories) {
        $preserved = Move-UpgradeDataAside $directory
        if ($null -ne $preserved) {
            $state.PreservedDirectories = @($state.PreservedDirectories) + $preserved
            Save-UpgradeState $state $statePath
            Write-Output "Preserved existing data: $($preserved.Preserved)"
        }
    }
    # Seed the physical MSIX-virtualized Roaming directory used by this Store family.
    Assert-UpgradeRegularPath $legacy
    if (Test-Path -LiteralPath $legacy) { throw 'Occupied legacy destination.' }
    New-Item -ItemType Directory -Path $legacy -Force | Out-Null
    Protect-UpgradeKitDirectory $legacy
    foreach ($name in $hashes.Keys) {
        $destination = Join-Path $legacy $name
        [IO.File]::Copy((Join-Path $source $name), $destination, $false)
        if ((Get-UpgradeHash $destination) -cne $hashes[$name]) { throw 'Synthetic v2 installation verification failed.' }
    }
    $state.Stage = 'Seeded'
    Save-UpgradeState $state $statePath
    Write-Output 'READY: encrypted v2 vault with four synthetic accounts installed. No existing data was deleted.'
    Write-Output 'Open/update OTP Harbor v3 and unlock with test password: OTP-Harbor-v2-test-only!'
    Write-Output 'Confirm four accounts appear, close the app, then run VerifySyntheticV3.cmd.'
    return
}

if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) { throw 'Run SeedSyntheticV2.cmd first.' }
Assert-UpgradeRegularPath $statePath
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.Schema -ne 1 -or $state.Kind -cne 'SyntheticV2VaultMigration' -or $state.Stage -cne 'Seeded' -or
    $state.Sid -cne $sid -or $state.Machine -cne [Environment]::MachineName -or
    $state.PackageFamilyName -cne $package.PackageFamilyName -or ([version]$package.Version).Major -lt 3) {
    throw 'Expected the seeded test profile and the same Store package family running v3 or later.'
}
foreach ($property in $state.Hashes.PSObject.Properties) {
    if ((Get-UpgradeHash (Join-Path $state.Source $property.Name)) -cne $property.Value) { throw 'The encrypted v2 baseline changed.' }
}
$current = Get-UpgradeVaultDirectory $directories 'master.vault'
$vault = Join-Path $current 'master.vault'
$envelope = Join-Path $current 'authorization-envelope.bin'
$before = Get-UpgradeHash $vault
& $verifier synthetic-v3 (Join-Path $state.Source 'master.totp') (Join-Path $state.Source 'authorization-envelope.bin') $vault $envelope
if ($LASTEXITCODE -ne 0) { throw 'The app did not preserve the synthetic v2 accounts and recovery files.' }
foreach ($property in $state.Hashes.PSObject.Properties) {
    if ((Get-UpgradeHash (Join-Path (Join-Path $current 'pre-upgrade-rollback') $property.Name)) -cne $property.Value) {
        throw 'The app migration did not preserve a v2 rollback file.'
    }
}
if ((Get-UpgradeHash $vault) -cne $before) { throw 'Application data changed during read-only verification.' }
$state.Verifications = @($state.Verifications) + [ordered]@{
    VerifiedUtc = [DateTime]::UtcNow.ToString('o'); Result = 'PASS'; AccountCount = 4; Package = $package.PackageFullName
}
Save-UpgradeState $state $statePath
Write-Output 'PASS: actual v3 app migrated all four synthetic v2 accounts with original IDs, seeds, names, periods and recovery files.'
Write-Output 'Restart/unlock/close v3 and run VerifySyntheticV3.cmd again to check the next load.'
