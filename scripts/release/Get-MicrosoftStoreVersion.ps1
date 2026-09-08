[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ReleaseTag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ReleaseTag -notmatch '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-rc(?<rc>\d+))?$') {
    throw 'Microsoft Store packages require a v<major>.<minor>.<patch> or v<major>.<minor>.<patch>-rc<number> tag.'
}

$major = [uint64]$Matches.major
$minor = [uint64]$Matches.minor
$patch = [uint64]$Matches.patch
$rcText = if ($Matches.ContainsKey('rc')) { $Matches.rc } else { '' }

if ($major -eq 0 -or $major -gt 65535) {
    throw 'The release major version must be between 1 and 65535.'
}
if ($minor -gt 65 -or $patch -gt 999) {
    throw 'The Store version encoding supports minor versions through 65 and patch versions through 999.'
}

# The Store only exposes three maintainer-controlled numeric components. Fold
# SemVer minor/patch into the second component, then reserve 65535 in the third
# component for the stable build so every RC sorts below its matching release.
$storeMinorPatch = ($minor * 1000) + $patch
if ($storeMinorPatch -gt 65535) {
    throw 'The encoded Store minor/patch component exceeds 65535.'
}

$storePhase = if ([string]::IsNullOrEmpty($rcText)) {
    [uint64]65535
}
else {
    $rc = [uint64]$rcText
    if ($rc -eq 0 -or $rc -gt 65534) {
        throw 'The release-candidate number must be between 1 and 65534.'
    }
    $rc
}

Write-Output "$major.$storeMinorPatch.$storePhase.0"
