<#
.SYNOPSIS
Validates Windows signing-key storage using a fresh, disposable test identity.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TestDirectory,
    [Parameter(Mandatory)][string]$KeytoolPath
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$initializer = Join-Path $repositoryRoot 'scripts/release/Initialize-FDroidRepositorySigning.ps1'
& $initializer -OutputDirectory $TestDirectory -KeytoolPath $KeytoolPath
$password = [Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes((Join-Path $TestDirectory 'keystore-password.dpapi')),
    $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
try {
    if ($password.Length -ne 64) { throw 'Protected password round-trip failed.' }
}
finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($password) }
if (Test-Path -LiteralPath (Join-Path $TestDirectory 'keystore-password.txt')) {
    throw 'Plaintext password file was created.'
}
if (-not (Get-Acl -LiteralPath $TestDirectory).AreAccessRulesProtected) {
    throw 'The private signing directory inherits access permissions.'
}
$keystore = Join-Path $TestDirectory 'repository-index.p12'
$originalHash = (Get-FileHash -LiteralPath $keystore).Hash
$rejected = $false
try { & $initializer -OutputDirectory $TestDirectory -KeytoolPath $KeytoolPath }
catch { $rejected = $_.Exception.Message -like '*already exists*' }
if (-not $rejected -or (Get-FileHash -LiteralPath $keystore).Hash -cne $originalHash) {
    throw 'The existing identity was not preserved.'
}
$rejected = $false
try { & $initializer -OutputDirectory $repositoryRoot -KeytoolPath $KeytoolPath }
catch { $rejected = $_.Exception.Message -like '*outside the checkout*' }
if (-not $rejected) { throw 'The checkout storage guard failed.' }
Write-Output 'Signing storage, DPAPI round-trip and overwrite/checkout guards passed.'
Write-Output "Disposable test identity: $([IO.Path]::GetFullPath($TestDirectory))"
