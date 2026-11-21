<#
.SYNOPSIS
Creates the release artifact integrity and ownership manifest.

.DESCRIPTION
Classifies supported package filenames by platform, format, ownership, and update policy; records byte length and SHA-256 for every artifact; and writes deterministic release metadata tied to a full source commit.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseVersion,

    [Parameter(Mandatory = $true)]
    [string]$SourceCommit,

    [Parameter(Mandatory = $true)]
    [string[]]$ArtifactPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [ValidateSet("signed", "unsigned-platform-preview", "unsigned-preview")]
    [string]$ReleaseProfile = "signed"
)

$ErrorActionPreference = "Stop"

if ($ReleaseVersion -notmatch '^\d+\.\d+\.\d+(?:-rc\d+)?$') {
    throw "ReleaseVersion must match <major>.<minor>.<patch>[-rc<nr>]."
}
if ($SourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "SourceCommit must be a full 40-character Git commit SHA."
}
if ($ArtifactPath.Count -eq 0) {
    throw "At least one artifact is required."
}

function Get-ArtifactTarget {
    param([string]$FileName)

    switch -Regex ($FileName) {
        '^OTP-Harbor-windows-x64-setup-(?<version>\d+\.\d+\.\d+(?:-rc\d+)?)\.exe$' {
            return [ordered]@{
                operatingSystem = "windows"
                architecture = "x64"
                format = "exe"
                ownership = "windows-installer"
                updatePolicy = "manual-download"
                releaseVersion = $Matches.version
            }
        }
        '^(?:OTP-Harbor|TOTP-Manager)-windows-x64-(?<version>\d+\.\d+\.\d+(?:-rc\d+)?)\.msi$' {
            return [ordered]@{
                operatingSystem = "windows"
                architecture = "x64"
                format = "msi"
                ownership = "windows-installer"
                updatePolicy = "manual-download"
                releaseVersion = $Matches.version
            }
        }
        '^(?:OTP-Harbor|TOTP-Manager)-windows-x64-fast-(?<version>\d+\.\d+\.\d+(?:-rc\d+)?)\.zip$' {
            return [ordered]@{
                operatingSystem = "windows"
                architecture = "x64"
                format = "zip"
                ownership = "application"
                updatePolicy = "manual-download"
                releaseVersion = $Matches.version
            }
        }
        '^(?:OTP-Harbor|TOTP-Manager)-windows-x64-(?<version>\d+\.\d+\.\d+(?:-rc\d+)?)\.zip$' {
            return [ordered]@{
                operatingSystem = "windows"
                architecture = "x64"
                format = "zip"
                ownership = "application"
                updatePolicy = "signed-appcast"
                releaseVersion = $Matches.version
            }
        }
        '^(?:OTP-Harbor|TOTP-Manager)-(?:fast|portable)\.zip$' {
            return [ordered]@{
                operatingSystem = "windows"
                architecture = "x64"
                format = "zip"
                ownership = "application"
                updatePolicy = "signed-appcast"
                releaseVersion = $null
            }
        }
        '^(?:OTP-Harbor|TOTP-Manager)-macos-arm64-(?<version>\d+\.\d+\.\d+(?:-rc\d+)?)\.dmg$' {
            return [ordered]@{
                operatingSystem = "macos"
                architecture = "arm64"
                format = "dmg"
                ownership = "application"
                updatePolicy = "manual-signed-release"
                releaseVersion = $Matches.version
            }
        }
        '^(?:OTP-Harbor|TOTP-Manager)-linux-x64-(?<version>\d+\.\d+\.\d+(?:-rc\d+)?)\.tar\.gz$' {
            return [ordered]@{
                operatingSystem = "linux"
                architecture = "x64"
                format = "tar.gz"
                ownership = "application"
                updatePolicy = "manual-signed-release"
                releaseVersion = $Matches.version
            }
        }
        '^(?:otp-harbor|totp-manager)_(?<version>\d+\.\d+\.\d+(?:(?:~rc|-rc)\d+)?)_amd64\.deb$' {
            return [ordered]@{
                operatingSystem = "linux"
                architecture = "x64"
                format = "deb"
                ownership = "package-manager"
                updatePolicy = "package-manager"
                releaseVersion = $Matches.version.Replace("~rc", "-rc")
            }
        }
        '^OTP-Harbor-android-universal-(?<version>\d+\.\d+\.\d+(?:-rc\d+)?)\.apk$' {
            return [ordered]@{
                operatingSystem = "android"
                architecture = "universal"
                format = "apk"
                ownership = "android-package"
                updatePolicy = "manual-download"
                releaseVersion = $Matches.version
            }
        }
        default {
            throw "Artifact name does not match a supported release target: $FileName"
        }
    }
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw "OutputPath must include a valid parent directory."
}
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$seenNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$maximumDirectUpdateBytes = 128L * 1024 * 1024
$artifacts = foreach ($path in $ArtifactPath) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $file = Get-Item -LiteralPath $resolved
    if (-not $file.PSIsContainer -and $file.Length -gt 0) {
        if (-not $seenNames.Add($file.Name)) {
            throw "Artifact names must be unique: $($file.Name)"
        }
        $target = Get-ArtifactTarget $file.Name
        if ($ReleaseProfile -eq "unsigned-preview") {
            $target.updatePolicy = "unsigned-preview-manual-download"
        }
        if ($null -ne $target.releaseVersion -and $target.releaseVersion -cne $ReleaseVersion) {
            throw "Artifact version does not match ReleaseVersion: $($file.Name)"
        }
        if ($target.updatePolicy -eq "signed-appcast" -and $file.Length -gt $maximumDirectUpdateBytes) {
            throw "Direct-update artifact exceeds the 128 MiB client safety limit: $($file.Name)"
        }
        [ordered]@{
            fileName = $file.Name
            operatingSystem = $target.operatingSystem
            architecture = $target.architecture
            format = $target.format
            bytes = $file.Length
            sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
            ownership = $target.ownership
            updatePolicy = $target.updatePolicy
        }
    }
    else {
        throw "Artifact must be a non-empty regular file: $path"
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    releaseVersion = $ReleaseVersion
    sourceCommit = $SourceCommit.ToLowerInvariant()
    releaseProfile = $ReleaseProfile
    artifacts = @($artifacts | Sort-Object fileName)
}
$json = $manifest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($resolvedOutput, "$json`n", [Text.UTF8Encoding]::new($false))
Write-Output $resolvedOutput
