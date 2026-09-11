<#
.SYNOPSIS
Builds and installs the OTP Harbor Android development APK on a connected device.

.DESCRIPTION
Locates adb, optionally builds the signed Debug APK, requires exactly one usable Android device, installs the package with replacement enabled, and launches its main activity for device testing.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Android\TOTP.UI.Avalonia.Android.csproj'
$apkPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Android\bin\Debug\net10.0-android\io.github.legends.otpharbor.debug-Signed.apk'
$packageName = 'io.github.legends.otpharbor.debug'

$adbCommand = Get-Command adb -ErrorAction SilentlyContinue
if ($null -eq $adbCommand) {
    $sdkAdb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
    if (-not (Test-Path -LiteralPath $sdkAdb -PathType Leaf)) {
        throw 'adb was not found. Install Android SDK Platform-Tools or add adb to PATH.'
    }

    $adbPath = $sdkAdb
}
else {
    $adbPath = $adbCommand.Source
}

if (-not $SkipBuild) {
    dotnet build $projectPath -c Debug
    if ($LASTEXITCODE -ne 0) {
        throw 'The Android development build failed.'
    }
}

if (-not (Test-Path -LiteralPath $apkPath -PathType Leaf)) {
    throw "The signed development APK was not found: $apkPath"
}

& $adbPath start-server | Out-Null
$devices = @(
    & $adbPath devices |
        Select-Object -Skip 1 |
        ForEach-Object {
            if ($_ -match '^(\S+)\s+device$') { $Matches[1] }
        }
)

if ($devices.Count -ne 1) {
    throw "Expected exactly one authorized Android device, but found $($devices.Count)."
}

$device = $devices[0]
& $adbPath -s $device install -r $apkPath
if ($LASTEXITCODE -ne 0) {
    throw 'Installing the Android development APK failed.'
}

& $adbPath -s $device shell monkey -p $packageName -c android.intent.category.LAUNCHER 1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'The APK was installed, but launching the app failed.'
}

Write-Host "OTP Harbor was installed and launched on $device."
