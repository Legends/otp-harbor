# Code signing policy

**Windows code-signing status:** The previous SignPath Foundation application was not approved at this stage. Reapplication is deferred until the project has materially stronger public adoption signals, including GitHub stars and verified download usage. Windows is distributed only through Microsoft Store certification; stable GitHub releases do not attach unsigned Windows binaries.

This policy covers the distinct platform distribution paths. They must never be presented as interchangeable:

- **Microsoft Store (primary):** OTP Harbor is publicly available as Store product `9P31KH5L924P`. CI creates unsigned MSIX inputs solely for Partner Center; Microsoft signs accepted packages and the Store manages updates.
- **GitHub (secondary):** stable desktop tags publish source archives, Linux packages, and release-integrity metadata. Independent Android tags publish the production-signed APK and Android integrity metadata. Portable Linux packages use an Ed25519-signed appcast. A future Windows direct-download channel remains blocked unless an independent Authenticode trust path is approved and verified.
- **Android on GitHub:** a separately downloadable universal APK under the permanent
  `io.github.legends.otpharbor` ID. Every public APK must carry the pinned production Android
  app-signing certificate; development builds use the isolated `.debug` application ID.

An unsigned Store submission MSIX is not a sideloading artifact. It must not be attached to a GitHub Release, linked as an installer, or described as trusted before Store certification.

## Team roles

- Committer and reviewer: [Legends](https://github.com/Legends)
- Approver: [Legends](https://github.com/Legends)

Changes from contributors who do not have commit access require maintainer review before merge. Participation in source control, Partner Center, or any future signing service requires multi-factor authentication. Store submissions and any future direct-package signing requests require explicit maintainer approval.

## Build and signing controls

- Release binaries are built from this public repository by the tag-triggered GitHub Actions workflow on GitHub-hosted runners.
- Desktop releases use `v<major>.<minor>.<patch>`; Android releases use `android-v<major>.<minor>.<patch>`. Microsoft Store package versions use four components and reserve the fourth component as `0`.
- The Store package is built from this public repository with the exact case-sensitive identity supplied by Partner Center. Placeholder CI identities are smoke-test inputs only.
- Store packages set `DistributionMode` to `store`, disable application-owned updates, and exclude the standalone updater.
- Portable Linux packages set `DistributionMode` to `direct` and use the stable GitHub Release appcast. Microsoft Store and Linux DEB packages keep application-owned updates disabled.
- The legacy public RC endpoint remains only to move already-installed RC clients to the stable release. It mirrors only a published release whose appcast signature verifies against the client-embedded Ed25519 key and never signs or modifies metadata.
- The generated unsigned MSIX and its SHA-256 metadata are retained only for the controlled Partner Center handoff.
- Android uses stable-only `android-v<major>.<minor>.<patch>` tags and an independent visible version, while its deterministic integer version code remains monotonic and compatible with existing APK upgrades.
- The Android release job is protected by the `android-release` environment, reconstructs the signing key only in temporary runner storage, passes passwords through files, verifies the resulting APK signature and manifest identity, and rejects a certificate-fingerprint mismatch.
- The production Android package name and certificate must be registered through Android developer verification before the first public APK. A future Play App Signing enrollment must use the existing app-signing key so GitHub and Play packages remain upgrade-compatible.
- The Store package is published only after certification plus physical acceptance of install, launch, Windows Hello, QR scanning, encrypted backup/restore, lock behavior, and Store-managed updates.
- Any future direct-download signing integration must bind the artifact to its GitHub workflow run and source commit, expose no certificate private key to the repository, sign only reviewed first-party binaries, and verify product metadata plus Authenticode status before publication.
- Published release tags are immutable and must not be moved or deleted to replace artifacts.

Release engineering files are owned through [CODEOWNERS](.github/CODEOWNERS). Changes to the workflow, signing policy, release scripts, or update trust configuration require security-focused review.

## Privacy

See the [privacy policy](PRIVACY.md). OTP Harbor does not transfer vault or usage information to project-operated systems. Store packages use Store-managed updates and disable the application-owned GitHub update client. Portable Linux packages contact only the configured signed update feed and artifact URLs during an update check.

## Verification and incident response

For a Microsoft Store installation, users can inspect package trust with:

```powershell
Get-AppxPackage | Where-Object Name -Like '*OtpHarbor*' |
  Select-Object Name, Publisher, Version, SignatureKind
```

An accepted Store package should report `SignatureKind` as `Store`. For a future signed direct-download build, users can inspect an extracted executable with:

```powershell
Get-AuthenticodeSignature .\TOTP.UI.Avalonia.Desktop.exe | Format-List Status,StatusMessage,SignerCertificate
```

The Authenticode status must be `Valid` and the signer must match the issuer documented for that release. Checksums provide transport-integrity evidence; they do not replace platform signature verification.

Android users and maintainers can inspect a downloaded APK with Android SDK Build Tools:

```powershell
apksigner verify --verbose --print-certs .\OTP-Harbor-android-universal-<version>.apk
```

The certificate SHA-256 digest must match the fingerprint published and pinned for OTP Harbor.

Suspected signing-policy violations, compromised release automation, or malicious artifacts must be reported privately as described in [SECURITY.md](SECURITY.md). Maintainers will stop affected releases, contact the relevant distribution/signing provider, and rotate or revoke affected credentials and trust material when required.
