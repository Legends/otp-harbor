<# .SYNOPSIS
    Runs synthetic verifier regressions and test-kit script safety checks without accessing app data.
#>
[CmdletBinding()]
param([switch]$ScriptsOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. ([IO.Path]::Combine($PSScriptRoot, 'StoreUpgrade.Preflight.ps1'))
Invoke-UpgradePreflight -RequireBuildTools:(-not $ScriptsOnly)
. (Join-Path $PSScriptRoot '../Testing.Common.ps1')
. (Join-Path $PSScriptRoot 'StoreUpgrade.Testing.ps1')
$repositoryRoot = Get-OtpHarborRepositoryRoot -StartDirectory $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "otp-harbor-upgrade-script-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot | Out-Null
function Assert-Test([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Action) {
    $threw = $false
    try { & $Action | Out-Null } catch { $threw = $true }
    Assert-Test $threw 'Expected the safety boundary to reject this scenario.'
}
try {
    foreach ($name in @('StoreUpgrade.Preflight.ps1', 'StoreUpgrade.Testing.ps1', 'Invoke-StoreUpgradeTest.ps1', 'Invoke-SyntheticV2UpgradeTest.ps1', 'Prepare-StoreUpgradeTestKit.ps1')) {
        $parseErrors = $null
        $tokens = $null
        [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name), [ref]$tokens, [ref]$parseErrors)
        Assert-Test ($parseErrors.Count -eq 0) "PowerShell parse failed: $name"
    }
    $roaming = Join-Path $testRoot 'Roaming'
    $local = Join-Path $testRoot 'Local'
    New-Item -ItemType Directory -Path $roaming, $local | Out-Null
    $hashFile = Join-Path $testRoot 'hash-ü.bin'
    [IO.File]::WriteAllBytes($hashFile, [Text.Encoding]::ASCII.GetBytes('abc'))
    & {
        function Get-FileHash { throw 'The kit must work without the Get-FileHash module.' }
        Assert-Test ((Get-UpgradeHash $hashFile) -ceq 'BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD') 'SHA-256 known-vector or Unicode-path check failed.'
        [IO.File]::WriteAllBytes($hashFile, [byte[]]@())
        Assert-Test ((Get-UpgradeHash $hashFile) -ceq 'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855') 'Empty-file SHA-256 check failed.'
    }
    Assert-Throws { Get-UpgradeHash (Join-Path $testRoot 'missing.bin') }
    $locked = [IO.File]::Open($hashFile, 'Open', 'ReadWrite', 'None')
    try { Assert-Throws { Get-UpgradeHash $hashFile } } finally { $locked.Dispose() }
    Assert-Throws { Invoke-UpgradePreflight -VerifierPath (Join-Path $testRoot 'missing-verifier.exe') }
    & {
        function Import-Module { throw 'Synthetic missing-module failure.' }
        Assert-Throws { Invoke-UpgradePreflight }
    }
    $family = 'Legends77.OTPHarbor_test'
    $directories = @(Get-UpgradeDataDirectories -RoamingRoot $roaming -LocalRoot $local -PackageFamilyName $family)
    Assert-Test ($directories.Count -eq 4) 'Raw and Store-virtualized directories must both be checked.'
    Assert-UpgradeCleanProfile $directories
    $old = $directories[0]
    $virtual = $directories[2]
    New-Item -ItemType Directory -Path $old | Out-Null
    Assert-Throws { Assert-UpgradeCleanProfile $directories }
    $guardMessage = ''
    try { Assert-UpgradeCleanProfile $directories } catch { $guardMessage = $_.Exception.Message }
    Assert-Test ($guardMessage.Contains($old)) 'The clean-profile error must identify the blocking legacy directory.'
    [IO.File]::WriteAllText((Join-Path $old 'master.totp'), 'synthetic encrypted bytes')
    Assert-Test ((Get-UpgradeVaultDirectory $directories 'master.totp') -ceq $old) 'Expected raw legacy vault detection.'
    New-Item -ItemType Directory -Path $virtual -Force | Out-Null
    try { Assert-UpgradeCleanProfile $directories } catch { $guardMessage = $_.Exception.Message }
    Assert-Test ($guardMessage.Contains($old) -and $guardMessage.Contains($virtual)) 'The error must identify every blocking data directory.'
    [IO.File]::WriteAllText((Join-Path $virtual 'master.totp'), 'synthetic encrypted bytes')
    Assert-Throws { Get-UpgradeVaultDirectory $directories 'master.totp' }
    Assert-Throws { Get-UpgradeVaultDirectory $directories 'missing.vault' }
    [IO.File]::Delete((Join-Path $old 'master.totp'))
    Assert-Test ((Get-UpgradeVaultDirectory $directories 'master.totp') -ceq $virtual) 'Expected virtualized legacy vault detection.'
    $discovered = @(Get-UpgradeDataDirectories -RoamingRoot $roaming -LocalRoot $local)
    Assert-Test ($discovered -contains $virtual) 'Initialize must discover existing Store family data without a package query.'
    $state = [ordered]@{ Schema = 1; V2 = $null; Verifications = @() }
    $statePath = Join-Path $testRoot 'state.json'
    Save-UpgradeState $state $statePath
    Assert-Test ((Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).Schema -eq 1) 'State round trip failed.'
    $oldHash = Get-UpgradeHash (Join-Path $virtual 'master.totp')
    $saved = Move-UpgradeDataAside $virtual
    Assert-Test (-not (Test-Path -LiteralPath $virtual)) 'Preserved data must leave the original app path free.'
    Assert-Test ((Get-UpgradeHash (Join-Path $saved.Preserved 'master.totp')) -ceq $oldHash) 'Existing encrypted data must remain byte-identical.'
    Assert-Test ([IO.Path]::GetDirectoryName($saved.Original) -ceq [IO.Path]::GetDirectoryName($saved.Preserved)) 'Existing data must be preserved beside its original path.'
    $privateKit = Join-Path $testRoot 'kit-acl'
    New-Item -ItemType Directory -Path $privateKit | Out-Null
    & {
        function Set-Acl { throw 'The kit must work without the Set-Acl module.' }
        Protect-UpgradeKitDirectory $privateKit
    }
    $directory = [IO.DirectoryInfo]::new($privateKit)
    $actualAcl = if ($PSVersionTable.PSVersion.Major -lt 6) {
        $directory.GetAccessControl()
    } else {
        [IO.FileSystemAclExtensions]::GetAccessControl($directory)
    }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $rules = @($actualAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    Assert-Test $actualAcl.AreAccessRulesProtected 'The kit must disable inherited permissions.'
    Assert-Test ($actualAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ceq $sid.Value) 'The kit must be owned by the current user.'
    Assert-Test ($rules.Count -eq 1 -and $rules[0].IdentityReference.Value -ceq $sid.Value -and
        $rules[0].AccessControlType -eq 'Allow' -and $rules[0].FileSystemRights -eq 'FullControl' -and
        $rules[0].InheritanceFlags -eq 'ContainerInherit, ObjectInherit') 'Only the current user may inherit full control of test artifacts.'
    # Mock only the package query, never query the host installation during self-test.
    & {
        function Get-AppxPackage { [pscustomobject]@{ Publisher = 'incorrect'; SignatureKind = 'Store'; IsDevelopmentMode = $false } }
        Assert-Throws { Get-UpgradeStorePackage }
    }
    & {
        function Get-AppxPackage { [pscustomobject]@{ Publisher = 'CN=84095A7C-6458-436E-ABF2-DC02311E25F9'; SignatureKind = 'Developer'; IsDevelopmentMode = $false } }
        Assert-Throws { Get-UpgradeStorePackage }
    }
    & {
        function Get-AppxPackage { [pscustomobject]@{ Publisher = 'CN=84095A7C-6458-436E-ABF2-DC02311E25F9'; SignatureKind = 'Store'; IsDevelopmentMode = $false } }
        Assert-Test ((Get-UpgradeStorePackage).SignatureKind -eq 'Store') 'Valid Store identity was rejected.'
    }
    # Test a junction under our own synthetic directory; never traverse or remove its target.
    $link = Join-Path $testRoot 'junction'
    New-Item -ItemType Junction -Path $link -Target $roaming | Out-Null
    Assert-Throws { Assert-UpgradeRegularPath (Join-Path $link 'master.totp') }
    [IO.Directory]::Delete($link)
    Write-Output 'PASS: script parsing, clean-profile guard, virtualized paths, duplicate/missing vaults, Store identity, state, current-user ACL and junction safety.'
} finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $allowedPrefix = Join-Path ([IO.Path]::GetTempPath()) 'otp-harbor-upgrade-script-'
    if (-not $resolvedTestRoot.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test cleanup path.' }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
if ($ScriptsOnly) { return }
& dotnet test (Join-Path $repositoryRoot 'OtpHarbor.Tests/OtpHarbor.Tests.csproj') -c Debug --nologo --verbosity minimal `
    --filter 'FullyQualifiedName~StoreUpgradeTestKitVerifierTests|FullyQualifiedName~VaultFileNameMigrationTests|FullyQualifiedName~AccountDalIntegrationTests|FullyQualifiedName~WindowsApplicationDataMigrationTests|FullyQualifiedName~VaultServiceTests'
if ($LASTEXITCODE -ne 0) { throw 'Upgrade verifier/migration regressions failed.' }
