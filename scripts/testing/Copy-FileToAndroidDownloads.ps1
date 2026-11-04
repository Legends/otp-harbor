#requires -Version 5.1
<#
.SYNOPSIS
    Copies any local Windows file to a connected Android device's Downloads folder.
.DESCRIPTION
    Adapts the existing OTP Harbor backup transfer script: retains wireless ADB
    discovery, reconnection, pairing, and physical-device identity caching.
    Works with USB or wireless ADB and handles multiple transports for one phone.
    Use -Device with an ADB serial (including emulator-5554) when needed.

    Uses adb push to copy a local file into /sdcard/Download. Verifies both
    file size and SHA-256 before publishing the file under its original name.
    Files are not overwritten unless -Force is provided; the previous file is
    preserved with a timestamped suffix when -Force is used.

    This script never accesses Android application-private data.
.PARAMETER FilePath
    Absolute or relative path to the local file. Mandatory.
.PARAMETER Device
    Optional ADB device serial (e.g. emulator-5554) or wireless IPv4:port.
.PARAMETER DeviceAddress
    Wireless ADB IPv4 address, used with -Port.
.PARAMETER Port
    Wireless ADB port, used with -DeviceAddress.
.PARAMETER Force
    Replace an existing file while preserving the old one under a timestamped name.
.EXAMPLE
    .\Copy-FileToAndroidDownloads.ps1 -FilePath "C:\Users\Admin\Downloads\otp-harbor-demo-accounts.json"
.EXAMPLE
    .\Copy-FileToAndroidDownloads.ps1 "C:\Users\Admin\Downloads\My Image.png"
.EXAMPLE
    .\Copy-FileToAndroidDownloads.ps1 -FilePath "C:\Temp\demo.otphicons" -Device "192.168.2.105:37971"
.EXAMPLE
    .\Copy-FileToAndroidDownloads.ps1 -FilePath "C:\Temp\demo.json" -Device emulator-5554
.EXAMPLE
    .\Copy-FileToAndroidDownloads.ps1 -FilePath "C:\Temp\demo.json" -Force
.EXAMPLE
    .\Copy-FileToAndroidDownloads.ps1 -FilePath "C:\Temp\demo.json" -WhatIf
#>
[CmdletBinding(DefaultParameterSetName = 'Endpoint', SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(ParameterSetName = 'Endpoint')]
    [ValidateNotNullOrEmpty()]
    [string]$Device,

    [Parameter(Mandatory = $true, ParameterSetName = 'Components')]
    [ValidateRange(1, 65535)]
    [int]$Port,

    [Parameter(ParameterSetName = 'Components')]
    [ValidateNotNullOrEmpty()]
    [string]$DeviceAddress = '192.168.1.13',

    [Parameter(Mandatory = $true, Position = 0)]
    [Alias('Source', 'SourceFile')]
    [ValidateNotNullOrEmpty()]
    [string]$FilePath,

    [Parameter()]
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$downloadFolder = '/sdcard/Download'

if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
    throw "File not found: $FilePath"
}
$FilePath = (Resolve-Path -LiteralPath $FilePath).Path
$fileName = [IO.Path]::GetFileName($FilePath)
if ([string]::IsNullOrWhiteSpace($fileName)) {
    throw 'The supplied file path has no filename.'
}

