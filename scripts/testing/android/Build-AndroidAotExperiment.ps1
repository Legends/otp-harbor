#requires -Version 7.0
<#
.SYNOPSIS
Builds an isolated Release APK for a Mono AOT versus NativeAOT phone comparison.
.DESCRIPTION
Both modes use the same test package identity and manifest so data can be retained
between installations. No APK is installed and no production signing inputs are used.
#>
[CmdletBinding()]
param(
    [ValidateSet('Mono', 'NativeAOT')][string]$Runtime = 'NativeAOT',
    [ValidateSet('arm64', 'x64', 'universal')][string]$Architecture = 'arm64',
    [string]$NdkDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../shared/Testing.Common.ps1')
$aotRoot = Get-OtpHarborRepositoryRoot -StartDirectory $PSScriptRoot
$aotPackage = 'io.github.legends.otpharbor.nativeaotprobe'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $aotRoot "artifacts/android/testing/aot/$Runtime/$Architecture"
}
$aotOutput = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $aotOutput -Force
$aotPropsPath = Join-Path $aotOutput 'Experiment.props'
$aotProps = @'
<Project>
  <Import Project="__REPO_PROPS__" />
  <PropertyGroup>
    <NuGetLockFilePath>$(MSBuildProjectDirectory)/obj/aot-experiment.packages.lock.json</NuGetLockFilePath>
  </PropertyGroup>
  <PropertyGroup Condition="'$(MSBuildProjectName)' == 'OtpHarbor.UI.Avalonia.Android'">
    <BaseOutputPath>__OUTPUT__/build/$(MSBuildProjectName)/bin/</BaseOutputPath>
    <BaseIntermediateOutputPath>__OUTPUT__/build/$(MSBuildProjectName)/obj/</BaseIntermediateOutputPath>
    <DefaultItemExcludes>$(DefaultItemExcludes);bin/**;obj/**</DefaultItemExcludes>
    <AndroidManifest>../scripts/testing/android/AndroidNativeAotTestManifest.xml</AndroidManifest>
  </PropertyGroup>
</Project>
'@
$aotProps = $aotProps.Replace('__REPO_PROPS__', [Security.SecurityElement]::Escape((Join-Path $aotRoot 'Directory.Build.props'))).
    Replace('__OUTPUT__', [Security.SecurityElement]::Escape($aotOutput.Replace('\', '/')))
[IO.File]::WriteAllText($aotPropsPath, $aotProps)
$aotArguments = @(
    'publish', (Join-Path $aotRoot 'OtpHarbor.UI.Avalonia.Android/OtpHarbor.UI.Avalonia.Android.csproj'),
    '-c', 'Release', '-nr:false', '--nologo', '-v:minimal',
    '-o', (Join-Path $aotOutput 'publish'), "-p:DirectoryBuildPropsPath=$aotPropsPath",
    "-p:ApplicationId=$aotPackage", '-p:ApplicationVersion=1', '-p:ApplicationDisplayVersion=0.1.0-aot-test',
    '-p:Optimize=true', '-p:AndroidPackageFormats=apk', '-p:RestoreLockedMode=false')
$aotOriginalPath = $env:PATH
if ($Architecture -eq 'universal') { $aotArguments += '-p:UseCurrentRuntimeIdentifier=false' }
else { $aotArguments += @('-r', "android-$Architecture") }
try {
    if ($Runtime -eq 'NativeAOT') {
        if ([string]::IsNullOrWhiteSpace($NdkDirectory)) { throw 'Supply -NdkDirectory for the NativeAOT compiler (tested with NDK 28.2.13676358).' }
        $aotNdk = (Resolve-Path -LiteralPath $NdkDirectory).Path
        $aotNdkHost = if ($IsWindows) { 'windows-x86_64' } elseif ($IsMacOS) { 'darwin-x86_64' } else { 'linux-x86_64' }
        $aotCompilerDirectory = Join-Path $aotNdk "toolchains/llvm/prebuilt/$aotNdkHost/bin"
        if (-not (Test-Path -LiteralPath $aotCompilerDirectory -PathType Container)) { throw 'The NDK host toolchain is missing.' }
        $env:PATH = $aotCompilerDirectory + [IO.Path]::PathSeparator + $aotOriginalPath
        $aotArguments += @('-p:EnableAndroidNativeAot=true', "-p:AndroidNdkDirectory=$aotNdk", '-p:TrimmerSingleWarn=false')
    }
    else {
        $aotArguments += @('-p:RunAOTCompilation=true')
    }
    & dotnet @aotArguments
    if ($LASTEXITCODE -ne 0) { throw "The $Runtime experiment build failed." }
}
finally { $env:PATH = $aotOriginalPath }
$aotApk = Join-Path $aotOutput "publish/$aotPackage-Signed.apk"
if (-not (Test-Path -LiteralPath $aotApk -PathType Leaf)) { throw 'The isolated signed APK is missing.' }
$aotAbis = if ($Architecture -eq 'universal') { @('arm64-v8a', 'x86_64') } elseif ($Architecture -eq 'arm64') { @('arm64-v8a') } else { @('x86_64') }
& (Join-Path $aotRoot 'scripts/release/android/Test-AndroidRuntimePayload.ps1') -ApkPath $aotApk -Runtime $Runtime -Architectures $aotAbis
$aotArtifact = Join-Path $aotOutput "OTP-Harbor-AOT-Test-$Runtime.apk"
Copy-Item -LiteralPath $aotApk -Destination $aotArtifact -Force
Write-Output "$Runtime experiment APK: $aotArtifact"
Write-Output "SHA256: $((Get-FileHash -LiteralPath $aotArtifact -Algorithm SHA256).Hash)"
