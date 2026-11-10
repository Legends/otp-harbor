# v3 marketing release readiness

This is the local preparation state. It is not approval to publish the website, a release, or a Microsoft Store submission.

## Capture inventory

| Platform | Theme | Genuine captures | Reviewed website gallery screens |
| --- | --- | ---: | --- |
| Desktop | Light | 8 | Account list, account editor, settings, Imports |
| Desktop | Dark | 9 | Account list, account editor, context menu, settings, Imports |
| Android | Light | 7 | Account list, account editor, create group, settings |
| Android | Dark | 7 | Account list, account editor, create group, edit swipe, delete swipe, settings |

The canonical captures are under `source/`; the website candidate has 18 cleaned captures plus 20 designed blue-background frames under `website/optimized/`. The gallery points to the framed images, with Desktop/Dark first. Light/Dark comparisons for Desktop and Android are under `composites/`. Android comparisons remove the phone status and navigation bars from both genuine captures; no app UI is changed. `manifest.json` records the source and generated hashes, dimensions, platform, theme, app placements and Store plans.

Four scanner QR composites encode `https://legends.github.io/otp-harbor/` and pass decode validation. Two Windows Security cutouts have transparent backgrounds and preserve prompt pixels from the user's captures. All six are clearly marked as composites, excluded from the Store gallery. The Desktop scanner names a third-party authenticator, and the biometric cutouts display Windows Security branding, so those four are review-only under the fictional-only marketing rule.

The Android Light swipe captures show partially drawn icons and are excluded from the gallery. Older Desktop Light screenshots showed retired demo names and are excluded. The discarded Android Dark account-list capture contained a visible Loading state; it was replaced by the user's clean 21:55 capture.

Feature coverage still lacks genuine v3 captures of Desktop search, icon management and security screens, and Android import/export, backup/restore and a safe biometric unlock state. The 31 Desktop and Android captures now include Back & Restore and Imports in both Desktop themes; no missing feature screen is fabricated in the gallery.

## Store package state

The optional `store/windows/icons/otp-harbor-tile-300.png` is generated from the application's own 1024-pixel icon. `manifest.json` lists recommended Desktop screenshot slots, captions and current readiness. The fixed-width app produced 950-pixel account lists and 1300-pixel settings windows. Conservative Store gallery candidates use those exact captured app pixels centered on neutral frames of at least 1366 × 768; the app UI is never upscaled or repainted. A separate branded campaign set follows the current published Store artwork style: ten 1920 × 1080 slides in each of English, German, French and Spanish under `store/windows/promotional/`. The Dark account list leads, Light is shown in the retained theme comparison, and no Light-only opener remains. The campaign includes an actual Windows Security quick-unlock prompt cutout and a documented QR demonstration. Slides 08–11 use repo-owned OTP Harbor onboarding illustrations for groups/favourites, icon packs, backup/restore, and account addition with verified import formats. They are marked conceptual promotions rather than app screenshots. Rounded masks on the genuine full-window captures remove foreign pixels from outside app corners while preserving the interior. The `kind`, source hashes, crops and placements in the manifest distinguish designed images from genuine unmodified screenshots.

Android submission candidates live under `store/android`: eight 1920 × 1080 Dark/Light website campaign images, six 1080 × 1920 Play/Fastlane phone screenshots, a 1024 × 500 feature graphic and a 512 × 512 high-resolution icon. The packaging and Fastlane copies are synchronized by hash. The backup/restore campaign uses a labeled project-owned concept illustration; the scanner uses a labeled demonstration QR. All candidates pass technical dimensions but still require full-resolution visual, legal and store-policy review.

## Automated verification

- `Generate-MarketingAssets.ps1` generated both comparisons, website files, the Store tile icon, 40 localized Windows campaign candidates and 16 Android submission images, then synchronized hash-identical packaging and Fastlane copies.
- `Validate-MarketingAssets.ps1 -RequireCaptures` passed with 26 synthetic accounts, 26 distinct original icon assets and 31 genuine captures. It checks provenance hashes, aspect and size, metadata, gallery references, QR payload and transparent alpha.
- `Test-PublicWebsite.ps1` passed. Both gallery JavaScript files passed `node --check`.
- Repeated generation produced the same manifest hash, confirming deterministic output for the current inputs.

## Manual release blockers

1. Have a qualified human review the 26 fictional names and original marks for trademark conflicts and confirm graphic ownership. The preliminary exact-name screen is documented in `TRADEMARK_REVIEW.md`; it is not legal clearance.
2. Inspect every gallery image at full size for fictional accounts, icon consistency, no personal information, correct theme, no rendering artifacts and accessible alt text. The empty account-editor screens include ordinary functional placeholder examples, which the user explicitly accepted; no demo account or icon uses a real service identity.
3. Manually review the neutral framed Windows Store candidates and all branded campaign candidates. Slides 08–11 use conceptual OTP Harbor onboarding artwork and must not be represented as actual UI captures. The branded set matches the style currently published in the Store, but prior acceptance does not guarantee acceptance of these v3 files. The app itself has a fixed width; no window resizing is required.
4. Set `publicationStatus` in `manifest.json` to `approved for website publication` only after human visual/legal approval and the required capture validation. The Pages workflow otherwise keeps the existing published campaign images as fallback and does not copy the v3 gallery.
5. Confirm the exact v3 release version and public availability from release and Store evidence before changing download claims. Do not publish, submit to Partner Center or release from this preparation branch without the user's authorization.
