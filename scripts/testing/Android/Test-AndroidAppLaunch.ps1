#requires -Version 5.1
<#
.SYNOPSIS
Checks that an Android app survives repeated process-cold launches.
.DESCRIPTION
Force-stops only the specified package, launches its normal launcher activity,
and verifies that the same process remains alive and its activity is resumed.
A successful am start request alone is insufficient: native runtime failures can
occur after Android accepts the request. No app data is cleared, no vault is
read, and no screenshots or account contents are collected. Close any unfinished
work in the app before running this check.
.EXAMPLE
.\Test-AndroidAppLaunch.ps1 -Device emulator-5554 -PackageName io.github.legends.otpharbor.debug
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Device,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$')]
    [string]$PackageName,

    [string]$AdbPath,

    [ValidateRange(1, 10)]
    [int]$LaunchCount = 3,

    [ValidateRange(10, 60)]
    [int]$ObservationSeconds = 20
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($AdbPath)) {
    $adbCommand = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -ne $adbCommand) {
        $AdbPath = $adbCommand.Source
    }
    else {
        $AdbPath = Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe'
    }
}
if (-not (Test-Path -LiteralPath $AdbPath -PathType Leaf)) {
    throw 'adb was not found. Supply -AdbPath with the Android platform-tools executable.'
}

function Invoke-LaunchAdb {
    param([string[]]$Arguments)
    $output = @(& $AdbPath -s $Device @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Android command failed: $($Arguments -join ' ')"
    }
    return $output
}

$resolved = Invoke-LaunchAdb -Arguments @('shell', 'cmd', 'package', 'resolve-activity', '--brief', $PackageName)
$component = @($resolved | ForEach-Object { $_.ToString().Trim() } |
    Where-Object { $_ -match '^[A-Za-z0-9_.$]+/[A-Za-z0-9_.$]+$' } | Select-Object -Last 1)
if ($component.Count -ne 1 -or -not $component[0].StartsWith($PackageName + '/', [StringComparison]::Ordinal)) {
    throw 'No launcher activity resolved for the requested package.'
}

foreach ($launchIndex in 1..$LaunchCount) {
    $launch = Invoke-LaunchAdb -Arguments @('shell', 'am', 'start', '-W', '-S', '-n', $component[0])
    if (-not ($launch -match '^Status:\s+ok\s*$')) {
        throw "Launch $launchIndex was not accepted by Android."
    }

    $initialProcess = $null
    foreach ($second in 1..$ObservationSeconds) {
        $processOutput = @(& $AdbPath -s $Device shell pidof $PackageName 2>$null)
        $processExit = $LASTEXITCODE
        $processId = ($processOutput -join ' ').Trim()
        if ($processExit -ne 0 -or $processId -notmatch '^\d+$') {
            throw "Launch $launchIndex failed: app process exited during startup observation."
        }
        if ($null -eq $initialProcess) { $initialProcess = $processId }
        if ($processId -cne $initialProcess) {
            throw "Launch $launchIndex failed: app process restarted during startup observation."
        }
        Start-Sleep -Seconds 1
    }

    $activities = Invoke-LaunchAdb -Arguments @('shell', 'dumpsys', 'activity', 'activities')
    $resumedPattern = '(?:mResumedActivity|topResumedActivity)[:=].*\s' + [regex]::Escape($component[0]) + '(?:\s|\})'
    if (-not ($activities -match $resumedPattern)) {
        throw "Launch $launchIndex failed: launcher activity is not resumed after startup."
    }
    Write-Output "PASS: launch $launchIndex/$LaunchCount remained alive for $ObservationSeconds seconds with its launcher activity resumed."
}

Write-Output 'PASS: repeated process-cold launch health checks. This does not replace visual interaction checks.'
