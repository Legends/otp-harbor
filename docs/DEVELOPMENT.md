# OTP Harbor developer guide

This document is the development entry point for OTP Harbor. It covers the toolchain, repository layout, local workflows, platform-specific requirements, CI and release boundaries, and every PowerShell helper maintained in the repository.

OTP Harbor is security-sensitive. Never use real OTP seeds, passwords, vaults, backup passwords, signing keys, tokens, or personal backup artifacts as development fixtures. Follow [AGENTS.md](../AGENTS.md), [CONTRIBUTING.md](../CONTRIBUTING.md), the [threat model](security/THREAT_MODEL.md), and the relevant release or security runbook before changing a sensitive workflow.

## Required development tools

| Tool | Development use |
| --- | --- |
| Git | Source control, safe source-launcher updates, version metadata, release-tag validation, and tracked-asset policy checks. |
| PowerShell | Runs the repository's development, test, validation, asset, security, and release automation. PowerShell 7 is recommended for cross-platform work; some Windows-only scripts require Windows APIs or an elevated session. |
| .NET SDK `10.0.401` | Builds and tests all projects. The exact SDK is pinned by [`global.json`](../global.json); roll-forward is disabled. |
| NuGet and [`NuGet.config`](../NuGet.config) | Restores the locked dependency graph through the `dotnet` CLI. Android and release validation expect locked restores. |
| An editor or IDE | Visual Studio, JetBrains Rider, VS Code, or another C# editor may be used. The command-line workflows remain authoritative. |

## Platform-specific and optional tools

Install only the tools required by the platform or workflow being changed.

| Tool | Development use |
| --- | --- |
| .NET Android workload | Builds `OtpHarbor.Android.sln` and the Android host projects. Install with `dotnet workload install android --skip-manifest-update`. |
| Android SDK and Platform-Tools | Supplies Android build tools and `adb` for device discovery, installation, relaunch, and logs. `ANDROID_HOME` or the standard `%LOCALAPPDATA%\Android\Sdk` location is used by the scripts. |
| JDK 21 | Used by Android release builds and signing tooling in CI. `keytool` creates or inspects the external release keystore. |
| `apksigner` | Verifies the production APK signature and exposes the signing certificate consumed by release validation. It is resolved from the Android SDK build-tools directory. |
| Windows SDK `makeappx.exe` | Creates the unsigned Microsoft Store submission MSIX. It is not an end-user sideload package. |
| WiX Toolset `5.0.2` | Builds and validates the Windows MSI and custom setup bundle. CI installs it with `dotnet tool install --global wix --version 5.0.2`. |
| NetSparkle appcast generator `2.9.0` | Generates signed desktop update appcasts. Install `NetSparkleUpdater.Tools.AppCastGenerator` as a global .NET tool when working on direct-update releases. |
| SignPath | Optional hosted Windows signing workflow. Signing results are applied and verified locally by repository automation; private signing material must never enter the repository. |
| Apple/Xcode command-line tools | `codesign`, `hdiutil`, `xcrun notarytool`, `stapler`, and Gatekeeper validation are used for signed and notarized macOS packages. |
| Linux packaging tools | `tar` creates portable archives, `dpkg-deb` creates and inspects DEB packages, and `ldd` validates native runtime dependencies. |
| Hyper-V PowerShell module | Creates the guarded Ubuntu test-VM network and discovers the configured VM during Windows-hosted Linux testing. |
| OpenSSH client and server | Windows uses `ssh` and `scp` to transfer and run the Linux test build. The Ubuntu VM requires `openssh-server`. |
| `rsync` | Synchronizes the transferred source tree inside the Ubuntu VM without copying build outputs or Git metadata. |
| WSL/WSLg | Provides quick Linux development checks. Use the Hyper-V VM or a physical target for end-user acceptance testing. |
| PowerShell SecretManagement and SecretStore | Preferred optional storage for the GitHub token used by branch-protection scripts. The Windows-only fallback is a current-user DPAPI-protected file. |
| GitHub Actions | Authoritative multi-platform CI, security audits, packaging, release publication, website deployment, and icon-pack compatibility checks. |
| `System.Drawing` on Windows | Used by the reviewed asset-generation and screenshot scripts; it is loaded by those scripts rather than added as a separate repository tool. |

