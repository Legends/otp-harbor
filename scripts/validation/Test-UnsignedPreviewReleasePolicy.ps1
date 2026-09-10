<#
.SYNOPSIS
Regression-tests update policy and manifests for unsigned preview releases.

.DESCRIPTION
Builds synthetic package and release fixtures in a temporary directory, exercises direct RC/stable and disabled-update policy combinations plus feed selection, and rejects any unsigned-preview metadata that could imply signed automatic updates.
#>
$ErrorActionPreference = "Stop"

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("totp-unsigned-preview-" + [Guid]::NewGuid().ToString("N"))
$packageRoot = Join-Path $testRoot "package"
$artifactRoot = Join-Path $testRoot "artifacts"

try {
    New-Item -ItemType Directory -Path $packageRoot, $artifactRoot | Out-Null
    $settings = @{
        AutoUpdate = @{
            Enabled = $true
            AppcastUrl = "https://example.invalid/appcast-v2.xml"
            PublicKey = "synthetic-test-key"
            Channel = "stable"
            DistributionMode = "direct"
        }
    } | ConvertTo-Json -Depth 3
    [IO.File]::WriteAllText(
        (Join-Path $packageRoot "appsettings.json"),
        "$settings`n",
        [Text.UTF8Encoding]::new($false))

    & (Join-Path $PSScriptRoot "../release/Set-PackageUpdatePolicy.ps1") `
        -PackageDirectory $packageRoot `
        -DistributionMode direct `
        -Channel rc `
        -AppcastUrl "https://legends.github.io/otp-harbor/updates/rc/appcast-v2.xml"

    $configured = Get-Content (Join-Path $packageRoot "appsettings.json") -Raw | ConvertFrom-Json
    if ($configured.AutoUpdate.Enabled -ne $true -or
        $configured.AutoUpdate.Channel -ne "rc" -or
        $configured.AutoUpdate.AppcastUrl -cne "https://legends.github.io/otp-harbor/updates/rc/appcast-v2.xml" -or
        $configured.AutoUpdate.DistributionMode -ne "direct") {
        throw "Direct preview package policy did not enable the signed RC feed."
    }

    & (Join-Path $PSScriptRoot "../release/Set-PackageUpdatePolicy.ps1") `
        -PackageDirectory $packageRoot `
        -DistributionMode direct `
        -Channel stable

    $configured = Get-Content (Join-Path $packageRoot "appsettings.json") -Raw | ConvertFrom-Json
    if ($configured.AutoUpdate.Enabled -ne $true -or
        $configured.AutoUpdate.Channel -ne "stable" -or
        $configured.AutoUpdate.DistributionMode -ne "direct") {
        throw "Stable direct package policy did not enable automatic updates."
    }

    & (Join-Path $PSScriptRoot "../release/Set-PackageUpdatePolicy.ps1") `
        -PackageDirectory $packageRoot `
        -DistributionMode store `
        -Channel stable
    $configured = Get-Content (Join-Path $packageRoot "appsettings.json") -Raw | ConvertFrom-Json
    if ($configured.AutoUpdate.Enabled -ne $false -or
        $configured.AutoUpdate.DistributionMode -ne "store") {
        throw "Externally managed Store policy did not disable application-owned updates."
    }

    $artifactNames = @(
        "OTP-Harbor-windows-x64-2.0.0-rc3.zip",
        "OTP-Harbor-windows-x64-fast-2.0.0-rc3.zip",
        "OTP-Harbor-windows-x64-2.0.0-rc3.msi",
        "OTP-Harbor-windows-x64-setup-2.0.0-rc3.exe",
        "OTP-Harbor-linux-x64-2.0.0-rc3.tar.gz",
        "otp-harbor_2.0.0-rc3_amd64.deb")
    $artifactPaths = foreach ($name in $artifactNames) {
        $path = Join-Path $artifactRoot $name
        [IO.File]::WriteAllBytes($path, [byte[]](1, 2, 3, 4))
        $path
    }

    $manifestPath = Join-Path $artifactRoot "release-artifacts-unsigned-preview.json"
    & (Join-Path $PSScriptRoot "../release/New-ReleaseArtifactManifest.ps1") `
        -ReleaseVersion "2.0.0-rc3" `
        -SourceCommit ("a" * 40) `
        -ArtifactPath $artifactPaths `
        -OutputPath $manifestPath `
        -ReleaseProfile unsigned-platform-preview | Out-Null
    & (Join-Path $PSScriptRoot "../release/Test-ReleaseArtifactManifest.ps1") `
        -ManifestPath $manifestPath `
        -ArtifactDirectory $artifactRoot | Out-Null

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.releaseProfile -ne "unsigned-platform-preview" -or
        @($manifest.artifacts).Count -ne 6 -or
        @($manifest.artifacts | Where-Object { $_.fileName -match '~' }).Count -ne 0 -or
        @($manifest.artifacts | Where-Object { $_.updatePolicy -eq "signed-appcast" }).Count -ne 1 -or
        @($manifest.artifacts | Where-Object { $_.updatePolicy -eq "manual-signed-release" }).Count -ne 1 -or
        @($manifest.artifacts | Where-Object { $_.updatePolicy -eq "package-manager" }).Count -ne 1 -or
        @($manifest.artifacts | Where-Object { $_.updatePolicy -eq "manual-download" }).Count -ne 3 -or
        @($manifest.artifacts | Where-Object { $_.format -eq "exe" -and $_.ownership -eq "windows-installer" }).Count -ne 1 -or
        @($manifest.artifacts | Where-Object { $_.format -eq "msi" -and $_.ownership -eq "windows-installer" }).Count -ne 1) {
        throw "Unsigned-platform preview manifest contains an unsafe update policy."
    }

    $publishedReleasesPath = Join-Path $testRoot "published-releases.json"
    $publishedReleases = @(
        @{
            tag_name = "v2.0.0"
            draft = $false
            assets = @(@{ name = "appcast-v2.xml" }, @{ name = "appcast-v2.xml.signature" })
        },
        @{
            tag_name = "v2.1.0-rc2"
            draft = $false
            assets = @(@{ name = "appcast-v2.xml" }, @{ name = "appcast-v2.xml.signature" })
        },
        @{
            tag_name = "v9.0.0"
            draft = $true
            assets = @(@{ name = "appcast-v2.xml" }, @{ name = "appcast-v2.xml.signature" })
        },
        @{
            tag_name = "v3.0.0-rc1"
            draft = $false
            assets = @(@{ name = "appcast-v2.xml" })
        }
    ) | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText(
        $publishedReleasesPath,
        $publishedReleases,
        [Text.UTF8Encoding]::new($false))
    $selectedFeed = & (Join-Path $PSScriptRoot "../release/Select-PublishedUpdateFeed.ps1") `
        -ReleasesJsonPath $publishedReleasesPath
    if ($selectedFeed -cne "v2.1.0-rc2") {
        throw "The public RC endpoint did not select the highest signed published feed."
    }

    $sameBaseReleasesPath = Join-Path $testRoot "same-base-releases.json"
    $sameBaseReleases = @(
        @{
            tag_name = "v2.1.0-rc12"
            draft = $false
            assets = @(@{ name = "appcast-v2.xml" }, @{ name = "appcast-v2.xml.signature" })
        },
        @{
            tag_name = "v2.1.0"
            draft = $false
            assets = @(@{ name = "appcast-v2.xml" }, @{ name = "appcast-v2.xml.signature" })
        }
    ) | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText(
        $sameBaseReleasesPath,
        $sameBaseReleases,
        [Text.UTF8Encoding]::new($false))
    $selectedStableFeed = & (Join-Path $PSScriptRoot "../release/Select-PublishedUpdateFeed.ps1") `
        -ReleasesJsonPath $sameBaseReleasesPath
    if ($selectedStableFeed -cne "v2.1.0") {
        throw "A stable release did not supersede RCs of the same base version."
    }

    $legacyArtifactRoot = Join-Path $testRoot "legacy-artifacts"
    New-Item -ItemType Directory -Path $legacyArtifactRoot | Out-Null
    $legacyArtifactPaths = foreach ($name in @(
        "TOTP-Manager-windows-x64-2.0.0-rc3.zip",
        "TOTP-Manager-linux-x64-2.0.0-rc3.tar.gz",
        "totp-manager_2.0.0-rc3_amd64.deb")) {
        $path = Join-Path $legacyArtifactRoot $name
        [IO.File]::WriteAllBytes($path, [byte[]](1, 2, 3, 4))
        $path
    }
    $legacyManifestPath = Join-Path $legacyArtifactRoot "legacy-release-artifacts.json"
    & (Join-Path $PSScriptRoot "../release/New-ReleaseArtifactManifest.ps1") `
        -ReleaseVersion "2.0.0-rc3" `
        -SourceCommit ("b" * 40) `
        -ArtifactPath $legacyArtifactPaths `
        -OutputPath $legacyManifestPath `
        -ReleaseProfile unsigned-preview | Out-Null
    & (Join-Path $PSScriptRoot "../release/Test-ReleaseArtifactManifest.ps1") `
        -ManifestPath $legacyManifestPath `
        -ArtifactDirectory $legacyArtifactRoot | Out-Null

    Write-Output "Package ownership and signed preview update policies are valid."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
