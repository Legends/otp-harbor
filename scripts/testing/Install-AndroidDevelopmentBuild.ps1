<#
.SYNOPSIS
Builds and deploys a fresh OTP Harbor Android development APK to a specific device.

.DESCRIPTION
Locates adb, discovers the previously used phone through ADB mDNS and its stable
ro.serialno value, creates a non-incremental signed Debug APK, installs the package
without clearing app data, and relaunches its main activity for device testing.

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1 -Device "192.168.1.13:43101"

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1 -DeviceAddress 192.168.1.13 -Port 43101
#>
[CmdletBinding(DefaultParameterSetName = 'Endpoint')]
param(
    [Parameter(Position = 0, ParameterSetName = 'Endpoint')]
    [ValidateNotNullOrEmpty()]
    [string]$Device,

    [Parameter(Mandatory = $true, ParameterSetName = 'Components')]
    [ValidateRange(1, 65535)]
    [int]$Port,

    [Parameter(ParameterSetName = 'Components')]
    [ValidateNotNullOrEmpty()]
    [string]$DeviceAddress = '192.168.1.13'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Android\TOTP.UI.Avalonia.Android.csproj'
$apkPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Android\bin\Debug\net10.0-android\io.github.legends.otpharbor.debug-Signed.apk'
$packageName = 'io.github.legends.otpharbor.debug'
$deviceWasSupplied = $PSBoundParameters.ContainsKey('Device')
if ($deviceWasSupplied) {
    if ($device -notmatch '^(?<Address>(?:\d{1,3}\.){3}\d{1,3}):(?<Port>\d{1,5})$') {
        throw "Device must use the IPv4:port format, for example 192.168.1.13:43101."
    }

    $parsedAddress = $null
    $parsedPort = 0
    if (-not [Net.IPAddress]::TryParse($Matches.Address, [ref]$parsedAddress) -or
        -not [int]::TryParse($Matches.Port, [ref]$parsedPort) -or
        $parsedPort -lt 1 -or
        $parsedPort -gt 65535) {
        throw "Device contains an invalid IPv4 address or TCP port."
    }

    $device = '{0}:{1}' -f $parsedAddress, $parsedPort
}
elseif ($PSCmdlet.ParameterSetName -eq 'Components') {
    $device = '{0}:{1}' -f $DeviceAddress, $Port
    $deviceWasSupplied = $true
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
$physicalSerial = $null
if ($deviceWasSupplied) {
    & $adbPath connect $device
    if ($LASTEXITCODE -ne 0) {
        throw "Connecting to Android device $device failed."
    }

    $deviceState = (& $adbPath -s $device get-state 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $deviceState -ne 'device') {
        throw "Android device $device is not connected and authorized (state: '$deviceState')."
    }

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
