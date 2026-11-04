# Android v3 marketing images

This folder contains the committed Android campaign mirrored to
`https://legends.github.io/otp-harbor/android/`. The canonical, manifest-backed derivatives live
under `artifacts/marketing/store/android`; do not edit the committed mirrors directly.

## Synthetic capture data

All captures must use the deterministic fictional roster and project-owned geometric icons from
`artifacts/marketing/demo-data`. Never capture a real account, OTP seed, backup password,
notification, device identifier, or personal file name. The reviewed source captures are stored in
`artifacts/marketing/source/android/<theme>` and their hashes are recorded in
`artifacts/marketing/manifest.json`.

The QR scanner campaign uses a labeled demonstration QR that resolves only to the OTP Harbor
GitHub repository. It is not an OTP account or migration payload.

## v3 campaign set

The English website set contains eight opaque 1920×1080 PNGs:

1. `01-dark-account-list.png`
2. `02-dark-settings.png`
3. `03-dark-qr-scanning.png`
4. `04-dark-backup-restore.png`
5. `05-light-account-list.png`
6. `06-light-settings.png`
7. `07-light-qr-scanning.png`
8. `08-light-backup-restore.png`

The account-list, settings, and scanner visuals contain genuine app pixels. Backup/restore is a
labeled marketing composition that combines a genuine settings capture with project-owned
onboarding artwork; it must not be described as a literal backup screen.

The shared Play/Fastlane set contains six 1080×1920 phone screenshots—Dark and Light account list,
settings, and scanner—plus the 1024×500 feature graphic and 512×512 high-resolution icon. All are
generated from the same reviewed sources.

## Regeneration

From the repository root, run:

```powershell
./scripts/marketing/Generate-MarketingAssets.ps1 -Platform android
```

The workflow generates and validates the canonical derivatives, synchronizes this folder and
`fastlane/metadata/android/en-US/images`, removes stale v2 files from those destinations, and
verifies every mirror by SHA-256. The reusable background source remains at
`source/android-marketing-background.png`; the obsolete v2 `source/captures` set is intentionally
not used.

## Isolated capture build

Never relax screen-capture protection in a release package. Create new reviewed source captures
only with the separate debug application ID and explicit capture flag:

```powershell
dotnet build OtpHarbor.UI.Avalonia.Android/OtpHarbor.UI.Avalonia.Android.csproj `
  -c Debug -p:EnableMarketingCapture=true
```

This defines `OTP_HARBOR_MARKETING_CAPTURE` only for Debug. Normal Debug and every Release build
retain Android secure-window protection for secret-bearing screens.
