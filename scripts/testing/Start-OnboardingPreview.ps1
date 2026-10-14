<#
.SYNOPSIS
Starts the desktop or Android debug app with the onboarding preview visible.

.EXAMPLE
.\scripts\testing\Start-OnboardingPreview.ps1 -Target Desktop

.EXAMPLE
.\scripts\testing\Start-OnboardingPreview.ps1 -Target Mobile -Install

.EXAMPLE
.\scripts\testing\Start-OnboardingPreview.ps1 -Target Mobile -Device "192.168.1.13:43101"
#>
[CmdletBinding()]
param(
    [ValidateSet('Desktop', 'Mobile')]
    [string]$Target = 'Desktop',

    [string]$Device,

    [switch]$Install
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ($Target -eq 'Desktop') {
    if (Get-Process -Name 'TOTP.UI.Avalonia.Desktop' -ErrorAction SilentlyContinue) {
        throw 'OTP Harbor is already running. Close it first so the startup preview switch can take effect.'
    }

    $projectPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Desktop\TOTP.UI.Avalonia.Desktop.csproj'
    $previousPreviewSetting = $env:TOTP_Debug__ShowOnboardingOnStartup
    try {
        $env:TOTP_Debug__ShowOnboardingOnStartup = 'true'
        Start-Process -FilePath 'dotnet' `
            -ArgumentList @('run', '--project', "`"$projectPath`"", '--configuration', 'Debug') `
            -WorkingDirectory $repositoryRoot
    }
    finally {
        if ($null -eq $previousPreviewSetting) {
            Remove-Item Env:TOTP_Debug__ShowOnboardingOnStartup -ErrorAction SilentlyContinue
        }
        else {
            $env:TOTP_Debug__ShowOnboardingOnStartup = $previousPreviewSetting
        }
    }

    Write-Host 'Started the desktop debug app with the onboarding preview enabled.'
    return
}

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

function Get-AuthorizedAndroidDevices {
    $lines = & $adbPath devices 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Listing authorized Android devices failed.'
    }

    @($lines | ForEach-Object {
        if ($_ -match '^(?<Device>\S+)\s+device(?:\s|$)') {
            $Matches.Device
        }
    })
}

function Get-AndroidPhysicalSerial([string]$TargetDevice) {
    $serial = (& $adbPath -s $TargetDevice shell getprop ro.serialno 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($serial)) {
        return $null
    }

    $serial
}

& $adbPath start-server
if ($LASTEXITCODE -ne 0) {
    throw 'Starting the ADB server failed.'
}

