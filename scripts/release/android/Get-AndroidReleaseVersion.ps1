<#
.SYNOPSIS
Maps an OTP Harbor release tag to Android versionName and monotonic versionCode values.

.DESCRIPTION
Maps the independent android-v release tag to a deterministic user-visible version and monotonically
increasing Android versionCode. Rebuilding a tag cannot silently change its package identity.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseTag
)

$ErrorActionPreference = "Stop"

if ($ReleaseTag -notmatch '^android-v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$') {
    throw "ReleaseTag must match android-v<major>.<minor>.<patch>."
}

$major = [int64]$Matches.major
$minor = [int64]$Matches.minor
$patch = [int64]$Matches.patch
if ($major -gt 209 -or $minor -gt 99 -or $patch -gt 999) {
    throw "Android release versions support major <= 209, minor <= 99, and patch <= 999."
}

$versionCode = ($major * 10000000L) + ($minor * 100000L) + ($patch * 100L) + 99L
if ($versionCode -lt 1 -or $versionCode -gt 2100000000L) {
    throw "The mapped Android versionCode is outside Google Play's supported range."
}

[ordered]@{
    applicationId = "io.github.legends.otpharbor"
    displayVersion = $ReleaseTag.Substring('android-v'.Length)
    versionCode = $versionCode
} | ConvertTo-Json -Compress
