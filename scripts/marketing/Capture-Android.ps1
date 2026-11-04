[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Device,
    [Parameter(Mandatory)][ValidateSet('light','dark')][string]$Theme,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Screen,
    [switch]$ConfirmedSyntheticData
)
$ErrorActionPreference = 'Stop'
if (-not $ConfirmedSyntheticData) { throw 'Inspect visible accounts, notifications and status bar; pass -ConfirmedSyntheticData.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $root 'scripts/testing/DevelopmentAccountSnapshot.Common.ps1')
$connection = Resolve-AndroidDevice -Device $Device
Assert-AndroidDebugPackage -Connection $connection
$foreground = & $connection.Adb -s $connection.Device shell dumpsys activity activities | Select-String 'topResumedActivity' | Select-Object -First 1
if (-not $foreground -or $foreground.Line -notmatch [regex]::Escape($script:AndroidPackageName)) {
    throw 'The Android Debug app must be foreground before capture.'
}
$output = Join-Path $root "artifacts/marketing/source/android/$Theme/$Screen.png"
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
& $connection.Adb -s $connection.Device exec-out screencap -p > $output
if ($LASTEXITCODE -ne 0) { throw 'ADB capture failed.' }
& python -c 'from PIL import Image,ImageStat; import sys; im=Image.open(sys.argv[1]).convert("RGB"); assert max(ImageStat.Stat(im).stddev)>8, "Blank capture: use the explicit Debug marketing build"' $output
if ($LASTEXITCODE -ne 0) { Remove-Item -LiteralPath $output -Force; throw 'Android capture is blank or protected.' }
& python (Join-Path $PSScriptRoot 'Record-MarketingCaptures.py')
if ($LASTEXITCODE -ne 0) { throw 'Could not record Android capture.' }
Write-Host "Captured genuine Android screen: $output"
