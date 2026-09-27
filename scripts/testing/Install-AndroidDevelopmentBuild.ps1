<#
.SYNOPSIS
Builds and deploys a fresh OTP Harbor Android development APK to a specific device.

.DESCRIPTION
Locates adb, creates a non-incremental signed Debug APK, connects to the requested
wireless-debugging endpoint, installs the package without clearing app data, and
relaunches its main activity for device testing.

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1 -Port 43101

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1 -DeviceAddress 192.168.1.13 -Port 43101
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 65535)]
    [int]$Port,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$DeviceAddress = '192.168.1.13'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Android\TOTP.UI.Avalonia.Android.csproj'
$apkPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Android\bin\Debug\net10.0-android\io.github.legends.otpharbor.debug-Signed.apk'
$packageName = 'io.github.legends.otpharbor.debug'
$device = '{0}:{1}' -f $DeviceAddress, $Port

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

$buildStartedUtc = [DateTime]::UtcNow
dotnet build $projectPath `
    -c Debug `
    -t:SignAndroidPackage `
    --no-incremental `
    -nr:false `
    -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) {
    throw 'The fresh signed Android development package build failed.'
}

if (-not (Test-Path -LiteralPath $apkPath -PathType Leaf)) {
    throw "The signed development APK was not found: $apkPath"
}

$apk = Get-Item -LiteralPath $apkPath
if ($apk.LastWriteTimeUtc -lt $buildStartedUtc) {
    throw "The Android build completed without refreshing the signed APK: $apkPath"
}

$apkHash = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
Write-Host "Installing: $($apk.FullName)"
Write-Host "Timestamp: $($apk.LastWriteTime.ToString('O'))"
Write-Host "SHA-256: $apkHash"
Write-Host "Device: $device"

& $adbPath start-server
if ($LASTEXITCODE -ne 0) {
    throw 'Starting the ADB server failed.'
}

& $adbPath connect $device
if ($LASTEXITCODE -ne 0) {
    throw "Connecting to Android device $device failed."
}

$deviceState = (& $adbPath -s $device get-state 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $deviceState -ne 'device') {
    throw "Android device $device is not connected and authorized (state: '$deviceState')."
}

& $adbPath -s $device install -r $apkPath
if ($LASTEXITCODE -ne 0) {
    throw 'Installing the Android development APK failed.'
}

& $adbPath -s $device shell am force-stop $packageName
if ($LASTEXITCODE -ne 0) {
    throw 'The APK installed, but stopping the previous app process failed.'
}

& $adbPath -s $device shell monkey -p $packageName -c android.intent.category.LAUNCHER 1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'The APK was installed, but launching the app failed.'
}

Write-Host "OTP Harbor was installed and launched on $device."
