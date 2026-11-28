#requires -Version 7.0
param([string]$ArtifactId, [string]$RunId, [Parameter(Mandatory)][string]$CandidateDigest)
$ErrorActionPreference = 'Stop'
if (-not $ArtifactId) {
    $artifacts = & gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID/artifacts?per_page=100" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect prior Store state for this workflow retry.' }
    $prior = @($artifacts.artifacts | Where-Object { $_.name -ceq 'store-state' -and -not $_.expired })
    if ($prior.Count -gt 1) { throw 'Ambiguous Store retry state.' }
    if ($prior.Count -eq 0) { return }
    $ArtifactId = [string]$prior[0].id
    $RunId = $env:GITHUB_RUN_ID
    $directory = Join-Path $env:RUNNER_TEMP 'otp-store-state-retry'
    & gh run download $RunId --repo $env:GITHUB_REPOSITORY --name store-state --dir $directory
    if ($LASTEXITCODE -ne 0) { throw 'Prior Store state is unavailable.' }
    Copy-Item -LiteralPath (Join-Path $directory 'store-state.json') -Destination store-state.json
}
if ($RunId -cnotmatch '^[1-9][0-9]*$' -or $ArtifactId -cnotmatch '^[1-9][0-9]*$') { throw 'Invalid Store state provenance identity.' }
$run = & gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$RunId" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $run.head_branch -cne 'master' -or $run.path -cnotin @(
    '.github/workflows/release.yml', '.github/workflows/release-recovery.yml', '.github/workflows/release-reconciliation.yml')) { throw 'Store state came from an untrusted workflow.' }
$artifact = & gh api "repos/$env:GITHUB_REPOSITORY/actions/artifacts/$ArtifactId" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $artifact.expired -or $artifact.name -cne 'store-state' -or [string]$artifact.workflow_run.id -cne $RunId) { throw 'Store state artifact origin mismatch.' }
$file = Get-Item store-state.json
if ($file.Length -gt 65536) { throw 'Store state exceeds its bounded size.' }
$state = Get-Content store-state.json -Raw | ConvertFrom-Json -AsHashtable
$allowed = @('schemaVersion', 'candidateSha256', 'candidateId', 'sourceCommit', 'productId', 'packageVersion', 'packageSha256',
    'submissionId', 'state', 'remoteStatus', 'creationUncertain', 'commitUncertain', 'submittedAt', 'publishedAt', 'updatedAt')
if (@($state.Keys | Where-Object { $_ -notin $allowed }).Count -gt 0 -or $state.candidateSha256 -cne $CandidateDigest -or
    $state.candidateId -cnotmatch '^[1-9][0-9]*-[1-9][0-9]*$' -or $state.sourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
    ($state.submissionId -and $state.submissionId -cnotmatch '^[0-9]+$')) { throw 'Store retry state is malformed or belongs to another candidate.' }