$deviceCacheDirectory = Join-Path $env:LOCALAPPDATA 'OTP Harbor\Development'
$deviceSerialCachePath = Join-Path $deviceCacheDirectory 'android-device-serial.txt'
$deviceWasSupplied = $PSBoundParameters.ContainsKey('Device')
$physicalSerial = $null
if ($deviceWasSupplied) {
    if ($Device -match '^(?:\d{1,3}\.){3}\d{1,3}:\d{1,5}$') {
        & $adbPath connect $Device
        if ($LASTEXITCODE -ne 0) {
            throw "Connecting to Android device $Device failed."
        }
    }

    $deviceState = (& $adbPath -s $Device get-state 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $deviceState -ne 'device') {
        throw "Android device $Device is not connected and authorized (state: '$deviceState')."
    }

    $device = $Device
    $physicalSerial = Get-AndroidPhysicalSerial $device
    if ([string]::IsNullOrWhiteSpace($physicalSerial)) {
        throw "Android device $device did not expose ro.serialno."
    }
}
else {
    $mdnsEndpoints = @(& $adbPath mdns services 2>$null | ForEach-Object {
        if ($_ -match '_adb-tls-connect\._tcp.*?(?<Endpoint>(?:\d{1,3}\.){3}\d{1,3}:\d{1,5})') {
            $Matches.Endpoint
        }
    } | Sort-Object -Unique)
    foreach ($endpoint in $mdnsEndpoints) {
        & $adbPath connect $endpoint *> $null
    }

    $deviceRecords = @(Get-AuthorizedAndroidDevices | ForEach-Object {
        $candidateSerial = Get-AndroidPhysicalSerial $_
        if (-not [string]::IsNullOrWhiteSpace($candidateSerial)) {
            [pscustomobject]@{
                Device = $_
                PhysicalSerial = $candidateSerial
                IsMdnsEndpoint = $mdnsEndpoints -contains $_
            }
        }
    })
    if ($deviceRecords.Count -eq 0) {
        throw 'No authorized Android device was found through ADB or wireless-debugging mDNS. Pair the phone or pass -Device "IP:Port" once.'
    }

    $serialGroups = @($deviceRecords | Group-Object PhysicalSerial)
    $cachedSerial = if (Test-Path -LiteralPath $deviceSerialCachePath -PathType Leaf) {
        ([IO.File]::ReadAllText($deviceSerialCachePath)).Trim()
    }
    else {
        $null
    }
    $targetGroup = if (-not [string]::IsNullOrWhiteSpace($cachedSerial)) {
        $serialGroups | Where-Object Name -CEQ $cachedSerial | Select-Object -First 1
    }
    elseif ($serialGroups.Count -eq 1) {
        $serialGroups[0]
    }
    else {
        $null
    }
    if ($null -eq $targetGroup) {
        throw 'Multiple Android devices were found and none matched the locally remembered phone. Pass -Device "IP:Port" once to select it.'
    }

    $selectedRecord = $targetGroup.Group |
        Where-Object IsMdnsEndpoint |
        Select-Object -First 1
    if ($null -eq $selectedRecord) {
        $selectedRecord = $targetGroup.Group |
            Where-Object { $_.Device -match ':' } |
            Select-Object -First 1
    }
    if ($null -eq $selectedRecord) {
        $selectedRecord = $targetGroup.Group | Select-Object -First 1
    }
    $device = $selectedRecord.Device
    $physicalSerial = $targetGroup.Name
    Write-Host "Discovered device: $device (serial: $physicalSerial)"
}

New-Item -ItemType Directory -Path $deviceCacheDirectory -Force | Out-Null
[IO.File]::WriteAllText($deviceSerialCachePath, $physicalSerial)

$packageName = 'io.github.legends.otpharbor.debug'
$receiverName = 'io.github.legends.otpharbor.debug.DebugAccountDataReceiver'
$actionName = 'io.github.legends.otpharbor.debug.action.SHOW_ONBOARDING'
if ($Install) {
    $installerPath = Join-Path $PSScriptRoot 'Install-AndroidDevelopmentBuild.ps1'
    if ($device -match '^(?:\d{1,3}\.){3}\d{1,3}:\d{1,5}$') {
        & $installerPath -Device $device
    }
    else {
        & $installerPath
    }
}

$packagePath = (& $adbPath -s $device shell pm path $packageName 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $packagePath.StartsWith('package:', [StringComparison]::Ordinal)) {
    throw 'The Android debug app is not installed. Rerun with -Install.'
}

$packageDetails = (& $adbPath -s $device shell dumpsys package $packageName 2>$null | Out-String)
if ($LASTEXITCODE -ne 0 -or $packageDetails -notmatch [regex]::Escape($actionName)) {
    throw 'The installed Android debug app is older than the onboarding preview switch. Rerun with -Install.'
}

& $adbPath -s $device shell input keyevent KEYCODE_WAKEUP | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Waking the Android device failed.' }
Start-Sleep -Milliseconds 500

$windowPolicy = (& $adbPath -s $device shell dumpsys window policy 2>$null | Out-String)
$deviceIsLocked = $windowPolicy -match '(?m)^\s*mInputRestricted=true\s*$'

& $adbPath -s $device shell monkey -p $packageName -c android.intent.category.LAUNCHER 1 *> $null
if ($LASTEXITCODE -ne 0) { throw 'Launching the Android debug app failed.' }
Start-Sleep -Milliseconds 1500
& $adbPath -s $device shell am broadcast -a $actionName -n "$packageName/$receiverName" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Showing the Android onboarding preview failed.' }

if ($deviceIsLocked) {
    Write-Warning 'The onboarding preview is ready behind the Android lock screen. Unlock the phone to see it.'
}
else {
    Write-Host "Started the mobile debug app with the onboarding preview enabled on $device."
}
