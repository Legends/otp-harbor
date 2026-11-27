[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $root 'scripts/shared/Build.Common.ps1')
& python (Join-Path $PSScriptRoot 'Prepare-DemoData.py')
if ($LASTEXITCODE -ne 0) { throw 'Demo generation failed.' }
Invoke-OtpHarborCachedBuild -RepositoryRoot $root -CacheName 'Unit tests Debug' `
    -CommandArguments @('build', (Join-Path $root 'OtpHarbor.Tests/OtpHarbor.Tests.csproj'), '-c', 'Debug') `
    -OutputPaths @((Join-Path $root 'OtpHarbor.Tests/bin/Debug'))
& dotnet test (Join-Path $root 'OtpHarbor.Tests/OtpHarbor.Tests.csproj') -c Debug --no-build --filter 'FullyQualifiedName~FictionalMarketingPack_ImportsAllOriginalThemedIcons' --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'The generated v3 icon pack did not pass the app importer.' }
