[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& python (Join-Path $PSScriptRoot 'Prepare-DemoData.py')
if ($LASTEXITCODE -ne 0) { throw 'Demo generation failed.' }
& dotnet test (Join-Path $root 'OtpHarbor.Tests/OtpHarbor.Tests.csproj') -c Debug --filter 'FullyQualifiedName~FictionalMarketingPack_ImportsAllOriginalThemedIcons' --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'The generated v3 icon pack did not pass the app importer.' }
