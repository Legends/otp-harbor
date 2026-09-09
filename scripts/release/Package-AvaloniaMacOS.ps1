<#
.SYNOPSIS
Packages an Avalonia macOS publish as an OTP Harbor application bundle.

.DESCRIPTION
Builds the .app layout and release archive, applies release-channel metadata, and optionally performs Developer ID signing and Apple notarization when a complete supported credential configuration is supplied.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ReleaseVersion,

    [string]$SigningIdentity,
    [string]$NotaryKeychainProfile,
    [string]$NotaryKeyPath,
    [string]$NotaryKeyId,
    [string]$NotaryIssuerId
)

$ErrorActionPreference = "Stop"
if (-not $IsMacOS) { throw "The macOS package must be assembled on macOS." }
if ($ReleaseVersion -notmatch '^(?<base>\d+\.\d+\.\d+)(?:-rc(?<rc>\d+))?$') {
    throw "ReleaseVersion must match <major>.<minor>.<patch>[-rc<nr>]."
}
$baseVersion = $Matches.base
$releaseCandidateNumber = $Matches.rc
$releaseChannel = if ($releaseCandidateNumber) { "rc" } else { "stable" }
$hasNotaryProfile = -not [string]::IsNullOrWhiteSpace($NotaryKeychainProfile)
$hasAnyApiKeyValue = -not [string]::IsNullOrWhiteSpace($NotaryKeyPath) -or
    -not [string]::IsNullOrWhiteSpace($NotaryKeyId) -or
    -not [string]::IsNullOrWhiteSpace($NotaryIssuerId)
$hasCompleteApiKey = -not [string]::IsNullOrWhiteSpace($NotaryKeyPath) -and
    -not [string]::IsNullOrWhiteSpace($NotaryKeyId) -and
    -not [string]::IsNullOrWhiteSpace($NotaryIssuerId)
if ($hasNotaryProfile -and $hasAnyApiKeyValue) {
    throw "Choose either a notary Keychain profile or App Store Connect API key credentials."
}
if ($hasAnyApiKeyValue -and -not $hasCompleteApiKey) {
    throw "Notary API key path, key ID, and issuer ID must be provided together."
}
if (($hasNotaryProfile -or $hasCompleteApiKey) -and [string]::IsNullOrWhiteSpace($SigningIdentity)) {
    throw "Notarization requires SigningIdentity."
}

$resolvedPublish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $resolvedOutput) {
    if (@(Get-ChildItem -LiteralPath $resolvedOutput -Force).Count -ne 0) {
        throw "OutputDirectory must be absent or empty."
    }
}
else {
    New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
}

