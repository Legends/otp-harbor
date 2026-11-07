<#
.SYNOPSIS
Installs the separate repository-index identity into a master-only GitHub environment using stdin.
#>
[CmdletBinding()]
param([string]$SigningDirectory = (Join-Path $env:LOCALAPPDATA 'OTP-Harbor/fdroid-repository-signing'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'The local repository password is protected with Windows DPAPI.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$settings = Get-Content (Join-Path $repositoryRoot 'packaging/fdroid/repository.json') -Raw | ConvertFrom-Json
$publicInfo = Get-Content (Join-Path $SigningDirectory 'signing-public.json') -Raw | ConvertFrom-Json
if ($settings.sourceRepository -cne 'Legends/otp-harbor' -or
    $publicInfo.certificateSha256 -cne $settings.indexCertificateSha256) {
    throw 'The signing identity or target repository does not match the committed public pins.'
}

function Invoke-GhWithInput([string[]]$Arguments, [string]$InputText = '') {
    $start = [Diagnostics.ProcessStartInfo]::new('gh')
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.Write($InputText)
        $process.StandardInput.Close()
        $output = $process.StandardOutput.ReadToEnd()
        $null = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "GitHub configuration failed with exit code $($process.ExitCode)." }
        return $output
    }
    finally { $process.Dispose() }
}

$repo = [string]$settings.sourceRepository
$environmentPath = "repos/$repo/environments/fdroid-repository"
$environmentPolicy = '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}'
$null = Invoke-GhWithInput @('api', $environmentPath, '--method', 'PUT', '--input', '-') $environmentPolicy
$rules = Invoke-GhWithInput @('api', "$environmentPath/deployment-branch-policies") | ConvertFrom-Json
if (@($rules.branch_policies | Where-Object { $_.name -ceq 'master' -and $_.type -ceq 'branch' }).Count -eq 0) {
    $null = Invoke-GhWithInput @('api', "$environmentPath/deployment-branch-policies", '--method', 'POST', '--input', '-') '{"name":"master","type":"branch"}'
}

$passwordBytes = [Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes((Join-Path $SigningDirectory 'keystore-password.dpapi')),
    $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
try {
    $null = Invoke-GhWithInput @('secret', 'set', 'FDROID_INDEX_KEYSTORE_PASSWORD', '--repo', $repo, '--env', 'fdroid-repository') `
        ([Text.Encoding]::UTF8.GetString($passwordBytes))
    $null = Invoke-GhWithInput @('secret', 'set', 'FDROID_INDEX_KEYSTORE_BASE64', '--repo', $repo, '--env', 'fdroid-repository') `
        ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $SigningDirectory 'repository-index.p12'))))
}
finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($passwordBytes) }
Write-Output 'Installed the repository-index identity in the master-only fdroid-repository environment.'
