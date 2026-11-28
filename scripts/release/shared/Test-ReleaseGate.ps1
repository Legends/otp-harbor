#requires -Version 7.0
param([Parameter(Mandatory)][string]$NeedsJson, [Parameter(Mandatory)][string[]]$Required, [string[]]$Optional = @())
$ErrorActionPreference = 'Stop'
$needs = $NeedsJson | ConvertFrom-Json -AsHashtable
$expected = @($Required) + @($Optional)
if (@($needs.Keys | Where-Object { $_ -notin $expected }).Count -gt 0 -or $needs.Count -ne $expected.Count) {
    throw 'Release gate has missing or unexpected jobs.'
}
foreach ($job in $Required) {
    if ($needs[$job].result -cne 'success') { throw "Mandatory release job $job did not succeed." }
}
foreach ($job in $Optional) {
    if ($needs[$job].result -cnotin @('success', 'skipped')) { throw "Optional release job $job failed or was cancelled." }
}
Write-Output 'All mandatory release gates passed.'