## Primary frameworks and test libraries

| Framework or library | Role |
| --- | --- |
| Avalonia | Shared native desktop/mobile UI, themes, controls, headless UI tests, and Android hosting. |
| FluentResults | Explicit result-based control flow for expected failures. |
| Serilog | Structured diagnostics with repository redaction controls. |
| Otp.NET | TOTP generation and validation primitives. |
| QRCoder and ZXing.Net | QR generation and decoding. |
| OpenCvSharp | Desktop camera capture behind `OtpHarbor.Camera.OpenCv`. |
| AndroidX Camera, Core, and RecyclerView | Android camera lifecycle, platform compatibility, and native account-list rendering. |
| xUnit v3 | Unit, integration, platform, and regression tests. |
| Moq and Moq.AutoMock | Dependency isolation in tests. |
| FluentAssertions | Readable behavioral assertions. |

Package versions are defined by the project files and lock files. Update them only as an explicit dependency review.

## Solutions and project boundaries

- [`OtpHarbor.sln`](../OtpHarbor.sln) contains the desktop, shared, infrastructure, persistence, installer, updater, and test projects. Desktop-only work does not require the Android workload.
- [`OtpHarbor.Android.sln`](../OtpHarbor.Android.sln) contains the Android platform, mobile UI, Android host, and their shared dependencies.

| Project | Responsibility |
| --- | --- |
| `OtpHarbor.Core` | Domain models, enums, contracts, validation, and security abstractions. |
| `OtpHarbor.Infrastructure` | Concrete security orchestration, settings, logging, account, import/export, and backup services. |
| `OtpHarbor.DAL` | Persistence, filesystem access, and storage mapping. |
| `OtpHarbor.Camera.OpenCv` | Desktop OpenCV camera capture and QR scanning. |
| `OtpHarbor.Platform.Windows`, `OtpHarbor.Platform.Linux`, `OtpHarbor.Platform.MacOS`, `OtpHarbor.Platform.Unix` | Operating-system integrations. |
| `OtpHarbor.Platform.Android` | Android Keystore, biometric/device-credential, and Android platform services. |
| `OtpHarbor.UI.Avalonia.Shared` | Shared Avalonia controls and presentation contracts. |
| `OtpHarbor.UI.Avalonia.Desktop` | Desktop views, view models, startup, composition, and UI adapters. |
| `OtpHarbor.UI.Avalonia.Mobile` | Shared mobile views and mobile presentation logic. |
| `OtpHarbor.UI.Avalonia.Android` | Android application host and native Android presentation adapters. |
| `OtpHarbor.Installer` | Custom Windows installer UI. |
| `OtpHarbor.Updater` | Desktop updater/install support. |
| `OtpHarbor.Tests`, `OtpHarbor.Tests.Avalonia.Headless`, `OtpHarbor.Tests.Unix` | Unit, regression, headless UI, integration, and Unix-specific tests. |

Keep MVVM boundaries strict, keep security and persistence policy out of code-behind, and register desktop composition in `OtpHarbor.UI.Avalonia.Desktop/Startup/AvaloniaCompositionRoot.cs`.

## Initial setup

```powershell
git clone https://github.com/Legends/otp-harbor.git
cd otp-harbor
dotnet --version
dotnet restore OtpHarbor.sln --configfile NuGet.config
dotnet build OtpHarbor.sln -c Debug
dotnet test OtpHarbor.sln -c Debug
```

`dotnet --version` must report `10.0.401`. Restore Android independently when Android work is required:

```powershell
dotnet workload install android --skip-manifest-update
dotnet restore OtpHarbor.Android.sln --locked-mode --configfile NuGet.config
dotnet build OtpHarbor.Android.sln -c Debug --no-restore
```

