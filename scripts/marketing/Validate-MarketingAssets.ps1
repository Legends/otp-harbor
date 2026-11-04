[CmdletBinding()]
param([switch]$RequireCaptures)
$ErrorActionPreference='Stop'
$arguments=@((Join-Path $PSScriptRoot 'Validate-MarketingAssets.py'))
if ($RequireCaptures) { $arguments+='--require-captures' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Marketing validation failed.' }
