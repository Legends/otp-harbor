<#
.SYNOPSIS
Creates the separate F-Droid repository-index signing identity outside the checkout.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $env:LOCALAPPDATA 'OTP-Harbor/fdroid-repository-signing'),
    [Parameter(Mandatory)][string]$KeytoolPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Initialize the repository identity on Windows for current-user DPAPI protection.' }

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if ($destination.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $destination.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Repository signing material must be stored outside the checkout.'
}
if (Test-Path -LiteralPath $destination) {
    throw 'Signing directory already exists. Reuse and back up the existing identity; do not rotate it accidentally.'
}
if (-not (Test-Path -LiteralPath $KeytoolPath -PathType Leaf)) { throw 'keytool was not found.' }

$directory = New-Item -ItemType Directory -Path $destination
if ($IsWindows) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($principal in @($identity, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $principal, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    Set-Acl -LiteralPath $directory.FullName -AclObject $acl
}
else {
    & chmod 700 $destination
    if ($LASTEXITCODE -ne 0) { throw 'Could not restrict the signing directory.' }
}

$passwordFile = Join-Path $destination 'keystore-password.dpapi'
$keyStore = Join-Path $destination 'repository-index.p12'
$certificateFile = Join-Path $destination 'repository-index.cer'
$randomBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(48)
$passwordBytes = $null
try {
    $passwordBytes = [Text.Encoding]::UTF8.GetBytes([Convert]::ToBase64String($randomBytes))
    [IO.File]::WriteAllBytes($passwordFile, [Security.Cryptography.ProtectedData]::Protect(
        $passwordBytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
    $env:OTP_HARBOR_FDROID_KEY_PASSWORD = [Text.Encoding]::UTF8.GetString($passwordBytes)
    & $KeytoolPath -genkeypair -keystore $keyStore -storetype PKCS12 -alias otp-harbor-repository `
        -keyalg RSA -keysize 3072 -sigalg SHA256withRSA -validity 10000 `
        -dname 'CN=OTP Harbor F-Droid Repository,O=OTP Harbor' `
        '-storepass:env' OTP_HARBOR_FDROID_KEY_PASSWORD '-keypass:env' OTP_HARBOR_FDROID_KEY_PASSWORD -noprompt
    if ($LASTEXITCODE -ne 0) { throw 'Repository signing-key generation failed.' }
    & $KeytoolPath -exportcert -keystore $keyStore -alias otp-harbor-repository `
        '-storepass:env' OTP_HARBOR_FDROID_KEY_PASSWORD -file $certificateFile
    if ($LASTEXITCODE -ne 0) { throw 'Repository signing certificate export failed.' }
}
finally {
    Remove-Item Env:OTP_HARBOR_FDROID_KEY_PASSWORD -ErrorAction SilentlyContinue
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($randomBytes)
    if ($null -ne $passwordBytes) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($passwordBytes) }
}
$certificate = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadCertificateFromFile($certificateFile)
try { $fingerprint = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256) }
finally { $certificate.Dispose() }
$publicInfo = [ordered]@{ alias = 'otp-harbor-repository'; certificateSha256 = $fingerprint }
[IO.File]::WriteAllText((Join-Path $destination 'signing-public.json'),
    ($publicInfo | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "Repository certificate SHA-256: $fingerprint"
Write-Output "Private signing files are stored in: $destination"
