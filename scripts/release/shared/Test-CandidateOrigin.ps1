#requires -Version 7.0
param([Parameter(Mandatory)][string]$RunId, [Parameter(Mandatory)][string]$ArtifactId, [Parameter(Mandatory)][string]$Digest, [switch]$ReadOnly)
$ErrorActionPreference = 'Stop'
if ($RunId -cnotmatch '^[1-9][0-9]*$' -or $ArtifactId -cnotmatch '^[1-9][0-9]*$' -or $Digest -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid recovery identity.' }
if ($env:GITHUB_REPOSITORY -cne 'Legends/otp-harbor' -or $env:GITHUB_REF -cne 'refs/heads/master') { throw 'Promotion requires the trusted protected branch workflow.' }
$manifest = Get-Content candidate.json -Raw | ConvertFrom-Json
$plan = $manifest.plan
$run = & gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$RunId" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect original workflow provenance.' }
if ($run.path -cne '.github/workflows/release.yml' -or $run.head_branch -cne 'master' -or $run.head_sha -cne $plan.sourceCommit -or [string]$plan.workflowRunId -cne $RunId) {
    throw 'Candidate was not prepared by the trusted release entry point.'
}
$artifact = & gh api "repos/$env:GITHUB_REPOSITORY/actions/artifacts/$ArtifactId" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $artifact.expired -or [string]$artifact.workflow_run.id -cne $RunId -or
    $artifact.name -cne "release-candidate-$RunId-$($plan.workflowRunAttempt)") { throw 'Candidate artifact provenance mismatch or expired.' }
$actual = (Get-FileHash candidate.json -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -cne $Digest) { throw 'Candidate digest changed; original approval is invalid.' }
& gh attestation verify candidate.json --repo $env:GITHUB_REPOSITORY --signer-workflow 'Legends/otp-harbor/.github/workflows/release.yml' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Candidate provenance attestation is missing or invalid.' }
& git fetch --no-tags origin master
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect protected source ancestry.' }
& git merge-base --is-ancestor $plan.sourceCommit origin/master
if ($LASTEXITCODE -ne 0) { throw 'Candidate source is outside protected master history.' }
if (-not $ReadOnly -and $plan.mode -eq 'automatic' -and $env:RELEASE_AUTOMATIC_PROMOTION_ENABLED -cne 'true') { throw 'Automatic promotion policy is not enabled.' }
& python (Join-Path $PSScriptRoot 'release_candidate.py') verify --root candidate-payload --manifest candidate.json --digest $Digest
if ($LASTEXITCODE -ne 0) { throw 'Candidate integrity verification failed.' }
Write-Output 'Candidate source, origin, plan, and hashes verified.'
