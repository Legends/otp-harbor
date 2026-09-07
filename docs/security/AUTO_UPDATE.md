# Automatic Update Setup

OTP Harbor's direct Avalonia packages use an Ed25519-signed, target-qualified `appcast-v2.xml`. Windows and macOS releases also require platform signing/notarization; Ed25519 package signatures do not replace operating-system trust.

Linux DEB and Microsoft Store builds are stamped as externally managed and do not use application-owned updates. Microsoft Store is the primary Windows distribution channel; Store certification supplies the package signature and the Store owns update delivery.

After the authorized desktop shell opens, a GitHub direct package performs one best-effort signed-feed check. An available update is announced in the main notification area and can be reviewed under Settings → About. Feed failures do not disturb vault use, and no package is downloaded without a separate user action. Store and package-manager builds return `Disabled` before any application-owned network request.

## Brand migration compatibility

The OTP Harbor rebrand changes the GitHub repository URL, product metadata, package display names, and future release-asset names. It does not change the Ed25519 public key, appcast schema, channel policy, verification order, or update-installation trust boundaries. The release-manifest generator accepts both current `OTP-Harbor` and legacy `TOTP-Manager` asset names so previously published artifacts remain verifiable. Existing installations may follow GitHub's repository redirect to the renamed repository, while newly built packages use the canonical `Legends/otp-harbor` feed URL.

## Trust model

- The client accepts only `appcast-v2.xml` entries whose OS, architecture, channel, and package policy match the running package.
- Every direct payload, the release manifest, and the appcast are signed with the configured NetSparkle Ed25519 key.
- Microsoft Store Windows packages require successful Store certification and a Store signature. Any future stable direct-download Windows executable requires independently verified Authenticode signing.
- Stable macOS artifacts require Developer ID signing and notarization.
- RC Windows executables remain unsigned at the operating-system level, but direct Windows and portable Linux RC packages use the same Ed25519 payload/appcast trust boundary as other GitHub direct packages. The first RC download remains an explicitly labeled preview and must be verified independently.
- RC clients read `https://legends.github.io/otp-harbor/updates/rc/appcast-v2.xml`. The Pages workflow mirrors only the highest published signed feed after verifying its Ed25519 signature; RC clients accept a newer RC or stable entry.

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
- macOS Developer ID/notarization secrets documented by the release workflow

The active Store packaging workflow requires no certificate secret and produces an unsigned Partner Center input that must never be directly distributed. The optional stable direct-download workflow retains dormant SignPath controls documented in [SIGNPATH_FOUNDATION_ONBOARDING.md](SIGNPATH_FOUNDATION_ONBOARDING.md), but there is no Foundation certificate or active SignPath production configuration. That path fails closed without an approved provider configuration. RC tags publish explicitly labeled Windows/Linux previews; their Windows executables are not Authenticode-signed, while eligible direct artifacts and update metadata require the configured NetSparkle Ed25519 credentials.

## Release behavior

For a GitHub direct release, CI:

1. Builds and tests all supported projects.
2. Produces target-qualified Avalonia packages.
3. Applies platform signatures where required; RC Windows previews remain explicitly unsigned at this layer.
4. Signs every direct payload and the aggregate release manifest.
5. Generates and verifies `appcast-v2.xml`.
6. Uploads the complete asset set to a draft and publishes it only after validation succeeds.

After publishing either an RC or stable GitHub release, CI requests a website deployment. That deployment selects the highest versioned published release containing both appcast files, verifies the appcast against the public key embedded in the client, and publishes it at the stable RC endpoint. Release assets remain immutable; the public endpoint is only a signed-feed pointer.

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
