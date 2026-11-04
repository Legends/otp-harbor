[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][ValidateSet('desktop','android')][string]$Platform,
    [Parameter(Mandatory)][ValidateSet('light','dark')][string]$Theme,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]+(-[a-z0-9]+)*$')][string]$Screen,
    [switch]$Replace
)
$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'Import-MarketingCapture.py'), $Source,
    '--platform', $Platform, '--theme', $Theme, '--screen', $Screen)
if ($Replace) { $arguments += '--replace' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Marketing capture import failed.' }
