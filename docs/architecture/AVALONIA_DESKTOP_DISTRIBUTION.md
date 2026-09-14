# Avalonia desktop distribution policy

## Supported release targets

| Target | Initial artifact | Install/update ownership |
| --- | --- | --- |
| Windows x64 | Microsoft Store MSIX after certification | Microsoft Store signs the accepted package and owns update delivery; stable GitHub releases contain no Windows binary |
| macOS 14+ ARM64 | Developer ID signed and notarized DMG | User installs the app bundle; replacement must use a future signed/notarized target adapter or an explicit manual download |
| Ubuntu 24.04 x64 | Self-contained tar.gz and DEB | Portable extraction or Debian package manager; no in-place app updater claims support |

macOS x64 remains outside the initial support policy because the aligned OpenCV native runtime failed the retained Intel probe. Linux AppImage and macOS PKG are not initial formats. AppImage needs a maintained D-Bus, desktop integration, and update policy; PKG adds privileged installation machinery without a current product need.

The stable Windows release is a self-contained MSIX for Microsoft Store. Repository CI creates the unsigned Partner Center submission input with package identity values supplied by the reserved Store product. It sets `DistributionMode=store`, disables application-owned updates, and removes the standalone updater. Microsoft signs the package after successful certification. The unsigned MSIX must never be sideloaded or attached to a GitHub Release.

The direct Windows ZIP/update implementation is retained only as dormant future-provider groundwork. Stable desktop GitHub releases publish Linux packages, integrity metadata, and source archives; they do not publish unsigned Windows executables. Android APKs use independent Android releases.

## macOS release procedure

Publish the ARM64 host self-contained on macOS, then run:

```powershell
./scripts/release/Package-AvaloniaMacOS.ps1 `
  -PublishDirectory ./publish-osx-arm64 `
  -OutputDirectory ./distribution-osx-arm64 `
  -ReleaseVersion 2.0.0 `
  -SigningIdentity "Developer ID Application: Example (TEAMID)" `
  -NotaryKeychainProfile "totp-manager-notary"
```

The notary profile must be created outside the repository with `xcrun notarytool store-credentials`. Never pass Apple credentials directly in repository scripts or command-line arguments. The script requires Developer ID signing, hardened runtime, secure timestamps, `notarytool`, stapling, and Gatekeeper assessment before a credentialed package is accepted.

CI may instead supply the App Store Connect API-key triplet `-NotaryKeyPath`, `-NotaryKeyId`, and `-NotaryIssuerId`. The private key is materialized only in the runner's temporary directory and only its path is passed to `notarytool`; profile and API-key modes are mutually exclusive.

Conditional direct-download tag publication expects these GitHub Actions secrets only after the relevant provider integration is approved:

- Windows: an approved Authenticode provider configuration. There is currently no SignPath Foundation certificate or active Windows direct-publication configuration;
- macOS signing: `MACOS_SIGNING_CERTIFICATE_BASE64`, `MACOS_SIGNING_CERTIFICATE_PASSWORD`, `MACOS_SIGNING_IDENTITY`;
- macOS notarization: `MACOS_NOTARY_KEY_BASE64`, `MACOS_NOTARY_KEY_ID`, `MACOS_NOTARY_ISSUER_ID`;
- update feed: `NETSPARKLE_PUBLIC_KEY`, `NETSPARKLE_PRIVATE_KEY`.

A missing credential fails the conditional direct tag workflow. It never downgrades a production artifact to unsigned output. Store packaging uses its separate Partner Center-only workflow and no repository certificate secret.

Earlier release-candidate tags formed a temporary preview channel and remain immutable historical evidence. The `v2.0.0` release ended that channel. Current stable desktop releases are marked `Latest`, publish Linux artifacts, and use the Windows payload only for the separate Partner Center artifact. Android now publishes from independent `android-v` tags. Existing RC clients may use the legacy verified endpoint to advance to stable.

The entitlements are limited to the camera capability and the current Microsoft-documented defaults required by a notarized .NET app host. Any removal or addition requires a physical launch/camera/Keychain regression on the signed bundle.

## Linux release procedure

Publish the x64 host self-contained on Ubuntu 24.04, then run:

```powershell
./scripts/release/Package-AvaloniaLinux.ps1 `
  -PublishDirectory ./publish-linux-x64 `
  -OutputDirectory ./distribution-linux-x64 `
  -ReleaseVersion 2.0.0
```

`-FrameworkDependent` exists only for CI technical probes and adds the .NET 10 runtime dependency. Public packages should be self-contained so adding an external Microsoft package feed is not silently required.

The DEB depends on `libsecret-tools` so the Secret Service capability has a concrete client. If the desktop has no session D-Bus or supported secret collection, the app reports that capability as misconfigured/unavailable and continues with master-password authorization.

The DEB installs the application mark in the hicolor icon theme and references its reverse-DNS icon name from the desktop entry. The macOS packager derives a complete `.icns` iconset from the published 1024-pixel application asset and records it in `Info.plist` before code signing.

Package assembly stamps the DEB with `AutoUpdate:DistributionMode=package-manager`, so the application cannot replace package-manager-owned files. The portable tarball remains `direct`; it may consume only a matching signed `appcast-v2.xml` entry.

## Release guardrails

- Unsigned CI artifacts are technical evidence and must never be presented as production or stable releases.
- Stable GitHub releases contain no Windows binaries while SignPath is deferred; Windows release delivery is Store-only.
- Store and DEB packages disable the application-owned client; the portable Linux package enables the stable signed appcast.
- Artifact filenames, appcast target OS/architecture, assembly version, bundle/debian version, and Git tag must agree.
- Portable Linux packages consume only matching target-qualified entries from `appcast-v2.xml` and require an explicit stable channel match.
- Every release artifact is recorded in a deterministic manifest with its source commit, byte length, SHA-256, ownership, and update policy.
- The aggregate manifest, every direct payload, and `appcast-v2.xml` are Ed25519-signed with pinned NetSparkle tooling. The client-embedded public key must match the CI public key before publication.
- Direct-update artifacts above 128 MiB are rejected rather than expanding the client's bounded download policy.
- No package may contain authorization envelopes, vaults, logs, user preferences, private keys, certificates, or notarization credentials.
- Physical acceptance uses the exact retained candidate artifact, not a local development build.
- Headless CI loads the real application XAML on every supported runner; this catches resource/theme construction failures but does not replace interactive accessibility or rendering checks.

## Authoritative platform references

- Apple: [Notarizing macOS software before distribution](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)
- Apple: [Packaging Mac software for distribution](https://developer.apple.com/documentation/xcode/packaging-mac-software-for-distribution)
- Apple: [Keychain user-presence access control](https://developer.apple.com/documentation/security/secaccesscontrolcreateflags/userpresence)
- Apple: [Data-protection Keychain selection](https://developer.apple.com/documentation/security/ksecusedataprotectionkeychain)
- Microsoft: [.NET macOS notarization and required host entitlements](https://learn.microsoft.com/dotnet/core/install/macos-notarization-issues)
