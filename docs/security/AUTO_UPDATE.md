# Automatic Update Setup

OTP Harbor's portable Linux package uses an Ed25519-signed, target-qualified `appcast-v2.xml`. Windows is distributed through Microsoft Store, where Store certification supplies the package signature; Ed25519 package signatures do not replace operating-system trust.

Linux DEB and Microsoft Store builds are stamped as externally managed and do not use application-owned updates. Microsoft Store is the primary Windows distribution channel; Store certification supplies the package signature and the Store owns update delivery.

After the authorized desktop shell opens, a portable Linux package performs one best-effort signed-feed check. An available update is announced in the main notification area and can be reviewed under Settings → About. Feed failures do not disturb vault use, and no package is downloaded without a separate user action. Store and package-manager builds return `Disabled` before any application-owned network request.

## Brand migration compatibility

The OTP Harbor rebrand changes the GitHub repository URL, product metadata, package display names, and future release-asset names. It does not change the Ed25519 public key, appcast schema, channel policy, verification order, or update-installation trust boundaries. The release-manifest generator accepts both current `OTP-Harbor` and legacy `TOTP-Manager` asset names so previously published artifacts remain verifiable. Existing installations may follow GitHub's repository redirect to the renamed repository, while newly built packages use the canonical `Legends/otp-harbor` feed URL.

## Trust model

- The client accepts only `appcast-v2.xml` entries whose OS, architecture, channel, and package policy match the running package.
- Every direct payload, the release manifest, and the appcast are signed with the configured NetSparkle Ed25519 key.
- Microsoft Store Windows packages require successful Store certification and a Store signature. Any future stable direct-download Windows executable requires independently verified Authenticode signing.
- macOS distribution remains withheld until Developer ID signing, notarization, and physical acceptance are available.
- Stable GitHub releases contain no unsigned Windows binaries. Historical RC clients read `https://legends.github.io/otp-harbor/updates/rc/appcast-v2.xml` only so they can advance to a verified stable release.

## Generate Ed25519 keys

Install the pinned tool:

```powershell
dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator --version 2.9.0
```

Generate keys:

```powershell
netsparkle-generate-appcast --generate-keys
```

Keep `NetSparkle_Ed25519.pub` and `NetSparkle_Ed25519.priv` together in a protected directory. Commit neither file. The public key configured in `TOTP.UI.Avalonia.Desktop/appsettings.json` must exactly match `NETSPARKLE_PUBLIC_KEY` in CI.

## Configure a development feed

Use user-secrets on the Avalonia desktop project:

```powershell
dotnet user-secrets set "AutoUpdate:Enabled" "true" --project TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj
dotnet user-secrets set "AutoUpdate:AppcastUrl" "https://example.com/appcast-v2.xml" --project TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj
dotnet user-secrets set "AutoUpdate:PublicKey" "<your-public-key>" --project TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj
```

Run the client with:

```powershell
dotnet run --project TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj
```

Do not enable application-owned updates in DEB, Microsoft Store, or other externally managed packages.

## Generate and validate the portable appcast

The release workflow first creates and validates `release-artifacts-v2.json`, then runs:

```powershell
./scripts/release/Generate-AvaloniaAppcast.ps1 `
  -ManifestPath ./release-assets/release-artifacts-v2.json `
  -ArtifactDirectory ./release-assets `
  -BaseDownloadUrl "https://github.com/<owner>/<repo>/releases/download/<tag>/" `
  -PrivateKeyPath "C:\secure\NetSparkle_Ed25519.priv" `
  -PublicKeyPath "C:\secure\NetSparkle_Ed25519.pub" `
  -ExpectedPublicKey "<your-public-key>" `
  -OutputDirectory ./release-feed

./scripts/release/Test-AvaloniaAppcast.ps1 `
  -AppcastPath ./release-feed/appcast-v2.xml `
  -ManifestPath ./release-assets/release-artifacts-v2.json `
  -BaseDownloadUrl "https://github.com/<owner>/<repo>/releases/download/<tag>/"
```

The private key path is supplied to tooling; private key contents must never appear in process arguments or logs.

## Required CI secrets

- `NETSPARKLE_PUBLIC_KEY`
- `NETSPARKLE_PRIVATE_KEY`

The active Store packaging workflow requires no certificate secret and produces an unsigned Partner Center input that must never be directly distributed. Stable desktop GitHub releases publish Linux packages, integrity metadata, and source archives; independent Android releases publish the production-signed APK. Neither publishes Windows binaries. Dormant SignPath controls remain gated by `SIGNPATH_PRODUCTION_ENABLED` and the requirements in [SIGNPATH_FOUNDATION_ONBOARDING.md](SIGNPATH_FOUNDATION_ONBOARDING.md). Reapplication is deferred until public stars and verified download/adoption signals are materially stronger.

## Release behavior

For a stable GitHub release, CI:

1. Builds and tests all supported projects.
2. Produces the internal Windows payload for the Partner Center MSIX plus public Linux packages.
3. Signs eligible Linux update metadata and the desktop release manifest.
4. Generates and verifies `appcast-v2.xml`.
5. Uploads the complete desktop asset set to a draft and publishes it only after validation succeeds.

For an independent `android-v` release, CI validates the shared code and Android graph, builds and
verifies the production-signed APK, creates Android-specific integrity metadata, and publishes only
those Android assets after draft validation.

After publishing a stable GitHub release, CI requests a website deployment. That deployment verifies the selected published appcast against the public key embedded in the client and mirrors it at the legacy RC endpoint so existing preview installations can advance to stable. Release assets remain immutable; the public endpoint is only a signed-feed pointer.

## Verified installation handoff

The desktop client owns update discovery, download progress, release notes, and explicit installation consent. A check never downloads a package, and a completed download never starts installation without a separate user action.

On Windows, `WindowsUpdateInstallerLauncher` hands a verified ZIP to the dedicated `TOTP.Updater` helper:

1. Accept only a regular ZIP within the portable 128 MiB limit.
2. Hold the package without write/delete sharing and repeat Ed25519 verification.
3. Reject reparse points in the bundled updater runtime.
4. Copy the trusted helper runtime into a fresh current-user temporary directory.
5. Start it with arguments supplied through `ProcessStartInfo.ArgumentList`.
6. Wait for its ready signal before requesting graceful Avalonia shutdown.

The helper stages the archive, backs up overwritten files, applies replacements with bounded retry handling, rolls back in reverse order on failure or cancellation, and relaunches the updated application after success. Incomplete rollback is a distinct failure and is never reported as success. Non-secret helper diagnostics are written to `%TEMP%\totp-update-helper.log`.

Linux package-manager builds disable application-owned updates. Direct Linux and macOS packages may verify and download matching artifacts but retain a manual platform handoff until a dedicated installer adapter is approved.

See [SIGNING_KEY_ROTATION.md](SIGNING_KEY_ROTATION.md) for rotation procedures.

## Incident response

If an Ed25519 private key or platform certificate is exposed, stop publishing, revoke/rotate the affected credential, update the embedded trust material through a reviewed release, and document the impact. Never weaken signature verification to recover from a rotation failure.
