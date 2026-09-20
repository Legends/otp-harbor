# OTP Harbor

**The local-first authenticator for desktop and Android.**

[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux%20%7C%20Android-5C6BC0)](https://github.com/Legends/otp-harbor)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Build](https://github.com/Legends/otp-harbor/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/Legends/otp-harbor/actions/workflows/build-and-test.yml)
[![Security](https://github.com/Legends/otp-harbor/actions/workflows/security-audit.yml/badge.svg)](https://github.com/Legends/otp-harbor/actions/workflows/security-audit.yml)
[![Microsoft Store version](https://img.shields.io/badge/Microsoft%20Store-2.0.1-0078D4?logo=microsoft)](https://apps.microsoft.com/detail/9P31KH5L924P)
[![License](https://img.shields.io/github/license/Legends/otp-harbor)](LICENSE.txt)

**OTP Harbor** is an open-source, local-first TOTP and 2FA authenticator for Windows, macOS, Linux, and Android. It protects OTP seeds in an encrypted local vault and supports QR workflows, platform quick unlock, and encrypted backup and restore without requiring a cloud account.

> **Release status:** OTP Harbor `v2.0.1` is the stable desktop GitHub release. Windows is delivered only through the [Microsoft Store](https://apps.microsoft.com/detail/9P31KH5L924P), where OTP Harbor `2.0.1` is publicly available and Microsoft signs accepted MSIX packages. The [latest desktop release](https://github.com/Legends/otp-harbor/releases/latest) provides Linux packages, integrity metadata, and source archives. Production-signed APKs use [independent Android releases](https://github.com/Legends/otp-harbor/releases?q=android-v) so mobile and desktop versions can ship separately.

<p align="center">
  <img src="packaging/windows-store/screenshots/en-US/marketing/01-local-vault.png" alt="OTP Harbor for Windows with an encrypted local TOTP vault and synthetic sample accounts" width="960" />
</p>

## Features

- AES-256-GCM encrypted local vault with Argon2id password derivation
- Windows Hello and macOS quick unlock with master-password recovery
- Account creation, editing, search, deletion, QR import/export, Google Authenticator bulk migration, and local `otpauth://` URI-list import
- Automatic clipboard clearing and idle/session locking
- Encrypted `.totp` backup, restore, and conflict handling
- English, German, French, and Spanish UI
- Native Avalonia desktop application for Windows, macOS, and Linux
- Focused Android app with strong-biometric, device-screen-lock (PIN, pattern, or password), or master-password unlock,
  camera-based QR import, swipe actions, and the
  same encrypted backup format as desktop

The public [Android product page](https://legends.github.io/otp-harbor/android/) presents the mobile edition, while the dedicated [Android user guide](https://legends.github.io/otp-harbor/android/guide/) explains first-run setup, tap-to-copy, left/right swipe actions, camera and Google Authenticator QR workflows, per-account QR display, encrypted backup conflict handling, unlock-method recovery, and the optional app-lock setting.

Accounts use the common TOTP profile by default: SHA-1, six digits, and a 30-second code period. The period can be adjusted between 5 and 3600 seconds under **Advanced options** when adding or editing an account, if the provider requires a value other than 30 seconds. For example, a 10-minute period is entered as 600 seconds.

On desktop, right-click any existing account to open its contextual actions: **Edit account**, **Show QR code**, or **Delete account**. Deletion still requires confirmation.

### Import from Google Authenticator

OTP Harbor recognizes both normal `otpauth://` account QR codes and Google Authenticator transfer QR codes. For a desktop migration, start **Transfer accounts** / **Export accounts** in Google Authenticator, capture each generated QR code as a crisp screenshot, and choose each saved image in **Settings > Import / Export > Import from Google Authenticator**. OTP Harbor shows the number of detected accounts and asks for confirmation before changing the vault; multi-part exports prompt you to choose the next QR image. Android can scan the transfer QR codes directly with its camera workflow.

Treat migration QR codes as secrets: anyone who captures one can recreate the exported accounts. After importing, verify several generated codes and create a fresh encrypted OTP Harbor backup.

Desktop and Android can also import a local `.txt` file containing one standard `otpauth://` TOTP URI per line. JSON, CSV, and OTP Harbor text account files remain supported. Imports are parsed locally, previewed before mutation, and protected by the same recovery-backup and conflict-resolution workflow. Treat every unencrypted account file as secret material and remove it from shared storage after verifying the migration.

## Desktop experience

<table>
  <tr>
    <td><img src="packaging/windows-store/screenshots/en-US/marketing/02-search-and-copy.png" alt="OTP Harbor instant account search and one-click TOTP copy workflow" /></td>
    <td><img src="packaging/windows-store/screenshots/en-US/marketing/03-add-and-import.png" alt="OTP Harbor account creation and QR-code import workflow" /></td>
  </tr>
  <tr>
    <td align="center"><strong>Find and copy codes quickly</strong></td>
    <td align="center"><strong>Add accounts or import QR codes</strong></td>
  </tr>
  <tr>
    <td><img src="packaging/windows-store/screenshots/en-US/marketing/04-windows-hello.png" alt="OTP Harbor quick unlock protected by Windows Hello with master-password recovery" /></td>
    <td><img src="packaging/windows-store/screenshots/en-US/marketing/05-import-export.png" alt="OTP Harbor Google Authenticator import and encrypted backup export workflows" /></td>
  </tr>
  <tr>
    <td align="center"><strong>Unlock securely with Windows Hello</strong></td>
    <td align="center"><strong>Migrate and back up your vault</strong></td>
  </tr>
</table>

All accounts, codes, and QR payloads shown in the marketing artwork are synthetic. Never publish screenshots containing a real account secret.

### Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Search accounts | <kbd>Ctrl</kbd> + <kbd>F</kbd> |
| Copy the selected account code | <kbd>Ctrl</kbd> + <kbd>C</kbd> |
| Add an account | <kbd>Ctrl</kbd> + <kbd>A</kbd> |
| Edit the selected account | <kbd>Ctrl</kbd> + <kbd>E</kbd> |
| Delete the selected account after confirmation | <kbd>Ctrl</kbd> + <kbd>D</kbd> or <kbd>Delete</kbd> |
| Lock the vault | <kbd>Ctrl</kbd> + <kbd>L</kbd> |
| Close the active search, editor, settings view, or QR preview | <kbd>Esc</kbd> |

## Distribution

**[Microsoft Store](https://apps.microsoft.com/detail/9P31KH5L924P) is the primary Windows distribution channel** (Store ID `9P31KH5L924P`). Microsoft signs the published MSIX and manages Store updates.

[GitHub Releases](https://github.com/Legends/otp-harbor/releases/latest) provides stable Linux downloads and source archives. [Android releases](https://github.com/Legends/otp-harbor/releases?q=android-v) independently provide production-signed APKs. Portable Linux packages use an Ed25519-signed appcast; Microsoft Store packages rely only on Store-managed updates.

| Platform | Package type |
| --- | --- |
| Windows 10/11 x64 | Public Microsoft Store MSIX only; Microsoft signs accepted packages and manages updates |
| Ubuntu 24.04 x64 | DEB or self-contained tarball |
| macOS ARM64 | Structural artifacts are built in CI; production distribution still requires signing and notarization |
| Android 9 or newer | Production-signed universal APK from an independent `android-vX.Y.Z` GitHub release; manual installation with in-place upgrade support |

After launch, create a master password and add an account manually, scan an `otpauth://` QR code, or import each saved QR image from a Google Authenticator bulk export in sequence. Treat QR images, OTPs, seeds, exports, and backups as secrets.

Maintainers can follow the [Microsoft Store release guide](docs/release/MICROSOFT_STORE.md) and [Android release guide](docs/release/ANDROID.md). The unsigned MSIX produced by the repository is exclusively a Partner Center submission input and must never be sideloaded or attached to a GitHub Release.

## Security and recovery

The master password is the portable recovery path. Quick unlock is a convenience and never replaces it. Keep an external encrypted export and test restoration periodically.

- [Security policy](SECURITY.md)
- [Threat model](docs/security/THREAT_MODEL.md)
- [Security verification](docs/security/SECURITY_VERIFICATION.md)
- [Recovery guide](docs/RECOVERY.md)
- [Privacy policy](PRIVACY.md)

Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md). Never attach real secrets, vaults, backups, or unreviewed logs.

## Code signing policy

**Windows code-signing status:** The previous SignPath Foundation application was not approved at this stage. Reapplication is deferred until the project has materially stronger public adoption signals, including GitHub stars and verified download usage. Windows is therefore distributed only through Microsoft Store certification; stable GitHub releases do not attach unsigned Windows binaries.

The Store and optional future direct-download trust models are defined in the [code signing policy](CODE_SIGNING_POLICY.md). Data handling is described in the [OTP Harbor privacy policy](PRIVACY.md).

## Build

Install the .NET 10 SDK, then run:

```powershell
git clone https://github.com/Legends/otp-harbor.git
cd otp-harbor
dotnet restore TOTP.sln --configfile NuGet.config
dotnet build TOTP.sln -c Debug
dotnet test TOTP.sln -c Debug
dotnet run --project .\TOTP.UI.Avalonia.Desktop\TOTP.UI.Avalonia.Desktop.csproj
```

### Keep a source build current

Users who intentionally run OTP Harbor from source can use the repository launcher:

```powershell
.\Start-OTP-Harbor.ps1
```

The launcher checks the official `origin/master` branch on every invocation. When a newer commit exists, it applies only a fast-forward update, restores dependencies, compiles the desktop app in Release mode, and starts it. When the source revision and the launcher's verified build are already current, it starts that build without compiling again. Git and the .NET 10 SDK are required.

For safety, the launcher refuses to update a modified, ahead, or diverged checkout and never discards local files. It is a convenience for source users, not a signed binary update channel; Microsoft Store installations use Store updates, while portable Linux packages use the signed appcast.

The Android host remains in the dedicated [Android solution](TOTP.Android.sln), so desktop-only development does not require the Android workload. Stable `android-vX.Y.Z` tags publish its signed APK without republishing desktop artifacts. See the [Android development guide](docs/android/FOUNDATION.md) for its implemented scope, security notes, and build commands.

See [CONTRIBUTING.md](CONTRIBUTING.md) for engineering rules and [docs/README.md](docs/README.md) for the maintained documentation map.

## License

OTP Harbor is distributed under [MIT](LICENSE.txt). Third-party notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
