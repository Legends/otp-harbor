<#
.SYNOPSIS
Applies a SignPath result to prepared Windows release directories.

.DESCRIPTION
Copies signed files into the named targets and then fails closed unless every first-party PE file has a valid Authenticode signature and the expected OTP Harbor product/version metadata.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SignedRoot,

    [Parameter(Mandatory = $true)]
    [string[]]$TargetDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-rc\d+)?$')]
    [string]$ExpectedProductVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedSignedRoot = (Resolve-Path -LiteralPath $SignedRoot).Path
$resolvedTargets = foreach ($directoryName in $TargetDirectory) {
    $signedDirectory = Join-Path $resolvedSignedRoot $directoryName
    if (-not (Test-Path -LiteralPath $signedDirectory -PathType Container)) {
        throw "The SignPath result is missing $directoryName."
    }

    $target = (Resolve-Path -LiteralPath $directoryName).Path
    foreach ($item in Get-ChildItem -LiteralPath $signedDirectory) {
        Copy-Item -LiteralPath $item.FullName -Destination $target -Recurse -Force
    }
    $target
}

$firstPartyFiles = @(
    Get-ChildItem -LiteralPath $resolvedTargets -Recurse -File |
        Where-Object { $_.Name -match '^TOTP.*\.(exe|dll)$' }
)
if ($firstPartyFiles.Count -eq 0) {
    throw "No first-party Windows PE files were found after signing."
}

foreach ($file in $firstPartyFiles) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Invalid Authenticode signature on $($file.Name): $($signature.Status)."
    }

    $versionInfo = $file.VersionInfo
    if ($versionInfo.ProductName -cne "OTP Harbor" -or
        $versionInfo.ProductVersion -cne $ExpectedProductVersion) {
        throw "Unexpected product metadata on $($file.Name)."
    }
}

Write-Output "Validated $($firstPartyFiles.Count) signed first-party Windows files."
