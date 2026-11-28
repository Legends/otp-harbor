#requires -Version 7.0
param([Parameter(Mandatory)][string]$NeedsJson, [string]$PlanPath = 'release-plan.json')
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
$required = @('plan', 'validation', 'security')
$optional = @()
if ($plan.desktopVersion) { $required += @('desktop', 'desktop_metadata') } else { $optional += @('desktop', 'desktop_metadata') }
if ($plan.androidVersion -and $plan.productionEligible) { $required += @('android', 'android_metadata') } else { $optional += @('android', 'android_metadata') }
& (Join-Path $PSScriptRoot 'Test-ReleaseGate.ps1') -NeedsJson $NeedsJson -Required $required -Optional $optional
$results = $NeedsJson | ConvertFrom-Json -AsHashtable
$evidence = @{}
foreach ($name in $required) { $evidence[$name] = $results[$name].result }
$evidence | ConvertTo-Json | Set-Content evidence.json -Encoding utf8NoBOM

foreach ($family in @('desktop', 'android')) {
    $number = if ($family -eq 'desktop') { $plan.desktopVersion } else { $plan.androidVersion }
    if (-not $number) { continue }
    $tag = if ($family -eq 'desktop') { "v$number" } else { "android-v$number" }
    $notesPath = "candidate-payload/$family/release-notes.md"
    $noteArguments = @('--tag', $tag, '--source', $plan.sourceCommit, '--output', $notesPath)
    if (-not $plan.productionEligible) { $noteArguments += '--validation' }
    & python (Join-Path $PSScriptRoot 'release_notes.py') @noteArguments
    if ($LASTEXITCODE -ne 0) { throw 'Read-only release note generation failed.' }
    $notes = Get-Content -LiteralPath $notesPath -Raw
        $policy = if (-not $plan.productionEligible) {
            'Validation-only candidate. No production publication is authorized. Desktop metadata is unsigned; Android uses the existing test package identity and test signing key. These retained outputs cannot be promoted as production artifacts.'
        } elseif ($family -eq 'desktop') {
            'Windows is distributed through Microsoft Store, which signs accepted MSIX packages and manages updates. No unsigned Windows executable is attached to this stable GitHub release. GitHub provides Linux packages and signed release integrity metadata. Android uses independent android-vX.Y.Z releases. SignPath production signing remains restricted by the project code-signing policy.'
        } else {
            'This universal NativeAOT APK uses the existing pinned production Android signing certificate and application identity. Verify the certificate and SHA256SUMS before installation. The signed project F-Droid repository is independent from the official F-Droid catalog.'
        }
        "$notes`n`n$policy`n`n[Code signing policy](https://github.com/$env:GITHUB_REPOSITORY/blob/$($plan.sourceCommit)/CODE_SIGNING_POLICY.md) · [Privacy policy](https://github.com/$env:GITHUB_REPOSITORY/blob/$($plan.sourceCommit)/PRIVACY.md)" |
            Set-Content -LiteralPath "candidate-payload/$family/release-notes.md" -Encoding utf8NoBOM
}

$pages = & gh api --paginate --slurp "repos/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID/artifacts?per_page=100" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve candidate artifact identities.' }
$names = @("test-evidence-$($plan.sourceCommit)")
if ($plan.desktopVersion) { $names += @("desktop-ready-$($plan.sourceCommit)", "otp-harbor-microsoft-store-$($plan.desktopVersion)") }
if ($plan.androidVersion) { $names += "$(if ($plan.productionEligible) { 'android-ready' } else { 'android-validation' })-$($plan.sourceCommit)" }
$artifacts = @($pages | ForEach-Object { $_.artifacts } | Where-Object { $_.name -in $names } | Select-Object id, name, size_in_bytes, digest)
if ($artifacts.Count -ne $names.Count) { throw 'Selected candidate artifacts are missing or ambiguous.' }
ConvertTo-Json -InputObject $artifacts -Depth 10 | Set-Content artifacts.json -Encoding utf8NoBOM
New-Item -ItemType Directory -Path candidate-payload/validation -Force | Out-Null
Copy-Item -LiteralPath test-evidence/test-evidence.json -Destination candidate-payload/validation/test-evidence.json
$arguments = @('create', '--root', 'candidate-payload', '--manifest', 'candidate.json', '--plan', $PlanPath, '--artifacts', 'artifacts.json', '--evidence', 'evidence.json', '--test-evidence', 'candidate-payload/validation/test-evidence.json')
if ($plan.desktopVersion) {
    $storeVersion = & (Join-Path $PSScriptRoot '../windows/Get-MicrosoftStoreVersion.ps1') -ReleaseTag "v$($plan.desktopVersion)"
    $arguments += @('--store-version', $storeVersion)
}
if ($plan.androidVersion -and $plan.productionEligible) {
    $android = & (Join-Path $PSScriptRoot '../android/Get-AndroidReleaseVersion.ps1') -ReleaseTag "android-v$($plan.androidVersion)" | ConvertFrom-Json
    $arguments += @('--android-code', [string]$android.versionCode, '--certificate', $env:ANDROID_SIGNING_CERTIFICATE_SHA256)
}
& python (Join-Path $PSScriptRoot 'release_candidate.py') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Candidate integrity verification failed.' }
$digest = (Get-FileHash candidate.json -Algorithm SHA256).Hash.ToLowerInvariant()
"digest=$digest" >> $env:GITHUB_OUTPUT
$manifest = Get-Content candidate.json -Raw | ConvertFrom-Json
"evidence=$($manifest | ConvertTo-Json -Depth 30 -Compress)" >> $env:GITHUB_OUTPUT
"## Verified candidate $($plan.candidateId)`n`nSource: $($plan.sourceCommit)`nScope: $($plan.scope)`nMode: $($plan.mode)`nCandidate SHA-256: $digest`n`nSelected: $($plan.platforms -join ', ')`nDisabled: $($plan.disabledPlatforms -join ', ')`n`nAll mandatory shared and selected preparation gates passed. Production authorization is still required.`n`n| File | Bytes | SHA-256 |`n|---|---:|---|" >> $env:GITHUB_STEP_SUMMARY
foreach ($file in $manifest.files) { "| $($file.fileName) | $($file.bytes) | $($file.sha256) |" >> $env:GITHUB_STEP_SUMMARY }
"`n| Source artifact | ID |`n|---|---|" >> $env:GITHUB_STEP_SUMMARY
foreach ($artifact in $manifest.artifacts) { "| $($artifact.name) | $($artifact.id) |" >> $env:GITHUB_STEP_SUMMARY }
"`nPackage identities and signing results are recorded in candidate.json. Recovery must use this candidate digest and the original retained artifacts. Validation candidates cannot be promoted." >> $env:GITHUB_STEP_SUMMARY
$tests = $manifest.testResults.counts
"`nActual full integration tests: $($tests.total) total, $($tests.passed) passed, $($tests.failed) failed, $($tests.skipped) skipped. Retained test evidence includes TRX outcomes." >> $env:GITHUB_STEP_SUMMARY
