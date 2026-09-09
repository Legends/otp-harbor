<#
.SYNOPSIS
Enforces the automated M3 performance budgets.

.DESCRIPTION
Reads an M3 JSON measurement report and fails when startup, working set, package footprint, native dependencies, or account-filtering percentiles exceed the reviewed thresholds.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ReportPath
)

$ErrorActionPreference = "Stop"
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json

$failures = [Collections.Generic.List[string]]::new()
if ($report.ProcessStartup.FirstMilliseconds -gt 10000) {
    $failures.Add("First technical probe exceeded 10000 ms.")
}
if ($report.ProcessStartup.WarmP95Milliseconds -gt 5000) {
    $failures.Add("Warm technical probe p95 exceeded 5000 ms.")
}
if ($report.WorkingSet.P95Bytes -gt 268435456) {
    $failures.Add("Technical probe working-set p95 exceeded 256 MiB.")
}
if ($report.Package.TotalBytes -gt 419430400) {
    $failures.Add("Framework-dependent technical package exceeded 400 MiB.")
}
if ($report.Package.NativeBytes -gt 262144000) {
    $failures.Add("Native dependency footprint exceeded 250 MiB.")
}

$filterBudgets = @{
    500 = 10.0
    1000 = 20.0
    5000 = 100.0
}
foreach ($measurement in $report.Filtering) {
    $budget = $filterBudgets[[int]$measurement.AccountCount]
    if ($null -eq $budget) {
        $failures.Add("Unexpected account-count measurement: $($measurement.AccountCount).")
    }
    elseif ($measurement.P95Milliseconds -gt $budget) {
        $failures.Add("Filtering $($measurement.AccountCount) accounts exceeded the $budget ms p95 budget.")
    }
}

if ($failures.Count -gt 0) {
    throw "M3 automated measurement budget failed: $($failures -join ' ')"
}

Write-Output "M3 automated measurement budgets passed."
