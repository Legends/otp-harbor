#requires -Version 7.0
<#
.SYNOPSIS
Builds and verifies the production-signed OTP Harbor APK for direct distribution.

.DESCRIPTION
Publishes the Android app with its permanent application ID, deterministic release version, and
an externally supplied signing key. Passwords are accepted only through files so they do not appear
in process arguments or logs. The signing key remains outside the repository.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseTag,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string]$KeyStorePath,

    [Parameter(Mandatory = $true)]
    [string]$KeyAlias,

    [Parameter(Mandatory = $true)]
    [string]$StorePasswordFile,

    [Parameter(Mandatory = $true)]
    [string]$KeyPasswordFile,

    [string]$ExpectedCertificateSha256,

    [switch]$UseNativeAot,
    [string]$NdkDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../.."))
$projectPath = Join-Path $repositoryRoot "OtpHarbor.UI.Avalonia.Android\OtpHarbor.UI.Avalonia.Android.csproj"

foreach ($requiredFile in @($projectPath, $KeyStorePath, $StorePasswordFile, $KeyPasswordFile)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required Android release input is missing: $requiredFile"
    }
}
if ([string]::IsNullOrWhiteSpace($KeyAlias)) {
    throw "KeyAlias must not be empty."
}

$version = & (Join-Path $PSScriptRoot "Get-AndroidReleaseVersion.ps1") -ReleaseTag $ReleaseTag |
    ConvertFrom-Json
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
if (@(Get-ChildItem -LiteralPath $resolvedOutput -Force).Count -ne 0) {
    throw "OutputDirectory must be empty: $resolvedOutput"
}

$publishDirectory = Join-Path $resolvedOutput "publish"
$arguments = @(
    "publish",
    $projectPath,
    "--configuration", "Release",
    "--framework", "net10.0-android",
    "--no-restore",
    "--nologo",
    "--verbosity", "minimal",
    "-p:RuntimeIdentifier=",
    "-p:UseCurrentRuntimeIdentifier=false",
    "-p:AndroidPackageFormats=apk",
    "-p:ApplicationId=$($version.applicationId)",
    "-p:ApplicationVersion=$($version.versionCode)",
    "-p:ApplicationDisplayVersion=$($version.displayVersion)",
    "-p:AndroidKeyStore=true",
    "-p:AndroidSigningKeyStore=$([IO.Path]::GetFullPath($KeyStorePath))",
    "-p:AndroidSigningKeyAlias=$KeyAlias",
    "-p:AndroidSigningStorePass=file:$([IO.Path]::GetFullPath($StorePasswordFile))",
    "-p:AndroidSigningKeyPass=file:$([IO.Path]::GetFullPath($KeyPasswordFile))",
    "--output", $publishDirectory)

$releaseOriginalPath = $env:PATH
try {
    if ($UseNativeAot) {
        if ([string]::IsNullOrWhiteSpace($NdkDirectory)) { throw 'NativeAOT release publishing requires -NdkDirectory.' }
        $releaseNdk = (Resolve-Path -LiteralPath $NdkDirectory).Path
        $releaseNdkProperties = Get-Content (Join-Path $releaseNdk 'source.properties') -Raw
        if ($releaseNdkProperties -notmatch '(?m)^Pkg\.Revision\s*=\s*28\.2\.13676358\s*$') { throw 'The NativeAOT release requires the validated NDK 28.2.13676358.' }
        $releaseNdkHost = if ($IsWindows) { 'windows-x86_64' } elseif ($IsMacOS) { 'darwin-x86_64' } else { 'linux-x86_64' }
        $releaseCompilerPath = Join-Path $releaseNdk "toolchains/llvm/prebuilt/$releaseNdkHost/bin"
        if (-not (Test-Path -LiteralPath $releaseCompilerPath -PathType Container)) { throw 'The NativeAOT host compiler is missing.' }
        $env:PATH = $releaseCompilerPath + [IO.Path]::PathSeparator + $releaseOriginalPath
        $arguments += @('-p:EnableAndroidNativeAot=true', '-p:AndroidAotExperiment=false', "-p:AndroidNdkDirectory=$releaseNdk", '-p:TrimmerSingleWarn=false')
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "The Android release publish failed." }
}
finally { $env:PATH = $releaseOriginalPath }

$signedApks = @(Get-ChildItem -LiteralPath $publishDirectory -Filter "*-Signed.apk" -File -Recurse)
if ($signedApks.Count -ne 1 -or $signedApks[0].Length -le 0) {
    throw "Expected exactly one non-empty signed APK, found $($signedApks.Count)."
}