## Common development workflows

Run the desktop application:

```powershell
dotnet run --project .\OtpHarbor.UI.Avalonia.Desktop\OtpHarbor.UI.Avalonia.Desktop.csproj
```

Run the full desktop solution tests:

```powershell
dotnet test OtpHarbor.sln -c Debug
```

Run the faster PR-style subset after a Release build:

```powershell
dotnet test OtpHarbor.sln -c Release --no-build --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~IdleMonitoringBackgroundServiceTests&FullyQualifiedName!~UserActivityServiceTests"
```

Prefer a targeted project or test-class filter while iterating. Run the full relevant suite before handing off a cross-cutting or security-sensitive change.

### Safe source launcher

[`Start-OTP-Harbor.ps1`](../Start-OTP-Harbor.ps1) is a developer/source-user convenience, not a signed binary update channel:

```powershell
.\Start-OTP-Harbor.ps1
```

It validates Git and the pinned .NET SDK, checks `origin/master`, permits only a safe fast-forward, restores and builds when required, and starts the verified Release output. It refuses dirty, ahead, or diverged checkouts and never discards local files.

### Android device testing

Use [`scripts/testing/Install-AndroidDevelopmentBuild.ps1`](../scripts/testing/Install-AndroidDevelopmentBuild.ps1) to build only when inputs changed, select the connected device ABI, install the isolated `.debug` application with `adb install -r`, preserve app data, and relaunch it. See [Android foundation](android/FOUNDATION.md) for platform architecture and [Android release and signing](release/ANDROID.md) for production packaging.

### Linux testing

Use WSL/WSLg for quick checks. Use the guarded Hyper-V workflow for full Linux desktop behavior. Commands and VM prerequisites are in [Desktop test commands](testing/DESKTOP_APP_TEST_COMMANDS.md) and [Ubuntu Hyper-V network](testing/UBUNTU_HYPERV_NETWORK.md).

## CI, releases, and security review

GitHub Actions restores through `NuGet.config`, builds Release configurations, runs platform-specific and security checks, and publishes only from the appropriate stable tag:

- Desktop: `v<major>.<minor>.<patch>`
- Android: `android-v<major>.<minor>.<patch>`

Read [Microsoft Store release](release/MICROSOFT_STORE.md), [Android release and signing](release/ANDROID.md), [automatic updates](security/AUTO_UPDATE.md), and [signing-key rotation](security/SIGNING_KEY_ROTATION.md) before changing package identity, signing, update feeds, manifests, or release automation.

Password handling, KDF parameters, vault/envelope formats, import/export formats, backups, logging/redaction, quick unlock, update verification, and signing changes require explicit threat, data-flow, compatibility, and test analysis.

## PowerShell script catalog

This catalog covers all 59 version-controlled PowerShell scripts currently maintained by the repository. Run scripts from the repository root unless their help states otherwise. Use `Get-Help <path> -Full` to inspect parameters and examples. Release and security scripts can change packages, credentials, or remote repository policy; do not run them speculatively.

### Root launcher

| Script | Purpose |
| --- | --- |
| [`Start-OTP-Harbor.ps1`](../Start-OTP-Harbor.ps1) | Safely updates a source checkout by fast-forward only, restores/builds when necessary, and starts the desktop app. Refuses dirty, ahead, or diverged repositories. |

### Asset tooling