# ADB serials (USB/emulator) are accepted. IPv4:port endpoints are also
# normalized and may trigger wireless connection recovery and pairing.
$deviceWasSupplied = $PSBoundParameters.ContainsKey('Device')
$deviceIsWirelessEndpoint = $false
if ($deviceWasSupplied) {
    if ($device -match '^(?<Address>(?:\d{1,3}\.){3}\d{1,3}):(?<Port>\d{1,5})$') {
        $parsedAddress = $null
        $parsedPort = 0
        if (-not [Net.IPAddress]::TryParse($Matches.Address, [ref]$parsedAddress) -or
            $parsedAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
            -not [int]::TryParse($Matches.Port, [ref]$parsedPort) -or
            $parsedPort -lt 1 -or $parsedPort -gt 65535) {
            throw 'Device contains an invalid IP address or TCP port.'
        }
        $device = '{0}:{1}' -f $parsedAddress, $parsedPort
        $deviceIsWirelessEndpoint = $true
    }
    elseif ($device -notmatch '^[A-Za-z0-9._:-]+$') {
        throw 'Device must be an ADB serial (such as emulator-5554) or an IPv4:port endpoint.'
    }
}
elseif ($PSCmdlet.ParameterSetName -eq 'Components') {
    $parsedAddress = $null
    if (-not [Net.IPAddress]::TryParse($DeviceAddress, [ref]$parsedAddress) -or
        $parsedAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) {
        throw 'DeviceAddress must be a valid IPv4 address.'
    }
    $device = '{0}:{1}' -f $parsedAddress, $Port
    $deviceWasSupplied = $true
    $deviceIsWirelessEndpoint = $true
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

function Get-WirelessAdbEndpoints(
    [string]$ServicePattern,
    [string]$PreferredAddress = '') {
    $endpoints = @(& $adbPath mdns services 2>$null | ForEach-Object {
        if ($_ -match "$ServicePattern.*?(?<Endpoint>(?:\d{1,3}\.){3}\d{1,3}:\d{1,5})") {
            $Matches.Endpoint
        }
    } | Sort-Object -Unique)

    if ([string]::IsNullOrWhiteSpace($PreferredAddress)) {
        return $endpoints
    }

    @($endpoints | Where-Object { $_.StartsWith("${PreferredAddress}:", [StringComparison]::Ordinal) })
}

function Read-AndroidEndpoint(
    [string]$Prompt,
    [string]$PreferredAddress = '') {
    $answer = Read-Host $Prompt
    if ([string]::IsNullOrWhiteSpace($answer)) {
        throw 'No Android endpoint was entered. Rerun in an interactive terminal or pass -Device "IP:Port" from the main Wireless debugging screen.'
    }
    $endpoint = $answer.Trim()
    if ($endpoint -notmatch '^(?<Address>(?:\d{1,3}\.){3}\d{1,3}):(?<Port>\d{1,5})$') {
        throw 'The Android endpoint must use the IPv4:port format.'
    }

    $parsedAddress = $null
    $parsedPort = 0
    if (-not [Net.IPAddress]::TryParse($Matches.Address, [ref]$parsedAddress) -or
        -not [int]::TryParse($Matches.Port, [ref]$parsedPort) -or
        $parsedPort -lt 1 -or
        $parsedPort -gt 65535 -or
        (-not [string]::IsNullOrWhiteSpace($PreferredAddress) -and
            $parsedAddress.ToString() -cne $PreferredAddress)) {
        throw 'The Android endpoint contains an invalid or unexpected address or port.'
    }

    '{0}:{1}' -f $parsedAddress, $parsedPort
}

function Request-AndroidWirelessPairing([string]$PreferredAddress = '') {
    Write-Host ''
    Write-Host 'This computer is not authorized for Android wireless debugging.'
    Write-Host 'On the phone, open Developer options > Wireless debugging > Pair device with pairing code.'
    [void](Read-Host 'Leave that dialog open, then press ENTER here')

    $pairingEndpoints = @(Get-WirelessAdbEndpoints `
        '_adb-tls-pairing\._tcp' `
        $PreferredAddress)
    $pairingEndpoint = if ($pairingEndpoints.Count -eq 1) {
        $pairingEndpoints[0]
    }
    else {
        $exampleAddress = if ([string]::IsNullOrWhiteSpace($PreferredAddress)) {
            '<PHONE-IP>'
        }
        else {
            $PreferredAddress
        }
        Read-AndroidEndpoint `
            "Enter the pairing IP:port shown by Android (for example ${exampleAddress}:12345)" `
            $PreferredAddress
    }

    Write-Host 'Enter the six-digit code when ADB prompts for the pairing code.'
    & $adbPath pair $pairingEndpoint
    if ($LASTEXITCODE -ne 0) {
        throw "ADB could not pair with $pairingEndpoint. Reopen the Android pairing dialog and try again."
    }

    Write-Host "Paired Android wireless debugging through $pairingEndpoint."
    ($pairingEndpoint -split ':', 2)[0]
}

function Test-AndroidTcpEndpoint([string]$Endpoint) {
    if ($Endpoint -notmatch '^(?<Address>(?:\d{1,3}\.){3}\d{1,3}):(?<Port>\d{1,5})$') {
        return $false
    }

    $client = [Net.Sockets.TcpClient]::new()
    try {
        $connection = $client.ConnectAsync($Matches.Address, [int]$Matches.Port)
        if (-not $connection.Wait(1500)) {
            return $false
        }

        -not $connection.IsFaulted -and $client.Connected
    }
    catch {
        $false
    }
    finally {
        $client.Dispose()
    }
}

function Connect-AndroidEndpoint(
    [string]$TargetDevice,
    [bool]$OfferRecovery = $true,
    [bool]$AllowEndpointPrompt = $true) {
    $connectOutput = (& $adbPath connect $TargetDevice 2>&1 | Out-String).Trim()
    $deviceState = (& $adbPath -s $TargetDevice get-state 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and $deviceState -ceq 'device') {
        return $TargetDevice
    }

    if (-not (Test-AndroidTcpEndpoint $TargetDevice)) {
        if (-not $OfferRecovery -or -not $AllowEndpointPrompt) {
            throw "Android wireless debugging is not reachable at $TargetDevice. Confirm the current IP:port on the main Wireless debugging screen."
        }

        Write-Host ''
        Write-Host "Android wireless debugging is not reachable at $TargetDevice."
        $replacement = Read-AndroidEndpoint `
            'Enter the current IP:port from the main Android Wireless debugging screen'
        return Connect-AndroidEndpoint $replacement $true $false
    }

    if (-not $OfferRecovery) {
        throw "Android device $TargetDevice is not connected and authorized (state: '$deviceState'; connect: '$connectOutput')."
    }

    # A reachable TLS debugging port that does not authorize this ADB server needs
    # Android's separate pairing endpoint and one-time pairing code.
    $targetAddress = ($TargetDevice -split ':', 2)[0]
    [void](Request-AndroidWirelessPairing $targetAddress)
    $connectOutput = (& $adbPath connect $TargetDevice 2>&1 | Out-String).Trim()
    $deviceState = (& $adbPath -s $TargetDevice get-state 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $deviceState -cne 'device') {
        throw "Pairing succeeded, but Android device $TargetDevice is not connected. Confirm the current IP:port on the main Wireless debugging screen and rerun with -Device (state: '$deviceState'; connect: '$connectOutput')."
    }

    $TargetDevice
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
$buildStatePath = Join-Path $deviceCacheDirectory 'android-development-build-state.json'
$physicalSerial = $null
if ($deviceWasSupplied) {
    if ($deviceIsWirelessEndpoint) {
        $device = Connect-AndroidEndpoint $device
    }
    else {
        $state = (& $adbPath -s $device get-state 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $state -cne 'device') {
            throw "ADB device '$device' is not connected or not authorized. Check 'adb devices -l'."
        }
    }

    $physicalSerial = Get-AndroidPhysicalSerial $device
    if ([string]::IsNullOrWhiteSpace($physicalSerial)) {
        throw "Android device $device did not expose ro.serialno."
    }
}
else {
    $mdnsEndpoints = @(Get-WirelessAdbEndpoints '_adb-tls-connect\._tcp')
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
        Write-Host ''
        Write-Host 'No connected Android device or discoverable wireless-debugging endpoint was found.'
        $device = Read-AndroidEndpoint `
            'Enter the current IP:port from the main Android Wireless debugging screen'
        $device = Connect-AndroidEndpoint $device
        $mdnsEndpoints = @($device)
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
            throw 'Android wireless debugging did not expose an authorized device after connection recovery.'
        }
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



# Everything below uses an explicit ADB serial. Never rely on ambiguous adb defaults.
function Invoke-SelectedAdb {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    & $adbPath -s $device @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ADB operation failed on ${device}: $($Arguments -join ' ') (exit $LASTEXITCODE)."
    }
}

function Get-SelectedAdbText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $result = (& $adbPath -s $device @Arguments 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "ADB operation failed on ${device}: $($Arguments -join ' '): $result"
    }
    return $result
}

