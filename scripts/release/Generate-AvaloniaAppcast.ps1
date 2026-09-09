<#
.SYNOPSIS
Generates and signs the Avalonia update appcast for release artifacts.

.DESCRIPTION
Validates HTTPS download metadata, release-manifest entries, Ed25519 key layout and public-key identity, invokes NetSparkle appcast generation, and writes the authenticated appcast payload to the requested output directory.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [Parameter(Mandatory = $true)]
    [string]$BaseDownloadUrl,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [Parameter(Mandatory = $true)]
    [string]$PublicKeyPath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedPublicKey,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
if (-not (Get-Command netsparkle-generate-appcast -ErrorAction SilentlyContinue)) {
    throw "netsparkle-generate-appcast is unavailable."
}
$parsedBaseUri = $null
$invalidBaseUrl = -not [Uri]::TryCreate(
    $BaseDownloadUrl,
    [UriKind]::Absolute,
    [ref]$parsedBaseUri) -or
    $parsedBaseUri.Scheme -cne "https"
if ($invalidBaseUrl) {
    throw "BaseDownloadUrl must be an absolute HTTPS URL."
}

$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$resolvedArtifacts = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$resolvedPrivateKey = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
$resolvedPublicKey = (Resolve-Path -LiteralPath $PublicKeyPath).Path
$keyDirectory = [IO.Path]::GetDirectoryName($resolvedPrivateKey)
$invalidKeyLayout = [IO.Path]::GetFileName($resolvedPrivateKey) -cne "NetSparkle_Ed25519.priv" -or
    [IO.Path]::GetFileName($resolvedPublicKey) -cne "NetSparkle_Ed25519.pub" -or
    [IO.Path]::GetDirectoryName($resolvedPublicKey) -cne $keyDirectory
if ($invalidKeyLayout) {
    throw "NetSparkle key files must use canonical names in the same directory."
}
$publicKey = (Get-Content -LiteralPath $resolvedPublicKey -Raw).Trim()
if ($publicKey -cne $ExpectedPublicKey.Trim()) {
    throw "The release public key does not match the key embedded in the client."
}
if ((Get-Item -LiteralPath $resolvedPrivateKey).Length -le 0) {
    throw "The release private key is empty."
}

& (Join-Path $PSScriptRoot "Test-ReleaseArtifactManifest.ps1") `
    -ManifestPath $resolvedManifest `
    -ArtifactDirectory $resolvedArtifacts | Out-Null
$manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
if ($manifest.releaseVersion -notmatch '^(?<base>\d+\.\d+\.\d+)(?:-rc(?<rc>\d+))?$') {
    throw "Manifest release version is invalid."
}
$baseVersion = $Matches.base
$rcNumber = $Matches.rc
if ($rcNumber -and [int]$rcNumber -gt 65534) {
    throw "Release candidate number must be between 1 and 65534."
}
$fileVersion = if ($rcNumber) { "$baseVersion.$rcNumber" } else { "$baseVersion.65535" }
$channel = if ($rcNumber) { "rc" } else { "stable" }
$directArtifacts = @($manifest.artifacts | Where-Object { $_.updatePolicy -eq "signed-appcast" -or $_.updatePolicy -eq "manual-signed-release" })
if ($directArtifacts.Count -eq 0) {
    throw "The manifest contains no direct-update artifacts."
}

$sparkleNamespace = "http://www.andymatuschak.org/xml-namespaces/sparkle"
[xml]$xml = '<?xml version="1.0" encoding="utf-8"?><rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle"><channel><title>OTP Harbor updates</title><link>https://github.com/Legends/otp-harbor/releases</link><description>Signed OTP Harbor desktop updates</description></channel></rss>'
$channelNode = $xml.SelectSingleNode("/rss/channel")