| Script | Purpose |
| --- | --- |
| [`scripts/assets/Capture-WindowsStoreScreenshot.ps1`](../scripts/assets/Capture-WindowsStoreScreenshot.ps1) | Resizes and captures only the visible OTP Harbor window for Store artwork; requires explicit confirmation that all visible data is synthetic. |
| [`scripts/assets/Generate-AdditionalLanguageFlags.ps1`](../scripts/assets/Generate-AdditionalLanguageFlags.ps1) | Deterministically generates the reviewed French, German, and Spanish desktop language-flag PNG files in the approved asset directory. |
| [`scripts/assets/Generate-AppIcon.ps1`](../scripts/assets/Generate-AppIcon.ps1) | Produces required PNG sizes and the multi-resolution Windows ICO from a supplied source image. |
| [`scripts/assets/New-AndroidMarketingImages.ps1`](../scripts/assets/New-AndroidMarketingImages.ps1) | Composites reviewed synthetic Android captures into website and store campaign artwork without reconstructing application or system UI. |
| [`scripts/assets/New-WindowsStoreMarketingAssets.ps1`](../scripts/assets/New-WindowsStoreMarketingAssets.ps1) | Generates exact-size Partner Center hero, poster, box-art, and Store-logo assets from reviewed sources. |
| [`scripts/assets/New-WindowsStoreMarketingScreenshots.ps1`](../scripts/assets/New-WindowsStoreMarketingScreenshots.ps1) | Generates localized Microsoft Store screenshot campaigns from reviewed synthetic captures and localized copy. |

### Local development tooling

| Script | Purpose |
| --- | --- |
| [`scripts/dev/Publish-And-Run-AvaloniaWindows.ps1`](../scripts/dev/Publish-And-Run-AvaloniaWindows.ps1) | Publishes a local Windows Avalonia build into `artifacts/dev` without rewriting canonical NuGet locks, optionally stops the existing instance, validates the executable, reports its hash, and launches it. |

### Release tooling

| Script | Purpose |
| --- | --- |
| [`scripts/release/Apply-SignPathSignedPayload.ps1`](../scripts/release/Apply-SignPathSignedPayload.ps1) | Applies a SignPath result to prepared Windows directories and fails unless every first-party PE has a valid Authenticode signature and expected product metadata. |
| [`scripts/release/Generate-AvaloniaAppcast.ps1`](../scripts/release/Generate-AvaloniaAppcast.ps1) | Validates release metadata and Ed25519 identity, invokes the NetSparkle generator, and writes the authenticated desktop update appcast. |
| [`scripts/release/Get-AndroidReleaseVersion.ps1`](../scripts/release/Get-AndroidReleaseVersion.ps1) | Converts an `android-vX.Y.Z` tag into deterministic Android `versionName`, monotonic `versionCode`, and application identity metadata. |
| [`scripts/release/Get-AndroidSigningCertificateFingerprint.ps1`](../scripts/release/Get-AndroidSigningCertificateFingerprint.ps1) | Parses exactly one PEM signing certificate from `apksigner` output and returns its SHA-256 fingerprint. |
| [`scripts/release/Get-MicrosoftStoreVersion.ps1`](../scripts/release/Get-MicrosoftStoreVersion.ps1) | Maps stable or RC release tags into four-part Microsoft Store versions while preserving correct ordering. |
| [`scripts/release/New-AndroidRelease.ps1`](../scripts/release/New-AndroidRelease.ps1) | Produces and verifies the production-signed Android APK using an external keystore and password files that do not enter process arguments or logs. |
| [`scripts/release/New-CurrentSourceMicrosoftStoreMsix.ps1`](../scripts/release/New-CurrentSourceMicrosoftStoreMsix.ps1) | Resolves and validates the current stable source/tag, then invokes the Store packager with the reserved Partner Center identity. |
| [`scripts/release/New-MicrosoftStoreMsix.ps1`](../scripts/release/New-MicrosoftStoreMsix.ps1) | Publishes Windows, renders the reviewed Appx manifest/assets, enforces Store update policy, validates boundaries, and creates the unsigned Partner Center MSIX input. |
| [`scripts/release/New-ReleaseArtifactManifest.ps1`](../scripts/release/New-ReleaseArtifactManifest.ps1) | Creates deterministic release inventory metadata containing platform, architecture, ownership, update policy, byte length, SHA-256, and source commit. |
| [`scripts/release/New-WindowsMsi.ps1`](../scripts/release/New-WindowsMsi.ps1) | Stages the Windows payload and package-manager update policy, generates WiX source, builds the MSI, and validates it. |
| [`scripts/release/New-WindowsSetup.ps1`](../scripts/release/New-WindowsSetup.ps1) | Publishes the custom installer UI and uses WiX to wrap a validated MSI in the OTP Harbor setup executable. |
| [`scripts/release/Package-AvaloniaLinux.ps1`](../scripts/release/Package-AvaloniaLinux.ps1) | Validates a Linux publish, applies update policy, adds launcher/desktop metadata, and creates portable tarball and DEB packages. |
| [`scripts/release/Package-AvaloniaMacOS.ps1`](../scripts/release/Package-AvaloniaMacOS.ps1) | Creates the macOS `.app` and DMG, applies channel metadata, and optionally performs Developer ID signing and notarization. |
| [`scripts/release/Prepare-AvaloniaReleasePayload.ps1`](../scripts/release/Prepare-AvaloniaReleasePayload.ps1) | Sanitizes a publish directory, rejects stale updater subtrees, removes contained debug symbols safely, and verifies the release payload. |
| [`scripts/release/Select-PublishedUpdateFeed.ps1`](../scripts/release/Select-PublishedUpdateFeed.ps1) | Selects the newest non-draft GitHub release with a complete appcast and signature pair. |
| [`scripts/release/Set-PackageUpdatePolicy.ps1`](../scripts/release/Set-PackageUpdatePolicy.ps1) | Writes and validates the distribution mode, update channel, and optional HTTPS appcast URL embedded in a package. |
| [`scripts/release/Set-PublicMicrosoftStoreVersion.ps1`](../scripts/release/Set-PublicMicrosoftStoreVersion.ps1) | Updates or checks canonical public Store-version metadata, the README Store badge/status, and Store-listing heading. |
| [`scripts/release/Test-AvaloniaAppcast.ps1`](../scripts/release/Test-AvaloniaAppcast.ps1) | Verifies appcast signatures, URLs, versions, platforms, architectures, byte sizes, channels, and manifest correspondence. |
| [`scripts/release/Test-ReleaseArtifactManifest.ps1`](../scripts/release/Test-ReleaseArtifactManifest.ps1) | Validates release-manifest schema, safe paths, package policies, file inventory, sizes, and SHA-256 hashes. |

