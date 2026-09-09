<#
.SYNOPSIS
Stages a platform-specific package for M3 performance measurement.

.DESCRIPTION
Copies an existing publish into an empty measurement directory, creating a minimal macOS .app layout when required, and preserves the native payload shape used by the automated M3 measurements.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string]$RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$resolvedPublish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $resolvedOutput) {
    $existing = @(Get-ChildItem -LiteralPath $resolvedOutput -Force)
    if ($existing.Count -gt 0) {
        throw "M3 package output must be absent or empty."
    }
}
else {
    New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
}

if ($RuntimeIdentifier -eq "osx-arm64") {
    $appRoot = Join-Path $resolvedOutput "OTP Harbor.app"
    $contents = Join-Path $appRoot "Contents"
    $macOs = Join-Path $contents "MacOS"
    $resources = Join-Path $contents "Resources"
    New-Item -ItemType Directory -Path $macOs -Force | Out-Null
    New-Item -ItemType Directory -Path $resources -Force | Out-Null
    Copy-Item -Path (Join-Path $resolvedPublish "*") -Destination $macOs -Recurse

    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>OTP Harbor</string>
  <key>CFBundleExecutable</key><string>TOTP.UI.Avalonia.Desktop</string>
  <key>CFBundleIdentifier</key><string>io.github.legends.totpmanager</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>OTP Harbor</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>2.0.0</string>
  <key>CFBundleVersion</key><string>2.0.0</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSCameraUsageDescription</key><string>Scan a TOTP setup QR code after you explicitly start the camera scanner.</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
"@
    $plistPath = Join-Path $contents "Info.plist"
    [IO.File]::WriteAllText($plistPath, $plist, [Text.UTF8Encoding]::new($false))
    & chmod "+x" (Join-Path $macOs "TOTP.UI.Avalonia.Desktop")
    if ($LASTEXITCODE -ne 0) { throw "Could not mark the macOS host executable." }
}
else {
    Copy-Item -Path (Join-Path $resolvedPublish "*") -Destination $resolvedOutput -Recurse
    if ($RuntimeIdentifier -eq "linux-x64") {
        & chmod "+x" (Join-Path $resolvedOutput "TOTP.UI.Avalonia.Desktop")
        if ($LASTEXITCODE -ne 0) { throw "Could not mark the Linux host executable." }
    }
}

Write-Output $resolvedOutput
