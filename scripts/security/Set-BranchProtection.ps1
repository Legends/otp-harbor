<#
.SYNOPSIS
Applies the reviewed GitHub branch-protection policy.

.DESCRIPTION
Obtains a GitHub token from the parameter, SecretStore, or the current user's DPAPI-protected fallback and updates the selected remote branch to require the repository's review and status-check rules.
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$Owner = "Legends",

    [Parameter(Mandatory = $false)]
    [string]$Repo = "otp-harbor",

    [Parameter(Mandatory = $false)]
    [string]$Branch = "master",

    [Parameter(Mandatory = $false)]
    [bool]$RequireLastPushApproval = $false,

    [Parameter(Mandatory = $false)]
    [string]$Token = $env:GITHUB_TOKEN
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Token)) {
    try {
        # Preferred: user-scoped secret from SecretManagement/SecretStore
        $secretToken = Get-Secret -Name "GitHub.Token.TOTPManager" -AsPlainText -ErrorAction Stop
        if (-not [string]::IsNullOrWhiteSpace($secretToken)) {
            $Token = $secretToken
        }
    }
    catch {
        # Intentionally ignored; fallback below.
    }
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    try {
        # Fallback: DPAPI user-scoped encrypted token file
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
        # Intentionally ignored; validated below.
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

function New-BranchProtectionPayload {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$IsOrganizationRepo,

        [Parameter(Mandatory = $true)]
        [bool]$RequireLastPushApproval
    )

    $reviewConfig = @{
        dismiss_stale_reviews           = $true
        require_code_owner_reviews      = $false
        require_last_push_approval      = $RequireLastPushApproval
        required_approving_review_count = 1
    }

    if ($IsOrganizationRepo) {
        $reviewConfig["dismissal_restrictions"] = @{
            users = @()
            teams = @()
        }
        $reviewConfig["bypass_pull_request_allowances"] = @{
            users = @()
            teams = @()
            apps  = @()
        }
    }

    $payloadObject = @{
        required_status_checks = @{
            strict   = $true
            contexts = @(
                "build-test",
                "Workflow Lint (actionlint)",
                "Dependency Review (PR)",
                "SAST (CodeQL C#)",
                "Dependency Vulnerability Scan (.NET)",
                "Secret Scan (Gitleaks)"
            )
        }
        enforce_admins                   = $true
        required_pull_request_reviews    = $reviewConfig
        restrictions                     = $null
        required_linear_history          = $true
        allow_force_pushes               = $false
        allow_deletions                  = $false
        block_creations                  = $false
        required_conversation_resolution = $true
        lock_branch                      = $false
        allow_fork_syncing               = $true
    }

    return ($payloadObject | ConvertTo-Json -Depth 20)
}

$repoUri = "https://api.github.com/repos/$Owner/$Repo"
$repoInfo = Invoke-RestMethod -Uri $repoUri -Method Get -Headers $headers
$isOrgRepo = $repoInfo.owner.type -eq "Organization"
$payload = New-BranchProtectionPayload -IsOrganizationRepo:$isOrgRepo -RequireLastPushApproval:$RequireLastPushApproval

try {
    Write-Host "Applying branch protection to $Owner/$Repo ($Branch), RequireLastPushApproval=$RequireLastPushApproval..."
    Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -Body $payload -ContentType "application/json" | Out-Null
    Write-Host "Branch protection applied successfully."
}
catch {
    $errorBody = $null
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
        $errorBody = $_.ErrorDetails.Message
    }

    if ($errorBody -and $errorBody -like "*Upgrade to GitHub Pro*") {
        Write-Error @"
Branch protection is unavailable for this repository plan.
GitHub response: Upgrade to GitHub Pro or make this repository public to enable this feature.

Options:
1. Upgrade repo owner/account plan to GitHub Pro/Team.
2. Make the repository public (if acceptable).
3. Keep workflow checks but enforce process via PR-only policy until plan upgrade.
"@
        throw
    }

    if ($errorBody) {
        Write-Error "Failed to apply branch protection. GitHub response: $errorBody"
        throw
    }

    Write-Error "Failed to apply branch protection: $($_.Exception.Message)"
    throw
}