### Repository security administration

| Script | Purpose |
| --- | --- |
| [`scripts/security/Remove-BranchProtection.ps1`](../scripts/security/Remove-BranchProtection.ps1) | Removes GitHub branch protection through the API after securely resolving a token. This intentionally weakens remote policy and must be used only for an approved administrative operation. |
| [`scripts/security/Set-BranchProtection.ps1`](../scripts/security/Set-BranchProtection.ps1) | Applies the reviewed required-review and status-check policy to a GitHub branch. |
| [`scripts/security/Set-GitHubTokenForCurrentUser.ps1`](../scripts/security/Set-GitHubTokenForCurrentUser.ps1) | Prompts with hidden input and stores a GitHub token in SecretStore or a Windows current-user DPAPI-protected fallback file. |

### Test and preview tooling

| Script | Purpose |
| --- | --- |
| [`scripts/testing/Ensure-UbuntuHyperVNetwork.ps1`](../scripts/testing/Ensure-UbuntuHyperVNetwork.ps1) | Creates or repairs the guarded internal Hyper-V switch, host address, static VM adapter, and NAT used by the Ubuntu test VM. Requires elevation. |
| [`scripts/testing/Generate-BrandAccountFixture.ps1`](../scripts/testing/Generate-BrandAccountFixture.ps1) | Creates deterministic synthetic account fixtures from the display names in a user-supplied `.otphicons` pack, ensuring every generated issuer is resolvable by that same imported pack. |
| [`scripts/testing/Generate-ProminentPlatformAccountFixture.ps1`](../scripts/testing/Generate-ProminentPlatformAccountFixture.ps1) | Downloads a pinned Tranco ranking and creates deterministic synthetic TOTP account fixtures for large-list and performance testing. |
| [`scripts/testing/Get-AndroidRuntimeIdentifier.ps1`](../scripts/testing/Get-AndroidRuntimeIdentifier.ps1) | Maps supported device ABIs (`arm64-v8a`, `x86_64`) to the corresponding .NET Android runtime identifier and rejects unsupported ABIs. |
| [`scripts/testing/Get-LatestIconPackCompatibilityAssets.ps1`](../scripts/testing/Get-LatestIconPackCompatibilityAssets.ps1) | Downloads bounded latest-release Aegis Icons and Simple Icons archives, records versions and SHA-256 values, and exposes paths to CI compatibility tests. |
| [`scripts/testing/Install-AndroidDevelopmentBuild.ps1`](../scripts/testing/Install-AndroidDevelopmentBuild.ps1) | Discovers or connects to a selected Android device, fingerprints build inputs, performs an ABI-specific incremental build or requested clean/fast deploy without rewriting canonical NuGet locks, preserves app data, installs, and relaunches the debug package. |
| [`scripts/testing/Start-OnboardingPreview.ps1`](../scripts/testing/Start-OnboardingPreview.ps1) | Starts desktop or Android Debug with onboarding forced visible; desktop also supports a Light or Dark preview theme. |
| [`scripts/testing/Sync-Restore-Run-AvaloniaLinuxVm.ps1`](../scripts/testing/Sync-Restore-Run-AvaloniaLinuxVm.ps1) | Archives the working tree without build/Git data, transfers it by SSH, synchronizes a guarded Ubuntu VM directory, restores, imports the desktop session environment, and runs the app. |

