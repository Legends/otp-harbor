# Code signing policy

**Windows code-signing status:** The previous SignPath Foundation application was not approved at this stage. A future reapplication may be considered after the project has established broader public adoption and independent trust signals. Current GitHub preview builds are unsigned. Microsoft Store is the planned primary Windows channel; Microsoft signs an accepted Store package during certification.

This policy covers the two distinct Windows distribution paths. They must never be presented as interchangeable:

- **Microsoft Store (primary):** CI creates an unsigned MSIX solely for Partner Center. Microsoft signs it after successful certification and the Store manages updates.
- **GitHub (secondary):** source code and explicitly labeled previews. Direct Windows and portable Linux packages use an Ed25519-signed appcast; current Windows RC executables remain unsigned at the operating-system level. A future stable Windows direct-download channel remains blocked unless an independent Authenticode trust path is approved and verified.

An unsigned Store submission MSIX is not a sideloading artifact. It must not be attached to a GitHub Release, linked as an installer, or described as trusted before Store certification.

## Team roles

- Committer and reviewer: [Legends](https://github.com/Legends)
- Approver: [Legends](https://github.com/Legends)

Changes from contributors who do not have commit access require maintainer review before merge. Participation in source control, Partner Center, or any future signing service requires multi-factor authentication. Store submissions and any future direct-package signing requests require explicit maintainer approval.

## Build and signing controls

- Release binaries are built from this public repository by the tag-triggered GitHub Actions workflow on GitHub-hosted runners.
- Release versions use the documented `v<major>.<minor>.<patch>` format. Microsoft Store package versions use four components and reserve the fourth component as `0`.
- The Store package is built from this public repository with the exact case-sensitive identity supplied by Partner Center. Placeholder CI identities are smoke-test inputs only.
- Store packages set `DistributionMode` to `store`, disable application-owned updates, and exclude the standalone updater.
- GitHub direct packages set `DistributionMode` to `direct`. Stable packages use the stable GitHub Release appcast; RC packages use the signed public RC feed and may advance to a newer RC or stable release.
- The public RC endpoint mirrors only the highest versioned published release whose appcast signature verifies against the client-embedded Ed25519 key. It never signs or modifies release metadata.
- The generated unsigned MSIX and its SHA-256 metadata are retained only for the controlled Partner Center handoff.
- The Store package is published only after certification plus physical acceptance of install, launch, Windows Hello, QR scanning, encrypted backup/restore, lock behavior, and Store-managed updates.
- Any future direct-download signing integration must bind the artifact to its GitHub workflow run and source commit, expose no certificate private key to the repository, sign only reviewed first-party binaries, and verify product metadata plus Authenticode status before publication.
- Published release tags are immutable and must not be moved or deleted to replace artifacts.

Release engineering files are owned through [CODEOWNERS](.github/CODEOWNERS). Changes to the workflow, signing policy, release scripts, or update trust configuration require security-focused review.

## Privacy

See the [privacy policy](PRIVACY.md). OTP Harbor does not transfer vault or usage information to project-operated systems. Store packages use Store-managed updates and disable the application-owned GitHub update client. GitHub direct packages contact only the configured signed update feed and artifact URLs during an update check.

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

Suspected signing-policy violations, compromised release automation, or malicious artifacts must be reported privately as described in [SECURITY.md](SECURITY.md). Maintainers will stop affected releases, contact the relevant distribution/signing provider, and rotate or revoke affected credentials and trust material when required.