$bundleVersion = if ($releaseCandidateNumber) {
    "$baseVersion.$releaseCandidateNumber"
}
else {
    "$baseVersion.0"
}
$appRoot = Join-Path $resolvedOutput "OTP Harbor.app"
$contents = Join-Path $appRoot "Contents"
$macOS = Join-Path $contents "MacOS"
$resources = Join-Path $contents "Resources"
New-Item -ItemType Directory -Path $macOS -Force | Out-Null
New-Item -ItemType Directory -Path $resources -Force | Out-Null
Copy-Item -Path (Join-Path $resolvedPublish "*") -Destination $macOS -Recurse
& (Join-Path $PSScriptRoot "Set-PackageUpdatePolicy.ps1") `
    -PackageDirectory $macOS `
    -DistributionMode direct `
    -Channel $releaseChannel

$iconSource = Join-Path $resolvedPublish "Assets/Icons/app-1024.png"
if (-not (Test-Path -LiteralPath $iconSource -PathType Leaf)) {
    throw "The published macOS application icon is missing."
}
$iconSet = Join-Path $resolvedOutput "TOTPManager.iconset"
New-Item -ItemType Directory -Path $iconSet | Out-Null
foreach ($icon in @(
    @{ Name = "icon_16x16.png"; Size = 16 },
    @{ Name = "icon_16x16@2x.png"; Size = 32 },
    @{ Name = "icon_32x32.png"; Size = 32 },
    @{ Name = "icon_32x32@2x.png"; Size = 64 },
    @{ Name = "icon_128x128.png"; Size = 128 },
    @{ Name = "icon_128x128@2x.png"; Size = 256 },
    @{ Name = "icon_256x256.png"; Size = 256 },
    @{ Name = "icon_256x256@2x.png"; Size = 512 },
    @{ Name = "icon_512x512.png"; Size = 512 },
    @{ Name = "icon_512x512@2x.png"; Size = 1024 })) {
    & sips -z $icon.Size $icon.Size $iconSource --out (Join-Path $iconSet $icon.Name) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not render a macOS icon size." }
}
$bundleIcon = Join-Path $resources "TOTPManager.icns"
& iconutil -c icns $iconSet -o $bundleIcon
if ($LASTEXITCODE -ne 0) { throw "Could not create the macOS icon resource." }

$plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>OTP Harbor</string>
  <key>CFBundleExecutable</key><string>TOTP.UI.Avalonia.Desktop</string>
  <key>CFBundleIdentifier</key><string>io.github.legends.totpmanager</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleIconFile</key><string>TOTPManager</string>
  <key>CFBundleName</key><string>OTP Harbor</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$baseVersion</string>
  <key>CFBundleVersion</key><string>$bundleVersion</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSMultipleInstancesProhibited</key><true/>
  <key>NSCameraUsageDescription</key><string>Scan a TOTP setup QR code after you explicitly start the camera scanner.</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
"@
$plistPath = Join-Path $contents "Info.plist"
[IO.File]::WriteAllText($plistPath, $plist, [Text.UTF8Encoding]::new($false))

$entitlements = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.allow-dyld-environment-variables</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
  <key>com.apple.security.device.camera</key><true/>
</dict>
</plist>
"@
$entitlementsPath = Join-Path $resolvedOutput "TOTP.Manager.entitlements"
[IO.File]::WriteAllText($entitlementsPath, $entitlements, [Text.UTF8Encoding]::new($false))

$mainExecutable = Join-Path $macOS "TOTP.UI.Avalonia.Desktop"
& chmod "+x" $mainExecutable
if ($LASTEXITCODE -ne 0) { throw "Could not mark the macOS host executable." }
& plutil -lint $plistPath
if ($LASTEXITCODE -ne 0) { throw "The generated Info.plist is invalid." }

if (-not [string]::IsNullOrWhiteSpace($SigningIdentity)) {
    $machOFiles = Get-ChildItem -LiteralPath $macOS -File -Recurse |
        Where-Object { $_.FullName -ne $mainExecutable -and (& file --brief $_.FullName) -like 'Mach-O*' } |
        Sort-Object { $_.FullName.Split([IO.Path]::DirectorySeparatorChar).Count } -Descending
    foreach ($file in $machOFiles) {
        & codesign --force --options runtime --timestamp --sign $SigningIdentity $file.FullName
        if ($LASTEXITCODE -ne 0) { throw "Could not sign a nested Mach-O file." }
    }

    & codesign --force --options runtime --timestamp --entitlements $entitlementsPath --sign $SigningIdentity $appRoot
    if ($LASTEXITCODE -ne 0) { throw "Could not sign the application bundle." }
    & codesign --verify --deep --strict --verbose=2 $appRoot
    if ($LASTEXITCODE -ne 0) { throw "Application signature verification failed." }
}

$dmgPath = Join-Path $resolvedOutput "OTP-Harbor-macos-arm64-$ReleaseVersion.dmg"
& hdiutil create -volname "OTP Harbor" -srcfolder $appRoot -ov -format UDZO $dmgPath
if ($LASTEXITCODE -ne 0) { throw "Could not create the DMG." }

if (-not [string]::IsNullOrWhiteSpace($SigningIdentity)) {
    & codesign --force --timestamp --sign $SigningIdentity $dmgPath
    if ($LASTEXITCODE -ne 0) { throw "Could not sign the DMG." }
}

if ($hasNotaryProfile -or $hasCompleteApiKey) {
    $notaryArguments = @("notarytool", "submit", $dmgPath, "--wait")
    if ($hasNotaryProfile) {
        $notaryArguments += @("--keychain-profile", $NotaryKeychainProfile)
    }
    else {
        $resolvedNotaryKey = (Resolve-Path -LiteralPath $NotaryKeyPath).Path
        $notaryArguments += @(
            "--key", $resolvedNotaryKey,
            "--key-id", $NotaryKeyId,
            "--issuer", $NotaryIssuerId)
    }
    & xcrun @notaryArguments
    if ($LASTEXITCODE -ne 0) { throw "Apple notarization failed." }
    & xcrun stapler staple $dmgPath
    if ($LASTEXITCODE -ne 0) { throw "Could not staple the notarization ticket." }
    & xcrun stapler validate $dmgPath
    if ($LASTEXITCODE -ne 0) { throw "The stapled notarization ticket is invalid." }
    & spctl --assess --type open --context context:primary-signature --verbose=2 $dmgPath
    if ($LASTEXITCODE -ne 0) { throw "Gatekeeper rejected the notarized DMG." }
}

Write-Output $dmgPath
