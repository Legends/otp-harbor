[CmdletBinding()]
param([switch]$RequireCaptures, [string]$PythonExecutable = 'python')
$ErrorActionPreference='Stop'
$arguments=@((Join-Path $PSScriptRoot 'Validate-MarketingAssets.py'))
if ($RequireCaptures) { $arguments+='--require-captures' }
& $PythonExecutable @arguments
if ($LASTEXITCODE -ne 0) { throw 'Marketing validation failed.' }
