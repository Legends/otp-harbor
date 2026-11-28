#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('desktop', 'mobile', 'all')][string]$Scope,
    [string]$DesktopVersion = 'auto',
    [string]$AndroidVersion = 'auto',
    [ValidateSet('validate', 'approval', 'automatic')][string]$Mode = 'approval',
    [ValidateSet('prepare', 'submit')][string]$StoreAction = 'prepare',
    [string]$OutputPath = 'release-plan.json'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-GitHubJson([string]$Endpoint) {
    $response = & gh api --paginate --slurp $Endpoint
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect required release state: $Endpoint" }
    $response | ConvertFrom-Json -Depth 100
}

if ($env:GITHUB_SHA -cnotmatch '^[0-9a-f]{40}$' -or ($Mode -ne 'validate' -and $env:GITHUB_REF -cne 'refs/heads/master')) {
    throw 'Production orchestration requires trusted master; validation still requires an immutable commit.'
}
$actualSource = & git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actualSource -cne $env:GITHUB_SHA) { throw 'Checked-out source differs from the release request.' }

if ($Mode -ne 'validate') {
    & (Join-Path $PSScriptRoot 'Test-ReleaseTagProtection.ps1') -Scope $Scope
    $branch = @(Read-GitHubJson "repos/$env:GITHUB_REPOSITORY/branches/master")[0]
    if (-not $branch.protected) { throw 'Production source branch is not protected.' }
    $environments = @($(if ($Mode -eq 'automatic') { 'release-automatic' } else { 'release-production' }))
    if ($Scope -ne 'mobile') { $environments += 'release-signing' }
    if ($Scope -ne 'desktop') { $environments += 'android-release' }
    if ($StoreAction -eq 'submit') { $environments += $(if ($Mode -eq 'automatic') { 'store-automatic' } else { 'store-release' }) }
    foreach ($name in $environments) {
        $environment = @(Read-GitHubJson "repos/$env:GITHUB_REPOSITORY/environments/$name")[0]
        if ($null -eq $environment.deployment_branch_policy) {
            throw "Environment $name must restrict deployment to protected master before production can run."
        }
        if (-not $environment.deployment_branch_policy.protected_branches) {
            $policies = @(Read-GitHubJson "repos/$env:GITHUB_REPOSITORY/environments/$name/deployment-branch-policies")
            $allowed = @($policies | ForEach-Object { $_.branch_policies })
            $branches = @($allowed | Where-Object { $_.type -ceq 'branch' })
            $tags = @($allowed | Where-Object { $_.type -ceq 'tag' })
            $tagPatterns = if ($name -eq 'release-production') { @('v*', 'android-v*') } elseif ($name -eq 'android-release') { @('android-v*') } else { @() }
            if ($branches.Count -ne 1 -or $branches[0].name -cne 'master' -or
                @($tags | Where-Object { $_.name -cnotin $tagPatterns }).Count -gt 0 -or $allowed.Count -ne $branches.Count + $tags.Count) {
                throw "Environment $name must permit master and only documented protected legacy release tags."
            }
        }
        if ($name -in @('release-production', 'store-release') -and @($environment.protection_rules | Where-Object { $_.type -eq 'required_reviewers' }).Count -ne 1) {
            throw "Approval mode requires a real required-reviewer rule on $name."
        }
    }
}

$state = @{
    tags = @(Read-GitHubJson "repos/$env:GITHUB_REPOSITORY/git/matching-refs/tags" | ForEach-Object { $_ })
    releases = @(Read-GitHubJson "repos/$env:GITHUB_REPOSITORY/releases?per_page=100" | ForEach-Object { $_ })
    artifacts = @(Read-GitHubJson "repos/$env:GITHUB_REPOSITORY/actions/artifacts?per_page=100" | ForEach-Object { $_.artifacts })
}
if ($Scope -ne 'mobile') {
    $publicPackageVersion = & python (Join-Path $PSScriptRoot '../windows/Get-PublicMicrosoftStoreVersion.py')
    if ($LASTEXITCODE -ne 0) {
        if ($Mode -ne 'validate') { throw 'Cannot confirm the public Store version before allocating a production desktop version.' }
        $publicPackageVersion = (Get-Content packaging/windows-store/public-version.json -Raw | ConvertFrom-Json).packageVersion
        Write-Warning 'Public Store catalog is unavailable; validation uses the last confirmed repository snapshot.'
    }
    $publicDisplayVersion = & (Join-Path $PSScriptRoot '../windows/Set-PublicMicrosoftStoreVersion.ps1') -PackageVersion $publicPackageVersion -ResolveOnly
    $state.releases += @{ tag_name = "v$publicDisplayVersion" }
}
$statePath = Join-Path $env:RUNNER_TEMP 'otp-release-state.json'
try {
    $state | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
    & python (Join-Path $PSScriptRoot 'release_plan.py') --state $statePath --output $OutputPath `
        --scope $Scope --desktop $DesktopVersion --android $AndroidVersion --mode $Mode --store-action $StoreAction `
        --source $env:GITHUB_SHA --ref $env:GITHUB_REF --run-id $env:GITHUB_RUN_ID --attempt $env:GITHUB_RUN_ATTEMPT `
        --automatic-enabled $(if ($env:RELEASE_AUTOMATIC_PROMOTION_ENABLED -ceq 'true') { 'true' } else { 'false' })
    if ($LASTEXITCODE -ne 0) { throw 'Release plan rejected.' }
}
finally { if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath -Force } }
$plan = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
if ($plan.desktopVersion) {
    $storeVersion = & (Join-Path $PSScriptRoot '../windows/Get-MicrosoftStoreVersion.ps1') -ReleaseTag "v$($plan.desktopVersion)"
    "store_version=$storeVersion" >> $env:GITHUB_OUTPUT
}
if ($plan.androidVersion) {
    $android = & (Join-Path $PSScriptRoot '../android/Get-AndroidReleaseVersion.ps1') -ReleaseTag "android-v$($plan.androidVersion)" | ConvertFrom-Json
    "android_code=$($android.versionCode)" >> $env:GITHUB_OUTPUT
}
$desktopTag = if ($plan.desktopVersion) { "v$($plan.desktopVersion)" } else { 'none' }
$androidTag = if ($plan.androidVersion) { "android-v$($plan.androidVersion)" } else { 'none' }
$prefix = if ($Mode -eq 'validate') { 'validation-plan' } else { 'release-plan' }
"artifact_name=$prefix-$desktopTag-$androidTag-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT" >> $env:GITHUB_OUTPUT
"plan=$($plan | ConvertTo-Json -Depth 10 -Compress)" >> $env:GITHUB_OUTPUT
"desktop_tag=$desktopTag" >> $env:GITHUB_OUTPUT
"android_tag=$androidTag" >> $env:GITHUB_OUTPUT
"## Release plan`n`nSource: $($plan.sourceCommit)`nScope: $Scope`nMode: $Mode`nDesktop: $desktopTag`nAndroid: $androidTag`nStore: $StoreAction`n`nNo publication has occurred." >> $env:GITHUB_STEP_SUMMARY
