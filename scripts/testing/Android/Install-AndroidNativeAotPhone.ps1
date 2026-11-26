#requires -Version 7.0
<#
.SYNOPSIS
Builds current source in NativeAOT Release mode and updates the USB phone's AOT Test app.
.DESCRIPTION
Run without arguments. Selects one physical USB phone, ignores emulators, and reuses
the existing NativeAOT builder. Installs with -r and launches once without clearing data.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../shared/Testing.Common.ps1')
$phoneRoot = Get-OtpHarborRepositoryRoot -StartDirectory $PSScriptRoot
$phonePackage = 'io.github.legends.otpharbor.nativeaotprobe'
$phoneSdkRoots = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)
if ($env:LOCALAPPDATA) { $phoneSdkRoots += Join-Path $env:LOCALAPPDATA 'Android/Sdk' }
if (${env:ProgramFiles(x86)}) { $phoneSdkRoots += Join-Path ${env:ProgramFiles(x86)} 'Android/android-sdk' }
$phoneSdkRoots = @($phoneSdkRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)

$phoneAdbCommand = Get-Command adb -ErrorAction SilentlyContinue
$phoneAdb = if ($phoneAdbCommand) { $phoneAdbCommand.Source } else {
    @('D:/android-platform-tools/adb.exe'; $phoneSdkRoots | ForEach-Object { Join-Path $_ 'platform-tools/adb.exe' }) |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($phoneAdb)) { throw 'adb was not found. Install Android platform-tools and add them to PATH.' }
$phoneSerialOutput = @(& $phoneAdb -d get-serialno 2>$null)
if ($LASTEXITCODE -ne 0 -or $phoneSerialOutput.Count -ne 1 -or
    [string]::IsNullOrWhiteSpace([string]$phoneSerialOutput[0]) -or
    ([string]$phoneSerialOutput[0]).Trim() -match '^(unknown|emulator-.*)$') {
    throw 'Connect exactly one physical USB phone, unlock it, and allow USB debugging before retrying.'
}
$phoneSerial = ([string]$phoneSerialOutput[0]).Trim()
$phoneState = @(& $phoneAdb -s $phoneSerial get-state 2>$null)
if ($LASTEXITCODE -ne 0 -or ($phoneState -join '').Trim() -cne 'device') {
    throw 'The USB phone is offline or unauthorized. Unlock it and allow USB debugging, then retry.'
}

$phoneNdkCandidates = @(
    (Join-Path $phoneRoot '.codex/tools/android-sdk/ndk/28.2.13676358')
    $env:ANDROID_NDK_HOME
    $phoneSdkRoots | ForEach-Object { Join-Path $_ 'ndk/28.2.13676358' }
)
$phoneNdk = $phoneNdkCandidates | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_) -and
    (Test-Path -LiteralPath (Join-Path $_ 'source.properties') -PathType Leaf) -and
    (Get-Content -LiteralPath (Join-Path $_ 'source.properties') -Raw) -match '(?m)^Pkg\.Revision\s*=\s*28\.2\.13676358\s*$'
} | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($phoneNdk)) {
    throw 'Android NDK 28.2.13676358 was not found. Run scripts/testing/android/Install-AndroidNativeAotToolchain.ps1 with your Android SDK configured.'
}

Push-Location $phoneRoot
try {
    & (Join-Path $PSScriptRoot 'Build-AndroidAotExperiment.ps1') -Runtime NativeAOT -Architecture universal -NdkDirectory $phoneNdk
}
finally { Pop-Location }
$phoneApk = Join-Path $phoneRoot 'artifacts/android/testing/aot/NativeAOT/universal/OTP-Harbor-AOT-Test-NativeAOT.apk'
if (-not (Test-Path -LiteralPath $phoneApk -PathType Leaf)) { throw 'The NativeAOT build did not produce its APK; nothing was installed.' }

& $phoneAdb -s $phoneSerial install -r $phoneApk
if ($LASTEXITCODE -ne 0) { throw 'Installation failed. Existing app data was retained; no uninstall or clear fallback is performed.' }
$phoneActivities = @(& $phoneAdb -s $phoneSerial shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p $phonePackage)
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the installed AOT Test launcher.' }
$phoneLauncher = @($phoneActivities | Where-Object { $_ -match "^$([regex]::Escape($phonePackage))/[A-Za-z0-9_.`$]+$" })
if ($phoneLauncher.Count -ne 1) { throw 'The installed AOT Test launcher was not uniquely identified.' }
$phoneLaunch = @(& $phoneAdb -s $phoneSerial shell am start -W -n $phoneLauncher[0])
if ($LASTEXITCODE -ne 0 -or -not ($phoneLaunch -match '^Status:\s+ok\s*$')) { throw 'AOT Test was installed, but its launch failed.' }
$phoneLaunch | Write-Output
$phoneInitialProcess = $null
foreach ($phoneObservation in 1..10) {
    $phoneProcessOutput = @(& $phoneAdb -s $phoneSerial shell pidof $phonePackage 2>$null)
    $phoneProcess = ($phoneProcessOutput -join ' ').Trim()
    if ($LASTEXITCODE -ne 0 -or $phoneProcess -notmatch '^\d+$') {
        throw 'AOT Test exited during startup. Installation succeeded, but the app is not healthy.'
    }
    if ($null -eq $phoneInitialProcess) { $phoneInitialProcess = $phoneProcess }
    if ($phoneProcess -cne $phoneInitialProcess) { throw 'AOT Test restarted during startup; inspect the Android crash log.' }
    Start-Sleep -Seconds 1
}
Write-Output 'PASS: AOT Test remained running for ten seconds after launch.'
Write-Output 'Current-source NativeAOT Release installed and launched as OTP Harbor AOT Test. Existing accounts were retained.'
