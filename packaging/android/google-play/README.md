# Google Play listing artwork

The English Google Play artwork is generated from reviewed v3 synthetic-data Android captures by
`scripts/marketing/Generate-MarketingAssets.ps1`. Canonical, manifest-backed files live under
`artifacts/marketing/store/android` and are synchronized into
`fastlane/metadata/android/en-US/images` so F-Droid-compatible tooling and Google Play use the same
reviewed images.

Upload the files from that Fastlane image directory in Play Console under **Grow users > Store presence > Main store
listing > Graphics**:

| Play Console field | File(s) |
| --- | --- |
| App icon | `icon.png` |
| Feature graphic | `featureGraphic.png` |
| Phone screenshots | `phoneScreenshots/01-...` through `phoneScreenshots/06-...-1080x1920.png`, in numeric order |

The six phone images are 24-bit, non-transparent PNGs at 1080×1920. The feature graphic is a
24-bit, non-transparent PNG at 1024×500. The app icon is a 32-bit PNG with alpha at 512×512 and is
kept below 1,024 KB. These dimensions and formats follow Google's current preview-asset guidance:
https://support.google.com/googleplay/android-developer/answer/9866151

Suggested English alt text, each below 140 characters:

1. OTP Harbor account list in Dark theme with fictional demo accounts and project-owned icons.
2. OTP Harbor settings in Dark theme with local security and appearance options.
3. OTP Harbor QR scanner in Dark theme with camera scanning and image-import controls.
4. OTP Harbor account list in Light theme with fictional demo accounts and project-owned icons.
5. OTP Harbor settings in Light theme with local security and appearance options.
6. OTP Harbor QR scanner in Light theme with camera scanning and image-import controls.
