<#
.SYNOPSIS
Removes GitHub branch protection from a selected repository branch.

.DESCRIPTION
Obtains a GitHub token from the parameter, SecretStore, or the current user's DPAPI-protected fallback and calls the GitHub API to delete branch protection. This intentionally changes remote repository security settings.
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$Owner = "Legends",

    [Parameter(Mandatory = $false)]
    [string]$Repo = "otp-harbor",

    [Parameter(Mandatory = $false)]
    [string]$Branch = "master",

    [Parameter(Mandatory = $false)]
    [string]$Token = $env:GITHUB_TOKEN
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Token)) {
    try {
        $secretToken = Get-Secret -Name "GitHub.Token.TOTPManager" -AsPlainText -ErrorAction Stop
        if (-not [string]::IsNullOrWhiteSpace($secretToken)) {
            $Token = $secretToken
        }
    }
    catch {
        # Fallback below.
    }
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    try {
        $tokenFile = Join-Path $env:APPDATA "TOTP-Manager\github-token.dpapi"
        if (Test-Path $tokenFile) {
            $secure = Get-Content -Path $tokenFile -ErrorAction Stop | ConvertTo-SecureString
            $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
            try {
                $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
                if (-not [string]::IsNullOrWhiteSpace($plain)) {
                    $Token = $plain
                }
            }
            finally {
                if ($bstr -ne [IntPtr]::Zero) {
                    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
                }
            }
        }
    }
    catch {
        # Validation below.
    }
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "Missing GitHub token. Use -Token, user env GITHUB_TOKEN, SecretStore secret 'GitHub.Token.TOTPManager', or DPAPI file at %APPDATA%\\TOTP-Manager\\github-token.dpapi."
}

$headers = @{
    "Accept"               = "application/vnd.github+json"
    "Authorization"        = "Bearer $Token"
    "X-GitHub-Api-Version" = "2022-11-28"
}

$uri = "https://api.github.com/repos/$Owner/$Repo/branches/$Branch/protection"

try {
    Write-Host "Removing branch protection from $Owner/$Repo ($Branch)..."
    Invoke-RestMethod -Uri $uri -Method Delete -Headers $headers | Out-Null
    Write-Host "Branch protection removed."
}
catch {
    $errorBody = $null
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
        $errorBody = $_.ErrorDetails.Message
    }

    if ($errorBody) {
        Write-Error "Failed to remove branch protection. GitHub response: $errorBody"
        throw
    }

    Write-Error "Failed to remove branch protection: $($_.Exception.Message)"
    throw
}
