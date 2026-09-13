<#
.SYNOPSIS
Extracts the single APK signing-certificate SHA-256 fingerprint from apksigner output.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [AllowEmptyCollection()]
    [string[]]$VerificationOutput
)

$verificationText = $VerificationOutput -join [Environment]::NewLine
$fingerprints = @(
    [regex]::Matches(
        $verificationText,
        '(?im)Signer\s+#\d+\s+certificate\s+SHA-?256\s+digest\s*:\s*(?<digest>(?:[0-9a-f][:\s-]?){64})') |
        ForEach-Object {
            ($_.Groups['digest'].Value -replace '[^0-9a-fA-F]', '').ToUpperInvariant()
        }
)

if ($fingerprints.Count -ne 1 -or $fingerprints[0].Length -ne 64) {
    throw 'Could not read exactly one APK signing certificate SHA-256 fingerprint.'
}

$fingerprints[0]
