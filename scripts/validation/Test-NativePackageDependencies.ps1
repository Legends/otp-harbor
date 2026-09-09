<#
.SYNOPSIS
Validates native runtime contents for a platform package.

.DESCRIPTION
Requires the correct app host and OpenCV native library, rejects foreign-platform binaries, and on Unix verifies dynamic-library resolution so missing native dependencies fail the release.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string]$RuntimeIdentifier
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $PackageDirectory).Path

function Assert-NoForeignRuntime {
    param([string[]]$Patterns)
    foreach ($pattern in $Patterns) {
        if (Get-ChildItem -LiteralPath $root -File -Recurse -Filter $pattern | Select-Object -First 1) {
            throw "Package contains a native library for a foreign runtime: $pattern"
        }
    }
}

switch ($RuntimeIdentifier) {
    "win-x64" {
        $appHost = Join-Path $root "TOTP.UI.Avalonia.Desktop.exe"
        $openCv = Get-ChildItem -LiteralPath $root -File -Recurse -Filter "OpenCvSharpExtern.dll" |
            Select-Object -First 1
        if (-not (Test-Path -LiteralPath $appHost -PathType Leaf) -or $null -eq $openCv) {
            throw "Windows package is missing its host or OpenCV native runtime."
        }
        Assert-NoForeignRuntime @("libOpenCvSharpExtern.so", "libOpenCvSharpExtern.dylib")
    }
    "linux-x64" {
        $appHost = Join-Path $root "TOTP.UI.Avalonia.Desktop"
        $openCv = Get-ChildItem -LiteralPath $root -File -Recurse -Filter "libOpenCvSharpExtern.so" |
            Select-Object -First 1
        if (-not (Test-Path -LiteralPath $appHost -PathType Leaf) -or $null -eq $openCv) {
            throw "Linux package is missing its host or OpenCV native runtime."
        }
        Assert-NoForeignRuntime @("OpenCvSharpExtern.dll", "libOpenCvSharpExtern.dylib")
        foreach ($binary in @($appHost, $openCv.FullName)) {
            $dependencies = & ldd $binary 2>&1
            if ($LASTEXITCODE -ne 0 -or ($dependencies -join "`n") -match 'not found') {
                throw "Linux native dependency validation failed."
            }
        }
    }
    "osx-arm64" {
        $appHost = Join-Path $root "OTP Harbor.app/Contents/MacOS/TOTP.UI.Avalonia.Desktop"
        $openCv = Get-ChildItem -LiteralPath $root -File -Recurse -Filter "libOpenCvSharpExtern.dylib" |
            Select-Object -First 1
        if (-not (Test-Path -LiteralPath $appHost -PathType Leaf) -or $null -eq $openCv) {
            throw "macOS package is missing its host or OpenCV native runtime."
        }
        Assert-NoForeignRuntime @("OpenCvSharpExtern.dll", "libOpenCvSharpExtern.so")
        foreach ($binary in @($appHost, $openCv.FullName)) {
            & otool -L $binary | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "macOS native dependency validation failed."
            }
        }
    }
}

Write-Output "Native package dependencies are valid for $RuntimeIdentifier."