function Test-SelectedAdb {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    try {
        $null = & $adbPath -s $device @Arguments 2>&1
        return ($LASTEXITCODE -eq 0)
    }
    catch { return $false }
}

# ADB shell joins its arguments into a remote shell command. Quote Android
# paths to support filenames containing spaces, apostrophes and shell symbols.
function Quote-AndroidShellPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return "'" + $Path.Replace("'", "'\''") + "'"
}

Write-Host "Selected Android device: $device"
$destination = "$downloadFolder/$fileName"
$incoming = "$destination.incoming-$([guid]::NewGuid().ToString('N'))"
$oldFile = "$destination.previous-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([guid]::NewGuid().ToString('N').Substring(0, 6))"
$quotedDestination = Quote-AndroidShellPath $destination
$quotedIncoming = Quote-AndroidShellPath $incoming
$quotedOldBackup = Quote-AndroidShellPath $oldFile

$exists = Test-SelectedAdb -Arguments @('shell', 'test', '-f', $quotedDestination)
if ($exists -and -not $Force) {
    throw "File already exists in Android Downloads: $destination`nPass -Force to preserve the existing file under a .previous- timestamp and replace it."
}

if (-not $PSCmdlet.ShouldProcess("${device}:$destination", 'Copy the file into Android Downloads')) {
    return
}

