[CmdletBinding()]
param([string]$Device)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'DevelopmentAccountSnapshot.Common.ps1')

$connection = Resolve-AndroidDevice -Device $Device
$fixtureRoot = Join-Path $PSScriptRoot 'fixtures'
$fixtures = @(
    'mixed-account-current-v3.json',
    'mixed-account-older.json'
)

foreach ($name in $fixtures) {
    $source = Join-Path $fixtureRoot $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Scenario fixture is missing: $source"
    }

    $destination = "/sdcard/Download/$name"
    & $connection.Adb -s $connection.Device push $source $destination | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not copy $name to the Android device." }

    $remoteOutput = (& $connection.Adb -s $connection.Device shell sha256sum $destination | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $remoteOutput -notmatch '^(?<Hash>[0-9a-fA-F]{64})\s') {
        throw "Could not verify $name on the Android device."
    }
    if ($Matches.Hash.ToLowerInvariant() -ne (Get-Sha256 -Path $source)) {
        throw "Android checksum verification failed for $name."
    }
}

Write-Host "Copied and verified the mixed-account scenario in Android Downloads on $($connection.Device)."
