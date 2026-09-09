<#
.SYNOPSIS
Stores a GitHub token securely for the current Windows user.

.DESCRIPTION
Prompts for the token with hidden input and stores it either through PowerShell SecretStore or as a DPAPI-protected current-user file under AppData. The plaintext token is not written to disk.
#>
param(
    [Parameter(Mandatory = $false)]
    [switch]$UseSecretStore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "Enter GitHub token (input hidden)."
$secureToken = Read-Host -AsSecureString

if ($UseSecretStore) {
    try {
        Set-Secret -Name "GitHub.Token.TOTPManager" -Secret $secureToken -ErrorAction Stop
        Write-Host "Token stored in SecretStore as 'GitHub.Token.TOTPManager' (current user scope)."
        return
    }
    catch {
        throw "Failed to store token in SecretStore. Ensure SecretManagement/SecretStore is installed and configured."
    }
}

$targetDir = Join-Path $env:APPDATA "TOTP-Manager"
$targetFile = Join-Path $targetDir "github-token.dpapi"

New-Item -Path $targetDir -ItemType Directory -Force | Out-Null
$secureToken | ConvertFrom-SecureString | Set-Content -Path $targetFile -Encoding UTF8

Write-Host "Token stored at $targetFile"
Write-Host "This file is encrypted with DPAPI and tied to the current Windows user profile."
