<#
.SYNOPSIS
Measures the M3 desktop performance and package-size targets.

.DESCRIPTION
Runs repeated technical startup probes against a packaged executable, calculates percentile startup and working-set measurements, exercises account filtering workloads, measures package/native bytes, and writes the resulting JSON report.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ExecutableName,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [ValidateRange(3, 50)]
    [int]$Iterations = 10
)

$ErrorActionPreference = "Stop"

$resolvedPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
$executable = Join-Path $resolvedPackage $ExecutableName
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "M3 package executable was not found."
}

function Get-Percentile {
    param([double[]]$Values, [double]$Percentile)
    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(0, [Math]::Min($sorted.Count - 1, [Math]::Ceiling($Percentile * $sorted.Count) - 1))
    return [Math]::Round($sorted[$index], 3)
}

$samples = @()
for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executable
    $startInfo.ArgumentList.Add("--m3-measurement-probe")
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "M3 measurement process could not be started."
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stopwatch.Stop()
    $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
    $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
    if ($process.ExitCode -ne 0) {
        throw "M3 measurement probe failed with exit code $($process.ExitCode). $stderr"
    }

    $jsonLine = @($stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($jsonLine)) {
        throw "M3 measurement probe did not return JSON evidence."
    }

    $probe = $jsonLine | ConvertFrom-Json
    if (-not $probe.NativeRuntimeAvailable) {
        throw "M3 measurement probe did not load the native camera runtime."
    }

    $samples += [PSCustomObject]@{
        ProcessElapsedMilliseconds = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
        Probe = $probe
    }
}

$packageFiles = @(Get-ChildItem -LiteralPath $resolvedPackage -File -Recurse)
$nativeFiles = foreach ($file in $packageFiles) {
    $isNative = $file.Extension -in @(".so", ".dylib")
    if ($file.Extension -eq ".dll") {
        try {
            [void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)
        }
        catch [BadImageFormatException] {
            $isNative = $true
        }
    }

    if ($isNative) { $file }
}

$elapsed = [double[]]@($samples | ForEach-Object ProcessElapsedMilliseconds)
$warmElapsed = if ($elapsed.Count -gt 1) { [double[]]$elapsed[1..($elapsed.Count - 1)] } else { $elapsed }
$workingSets = [double[]]@($samples | ForEach-Object { [double]$_.Probe.WorkingSetBytes })
$lastProbe = $samples[-1].Probe
$filterEvidence = foreach ($accountCount in @(500, 1000, 5000)) {
    $matching = @($samples | ForEach-Object {
        $_.Probe.FilterMeasurements | Where-Object AccountCount -eq $accountCount
    })
    [PSCustomObject]@{
        AccountCount = $accountCount
        P50Milliseconds = Get-Percentile ([double[]]@($matching | ForEach-Object P50Milliseconds)) 0.50
        P95Milliseconds = Get-Percentile ([double[]]@($matching | ForEach-Object P95Milliseconds)) 0.95
    }
}

$report = [ordered]@{
    SchemaVersion = 1
    Commit = (git rev-parse HEAD).Trim()
    WorkingTreeClean = [string]::IsNullOrWhiteSpace((git status --porcelain))
    RecordedAtUtc = [DateTime]::UtcNow.ToString("O")
    OperatingSystem = $lastProbe.OperatingSystem
    Architecture = $lastProbe.Architecture
    Iterations = $Iterations
    ProcessStartup = [ordered]@{
        FirstMilliseconds = [Math]::Round($elapsed[0], 3)
        WarmP50Milliseconds = Get-Percentile $warmElapsed 0.50
        WarmP95Milliseconds = Get-Percentile $warmElapsed 0.95
        AllP95Milliseconds = Get-Percentile $elapsed 0.95
    }
    WorkingSet = [ordered]@{
        P50Bytes = [long](Get-Percentile $workingSets 0.50)
        P95Bytes = [long](Get-Percentile $workingSets 0.95)
    }
    Filtering = @($filterEvidence)
    NativeRuntime = [ordered]@{
        Available = [bool]$lastProbe.NativeRuntimeAvailable
        Version = [string]$lastProbe.NativeRuntimeVersion
    }
    Package = [ordered]@{
        TotalBytes = [long](($packageFiles | Measure-Object Length -Sum).Sum)
        FileCount = $packageFiles.Count
        NativeFileCount = @($nativeFiles).Count
        NativeBytes = [long](($nativeFiles | Measure-Object Length -Sum).Sum)
    }
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
Write-Output $resolvedOutput
