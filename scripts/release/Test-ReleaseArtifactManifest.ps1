param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory
)

$ErrorActionPreference = "Stop"
$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$resolvedArtifacts = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json

$invalidMetadata = $manifest.schemaVersion -ne 1 -or
    $manifest.releaseVersion -notmatch '^\d+\.\d+\.\d+(?:-rc\d+)?$' -or
    $manifest.sourceCommit -notmatch '^[0-9a-f]{40}$' -or
    $manifest.releaseProfile -notin @("signed", "unsigned-platform-preview", "unsigned-preview") -or
    @($manifest.artifacts).Count -eq 0
if ($invalidMetadata) {
    throw "Release artifact manifest metadata is invalid."
}

$seenNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($artifact in $manifest.artifacts) {
    $invalidEntry = [string]::IsNullOrWhiteSpace($artifact.fileName) -or
        -not $seenNames.Add([string]$artifact.fileName) -or
        $artifact.operatingSystem -notin @("windows", "macos", "linux") -or
        $artifact.architecture -notin @("x64", "arm64") -or
        $artifact.bytes -le 0 -or
        $artifact.sha256 -notmatch '^[0-9a-f]{64}$' -or
        [string]::IsNullOrWhiteSpace($artifact.ownership) -or
        [string]::IsNullOrWhiteSpace($artifact.updatePolicy)
    if ($manifest.releaseProfile -eq "unsigned-preview") {
        $invalidEntry = $invalidEntry -or
            $artifact.updatePolicy -ne "unsigned-preview-manual-download"
    }
    elseif ($artifact.updatePolicy -eq "unsigned-preview-manual-download") {
        $invalidEntry = $true
    }
    if ($invalidEntry) {
        throw "Release artifact manifest contains an invalid entry."
    }

    $candidate = [IO.Path]::GetFullPath((Join-Path $resolvedArtifacts $artifact.fileName))
    $relative = [IO.Path]::GetRelativePath($resolvedArtifacts, $candidate)
    $invalidPath = $relative.StartsWith("..", [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relative) -or
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)
    if ($invalidPath) {
        throw "Manifest artifact is outside the artifact directory or missing."
    }

    $file = Get-Item -LiteralPath $candidate
    $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($file.Length -ne $artifact.bytes -or $hash -cne $artifact.sha256) {
        throw "Artifact size or SHA-256 does not match the manifest: $($artifact.fileName)"
    }
}

Write-Output "Release artifact manifest is valid."
