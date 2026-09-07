param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet("direct", "package-manager", "store")]
    [string]$DistributionMode,

    [Parameter(Mandatory = $true)]
    [ValidateSet("stable", "rc")]
    [string]$Channel,

    [string]$AppcastUrl,

    [switch]$DisableUpdates
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $PackageDirectory).Path
$settingsPath = Join-Path $root "appsettings.json"
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "The package is missing appsettings.json."
}

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$invalidConfiguration = $null -eq $settings.AutoUpdate -or
    [string]::IsNullOrWhiteSpace($settings.AutoUpdate.AppcastUrl) -or
    [string]::IsNullOrWhiteSpace($settings.AutoUpdate.PublicKey)
if ($invalidConfiguration) {
    throw "The package update configuration is incomplete."
}

if (-not [string]::IsNullOrWhiteSpace($AppcastUrl)) {
    $parsedAppcastUri = $null
    if (-not [Uri]::TryCreate($AppcastUrl, [UriKind]::Absolute, [ref]$parsedAppcastUri) -or
        $parsedAppcastUri.Scheme -cne "https") {
        throw "AppcastUrl must be an absolute HTTPS URL."
    }
    $settings.AutoUpdate.AppcastUrl = $parsedAppcastUri.AbsoluteUri
}

$settings.AutoUpdate.DistributionMode = $DistributionMode
$settings.AutoUpdate.Channel = $Channel
$settings.AutoUpdate.Enabled = $DistributionMode -eq "direct" -and -not [bool]$DisableUpdates
$json = $settings | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($settingsPath, "$json`n", [Text.UTF8Encoding]::new($false))
