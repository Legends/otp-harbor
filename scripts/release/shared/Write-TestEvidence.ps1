#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$files = @(Get-ChildItem TestResults -Filter '*.trx' -File -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) { throw 'No actual test results are available; validation cannot be claimed.' }
$counts = @{ total = 0; passed = 0; failed = 0; skipped = 0 }
foreach ($file in $files) {
    [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
    $counters = $document.SelectSingleNode('//*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
    if ($null -eq $counters) { throw 'Malformed test result counters.' }
    $results = @($document.SelectNodes('//*[local-name()="UnitTestResult"]'))
    if ($results.Count -ne [int]$counters.GetAttribute('total')) { throw 'Test results are incomplete or counters disagree.' }
    # xUnit's TRX adapter can report notExecuted=0 despite NotExecuted results.
    # Count the retained outcomes so skipped tests are reported accurately.
    foreach ($result in $results) {
        $counts.total++
        switch -CaseSensitive ($result.GetAttribute('outcome')) {
            'Passed' { $counts.passed++ }
            'NotExecuted' { $counts.skipped++ }
            default { $counts.failed++ }
        }
    }
    if ([int]$counters.GetAttribute('failed') -gt 0 -and $counts.failed -eq 0) { throw 'Test failure counters disagree with outcomes.' }
}
$evidence = @{ schemaVersion = 1; sourceCommit = $env:GITHUB_SHA; runId = $env:GITHUB_RUN_ID; counts = $counts;
    suite = $(if ($env:GITHUB_EVENT_NAME -eq 'pull_request') { 'filtered-pr' } else { 'full-integration' }) }
$evidence | ConvertTo-Json -Depth 5 | Set-Content TestResults/test-evidence.json -Encoding utf8NoBOM
"## Application test evidence`n`n$($counts.total) total · $($counts.passed) passed · $($counts.failed) failed · $($counts.skipped) skipped. See retained TRX files for reasons and per-test outcomes." >> $env:GITHUB_STEP_SUMMARY
if ($counts.passed -eq 0 -or $counts.failed -gt 0) { throw 'Required tests failed or no successful tests were executed.' }
