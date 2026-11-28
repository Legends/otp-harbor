#requires -Version 7.0
param([Parameter(Mandatory)][string]$ReleaseTag, [Parameter(Mandatory)][string]$SourceCommit, [switch]$Publication)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-ReleaseSource.ps1') -ReleaseTag $ReleaseTag -SourceCommit $SourceCommit
& (Join-Path $PSScriptRoot 'Test-ReleaseTagProtection.ps1') -Scope $(if ($ReleaseTag.StartsWith('android-v')) { 'mobile' } else { 'desktop' })
& git fetch --no-tags origin master
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect protected release source.' }
& git merge-base --is-ancestor $SourceCommit origin/master
if ($LASTEXITCODE -ne 0) { throw 'Legacy release source must belong to protected master history.' }
$environment = & gh api "repos/$env:GITHUB_REPOSITORY/environments/release-production" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $null -eq $environment.deployment_branch_policy -or
    @($environment.protection_rules | Where-Object { $_.type -eq 'required_reviewers' }).Count -ne 1) {
    throw 'Configure the protected release-production environment before enabling legacy tag promotion.'
}
if ($Publication) {
    $reference = & gh api "repos/$env:GITHUB_REPOSITORY/git/ref/tags/$ReleaseTag" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the existing release tag.' }
    $object = $reference.object
    for ($depth = 0; $object.type -ceq 'tag' -and $depth -lt 5; $depth++) {
        $tag = & gh api "repos/$env:GITHUB_REPOSITORY/git/tags/$($object.sha)" | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the release tag source.' }
        $object = $tag.object
    }
    if ($object.type -cne 'commit' -or $object.sha -cne $SourceCommit) { throw 'The release tag source changed; publication blocked.' }
    $existing = & gh api "repos/$env:GITHUB_REPOSITORY/releases/tags/$ReleaseTag" 2>&1
    if ($LASTEXITCODE -eq 0) { throw 'A release or draft already exists. Preserve its assets; reconcile the original retained candidate instead of replacing it.' }
    if (($existing -join ' ') -notmatch 'HTTP 404') { throw 'Could not establish absence of an existing release; publication blocked.' }
}