The testing directory also contains two shell helpers: `configure-ubuntu-hyperv-network.sh` configures the Ubuntu guest's stable network profile, and `run-avalonia-wsl.sh` runs the desktop project under WSL/WSLg.

### Validation tooling

| Script | Purpose |
| --- | --- |
| [`scripts/validation/Compare-AndroidApkPayload.ps1`](../scripts/validation/Compare-AndroidApkPayload.ps1) | Compares non-signature ZIP entries in two APKs and reports payload equivalence while intentionally ignoring signing records, timestamps, compression, and entry order. |
| [`scripts/validation/Measure-M3Target.ps1`](../scripts/validation/Measure-M3Target.ps1) | Measures repeated startup, working set, account filtering, package size, and native-byte targets and writes JSON evidence. |
| [`scripts/validation/Package-M3Target.ps1`](../scripts/validation/Package-M3Target.ps1) | Stages an existing platform publish into a clean M3 measurement package, including a minimal macOS `.app` layout when needed. |
| [`scripts/validation/Test-AndroidApkPayloadComparison.ps1`](../scripts/validation/Test-AndroidApkPayloadComparison.ps1) | Regression-tests APK payload comparison with deterministic equivalent, changed, missing, and duplicate-entry fixtures. |
| [`scripts/validation/Test-AndroidDependencyLicenses.ps1`](../scripts/validation/Test-AndroidDependencyLicenses.ps1) | Validates license declarations and cached hashes for the locked Android NuGet graph after restore. |
| [`scripts/validation/Test-AndroidDistributionChannels.ps1`](../scripts/validation/Test-AndroidDistributionChannels.ps1) | Enforces Android application identity, signing-channel migration guidance, disabled Android system backup, and CI policy wiring. |
| [`scripts/validation/Test-AndroidFossBuild.ps1`](../scripts/validation/Test-AndroidFossBuild.ps1) | Checks the pinned SDK, locked Android dependency graph, package source, prohibited dependency families, CI restore controls, and F-Droid readiness disclosure. |
| [`scripts/validation/Test-AndroidMarketingAssets.ps1`](../scripts/validation/Test-AndroidMarketingAssets.ps1) | Validates Android website/store image formats and dimensions, localized listing limits and claims, and the reviewed source-capture inventory. |
| [`scripts/validation/Test-AndroidReleasePackaging.ps1`](../scripts/validation/Test-AndroidReleasePackaging.ps1) | Regression-checks Android identity, Debug isolation, version mapping, signing-certificate parsing, device installer controls, workflow separation, and release documentation. |
| [`scripts/validation/Test-BrandAssetPolicy.ps1`](../scripts/validation/Test-BrandAssetPolicy.ps1) | Enforces reviewed first-party asset allowlists/provenance and the offline, user-initiated, third-party icon-pack legal boundary. |
| [`scripts/validation/Test-LocalizationResources.ps1`](../scripts/validation/Test-LocalizationResources.ps1) | Compares invariant, German, French, and Spanish resource catalogs and rejects missing, extra, empty, duplicate, or format-placeholder-incompatible entries. |
| [`scripts/validation/Test-M3MeasurementBudget.ps1`](../scripts/validation/Test-M3MeasurementBudget.ps1) | Applies reviewed startup, memory, package, native dependency, and filtering budgets to an M3 JSON report. |
| [`scripts/validation/Test-MicrosoftStorePackaging.ps1`](../scripts/validation/Test-MicrosoftStorePackaging.ps1) | Validates the Store manifest, packager, workflow, version mapping/updater, documentation, listing, assets, capabilities, languages, and release controls. |
| [`scripts/validation/Test-NativePackageDependencies.ps1`](../scripts/validation/Test-NativePackageDependencies.ps1) | Confirms the correct app host and OpenCV native runtime, rejects foreign-platform binaries, and uses `ldd` on Unix to detect unresolved dependencies. |
| [`scripts/validation/Test-NuGetVulnerabilities.ps1`](../scripts/validation/Test-NuGetVulnerabilities.ps1) | Runs the transitive NuGet vulnerability audit, writes raw JSON evidence, lists advisories, and fails on any vulnerable package. |
| [`scripts/validation/Test-PublicWebsite.ps1`](../scripts/validation/Test-PublicWebsite.ps1) | Validates public pages, SEO/verification/social metadata, structured data, release messaging, robots/sitemap files, and required media dimensions. |
| [`scripts/validation/Test-RepositoryLicensePolicy.ps1`](../scripts/validation/Test-RepositoryLicensePolicy.ps1) | Enforces GPL-3.0-only text, public disclosures, contribution terms, package legal files, immutable license history, F-Droid posture, and CI wiring. |
| [`scripts/validation/Test-SignPathFoundationReadiness.ps1`](../scripts/validation/Test-SignPathFoundationReadiness.ps1) | Validates signing policy/privacy, workflow controls, CODEOWNERS, product metadata, signed-payload checks, and reviewed asset provenance for dormant SignPath readiness. |
| [`scripts/validation/Test-SourceLauncher.ps1`](../scripts/validation/Test-SourceLauncher.ps1) | Dot-sources the root launcher and regression-tests current, behind, ahead, diverged, dirty, and stale-build planning plus safe Git guards. |
| [`scripts/validation/Test-UnsignedPreviewReleasePolicy.ps1`](../scripts/validation/Test-UnsignedPreviewReleasePolicy.ps1) | Exercises synthetic stable/RC package metadata and rejects unsigned-preview configurations that imply signed automatic updates. |
| [`scripts/validation/Test-WindowsMsiPackaging.ps1`](../scripts/validation/Test-WindowsMsiPackaging.ps1) | Validates WiX generation, installer resources/UI, per-machine installation, shortcuts, optional launch behavior, update policy, manifest classification, and workflow integration. |

## Related developer documentation

- [Documentation index](README.md)
- [Contribution guide](../CONTRIBUTING.md)
- [Android foundation](android/FOUNDATION.md)
- [Desktop test commands](testing/DESKTOP_APP_TEST_COMMANDS.md)
- [Community platform testing](testing/COMMUNITY_PLATFORM_TESTING.md)
- [Security verification](security/SECURITY_VERIFICATION.md)
- [Code-signing policy](../CODE_SIGNING_POLICY.md)
- [Automatic updates](security/AUTO_UPDATE.md)
- [Microsoft Store release](release/MICROSOFT_STORE.md)
- [Android release and signing](release/ANDROID.md)
