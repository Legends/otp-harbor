# Android release and signing

OTP Harbor ships Android as a separate, installable artifact in the same versioned GitHub Release
as the desktop applications. The APK is not embedded in a desktop installer or archive. A Google
Play Android App Bundle will be produced by a separate submission workflow when Play distribution
is enabled; an AAB is not directly installable and is therefore not a GitHub download for users.

## Permanent application identity

The production application ID is:

```text
io.github.legends.otpharbor
```

This value is a release identity and must not be renamed after public distribution. Debug builds use
`io.github.legends.otpharbor.debug`, which prevents development signing keys and test installations
from occupying or replacing the production package.

Before publishing the first APK, register `io.github.legends.otpharbor` and the production signing
certificate through Android developer verification. If a Play Console account is available, use its
Android developer verification page so the same package can later be published on Google Play.

## Signing-key policy

Android updates require the same application ID and a compatible app-signing certificate. Create
the production app-signing key once, keep at least two encrypted offline backups, and never commit it
to this repository. The key validity must extend beyond the expected lifetime of the application.

For a future Google Play launch, choose the option to provide the existing app-signing key to Play
App Signing. Using the same app-signing key keeps GitHub APKs and Play-delivered APKs on the same app identity and allows users
to move between channels without uninstalling. After enrollment, create a separate Play upload key
for AAB submissions; do not replace the app-signing key used for GitHub APKs.

Generate the app-signing key on a trusted offline or maintainer-controlled machine, for example:

```powershell
keytool -genkeypair -v `
  -keystore otp-harbor-android-signing.jks `
  -alias otp-harbor `
  -keyalg RSA `
  -keysize 4096 `
  -validity 25000
```

Record its SHA-256 certificate fingerprint:

```powershell
keytool -list -v -keystore otp-harbor-android-signing.jks -alias otp-harbor
```

## GitHub release environment

Create a GitHub Actions environment named `android-release`. Require reviewer approval for release
jobs, then configure these environment values:

| Kind | Name | Value |
| --- | --- | --- |
| Secret | `ANDROID_SIGNING_KEYSTORE_BASE64` | Base64 encoding of the production `.jks` file |
| Secret | `ANDROID_SIGNING_KEY_ALIAS` | Alias of the app-signing key |
| Secret | `ANDROID_SIGNING_STORE_PASSWORD` | Keystore password |
| Secret | `ANDROID_SIGNING_KEY_PASSWORD` | Private-key password |
| Variable | `ANDROID_SIGNING_CERTIFICATE_SHA256` | Expected SHA-256 certificate fingerprint |

Create the Base64 value without writing another unencrypted key copy:

```powershell
[Convert]::ToBase64String(
  [IO.File]::ReadAllBytes('otp-harbor-android-signing.jks')) |
  Set-Clipboard
```

The release workflow reconstructs the key only in the runner's temporary directory, supplies
passwords through temporary files rather than command-line values, verifies the APK signature, and
rejects any certificate that differs from the pinned fingerprint. Certificate parsing accepts the
continuous and separator-formatted SHA-256 output emitted by supported `apksigner` versions, but
fails closed unless exactly one signer fingerprint is present.

## Versioning

The visible Android version matches the Git tag without its `v` prefix. Android's integer
`versionCode` is mapped deterministically by `scripts/release/Get-AndroidReleaseVersion.ps1`:

```text
major * 10,000,000 + minor * 100,000 + patch * 100 + qualifier
```

Release candidates use qualifier `1` through `98`; stable releases use `99`. This guarantees that
every stable build supersedes its release candidates and that later semantic versions remain newer.
Google Play's maximum version code is enforced by the script.

## First-public-release checklist

1. Create and back up the production app-signing key.
2. Register the package name and signing certificate through Android developer verification.
3. Configure and protect the `android-release` GitHub environment.
4. Run the Android CI build and install its signed candidate on a clean device.
5. Verify account creation, QR import, encrypted export/import, biometric unlock, background lock,
   and an in-place upgrade from the previous signed APK.
6. Publish the shared release tag. Download and independently verify the APK fingerprint and SHA-256.

Development builds previously installed under `io.github.legends.otpharbor` used a development key
and cannot be upgraded to the production-signed package. Export any needed test vault first, then
uninstall the old development package. New debug builds use the `.debug` suffix and can coexist with
the production application.

## Security review

- **Threat impact:** compromise of the app-signing key would permit a malicious package to replace
  OTP Harbor on devices. The key is excluded from source control, gated by a reviewed GitHub
  environment, materialized only temporarily, and pinned by its public SHA-256 fingerprint.
- **Data-flow impact:** packaging processes compiled application files and signing material only; it
  does not read, migrate, upload, or alter vaults, OTP seeds, passwords, or backups.
- **Compatibility impact:** release builds permanently use `io.github.legends.otpharbor`; debug
  builds use `.debug`. Existing packages signed with another key cannot be upgraded in place.
- **Verification evidence:** CI compiles the dedicated Android solution. Release packaging verifies
  the APK signature, application ID, visible version, version code, certificate fingerprint, file
  hash, and inclusion in the signed aggregate release manifest. Deterministic validation covers
  continuous and colon-separated certificate output and rejects missing or multiple signers.
