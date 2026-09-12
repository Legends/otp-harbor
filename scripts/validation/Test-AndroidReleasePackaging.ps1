<#
.SYNOPSIS
Regression-checks the permanent Android identity, version mapping, and release controls.
#>
$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Read-RequiredFile([string]$RelativePath) {
    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Android release file is missing: $RelativePath"
    }
    [IO.File]::ReadAllText($path)
}

$project = Read-RequiredFile "TOTP.UI.Avalonia.Android\TOTP.UI.Avalonia.Android.csproj"
$manifest = Read-RequiredFile "TOTP.UI.Avalonia.Android\Properties\AndroidManifest.xml"
$installer = Read-RequiredFile "scripts\testing\Install-AndroidDevelopmentBuild.ps1"
$workflow = Read-RequiredFile ".github\workflows\build-and-test.yml"
$documentation = Read-RequiredFile "docs\release\ANDROID.md"

foreach ($required in @(
    '<AndroidProductionApplicationId>io.github.legends.otpharbor</AndroidProductionApplicationId>',
    '<ApplicationId Condition="''$(Configuration)'' == ''Debug''">$(AndroidProductionApplicationId).debug</ApplicationId>',
    '<AndroidPackageFormats Condition="''$(Configuration)'' == ''Release''">apk</AndroidPackageFormats>',
    "'`$(Configuration)' == 'Debug' and '`$(EnableMarketingCapture)' == 'true'",
    "'`$(Configuration)' == 'Release' and '`$(EnableMarketingCapture)' == 'true'",
    'EnableMarketingCapture is restricted to the isolated Debug package.')) {
    if (-not $project.Contains($required, [StringComparison]::Ordinal)) {
        throw "The Android project is missing a permanent identity or distribution control: $required"
    }
}
$activity = Read-RequiredFile "TOTP.UI.Avalonia.Android\MainActivity.cs"
if (-not $activity.Contains('#if OTP_HARBOR_MARKETING_CAPTURE', [StringComparison]::Ordinal)) {
    throw 'The isolated marketing-capture mode is not compile-time restricted.'
}
if (-not $manifest.Contains('android:authorities="${applicationId}.fileprovider"', [StringComparison]::Ordinal) -or
    -not $installer.Contains("`$packageName = 'io.github.legends.otpharbor.debug'", [StringComparison]::Ordinal)) {
    throw "The Android manifest or development installer can collide with the production package."
}

$versionCases = @(
    @{ Tag = "v2.0.0-rc1"; Code = 20000001L; Name = "2.0.0-rc1" },
    @{ Tag = "v2.0.0-rc98"; Code = 20000098L; Name = "2.0.0-rc98" },
    @{ Tag = "v2.0.0"; Code = 20000099L; Name = "2.0.0" },
    @{ Tag = "v2.0.17"; Code = 20001799L; Name = "2.0.17" },
    @{ Tag = "v2.1.0-rc1"; Code = 20100001L; Name = "2.1.0-rc1" })
foreach ($case in $versionCases) {
    $actual = & (Join-Path $repositoryRoot "scripts\release\Get-AndroidReleaseVersion.ps1") `
        -ReleaseTag $case.Tag | ConvertFrom-Json
    if ($actual.applicationId -cne "io.github.legends.otpharbor" -or
        [int64]$actual.versionCode -ne $case.Code -or
        $actual.displayVersion -cne $case.Name) {
        throw "Android version mapping failed for $($case.Tag)."
    }
}

foreach ($invalidTag in @("2.0.0", "v2.0", "v2.0.0-rc0", "v2.0.0-rc99", "v210.0.0")) {
    try {
        & (Join-Path $repositoryRoot "scripts\release\Get-AndroidReleaseVersion.ps1") `
            -ReleaseTag $invalidTag 2>$null | Out-Null
        throw "Android version mapping accepted invalid tag $invalidTag."
    }
    catch {
        if ($_.Exception.Message -eq "Android version mapping accepted invalid tag $invalidTag.") { throw }
    }
}

foreach ($required in @(
    "android-build-test:",
    "package-android-release:",
    "java-version: '21'",
    "environment: android-release",
    "ANDROID_SIGNING_KEYSTORE_BASE64",
    "ANDROID_SIGNING_CERTIFICATE_SHA256",
    'name: android-release-${{ github.sha }}',
    "android-release.json",
    "New-AndroidRelease.ps1")) {
    if (-not $workflow.Contains($required, [StringComparison]::Ordinal)) {
        throw "The release workflow is missing an Android control: $required"
    }
}
foreach ($required in @(
    "io.github.legends.otpharbor",
    "Android developer verification",
    "same app-signing key")) {
    if (-not $documentation.Contains($required, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The Android release guide is missing required guidance: $required"
    }
}

Write-Output "Android identity, versioning, and release packaging controls are valid."
