<#
.SYNOPSIS
Captures and verifies a synthetic Store v2-to-v3 upgrade in a clean test profile.
.DESCRIPTION
Never installs, uninstalls, resets or writes application data. Run Initialize BEFORE
starting v2, CaptureV2 after importing the fixture, and VerifyV3 after a real Store update.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Initialize', 'CaptureV2', 'VerifyV3')]
    [string]$Phase
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. ([IO.Path]::Combine($PSScriptRoot, 'StoreUpgrade.Preflight.ps1'))
Invoke-UpgradePreflight -RequireStore -VerifierPath ([IO.Path]::Combine($PSScriptRoot, 'verifier', 'StoreUpgradeVerifier.exe'))
. (Join-Path $PSScriptRoot 'StoreUpgrade.Testing.ps1')
$kit = [IO.Path]::GetFullPath($PSScriptRoot)
Assert-UpgradeRegularPath $kit
$statePath = Join-Path $kit 'upgrade-state.json'
$verifier = Join-Path $kit 'verifier/StoreUpgradeVerifier.exe'
if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) { throw 'Run the script from the prepared test kit.' }
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Assert-UpgradeAppClosed

if ($Phase -eq 'Initialize') {
    if (Test-Path -LiteralPath $statePath) { throw 'This kit already belongs to a test run. Prepare a new kit for a new run.' }
    Assert-UpgradeCleanProfile @(Get-UpgradeDataDirectories)
    Protect-UpgradeKitDirectory $kit
    $state = [ordered]@{
        Schema = 1; Sid = $sid; Machine = [Environment]::MachineName
        InitializedUtc = [DateTime]::UtcNow.ToString('o'); V2 = $null; Verifications = @()
    }
    Save-UpgradeState $state $statePath
    Write-Output 'Initialized clean test profile. Start Store v2, create a test password and import synthetic-v2-accounts.txt.'
    return
}

if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) { throw 'Run Initialize before creating the v2 test vault.' }
Assert-UpgradeRegularPath $statePath
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.Schema -ne 1 -or $state.Sid -cne $sid -or $state.Machine -cne [Environment]::MachineName) {
    throw 'This kit was initialized for another Windows user, machine or schema.'
}
$package = Get-UpgradeStorePackage
$directories = @(Get-UpgradeDataDirectories -PackageFamilyName $package.PackageFamilyName)

if ($Phase -eq 'CaptureV2') {
    if ($null -ne $state.V2) { throw 'A v2 baseline is already captured; it cannot be overwritten.' }
    if (([version]$package.Version).Major -ne 2) {
        throw "CaptureV2 requires an installed Store v2 package; found $($package.Version). To test with a generated encrypted v2 vault, run SeedSyntheticV2.cmd instead."
    }
    $directory = Get-UpgradeVaultDirectory $directories 'master.totp'
    $vault = Join-Path $directory 'master.totp'
    $envelope = Join-Path $directory 'authorization-envelope.bin'
    $originalVaultHash = Get-UpgradeHash $vault
    $originalEnvelopeHash = Get-UpgradeHash $envelope
    & $verifier v2 $vault $envelope
    if ($LASTEXITCODE -ne 0) { throw 'The synthetic v2 vault was not approved. No baseline was captured.' }
    if ((Get-UpgradeHash $vault) -cne $originalVaultHash -or (Get-UpgradeHash $envelope) -cne $originalEnvelopeHash) {
        throw 'Test files changed during validation. Close the application and retry.'
    }
    $baseline = Join-Path $kit 'baseline'
    if (Test-Path -LiteralPath $baseline) { throw 'A partial baseline exists. Use a new kit; nothing will be overwritten.' }
    New-Item -ItemType Directory -Path $baseline | Out-Null
    $hashes = [ordered]@{}
    foreach ($name in @('master.totp', 'authorization-envelope.bin', 'master.totp.bak1', 'master.totp.bak2',
            'master.totp.bak3', 'master.totp.bak4', 'master.totp.bak5')) {
        $source = Join-Path $directory $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $hash = Get-UpgradeHash $source
        $destination = Join-Path $baseline $name
        Copy-Item -LiteralPath $source -Destination $destination
        if ((Get-UpgradeHash $destination) -cne $hash) { throw 'Encrypted snapshot verification failed.' }
        $hashes[$name] = $hash
    }
    if ($hashes['master.totp'] -cne $originalVaultHash -or $hashes['authorization-envelope.bin'] -cne $originalEnvelopeHash) {
        throw 'The baseline changed after password verification.'
    }
    $state.V2 = [ordered]@{
        CapturedUtc = [DateTime]::UtcNow.ToString('o'); PackageFullName = $package.PackageFullName
        PackageFamilyName = $package.PackageFamilyName; Version = $package.Version.ToString()
        VaultDirectory = $directory; Hashes = $hashes
    }
    Save-UpgradeState $state $statePath
    Write-Output 'Captured encrypted v2 baseline. Take a VM checkpoint, then install the Store v3 update without uninstalling or resetting v2.'
    return
}

if ($null -eq $state.V2) { throw 'Capture a v2 baseline first.' }
if ($package.PackageFamilyName -cne $state.V2.PackageFamilyName -or
    ([version]$package.Version).Major -ne 3 -or ([version]$package.Version) -le ([version]$state.V2.Version)) {
    throw 'Expected a newer Store v3 package with the same package family as the captured v2 installation.'
}
$baseline = Join-Path $kit 'baseline'
foreach ($property in $state.V2.Hashes.PSObject.Properties) {
    if ((Get-UpgradeHash (Join-Path $baseline $property.Name)) -cne $property.Value) {
        throw 'The captured encrypted baseline changed.'
    }
}
$directory = Get-UpgradeVaultDirectory $directories 'master.vault'
$vault = Join-Path $directory 'master.vault'
$envelope = Join-Path $directory 'authorization-envelope.bin'
$vaultHash = Get-UpgradeHash $vault
$envelopeHash = Get-UpgradeHash $envelope
& $verifier v3 (Join-Path $baseline 'master.totp') (Join-Path $baseline 'authorization-envelope.bin') $vault $envelope
if ($LASTEXITCODE -ne 0) { throw 'The v3 migration or rollback verification failed.' }
foreach ($property in $state.V2.Hashes.PSObject.Properties) {
    if ((Get-UpgradeHash (Join-Path (Join-Path $directory 'pre-upgrade-rollback') $property.Name)) -cne $property.Value) {
        throw 'A rollback snapshot file differs from the captured v2 baseline.'
    }
}
if ((Get-UpgradeHash $vault) -cne $vaultHash -or (Get-UpgradeHash $envelope) -cne $envelopeHash) {
    throw 'Application data changed during the read-only verification.'
}
$state.Verifications = @($state.Verifications) + [ordered]@{
    VerifiedUtc = [DateTime]::UtcNow.ToString('o'); PackageFullName = $package.PackageFullName
    Version = $package.Version.ToString(); AccountCount = 4; Result = 'PASS'
    VaultSha256 = $vaultHash; EnvelopeSha256 = $envelopeHash
}
Save-UpgradeState $state $statePath
Write-Output 'PASS: Store identity/version, original password, all four accounts and IDs, every supported field, unchanged envelope, and encrypted rollback files verified.'
Write-Output 'Restart and unlock v3 once more, close it, and repeat VerifyV3. Complete the manual checklist in README.md.'
