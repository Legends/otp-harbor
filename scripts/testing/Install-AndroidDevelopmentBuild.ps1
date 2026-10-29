<#
.SYNOPSIS
Builds when needed and deploys the OTP Harbor Android development APK to a specific device.

.DESCRIPTION
Locates adb, discovers the previously used phone through ADB mDNS and its stable
ro.serialno value, and fingerprints the Android build inputs. The normal path builds
an incremental signed Debug APK only for the connected device ABI. CleanBuild retains
the non-incremental universal-package verification path, while FastDeploy uses the
.NET for Android Install target and external Debug assemblies. The script preserves
app data and relaunches the main activity for device testing. When wireless ADB is
unavailable, it distinguishes a stale connection endpoint from a missing pairing and
prompts only for the information required by that state.

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1 -Device "192.168.1.13:43101"

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1 -DeviceAddress 192.168.1.13 -Port 43101

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1 -ForceBuild

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1 -CleanBuild

.EXAMPLE
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1 -FastDeploy
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
    [string]$DeviceAddress = '192.168.1.13',

    [Parameter()]
    [switch]$ForceBuild,

    [Parameter()]
    [switch]$CleanBuild,

    [Parameter()]
    [switch]$FastDeploy
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($CleanBuild -and $FastDeploy) {
    throw 'CleanBuild and FastDeploy cannot be used together.'
}
if ($ForceBuild -and $FastDeploy) {
    throw 'ForceBuild is unnecessary with FastDeploy because the Install target always runs.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repositoryRoot 'OtpHarbor.UI.Avalonia.Android\OtpHarbor.UI.Avalonia.Android.csproj'
$debugOutputRoot = Join-Path $repositoryRoot 'OtpHarbor.UI.Avalonia.Android\bin\Debug\net10.0-android'
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

function Get-AndroidDeviceAbi([string]$TargetDevice) {
    $abi = (& $adbPath -s $TargetDevice shell getprop ro.product.cpu.abi 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($abi)) {
        throw "Android device $TargetDevice did not expose ro.product.cpu.abi."
    }

    $abi
}

function Get-AndroidApkAbis([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        @($archive.Entries |
            Where-Object FullName -Match '^lib/[^/]+/.+\.so$' |
            ForEach-Object { $_.FullName.Split('/')[1] } |
            Sort-Object -Unique)
    }
    finally {
        $archive.Dispose()
    }
}

function Resolve-CachedAndroidApk([object]$State, [string]$OutputRoot) {
    if ($null -eq $State -or
        $State.PSObject.Properties.Name -notcontains 'ApkRelativePath') {
        return $null
    }

    $relativePath = [string]$State.ApkRelativePath
    if ([string]::IsNullOrWhiteSpace($relativePath) -or [IO.Path]::IsPathRooted($relativePath)) {
        return $null
    }

    $candidate = [IO.Path]::GetFullPath((Join-Path $OutputRoot $relativePath))
    $relativeToRoot = [IO.Path]::GetRelativePath($OutputRoot, $candidate)
    if ($relativeToRoot.StartsWith('..', [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relativeToRoot) -or
        [IO.Path]::GetExtension($candidate) -cne '.apk' -or
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        return $null
    }

    $candidate
}

function Get-AndroidBuildInputFingerprint {
    $inputPaths = @(
        'TOTP.Core',
        'TOTP.DAL',
        'TOTP.Infrastructure',
        'TOTP.Platform.Android',
        'TOTP.UI.Avalonia.Android',
        'TOTP.UI.Avalonia.Mobile',
        'TOTP.UI.Avalonia.Shared',
        'OtpHarbor.UI.Avalonia.Desktop\Assets\Icons\app-1024.png',
        'Directory.Build.props',
        'NuGet.config',
        'global.json',
        'LICENSE.txt',
        'LICENSING.md',
        'THIRD_PARTY_NOTICES.md'
    )
    $inputFiles = @(
        foreach ($relativeInputPath in $inputPaths) {
            $inputPath = Join-Path $repositoryRoot $relativeInputPath
            if (Test-Path -LiteralPath $inputPath -PathType Leaf) {
                Get-Item -LiteralPath $inputPath
                continue
            }

            if (-not (Test-Path -LiteralPath $inputPath -PathType Container)) {
                throw "Android build input was not found: $relativeInputPath"
            }

            Get-ChildItem -LiteralPath $inputPath -File -Recurse |
                Where-Object FullName -NotMatch '[\\/](?:bin|obj)[\\/]'
        }
    )
    $fingerprintRecords = @($inputFiles |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/')
            $fileHash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "{0}`t{1}" -f $relativePath, $fileHash
        })
    $fingerprintBytes = [Text.Encoding]::UTF8.GetBytes(($fingerprintRecords -join "`n"))
    $fingerprintAlgorithm = [Security.Cryptography.SHA256]::Create()
    try {
        -join ($fingerprintAlgorithm.ComputeHash($fingerprintBytes) |
            ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $fingerprintAlgorithm.Dispose()
    }
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
    $device = Connect-AndroidEndpoint $device

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

$deviceAbi = Get-AndroidDeviceAbi $device
$runtimeIdentifier = & (Join-Path $PSScriptRoot 'Get-AndroidRuntimeIdentifier.ps1') -Abi $deviceAbi
if ([string]::IsNullOrWhiteSpace($runtimeIdentifier)) {
    throw "Could not map Android ABI $deviceAbi to a .NET runtime identifier."
}
Write-Host "Target ABI: $deviceAbi ($runtimeIdentifier)"

# RID-specific convenience builds must not rewrite the repository's canonical,
# RID-neutral lock files used by CI. Each project gets an ignored local lock path.
$isolatedNuGetLockFilePath = '$(MSBuildProjectDirectory)/obj/otp-harbor-local-runtime.lock.json'

$inputFingerprint = Get-AndroidBuildInputFingerprint
$cachedBuildState = $null
if (Test-Path -LiteralPath $buildStatePath -PathType Leaf) {
    try {
        $cachedBuildState = [IO.File]::ReadAllText($buildStatePath) | ConvertFrom-Json
    }
    catch {
        Write-Verbose "Ignoring invalid Android build state: $($_.Exception.Message)"
    }
}
$cachedInputFingerprint = if ($null -ne $cachedBuildState -and
    $cachedBuildState.PSObject.Properties.Name -contains 'InputFingerprint') {
    [string]$cachedBuildState.InputFingerprint
}
else {
    $null
}
$cachedBuildMode = if ($null -ne $cachedBuildState -and
    $cachedBuildState.PSObject.Properties.Name -contains 'BuildMode') {
    [string]$cachedBuildState.BuildMode
}
else {
    $null
}
$cachedRuntimeIdentifier = if ($null -ne $cachedBuildState -and
    $cachedBuildState.PSObject.Properties.Name -contains 'RuntimeIdentifier') {
    [string]$cachedBuildState.RuntimeIdentifier
}
else {
    $null
}
$cachedApkHash = if ($null -ne $cachedBuildState -and
    $cachedBuildState.PSObject.Properties.Name -contains 'ApkSha256') {
    [string]$cachedBuildState.ApkSha256
}
else {
    $null
}
$apkPath = Resolve-CachedAndroidApk $cachedBuildState $debugOutputRoot
$existingApkHash = if ($null -ne $apkPath -and
    (Test-Path -LiteralPath $apkPath -PathType Leaf)) {
    (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
}
else {
    $null
}
$canReuseApk = -not $ForceBuild -and -not $CleanBuild -and -not $FastDeploy -and
    $cachedInputFingerprint -ceq $inputFingerprint -and
    $cachedBuildMode -ceq 'IncrementalDeviceApk' -and
    $cachedRuntimeIdentifier -ceq $runtimeIdentifier -and
    $cachedApkHash -ceq $existingApkHash -and
    -not [string]::IsNullOrWhiteSpace($existingApkHash)

if ($FastDeploy) {
    Write-Host "Fast-deploying the $deviceAbi Debug build to $device..."
    $fastDeployArguments = @(
        'build',
        $projectPath,
        '--configuration', 'Debug',
        '--target:Install',
        '--maxcpucount',
        '--runtime', $runtimeIdentifier,
        "-p:NuGetLockFilePath=$isolatedNuGetLockFilePath",
        "-p:Device=$device",
        '-p:EmbedAssembliesIntoApk=false')
    & dotnet @fastDeployArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Fast deployment failed. Retry without FastDeploy to install a standalone Debug APK.'
    }
}
elseif ($canReuseApk) {
    Write-Host 'Android build inputs are unchanged; skipping the build.'
}
else {
    $buildArguments = @(
        'build',
        $projectPath,
        '--configuration', 'Debug',
        '--target:SignAndroidPackage',
        '--maxcpucount',
        "-p:NuGetLockFilePath=$isolatedNuGetLockFilePath")
    if ($CleanBuild) {
        Write-Host 'Creating a clean universal Debug APK...'
        $buildArguments += @(
            '--no-incremental',
            '-nr:false',
            '-p:UseSharedCompilation=false')
    }
    else {
        Write-Host "Creating an incremental $deviceAbi Debug APK..."
        $buildArguments += @('--runtime', $runtimeIdentifier)
    }

    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'The signed Android development package build failed.'
    }

    $signedApks = @(Get-ChildItem -LiteralPath $debugOutputRoot `
        -Filter "$packageName-Signed.apk" -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending)
    $matchingApks = @($signedApks | Where-Object {
        $apkAbis = @(Get-AndroidApkAbis $_.FullName)
        if ($CleanBuild) {
            $apkAbis -contains 'arm64-v8a' -and $apkAbis -contains 'x86_64'
        }
        else {
            $apkAbis.Count -eq 1 -and $apkAbis[0] -ceq $deviceAbi
        }
    })
    if ($matchingApks.Count -eq 0) {
        $expectedPackage = if ($CleanBuild) {
            'a universal arm64-v8a/x86_64 package'
        }
        else {
            "a single-ABI $deviceAbi package"
        }
        throw "The build did not produce $expectedPackage below $debugOutputRoot."
    }
    $apkPath = $matchingApks[0].FullName

    $buildMode = if ($CleanBuild) { 'CleanUniversalApk' } else { 'IncrementalDeviceApk' }

    $buildState = [ordered]@{
        InputFingerprint = $inputFingerprint
        BuildMode = $buildMode
        RuntimeIdentifier = if ($CleanBuild) { '' } else { $runtimeIdentifier }
        DeviceAbi = if ($CleanBuild) { 'universal' } else { $deviceAbi }
        ApkRelativePath = [IO.Path]::GetRelativePath($debugOutputRoot, $apkPath)
        ApkSha256 = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
        BuiltAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        $buildStatePath,
        ($buildState | ConvertTo-Json))
}

Write-Host "Device: $device"

$deviceState = (& $adbPath -s $device get-state 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $deviceState -ne 'device') {
    throw "Android device $device is not connected and authorized (state: '$deviceState')."
}

if (-not $FastDeploy) {
    $apk = Get-Item -LiteralPath $apkPath
    $apkHash = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
    Write-Host "Installing: $($apk.FullName)"
    Write-Host "Timestamp: $($apk.LastWriteTime.ToString('O'))"
    Write-Host "SHA-256: $apkHash"

    & $adbPath -s $device install -r $apkPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Installing the Android development APK failed.'
    }
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
