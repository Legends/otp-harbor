<#
.SYNOPSIS
Extracts the single APK signing-certificate SHA-256 fingerprint from apksigner PEM output.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [AllowEmptyCollection()]
    [string[]]$VerificationOutput
)

$verificationText = $VerificationOutput -join [Environment]::NewLine
$certificateMatches = @(
    [regex]::Matches(
        $verificationText,
        '(?ms)-----BEGIN CERTIFICATE-----\s*(?<certificate>[A-Za-z0-9+/=\s]+?)\s*-----END CERTIFICATE-----')
)

if ($certificateMatches.Count -ne 1) {
    throw 'Could not read exactly one APK signing certificate SHA-256 fingerprint.'
}

try {
    $certificateBytes = [Convert]::FromBase64String(
        ($certificateMatches[0].Groups['certificate'].Value -replace '\s', ''))
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificateBytes)
    try {
        $fingerprint = $certificate.GetCertHashString(
            [Security.Cryptography.HashAlgorithmName]::SHA256)
    }
    finally {
        $certificate.Dispose()
    }
}
catch {
    throw 'Could not read exactly one APK signing certificate SHA-256 fingerprint.'
}
finally {
    if ($null -ne $certificateBytes) {
        [Array]::Clear($certificateBytes, 0, $certificateBytes.Length)
    }
}

if ($fingerprint.Length -ne 64) {
    throw 'Could not read exactly one APK signing certificate SHA-256 fingerprint.'
}

$fingerprint.ToUpperInvariant()