$expectedSize = (Get-Item -LiteralPath $FilePath).Length
$expectedSha256 = (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Transferring $fileName ($expectedSize bytes) to $device ..."

Invoke-SelectedAdb -Arguments @('shell', 'mkdir', '-p', $downloadFolder)
try {
    # adb push is binary-safe on Windows PowerShell 5.1 and PowerShell 7+.
    Invoke-SelectedAdb -Arguments @('push', $FilePath, $incoming)

    $sizeText = Get-SelectedAdbText -Arguments @('shell', 'stat', '-c', '%s', $quotedIncoming)
    $actualSize = [long]0
    if (-not [long]::TryParse($sizeText, [ref]$actualSize) -or $actualSize -ne $expectedSize) {
        throw "Size mismatch for ${fileName}: Windows=$expectedSize bytes, Android=$sizeText bytes. Destination has not been replaced."
    }
    $hashText = Get-SelectedAdbText -Arguments @('shell', 'sha256sum', $quotedIncoming)
    if ($hashText -notmatch '^([0-9a-fA-F]{64})\s') {
        throw "Could not parse Android SHA-256 result: $hashText"
    }
    if ($Matches[1].ToLowerInvariant() -cne $expectedSha256) {
        throw "SHA-256 verification failed for ${fileName}. Destination has not been replaced."
    }

    if ($exists) {
        Invoke-SelectedAdb -Arguments @('shell', 'cp', $quotedDestination, $quotedOldBackup)
        Write-Host "Preserved existing Android file as $oldFile"
    }
    Invoke-SelectedAdb -Arguments @('shell', 'mv', $quotedIncoming, $quotedDestination)
    Write-Host "Verified SHA-256 and copied $fileName successfully." -ForegroundColor Green
    Write-Host "Android path: $destination"
    Write-Host 'Visible in the Android Files / Downloads app.'
}
finally {
    # Clean only the temporary upload, not any previously existing files.
    $null = & $adbPath -s $device shell rm -f $quotedIncoming 2>$null
}
