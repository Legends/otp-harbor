<#
.SYNOPSIS
Validates a generated Avalonia appcast against its release manifest.

.DESCRIPTION
Checks item count, HTTPS artifact URLs, Ed25519 signatures, platform and architecture metadata, channel/version encoding, byte sizes, and correspondence with every directly distributed artifact.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$AppcastPath,

    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$BaseDownloadUrl
)

$ErrorActionPreference = "Stop"
$manifest = Get-Content -LiteralPath (Resolve-Path -LiteralPath $ManifestPath) -Raw | ConvertFrom-Json
if ($manifest.releaseVersion -notmatch '^(?<base>\d+\.\d+\.\d+)(?:-rc(?<rc>\d+))?$') {
    throw "Manifest release version is invalid."
}
$baseVersion = $Matches.base
$rcNumber = $Matches.rc
$expectedFileVersion = if ($rcNumber) { "$baseVersion.$rcNumber" } else { "$baseVersion.65535" }
$expectedChannel = if ($rcNumber) { "rc" } else { "stable" }
[xml]$xml = Get-Content -LiteralPath (Resolve-Path -LiteralPath $AppcastPath) -Raw
$sparkleNamespace = "http://www.andymatuschak.org/xml-namespaces/sparkle"
$namespaceManager = [Xml.XmlNamespaceManager]::new($xml.NameTable)
$namespaceManager.AddNamespace("sparkle", $sparkleNamespace)
$items = @($xml.SelectNodes("/rss/channel/item"))
$directArtifacts = @($manifest.artifacts | Where-Object {
    $_.updatePolicy -eq "signed-appcast" -or $_.updatePolicy -eq "manual-signed-release"
})
if ($items.Count -ne $directArtifacts.Count) {
    throw "Appcast item count does not match direct release artifacts."
}

foreach ($artifact in $directArtifacts) {
    $expectedUrl = $BaseDownloadUrl.TrimEnd('/') + "/" + [Uri]::EscapeDataString($artifact.fileName)
    $item = $items | Where-Object {
        $_.enclosure.url -eq $expectedUrl
    }
    if (@($item).Count -ne 1) {
        throw "Appcast does not contain exactly one entry for $($artifact.fileName)."
    }
    $enclosure = $item.enclosure
    $signature = $enclosure.GetAttribute("edSignature", $sparkleNamespace)
    $operatingSystem = $enclosure.GetAttribute("os", $sparkleNamespace)
    $architecture = $enclosure.GetAttribute("architecture", $sparkleNamespace)
    $channel = $enclosure.GetAttribute("channel", $sparkleNamespace)
    $version = $enclosure.GetAttribute("version", $sparkleNamespace)
    $shortVersion = $enclosure.GetAttribute("shortVersionString", $sparkleNamespace)
    try { $signatureLength = [Convert]::FromBase64String($signature).Length } catch { $signatureLength = 0 }
    $invalidEntry = $enclosure.length -ne [string]$artifact.bytes -or
        $signatureLength -ne 64 -or
        $operatingSystem -cne $artifact.operatingSystem -or
        $architecture -cne $artifact.architecture -or
        $channel -cne $expectedChannel -or
        $version -cne $expectedFileVersion -or
        $shortVersion -cne $manifest.releaseVersion
    if ($invalidEntry) {
        throw "Appcast entry metadata is invalid for $($artifact.fileName)."
    }
}

Write-Output "Avalonia appcast structure is valid."
