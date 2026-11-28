#requires -Version 7.0
param([Parameter(Mandatory)][ValidateSet('desktop', 'mobile', 'all')][string]$Scope)
$ErrorActionPreference = 'Stop'
$pages = & gh api --paginate --slurp "repos/$env:GITHUB_REPOSITORY/rulesets?per_page=100" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify immutable release tag protection.' }
$required = @()
if ($Scope -ne 'mobile') { $required += 'refs/tags/v*' }
if ($Scope -ne 'desktop') { $required += 'refs/tags/android-v*' }
$protected = @()
foreach ($page in $pages) {
    foreach ($summary in $page) {
        if ($summary.target -cne 'tag' -or $summary.enforcement -cne 'active') { continue }
        $rule = & gh api "repos/$env:GITHUB_REPOSITORY/rulesets/$($summary.id)" | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect release tag rules.' }
        if (@($rule.conditions.ref_name.exclude).Count -gt 0 -or
            @($rule.rules | Where-Object { $_.type -ceq 'update' }).Count -eq 0 -or
            @($rule.rules | Where-Object { $_.type -ceq 'deletion' }).Count -eq 0) { continue }
        $patterns = @($rule.conditions.ref_name.include)
        foreach ($pattern in $required) {
            if ($pattern -cin $patterns -or '~ALL' -cin $patterns) { $protected += $pattern }
        }
    }
}
foreach ($pattern in $required) {
    if ($pattern -cnotin $protected) { throw "Configure active update/deletion protection for $pattern before production release execution." }
}
