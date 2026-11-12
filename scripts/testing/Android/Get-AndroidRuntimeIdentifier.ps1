param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Abi
)

$normalizedAbi = $Abi.Trim().ToLowerInvariant()
switch ($normalizedAbi) {
    'arm64-v8a' { 'android-arm64'; break }
    'x86_64' { 'android-x64'; break }
    default { throw "The connected Android device uses an unsupported ABI: $Abi" }
}
