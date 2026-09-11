<#
.SYNOPSIS
Maps an OTP Harbor release tag to Android versionName and monotonic versionCode values.

.DESCRIPTION
Keeps the user-visible Android version aligned with the shared GitHub release while reserving a
strictly increasing integer range for release candidates and the stable release of each semantic
version. The mapping is deterministic so rebuilding a tag cannot silently change its identity.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseTag
)

$ErrorActionPreference = "Stop"

if ($ReleaseTag -notmatch '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-rc(?<rc>\d+))?$') {
    throw "ReleaseTag must match v<major>.<minor>.<patch>[-rc<nr>]."
}

$major = [int64]$Matches.major
$minor = [int64]$Matches.minor
$patch = [int64]$Matches.patch
$rcText = $Matches.rc

if ($major -gt 209 -or $minor -gt 99 -or $patch -gt 999) {
    throw "Android release versions support major <= 209, minor <= 99, and patch <= 999."
}

$qualifier = 99L
if (-not [string]::IsNullOrWhiteSpace($rcText)) {
    $qualifier = [int64]$rcText
    if ($qualifier -lt 1 -or $qualifier -gt 98) {
        throw "Android release-candidate numbers must be between 1 and 98."
    }
}

$versionCode = ($major * 10000000L) + ($minor * 100000L) + ($patch * 100L) + $qualifier
if ($versionCode -lt 1 -or $versionCode -gt 2100000000L) {
    throw "The mapped Android versionCode is outside Google Play's supported range."
}

[ordered]@{
    applicationId = "io.github.legends.otpharbor"
    displayVersion = $ReleaseTag.Substring(1)
    versionCode = $versionCode
} | ConvertTo-Json -Compress
