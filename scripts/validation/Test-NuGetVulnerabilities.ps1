<#
.SYNOPSIS
Audits all solution packages for known NuGet vulnerabilities.

.DESCRIPTION
Runs the dotnet transitive vulnerability report, writes the raw JSON evidence to the requested path, enumerates every advisory, and fails when any vulnerable top-level or transitive package is reported.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SolutionPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$resolvedSolution = (Resolve-Path -LiteralPath $SolutionPath).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null

$lines = & dotnet list $resolvedSolution package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) {
    throw "NuGet vulnerability enumeration failed."
}
$json = $lines -join [Environment]::NewLine
[IO.File]::WriteAllText($resolvedOutput, "$json`n", [Text.UTF8Encoding]::new($false))
$report = $json | ConvertFrom-Json

$findings = foreach ($project in @($report.projects)) {
    foreach ($framework in @($project.frameworks) | Where-Object { $null -ne $_ }) {
        foreach ($kind in @("topLevelPackages", "transitivePackages")) {
            foreach ($package in @($framework.$kind) | Where-Object { $null -ne $_ }) {
                foreach ($vulnerability in @($package.vulnerabilities) | Where-Object { $null -ne $_ }) {
                    [PSCustomObject]@{
                        Project = [IO.Path]::GetFileName($project.path)
                        Package = $package.id
                        Version = $package.resolvedVersion
                        Severity = $vulnerability.severity
                        Advisory = $vulnerability.advisoryurl
                    }
                }
            }
        }
    }
}

if (@($findings).Count -gt 0) {
    $findings | Sort-Object Project, Package, Advisory | Format-Table -AutoSize | Out-String | Write-Host
    throw "NuGet vulnerability audit found $(@($findings).Count) advisory match(es)."
}

Write-Output "NuGet vulnerability audit passed."
