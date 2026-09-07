param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ReleaseVersion,

    [string]$AppcastUrl,

    [switch]$FrameworkDependent,

    [switch]$DisableUpdates
)

$ErrorActionPreference = "Stop"
if (-not $IsLinux) { throw "The Linux packages must be assembled on Linux." }

function Write-Utf8FileWithFinalNewline {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $terminatedContent = $Content.TrimEnd([char[]]"`r`n") + "`n"
    [IO.File]::WriteAllText($Path, $terminatedContent, [Text.UTF8Encoding]::new($false))
}

if ($ReleaseVersion -notmatch '^(?<base>\d+\.\d+\.\d+)(?:-rc(?<rc>\d+))?$') {
    throw "ReleaseVersion must match <major>.<minor>.<patch>[-rc<nr>]."
}
$baseVersion = $Matches.base
$releaseCandidateNumber = $Matches.rc
$releaseChannel = if ($releaseCandidateNumber) { "rc" } else { "stable" }

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

$portableRoot = Join-Path $resolvedOutput "OTP-Harbor-linux-x64-$ReleaseVersion"
New-Item -ItemType Directory -Path $portableRoot | Out-Null
Copy-Item -Path (Join-Path $resolvedPublish "*") -Destination $portableRoot -Recurse
& (Join-Path $PSScriptRoot "Set-PackageUpdatePolicy.ps1") `
    -PackageDirectory $portableRoot `
    -DistributionMode direct `
    -Channel $releaseChannel `
    -AppcastUrl $AppcastUrl `
    -DisableUpdates:$DisableUpdates
$portableExecutable = Join-Path $portableRoot "TOTP.UI.Avalonia.Desktop"
& chmod "+x" $portableExecutable
if ($LASTEXITCODE -ne 0) { throw "Could not mark the Linux host executable." }

$tarPath = Join-Path $resolvedOutput "OTP-Harbor-linux-x64-$ReleaseVersion.tar.gz"
& tar -C $portableRoot -czf $tarPath .
if ($LASTEXITCODE -ne 0) { throw "Could not create the portable tarball." }

$debRoot = Join-Path $resolvedOutput "deb-root"
$debControl = Join-Path $debRoot "DEBIAN"
$debApp = Join-Path $debRoot "opt/totp-manager"
$debBin = Join-Path $debRoot "usr/bin"
$debDesktop = Join-Path $debRoot "usr/share/applications"
$debIcon = Join-Path $debRoot "usr/share/icons/hicolor/128x128/apps"
New-Item -ItemType Directory -Path $debControl, $debApp, $debBin, $debDesktop, $debIcon -Force | Out-Null
Copy-Item -Path (Join-Path $resolvedPublish "*") -Destination $debApp -Recurse
& (Join-Path $PSScriptRoot "Set-PackageUpdatePolicy.ps1") `
    -PackageDirectory $debApp `
    -DistributionMode package-manager `
    -Channel $releaseChannel `
    -DisableUpdates
& chmod "+x" (Join-Path $debApp "TOTP.UI.Avalonia.Desktop")
if ($LASTEXITCODE -ne 0) { throw "Could not mark the packaged Linux host executable." }

$debianVersion = if ($releaseCandidateNumber) {
    "$baseVersion~rc$releaseCandidateNumber"
}
else {
    $baseVersion
}
$dependencies = @("libsecret-tools", "libgl1", "libglib2.0-0")
if ($FrameworkDependent) { $dependencies = @("dotnet-runtime-10.0") + $dependencies }
$control = @"
Package: totp-manager
Version: $debianVersion
Section: utils
Priority: optional
Architecture: amd64
Maintainer: OTP Harbor maintainers
Depends: $($dependencies -join ', ')
Description: Local-first desktop TOTP authenticator
 A local-first Avalonia desktop authenticator with encrypted local storage.
"@
Write-Utf8FileWithFinalNewline -Path (Join-Path $debControl "control") -Content $control

$launcher = @'
#!/bin/sh
exec /opt/totp-manager/TOTP.UI.Avalonia.Desktop "$@"
'@
$launcherPath = Join-Path $debBin "totp-manager"
Write-Utf8FileWithFinalNewline -Path $launcherPath -Content $launcher
& chmod "+x" $launcherPath
if ($LASTEXITCODE -ne 0) { throw "Could not mark the Linux launcher executable." }

$iconSource = Join-Path $resolvedPublish "Assets/Icons/app-128.png"
if (-not (Test-Path -LiteralPath $iconSource -PathType Leaf)) {
    throw "The published Linux application icon is missing."
}
Copy-Item -LiteralPath $iconSource -Destination (Join-Path $debIcon "io.github.legends.totpmanager.png")

$desktopEntry = @"
[Desktop Entry]
Type=Application
Name=OTP Harbor
Comment=Local-first desktop TOTP authenticator
Exec=totp-manager
Icon=io.github.legends.totpmanager
Terminal=false
Categories=Utility;Security;
StartupWMClass=OTP Harbor
"@
Write-Utf8FileWithFinalNewline `
    -Path (Join-Path $debDesktop "io.github.legends.totpmanager.desktop") `
    -Content $desktopEntry

$artifactVersion = if ($releaseCandidateNumber) {
    "$baseVersion-rc$releaseCandidateNumber"
}
else {
    $baseVersion
}
$debPath = Join-Path $resolvedOutput "otp-harbor_${artifactVersion}_amd64.deb"
& dpkg-deb --root-owner-group --build $debRoot $debPath
if ($LASTEXITCODE -ne 0) { throw "Could not create the DEB package." }
& dpkg-deb --info $debPath
if ($LASTEXITCODE -ne 0) { throw "The generated DEB package is invalid." }

Write-Output $tarPath
Write-Output $debPath
