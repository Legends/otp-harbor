<#
.SYNOPSIS
Prepares a portable, synthetic Windows Store v2-to-v3 acceptance kit.
.DESCRIPTION
Builds only the read-only verifier and creates fictional import data below artifacts/windows/testing.
It never inspects or changes an installed application's data or installs a package.
#>
[CmdletBinding()]
param([string]$OutputDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. ([IO.Path]::Combine($PSScriptRoot, 'StoreUpgrade.Preflight.ps1'))
Invoke-UpgradePreflight -RequireBuildTools
if (-not $OutputDirectory) { $OutputDirectory = 'artifacts/windows/testing/store-v2-v3-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') }
. (Join-Path $PSScriptRoot '../shared/Testing.Common.ps1')
. (Join-Path $PSScriptRoot 'StoreUpgrade.Testing.ps1')
$repositoryRoot = Get-OtpHarborRepositoryRoot -StartDirectory $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/windows/testing'))
if (-not $output.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The kit must be created below artifacts/windows/testing.'
}
Assert-UpgradeRegularPath $output
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory; existing test kits are never overwritten.' }
New-Item -ItemType Directory -Path $output | Out-Null
Protect-UpgradeKitDirectory $output
$project = Join-Path $PSScriptRoot 'StoreUpgradeVerifier/StoreUpgradeVerifier.csproj'
& dotnet publish $project -c Release -r win-x64 --self-contained true --nologo --verbosity minimal `
    --output (Join-Path $output 'verifier')
if ($LASTEXITCODE -ne 0) { throw 'Could not publish the read-only verifier. Choose a new output directory before retrying.' }

foreach ($name in @('Invoke-StoreUpgradeTest.ps1', 'Invoke-SyntheticV2UpgradeTest.ps1', 'StoreUpgrade.Testing.ps1', 'StoreUpgrade.Preflight.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $output $name)
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/testing/STORE_V2_V3_UPGRADE.md') -Destination (Join-Path $output 'README.md')
& (Join-Path $output 'verifier/StoreUpgradeVerifier.exe') fixture (Join-Path $output 'synthetic-v2-accounts.txt')
if ($LASTEXITCODE -ne 0) { throw 'Could not create the synthetic fixture.' }
foreach ($entry in @{ Initialize = 'Initialize'; CaptureV2 = 'CaptureV2'; VerifyV3 = 'VerifyV3'; SeedSyntheticV2 = 'Seed'; VerifySyntheticV3 = 'Verify' }.GetEnumerator()) {
    $script = if ($entry.Key -in @('SeedSyntheticV2', 'VerifySyntheticV3')) { 'Invoke-SyntheticV2UpgradeTest.ps1' } else { 'Invoke-StoreUpgradeTest.ps1' }
    $launcher = @"
@echo off
setlocal
set "UPGRADE_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%UPGRADE_PS%" (echo Windows PowerShell 5.1 is missing. Repair Windows PowerShell. & pause & exit /b 1)
"%UPGRADE_PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0$script" -Phase $($entry.Value)
set "UPGRADE_RESULT=%ERRORLEVEL%"
if errorlevel 1 (echo TEST FAILED) else (echo STEP COMPLETE)
pause
exit /b %UPGRADE_RESULT%
"@
    [IO.File]::WriteAllText((Join-Path $output "$($entry.Key).cmd"), $launcher, [Text.ASCIIEncoding]::new())
}
$hashLines = Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
    '{0}  {1}' -f (Get-UpgradeHash $_.FullName).ToLowerInvariant(),
        [IO.Path]::GetRelativePath($output, $_.FullName)
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS'), [string[]]$hashLines, [Text.UTF8Encoding]::new($false))
Write-Output "Prepared kit: $output"
Write-Output 'Start with README.md. SeedSyntheticV2.cmd prepares encrypted v2 test data without a v2 installer.'