$androidHome = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($androidHome)) {
    $androidHome = Join-Path $env:LOCALAPPDATA "Android\Sdk"
}
$buildToolsRoot = Join-Path $androidHome "build-tools"
$buildToolDirectories = @(Get-ChildItem -LiteralPath $buildToolsRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending)
$apkSigner = foreach ($directory in $buildToolDirectories) {
    $candidate = Get-ChildItem -LiteralPath $directory.FullName -Filter "apksigner*" `
        -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $candidate) { $candidate; break }
}
if ($null -eq $apkSigner) {
    throw "Android apksigner was not found below $androidHome."
}
$aapt = foreach ($directory in $buildToolDirectories) {
    $candidate = Get-ChildItem -LiteralPath $directory.FullName -Filter "aapt*" `
        -File -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -ceq "aapt" } |
        Select-Object -First 1
    if ($null -ne $candidate) { $candidate; break }
}
if ($null -eq $aapt) {
    throw "Android aapt was not found below $androidHome."
}

if ([string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $javaExecutable = @(
        Get-ChildItem "C:\Program Files\Android\openjdk\jdk-*\bin\java.exe" `
            -File -ErrorAction SilentlyContinue
        Get-Item "C:\Program Files\Android\Android Studio\jbr\bin\java.exe" `
            -ErrorAction SilentlyContinue) |
        Select-Object -First 1
    if ($null -eq $javaExecutable) {
        throw "JAVA_HOME is not configured and no supported local Java runtime was found."
    }
    $env:JAVA_HOME = [IO.Directory]::GetParent($javaExecutable.Directory.FullName).FullName
}

$verification = @(& $apkSigner.FullName verify --verbose --print-certs-pem $signedApks[0].FullName 2>&1 |
    ForEach-Object { [string]$_ })
if ($LASTEXITCODE -ne 0) {
    throw "The Android APK signature is invalid."
}
$fingerprint = & (Join-Path $PSScriptRoot 'Get-AndroidSigningCertificateFingerprint.ps1') `
    -VerificationOutput $verification
$expectedFingerprint = ($ExpectedCertificateSha256 -replace '[^0-9a-fA-F]', '').ToUpperInvariant()
if (-not [string]::IsNullOrWhiteSpace($ExpectedCertificateSha256) -and
    $fingerprint -cne $expectedFingerprint) {
    throw "The APK was not signed by the pinned production certificate."
}

$badging = @(& $aapt.FullName dump badging $signedApks[0].FullName)
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect the Android APK manifest."
}
$packageLine = @($badging | Where-Object { $_ -match '^package:\s' })
if ($packageLine.Count -ne 1 -or
    $packageLine[0] -notmatch "name='$([regex]::Escape([string]$version.applicationId))'" -or
    $packageLine[0] -notmatch "versionCode='$([regex]::Escape([string]$version.versionCode))'" -or
    $packageLine[0] -notmatch "versionName='$([regex]::Escape([string]$version.displayVersion))'") {
    throw "The APK manifest identity or version does not match the release tag."
}
if ($badging -match '^application-debuggable') { throw 'The release APK must not be debuggable.' }
$releaseRuntime = if ($UseNativeAot) { 'NativeAOT' } else { 'Mono' }
& (Join-Path $PSScriptRoot 'Test-AndroidRuntimePayload.ps1') -ApkPath $signedApks[0].FullName -Runtime $releaseRuntime

$releaseVersion = [string]$version.displayVersion
$destination = Join-Path $resolvedOutput "OTP-Harbor-android-universal-$releaseVersion.apk"
Move-Item -LiteralPath $signedApks[0].FullName -Destination $destination

$metadata = [ordered]@{
    applicationId = [string]$version.applicationId
    displayVersion = $releaseVersion
    versionCode = [int64]$version.versionCode
    certificateSha256 = $fingerprint
    sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    fileName = [IO.Path]::GetFileName($destination)
}
$metadataPath = Join-Path $resolvedOutput "android-release.json"
[IO.File]::WriteAllText(
    $metadataPath,
    (($metadata | ConvertTo-Json -Depth 3) + "`n"),
    [Text.UTF8Encoding]::new($false))

Remove-Item -LiteralPath $publishDirectory -Recurse -Force
Write-Output $destination
Write-Output $metadataPath
