[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
& python (Join-Path $PSScriptRoot 'Create-RequestedComposites.py')
if ($LASTEXITCODE -ne 0) { throw 'Marketing composite generation failed.' }