foreach ($artifact in $directArtifacts | Sort-Object operatingSystem, architecture, fileName) {
    $artifactPath = Join-Path $resolvedArtifacts $artifact.fileName
    $signatureOutput = & netsparkle-generate-appcast `
        --generate-signature $artifactPath `
        --key-path $keyDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Could not sign release artifact $($artifact.fileName)."
    }
    $signatureMatches = @($signatureOutput | Select-String -Pattern '^Signature:\s*(?<value>.+)$')
    $signature = if ($signatureMatches.Count -eq 1) {
        $signatureMatches[0].Matches[0].Groups["value"].Value.Trim()
    }
    else {
        ""
    }
    try {
        if ([Convert]::FromBase64String($signature).Length -ne 64) {
            throw "Invalid signature length."
        }
    }
    catch {
        throw "The artifact signer returned an invalid Ed25519 signature."
    }

    $item = $xml.CreateElement("item")
    $title = $xml.CreateElement("title")
    $title.InnerText = "OTP Harbor $($manifest.releaseVersion) for $($artifact.operatingSystem)-$($artifact.architecture)"
    [void]$item.AppendChild($title)
    $description = $xml.CreateElement("description")
    $description.InnerText = "See the GitHub release notes for $($manifest.releaseVersion)."
    [void]$item.AppendChild($description)
    foreach ($field in ([ordered]@{
        version = $fileVersion
        shortVersionString = $manifest.releaseVersion
        os = $artifact.operatingSystem
        architecture = $artifact.architecture
        channel = $channel
    }).GetEnumerator()) {
        $node = $xml.CreateElement("sparkle", $field.Key, $sparkleNamespace)
        $node.InnerText = $field.Value
        [void]$item.AppendChild($node)
    }

    $enclosure = $xml.CreateElement("enclosure")
    $downloadBase = $BaseDownloadUrl.TrimEnd('/') + "/"
    [void]$enclosure.SetAttribute("url", $downloadBase + [Uri]::EscapeDataString($artifact.fileName))
    [void]$enclosure.SetAttribute("length", [string]$artifact.bytes)
    [void]$enclosure.SetAttribute("type", "application/octet-stream")
    [void]$enclosure.SetAttribute("version", $sparkleNamespace, $fileVersion)
    [void]$enclosure.SetAttribute("shortVersionString", $sparkleNamespace, $manifest.releaseVersion)
    [void]$enclosure.SetAttribute("edSignature", $sparkleNamespace, $signature)
    [void]$enclosure.SetAttribute("os", $sparkleNamespace, $artifact.operatingSystem)
    [void]$enclosure.SetAttribute("architecture", $sparkleNamespace, $artifact.architecture)
    [void]$enclosure.SetAttribute("channel", $sparkleNamespace, $channel)
    [void]$item.AppendChild($enclosure)
    [void]$channelNode.AppendChild($item)
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$appcastPath = Join-Path $resolvedOutput "appcast-v2.xml"
$settings = [Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$writer = [Xml.XmlWriter]::Create($appcastPath, $settings)
try { $xml.Save($writer) } finally { $writer.Dispose() }

$appcastSignatureOutput = & netsparkle-generate-appcast `
    --generate-signature $appcastPath `
    --key-path $keyDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Could not sign the Avalonia appcast."
}
$appcastSignatureMatches = @($appcastSignatureOutput | Select-String -Pattern '^Signature:\s*(?<value>.+)$')
$appcastSignature = if ($appcastSignatureMatches.Count -eq 1) {
    $appcastSignatureMatches[0].Matches[0].Groups["value"].Value.Trim()
}
else {
    ""
}
try {
    if ([Convert]::FromBase64String($appcastSignature).Length -ne 64) {
        throw "Invalid signature length."
    }
}
catch {
    throw "The appcast signer returned an invalid Ed25519 signature."
}
$signaturePath = "$appcastPath.signature"
[IO.File]::WriteAllText($signaturePath, $appcastSignature, [Text.UTF8Encoding]::new($false))

$manifestSignatureOutput = & netsparkle-generate-appcast `
    --generate-signature $resolvedManifest `
    --key-path $keyDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Could not sign the release artifact manifest."
}
$manifestSignatureMatches = @($manifestSignatureOutput | Select-String -Pattern '^Signature:\s*(?<value>.+)$')
$manifestSignature = if ($manifestSignatureMatches.Count -eq 1) {
    $manifestSignatureMatches[0].Matches[0].Groups["value"].Value.Trim()
}
else {
    ""
}
try {
    if ([Convert]::FromBase64String($manifestSignature).Length -ne 64) {
        throw "Invalid signature length."
    }
}
catch {
    throw "The manifest signer returned an invalid Ed25519 signature."
}
$manifestSignaturePath = "$resolvedManifest.signature"
[IO.File]::WriteAllText(
    $manifestSignaturePath,
    $manifestSignature,
    [Text.UTF8Encoding]::new($false))

Write-Output $appcastPath
Write-Output $signaturePath
Write-Output $manifestSignaturePath
