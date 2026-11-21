#requires -Version 5.1
<#
.SYNOPSIS
Builds current source and installs the isolated ARM64 Release Test app without clearing app data.
.DESCRIPTION
Retains Release optimization, AOT and signature/package checks. A normal install launches once.
Without -Device, adb selects the single physical USB phone and ignores running emulators.
TestStartup opts into three force-stop/cold-launch checks of twenty seconds each.
BuildOnly builds and verifies the APK without accessing a device. Local testing permits only
SDK patch roll-forward within the repository's pinned feature band; the repository pin is unchanged.
.EXAMPLE
.\Install-AndroidReleasePhone.ps1
.EXAMPLE
.\Install-AndroidReleasePhone.ps1 -TestStartup
.EXAMPLE
.\Install-AndroidReleasePhone.ps1 -BuildOnly
#>
[CmdletBinding()]
param(
    [string]$Device,
    [switch]$BuildOnly,
    [switch]$TestStartup,
    [string]$AdbPath,
    [string]$BuildToolsPath,
    [string]$AaptPath,
    [string]$JavaPath,
    [ValidateRange(3, 2100000000)]
    [int]$VersionCode = 3,
    [string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($BuildOnly -and $TestStartup) { throw 'BuildOnly and TestStartup cannot be used together.' }
. (Join-Path $PSScriptRoot '../Testing.Common.ps1')
$releaseRoot = Get-OtpHarborRepositoryRoot -StartDirectory $PSScriptRoot
$releasePackage = 'io.github.legends.otpharbor.releasetest'
$releaseVersion = '0.1.0-release-test'
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $releaseRoot 'artifacts/testing/android-release-phone'
}
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
$releaseRunner = Join-Path $ArtifactDirectory 'sdk-runner'
$null = New-Item -ItemType Directory -Path $releaseRunner -Force
$releaseSdk = (Get-Content -LiteralPath (Join-Path $releaseRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
@{ sdk = @{ version = $releaseSdk; rollForward = 'latestPatch'; allowPrerelease = $false } } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRunner 'global.json') -Encoding UTF8

$releaseSdkRoot = Join-Path $env:LOCALAPPDATA 'Android/Sdk'
if (-not $BuildOnly) {
    if ([string]::IsNullOrWhiteSpace($AdbPath)) {
        $releaseAdbCommand = Get-Command adb -ErrorAction SilentlyContinue
        if ($null -ne $releaseAdbCommand) { $AdbPath = $releaseAdbCommand.Source }
        elseif (Test-Path -LiteralPath 'D:/android-platform-tools/adb.exe') { $AdbPath = 'D:/android-platform-tools/adb.exe' }
        else { $AdbPath = Join-Path $releaseSdkRoot 'platform-tools/adb.exe' }
    }
    if (-not (Test-Path -LiteralPath $AdbPath -PathType Leaf)) { throw 'adb was not found. Supply -AdbPath.' }
    if ([string]::IsNullOrWhiteSpace($Device)) {
        $releasePhysicalSerial = @(& $AdbPath -d get-serialno 2>$null | ForEach-Object { ([string]$_).Trim() })
        if ($LASTEXITCODE -ne 0 -or $releasePhysicalSerial.Count -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$releasePhysicalSerial[0]) -or
            [string]$releasePhysicalSerial[0] -match '^(unknown|emulator-.*)$') {
            throw 'Could not select one physical USB phone. Connect and authorize one phone, or specify -Device if several phones are connected. Emulators are ignored.'
        }
        $Device = ([string]$releasePhysicalSerial[0]).Trim()
        $releasePhysicalState = @(& $AdbPath -s $Device get-state 2>$null)
        if ($LASTEXITCODE -ne 0 -or ($releasePhysicalState -join '').Trim() -cne 'device') {
            throw 'The physical phone is offline or unauthorized. Unlock it and allow USB debugging, then retry.'
        }
    }
}
if ([string]::IsNullOrWhiteSpace($BuildToolsPath)) {
    $releaseBuildTools = @(Get-ChildItem -LiteralPath (Join-Path $releaseSdkRoot 'build-tools') -Directory |
        Where-Object Name -Match '^\d+\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending)
    if ($releaseBuildTools.Count -eq 0) { throw 'Android SDK build-tools were not found. Supply -BuildToolsPath.' }
    $BuildToolsPath = $releaseBuildTools[0].FullName
}
if ([string]::IsNullOrWhiteSpace($JavaPath)) {
    $releaseJava = Get-Command java -ErrorAction SilentlyContinue
    if ($null -ne $releaseJava) { $JavaPath = $releaseJava.Source }
    else { $JavaPath = 'C:/Program Files/Android/openjdk/jdk-21.0.8/bin/java.exe' }
}
if (-not (Test-Path -LiteralPath $JavaPath -PathType Leaf)) { throw 'Java was not found. Supply -JavaPath.' }
if ([string]::IsNullOrWhiteSpace($AaptPath)) { $AaptPath = Join-Path $BuildToolsPath 'aapt.exe' }
$releaseSigner = Join-Path $BuildToolsPath 'lib/apksigner.jar'
if (-not (Test-Path -LiteralPath $AaptPath) -or -not (Test-Path -LiteralPath $releaseSigner)) {
    throw 'Android aapt or apksigner was not found in the build-tools directory.'
}

Push-Location -LiteralPath $releaseRunner
try {
    $releaseSelectedSdk = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $releaseSelectedSdk -notmatch '^\d+\.\d+\.\d+$') {
        throw "Install .NET SDK $releaseSdk or a later patch in the same feature band."
    }
    Write-Host "Building current source with .NET SDK $releaseSelectedSdk (local patch roll-forward; repository pin $releaseSdk)."
    $releaseBuildRoot = Join-Path $ArtifactDirectory "build/$releaseSelectedSdk"
    $releaseBin = Join-Path $releaseBuildRoot 'bin/'
    $releaseObj = Join-Path $releaseBuildRoot 'obj/'
    $releasePublish = Join-Path $releaseBuildRoot 'publish'
    $releaseProps = Join-Path $releaseRunner 'ReleasePhone.props'
    $releasePropsText = @'
<Project>
  <Import Project="__REPO_PROPS__" />
  <PropertyGroup>
    <NuGetLockFilePath>$(MSBuildProjectDirectory)/obj/otp-harbor-local-runtime.lock.json</NuGetLockFilePath>
  </PropertyGroup>
  <PropertyGroup Condition="'$(MSBuildProjectName)' == 'OtpHarbor.UI.Avalonia.Android'">
    <BaseOutputPath>__BIN__</BaseOutputPath>
    <BaseIntermediateOutputPath>__OBJ__</BaseIntermediateOutputPath>
    <DefaultItemExcludes>$(DefaultItemExcludes);bin/**;obj/**</DefaultItemExcludes>
    <AndroidManifest>../scripts/testing/Android/AndroidReleaseTestManifest.xml</AndroidManifest>
  </PropertyGroup>
</Project>
'@
    $releasePropsText = $releasePropsText.Replace('__REPO_PROPS__', [Security.SecurityElement]::Escape((Join-Path $releaseRoot 'Directory.Build.props'))).
        Replace('__BIN__', [Security.SecurityElement]::Escape($releaseBin)).Replace('__OBJ__', [Security.SecurityElement]::Escape($releaseObj))
    [IO.File]::WriteAllText($releaseProps, $releasePropsText)
    $releaseArguments = @('publish', (Join-Path $releaseRoot 'OtpHarbor.UI.Avalonia.Android/OtpHarbor.UI.Avalonia.Android.csproj'),
        '-c', 'Release', '-r', 'android-arm64', '-o', $releasePublish, '-nr:false',
        "-p:DirectoryBuildPropsPath=$releaseProps", "-p:ApplicationId=$releasePackage",
        "-p:ApplicationVersion=$VersionCode", "-p:ApplicationDisplayVersion=$releaseVersion",
        '-p:RunAOTCompilation=true', '-p:Optimize=true', '-p:AndroidPackageFormats=apk')
    & dotnet @releaseArguments
    if ($LASTEXITCODE -ne 0) { throw 'Current-source Release build failed; no APK was installed.' }
}
finally { Pop-Location }

$releaseApk = Join-Path $releasePublish "$releasePackage-Signed.apk"
if (-not (Test-Path -LiteralPath $releaseApk -PathType Leaf)) { throw 'The current build did not produce the signed Release Test APK.' }
$releaseBadging = @(& $AaptPath dump badging $releaseApk)
if ($LASTEXITCODE -ne 0) { throw 'APK inspection failed.' }
if (-not ($releaseBadging -match "package: name='$([regex]::Escape($releasePackage))'.*versionCode='$VersionCode'.*versionName='$([regex]::Escape($releaseVersion))'")) {
    throw 'Release APK identity/version mismatch.'
}
if ($releaseBadging -match '^application-debuggable') { throw 'Release APK is debuggable.' }
$releaseLauncher = @($releaseBadging | Where-Object { $_ -match "^launchable-activity: name='([^']+)'\s+label='OTP Harbor Release Test'" })
if ($releaseLauncher.Count -ne 1) { throw 'Release launcher label or intent filter missing.' }
$releaseActivity = [regex]::Match($releaseLauncher[0], "name='([^']+)'").Groups[1].Value
if (-not ($releaseBadging -match "^native-code: 'arm64-v8a'\s*$")) { throw 'Release APK ABI mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$releaseZip = [IO.Compression.ZipFile]::OpenRead($releaseApk)
try {
    $releaseAot = @($releaseZip.Entries | Where-Object FullName -Match '^lib/arm64-v8a/.*aot.*\.so$')
    if ($releaseAot.Count -eq 0 -or -not ($releaseAot.FullName -match 'TOTP.UI.Avalonia.Mobile')) { throw 'Mobile UI AOT library missing.' }
    Write-Output ('Verified ARM64 AOT libraries: ' + $releaseAot.Count)
}
finally { $releaseZip.Dispose() }
& $JavaPath -jar $releaseSigner verify $releaseApk
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
Write-Output 'PASS: current-source Release identity, launcher, non-debuggable flag, ARM64, AOT and signature.'
$releaseArtifact = Join-Path $ArtifactDirectory 'OTP-Harbor-Release-Test-0.1.0.apk'
Copy-Item -LiteralPath $releaseApk -Destination $releaseArtifact -Force
$releaseHash = (Get-FileHash -LiteralPath $releaseArtifact -Algorithm SHA256).Hash
"$releaseHash  OTP-Harbor-Release-Test-0.1.0.apk" | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'SHA256.txt') -Encoding ASCII
Write-Output "APK: $releaseArtifact"
Write-Output "SHA256: $releaseHash"
if ($BuildOnly) { return }

& $AdbPath -s $Device install -r $releaseArtifact
if ($LASTEXITCODE -ne 0) { throw 'Release installation failed; existing app data was retained. No uninstall fallback is performed.' }
if ($TestStartup) {
    & (Join-Path $PSScriptRoot 'Test-AndroidAppLaunch.ps1') -Device $Device -AdbPath $AdbPath -PackageName $releasePackage
}
else {
    & $AdbPath -s $Device shell am start -W -n "$releasePackage/$releaseActivity"
    if ($LASTEXITCODE -ne 0) { throw 'Release launch failed.' }
}
$releaseInstalled = @(& $AdbPath -s $Device shell dumpsys package $releasePackage)
if ($LASTEXITCODE -ne 0 -or -not ($releaseInstalled -match "versionName=$([regex]::Escape($releaseVersion))")) {
    throw 'Installed version verification failed.'
}
if (-not ($releaseInstalled -match "versionCode=$VersionCode(?:\s|$)")) { throw 'Installed version code verification failed.' }
if ($releaseInstalled -match '^\s*(pkgFlags|flags)=\[.*DEBUGGABLE') { throw 'Installed app is debuggable.' }
if ($TestStartup) { Write-Output 'PASS: current Release Test installed; repeated startup health checks passed.' }
else { Write-Output 'PASS: current Release Test installed and launched once. Use -TestStartup for repeated cold-launch checks.' }
