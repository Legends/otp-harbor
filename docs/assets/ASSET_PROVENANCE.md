# Project asset provenance

This record covers every versioned visual-media file in the repository, including application assets, public artwork, store material, screenshots, videos, and test fixtures. Their origins and review status are described below; this record does not purport to license third-party names or interface elements shown solely for compatibility documentation. No downloaded third-party brand artwork is included.

## Application icon

The application icon was generated specifically for the project, now named OTP Harbor, on 2026-08-29 using OpenAI's built-in image-generation tool at the maintainer's request. It was generated without an input or reference image. [`Generate-AppIcon.ps1`](../../scripts/assets/Generate-AppIcon.ps1) resizes a supplied master with high-quality bicubic interpolation and packages independently resized PNG frames at 16, 24, 32, 48, 64, 128, and 256 pixels in the Windows ICO. The production outputs are retained below; discarded candidates remain available in Git history.

Final generation prompt:

> Use case: logo-brand. Asset type: square desktop application icon candidate. Create an original minimal symbol for a secure TOTP authenticator, combining a circular six-segment one-time-code ring with a small shield cutout. Use a crisp, flat, vector-like, geometric, modern native desktop software identity; a single centered mark; a strong silhouette; generous padding; and forms readable at 16x16 pixels. Use deep navy `#0C1C33`, violet `#7D7FF4`, a restrained bright cyan accent, and white only for small negative-space separation. Use a genuinely transparent background. Include no text, letters, digits, watermark, mockup, rounded-square container, 3D rendering, photorealism, padlock cliché, excessive detail, or thin fragile lines.

The generated image was visually reviewed for prohibited text, third-party branding, and small-size legibility before conversion.

The static logo paths in `OtpHarbor.UI.Avalonia.Android/Resources/drawable-v31/splash_icon_animated.xml` are a hand-traced vector rendition of this project-owned icon, added on 2026-11-05 for Android's animated-vector splash format. Only the three surrounding geometric stars rotate. No third-party artwork was used.

## Biometric unlock artwork

`biometric-fingerprint-master.svg` and `biometric-lock-screen-master.svg` are the unmodified
Recraft exports supplied by the maintainer on 2026-09-30 for OTP Harbor's biometric unlock and
marketing presentation. Their embedded provenance metadata is retained in the repository. The
runtime PNGs under `OtpHarbor.UI.Avalonia.Shared/Assets/Biometric` are project-created derivatives:
generated placeholder text was removed, a light-theme treatment was produced, the canonical
OTP Harbor mark was composited where required, and the interactive fingerprint was separated from
the static background to keep unlock rendering efficient. The masters and derivatives were
visually reviewed for unrelated branding, personal data, and unexpected text before inclusion.

## Onboarding artwork

The five dark desktop onboarding illustrations under
`OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding` were generated specifically for OTP Harbor on
2026-10-12 and 2026-10-13 with OpenAI's built-in image-generation tool. The corresponding
`*-light.png` variants were generated on 2026-10-13 with the same tool, using only project-owned
dark artwork as composition references. Dedicated mobile portrait variants were generated on
2026-10-13 from the same project-owned references. Every mobile slide has separate light and dark
portrait artwork; Default and Light select the light assets, while Dark and High Contrast select
the dark assets.
The organize slide now depicts grouping and favorites, while the separate icon-pack slide depicts
importing a local icon pack. All variants use only generic geometric icon tiles and abstract UI.
They were reviewed for unexpected text, personal data, third-party branding, and trademarks.

The light desktop variants were generated using only each project-owned dark
illustration as its edit target and composition reference. Both sets use generic devices and
abstract interface elements; they were reviewed for unexpected text, personal data, third-party
branding, and trademarks.

The light-variant prompt set shared this instruction:

> Use case: lighting-weather. Asset type: OTP Harbor onboarding illustration, light-theme variant.
> Convert the referenced dark onboarding artwork into a polished light-theme version. Preserve the
> same subjects, conceptual meaning, 16:9 composition, object positions, proportions, and complete
> objects. Use a bright soft-white to pale ice-blue studio background, subtle daylight shadows,
> premium glossy 3D rendering, and restrained OTP Harbor cyan, blue, and violet accents. Use generic
> UI only. Include no company logos, trademarks, service names, legible words, watermarks, borders,
> captions, or added text. Avoid a dark overall image.

The five subject specifications were: adding an account by scanning or typing; moving encrypted
accounts between a phone and laptop; organizing generic account cards into groups, favorites, and
protected folders; importing a local pack of generic account icons; and quick unlock with a
fingerprint, secure vault, and key.

## Language flags

`en.png` and `de.png` were rendered inside the project workspace on 2026-08-29 from geometric primitives. `fr.png` and `es.png` were rendered on 2026-09-03 by [`Generate-AdditionalLanguageFlags.ps1`](../../scripts/assets/Generate-AdditionalLanguageFlags.ps1), also from geometric primitives. They do not derive from downloaded image files:

- `en.png` is a project-rendered representation of the public-domain United States flag design, used for the English locale.
- `de.png` is a project-rendered representation of the public-domain German federal flag design, used for the German locale.
- `fr.png` is a project-rendered representation of the public-domain French flag design, used for the French locale.
- `es.png` is a project-rendered language indicator based on the public-domain Spanish flag layout, used for the Spanish locale.

## Windows installer artwork

`scripts/release/installer/InstallerDialog.bmp` and `InstallerBanner.bmp` were rendered in the project workspace on 2026-09-08 from geometric shapes and the reviewed project-owned `app-1024.png`. They provide the branded WiX welcome/license and banner surfaces. No downloaded or third-party artwork is included. Both bitmaps were visually reviewed at their exact Windows Installer dimensions.

## Microsoft Store promotional artwork

`packaging/windows-store/assets/source/store-abstract-background.png` was generated specifically for the Microsoft Store listing on 2026-09-10 with OpenAI's built-in image-generation tool. It contains no application UI, user data, third-party branding, claims, text, or generated version of the app logo.

Generation prompt:

> Edit this existing 16:9 Microsoft Store promotional background. Remove the OTP Harbor emblem/logo completely and naturally continue the surrounding deep navy background and cyan/violet flowing light ribbons through that area. Preserve the premium futuristic local-security mood, layered soft glows, subtle particles, depth, and ample calm negative space. No logos, no icons, no text, no letters, no user interface, no devices, no QR codes. The result must be a clean polished abstract background suitable for later compositing with the exact official logo. Keep important visual energy centered and in the upper two-thirds; keep the bottom third quiet and dark. Landscape 16:9.

[`New-WindowsStoreMarketingAssets.ps1`](../../scripts/assets/New-WindowsStoreMarketingAssets.ps1) composites the canonical project-owned `app-1024.png` without modification and exports the reviewed background at the exact Partner Center dimensions:

- `store-super-hero-1920x1080.png`
- `store-poster-art-720x1080.png`
- `store-box-art-1080x1080.png`
- `store-app-tile-300x300.png`
- `store-logo-150x150.png`
- `store-logo-71x71.png`

The final images were visually reviewed for exact logo geometry, safe placement, absent text and third-party branding, and required dimensions.

## Microsoft Store marketing screenshot campaign

`packaging/windows-store/assets/source/store-marketing-screenshot-background.png` was generated on
2026-09-11 with OpenAI's built-in image-generation tool using only the existing project-owned
abstract Store background as a visual reference. Competitor screenshots supplied as general layout
inspiration were not passed to the generation tool and no third-party UI, logo, text, or artwork is
embedded in the generated background.

Final generation prompt:

> Use case: ads-marketing. Asset type: reusable 16:9 Microsoft Store marketing screenshot backdrop for OTP Harbor. Preserve the reference image's deep navy, cyan, and violet visual identity, but create a new original campaign composition. Create a premium futuristic cybersecurity backdrop with a calm dark navy text zone across the left 42 percent and an illuminated dimensional product stage across the right 58 percent. Use restrained flowing cyan and violet light ribbons, subtle encrypted-data particles, soft depth, and a polished desktop-software launch aesthetic. Make the center and right suitable for compositing authentic app windows later. Keep an exact landscape 16:9 feel, clean hierarchy, generous safe margins, no critical detail at the edges, a darker upper-left for large white copy, and a brighter right-side halo behind future UI. Background only: no text, letters, numbers, logos, icons, shields, locks, fingerprints, QR codes, app UI, devices, monitors, phones, people, or watermarks.

The v3 marketing generator at
[`Generate-MarketingAssets.py`](../../scripts/marketing/Generate-MarketingAssets.py)
deterministically composites the canonical icon, localized campaign copy, genuine v3 captures,
documented QR/Windows Security composites, and project-owned onboarding illustrations onto that
background. It creates ten 1920 x 1080 outputs for each supported Store locale (`en-US`, `de-DE`,
`fr-FR`, and `es-ES`). `Sync-WindowsStoreSubmissionAssets.ps1` copies those 40 files byte-for-byte
into `packaging/windows-store/screenshots`; Store validation compares every SHA-256 with the
canonical generated file. The former v2 raw UI sources and five-slide generator were removed so
they cannot be submitted accidentally. The generated screenshots are Store listing material and
are not shipped inside the MSIX.

## Light and dark theme showcase draft

`packaging/marketing/source/theme-showcase-background-draft.png` was generated on 2026-10-13 with
OpenAI's built-in image-generation tool. A maintainer-provided third-party marketing screenshot was
used only as broad composition inspiration. The prompt explicitly excluded its interface, copy,
logos, service icons, badges, and other artwork. The resulting draft is an abstract background only:
it contains no application UI, text, logo, account icon, device, QR code, personal data, or
third-party branding.

Generation prompt:

> Use case: ads-marketing. Asset type: reusable 16:9 OTP Harbor light/dark theme showcase background for both desktop and Android marketing composites. Input image: Image 1 is composition inspiration only; do not copy its UI, text, logos, service icons, badges, or artwork. Primary request: create an original premium cybersecurity campaign backdrop designed for later compositing authentic OTP Harbor light-theme and dark-theme app screenshots side by side. Scene/backdrop: deep navy-to-indigo abstract digital space with restrained cyan and violet flowing light ribbons, a subtle brighter cool daylight glow behind the future light-theme screenshot position, and a deeper night glow behind the future dark-theme screenshot position. Style/medium: polished modern software marketing background, calm and high-trust. Composition/framing: exact landscape 16:9 feel; calm copy-safe area across the left 38 percent; broad dimensional product stage across the right 62 percent sized for two overlapping app captures; generous safe margins; no important details at edges. Lighting/mood: balanced dual-theme contrast, elegant and restrained, not flashy. Constraints: background only; no text, letters, numbers, logos, icons, brands, trademarks, app UI, screens, devices, QR codes, people, badges, ratings, borders, or watermark.

This source remains a draft and must not be published until new synthetic-only, authentic light- and
dark-theme captures from the current desktop and isolated Android marketing builds have been
reviewed and composited. Generated or recolored UI must not be substituted for those captures.

## Android website marketing campaign

`packaging/android/marketing/source/android-marketing-background.png` was generated on 2026-09-12
with OpenAI's built-in image-generation tool without an input image. It is a background-only source
for authentic Android screenshots and contains no app UI, text, logo, device, QR code, user data, or
third-party artwork.

Final generation prompt:

> Use case: ads-marketing. Asset type: reusable 16:9 website campaign backdrop for OTP Harbor Android. Create an original premium futuristic cybersecurity background suitable for compositing authentic Android phone screenshots and editable HTML-generated marketing copy later. Use a deep navy abstract digital space with restrained cyan and violet flowing light ribbons, subtle encrypted-data particles, and a softly illuminated dimensional stage. Keep a dark calm copy-safe area across the left 42 percent and a brighter cyan-violet halo and product stage across the right 58 percent, with generous safe margins and no important details at the edges. The mood should be a calm, high-trust security product. Background only: no text, letters, numbers, logos, brands, icons, shields, locks, fingerprints, QR codes, app UI, devices, phones, monitors, people, watermarks, or borders.

The v3 capture plan and synthetic-data policy are documented in
[`packaging/android/marketing/README.md`](../../packaging/android/marketing/README.md). Reviewed
Dark and Light captures live under `artifacts/marketing/source/android`; the v3 marketing manifest
records their hashes, purposes, crops, and every generated derivative. The deterministic marketing
pipeline creates eight 1920×1080 website campaign images, six 1080×1920 Play/Fastlane phone
screenshots, the feature graphic, and the high-resolution icon. The committed packaging and
Fastlane files are hash-verified mirrors of `artifacts/marketing/store/android`.

The account-list, settings, and scanner visuals preserve genuine app pixels. The backup/restore
campaign is explicitly recorded as a marketing promotion using a genuine settings capture and the
project-owned `move-accounts.png` onboarding illustration; it must not be represented as a literal
application screen. The labeled scanner demonstration QR points only to the OTP Harbor GitHub
repository. The retired v2 capture set under `packaging/android/marketing/source/captures` is not a
source of v3 publication assets.

## Microsoft Store screenshots

The committed Store upload set under `packaging/windows-store/screenshots/<culture>/marketing`
contains the 40 synchronized v3 campaign candidates generated from the canonical sources under
`artifacts/marketing`. Each locale contains the same ten reviewed layouts, beginning with the Dark
account list. The source captures use deterministic fictional accounts and project-owned geometric
marks. Slides 08–11 are conceptual promotions built from project-owned onboarding artwork and must
not be described as literal UI. `artifacts/marketing/manifest.json` records source and derivative
hashes, crops, placements, locale, and publication status; automated validation does not replace
the required full-resolution visual and trademark review.

## QR import test fixtures

`OtpHarbor.Tests/Fixtures/Qr/google-authenticator-10-account-export-1.png` and
`google-authenticator-10-account-export-2.png` are machine-readable transfer QR fixtures containing
only synthetic test accounts. They contain no logo, brand artwork, real OTP secret, or personal
account data. They are retained solely to exercise the offline Google Authenticator migration parser.

## Reviewed file hashes

| File | SHA-256 |
| --- | --- |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/add-account-light.png` | `837cf96bd4fad3860bb48abc0d78b2d989366b23c4bd57b2dd5e5c4fd9c4ba9e` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/add-account.png` | `569d1f725e159973ae99cac06dfa7326b8037557705bef34fea8001270b805cd` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/move-accounts-light.png` | `9a63a76b408b204dcd9ca07b5b24f2a285e8200a172189bd70025924aa6484a9` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/move-accounts.png` | `eed277cf42570eecfaa1acf2090c4e5a1ed19ccdb6aec76415e8e6de39f593c4` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/organize-accounts-light.png` | `d27a300401442d941df2a2484cea3d6598afb2e619ef0391745c7074db8699c8` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/organize-accounts.png` | `190809e484c906734b8815b70f250c36bac5cbb2a26c071dadb4fc1b228875ad` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/icon-pack-light.png` | `a95a5efa4d23810d21d17c606e381115839773099657bb59bdd0e92cb8db45fe` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/icon-pack.png` | `3dc72dc3ed8dee5e11d6ae662d520d224ce94be631cf7ba642b4a62d6b120ec0` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-add-account-dark.png` | `6384deda547838c9c3082b1af17905ce539b950abf7870e71d76a7c85bd15be2` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-add-account-light.png` | `378e0c417584e9eaa1f080a973d04b176715365628236c1bc4e583e54c497ac8` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-move-accounts-dark.png` | `6ee4e887d9cf3160ef6bac89e501764f8f71e3de7a8a7a72f3dba44c1d1bfa2c` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-move-accounts-light.png` | `cd2d5f84643615916dbfa89c10747eee73797e6a856972038381271d3f86c48f` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-organize-accounts-dark.png` | `2a0ef19918c870d01a60f6055e909372c50b633333b7a9358999bc7173199277` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-organize-accounts-light.png` | `d733b032a653d59c49dc314fb13543b6941aec3e7daacc79ae8dbf0d188fcac8` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-icon-pack-dark.png` | `9c65e0b14b971f60dfc24c2ca9e126d74df061e9331bb3e3dfa4ecf2f6bba703` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-icon-pack-light.png` | `a6d466b4a6cef13bfb85fcd583e959d3d03fd843b8041792059cbb19e5a5e047` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-quick-unlock-dark.png` | `248091880571503a480be73a8036470efbfc116ab27c002b358eda6f8a8dc404` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/mobile-quick-unlock-light.png` | `d85bc5e8aa21bdfa78747299c20f79637dd19f03372f4d91907a2e5f5b928e31` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/quick-unlock-light.png` | `a538031256f03d9f343aeb71178394a665325214e3763f107c0e50ce6aa65632` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/quick-unlock.png` | `cf4c42dcd2c8183512d9dfe9411200f88a263172c5905de740b84c89d0c42bad` |
| `packaging/marketing/source/theme-showcase-background-draft.png` | `affcf38996d604a03b09c9be994f55c4cba017bb4631c1ba6ffdd0ff4f190740` |
| `packaging/android/marketing/source/android-marketing-background.png` | `282b99c342a4713b830cd905035eb579f81c05a5b92914b50adf8151d6cd7383` |
| `OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-1024.png` | `66748954507b3f9f9cff87dc23c97134c1d7d029e8275de179b9f3872f2d12b4` |
| `OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-128.png` | `26fe7fe9a91c7f2e939c7d794cbade4d1e22090ef3c40a59b8ae9ffb3c9aaf88` |
| `OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app.ico` | `7a71a423982499c438177e3b58126f003c3ece9a66cb2b91c07dc50a812ab81e` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/flags/en.png` | `1c2bcc20e5985e5f03a3a440f198b5d08a4ac609e9cebba00b639b0e50fba8fc` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/flags/de.png` | `2c8f253f3401d18df0a47bd7906102cf78ea7e4a2caac9e4c6f4efebc906de0a` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/flags/fr.png` | `b962887c6a6317b8e60a1c0b33ae5fed16b453a83e9026043480ccfd3cc3a340` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/flags/es.png` | `d45958f491e9cf1d10c0e6de74970c5fed11e826e702c14c9009197842e6d1bd` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Biometric/fingerprint.png` | `8fb24a9f292168bb5cfd696f4d2b476d668445449bc5d0a8474896207d83d503` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Biometric/lock-screen-dark.png` | `4af14f62d27b27d68a0c2312c1f40e8a369461e34cbc1ee1d190e55402eae327` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Biometric/lock-screen-light.png` | `176bdef9bec8a6596efa256a36e2ad3c87f1a2a298ff9df77d3a02d771590ba4` |
| `OtpHarbor.UI.Avalonia.Shared/Assets/Biometric/otp-harbor-mark.png` | `26fe7fe9a91c7f2e939c7d794cbade4d1e22090ef3c40a59b8ae9ffb3c9aaf88` |
| `packaging/marketing/source/recraft/biometric-fingerprint-master.svg` | `eb8d9731e7884ed53d5bb5ff2e542e37a51e5518d63b72d68f658de313f1534b` |
| `packaging/marketing/source/recraft/biometric-lock-screen-master.svg` | `1ffb46aab0b9247fa1ee3f9543b3c9e6f4a8bb70385acb24bcf0312a3aafeee0` |
| `scripts/release/installer/InstallerDialog.bmp` | `be89b19fbb5e0c3abcf6e6d6df916e8f79fa9c3528a3c2cdf5881f5b419ddacc` |
| `scripts/release/installer/InstallerBanner.bmp` | `7ba45f2c67dafb51e370d97cc31ac7555d986703ea1dcada4403b458108e8a58` |
| `packaging/windows-store/assets/source/store-abstract-background.png` | `9a45a1d07a9b3912af600cd0156847117b2cf2f9367665cf5c439df8324eb102` |
| `packaging/windows-store/assets/source/store-marketing-screenshot-background.png` | `2a08328ae20abe713330235b08071262ce00827838966cd474009a23b1e2bef4` |
| `packaging/windows-store/assets/store-super-hero-1920x1080.png` | `1df8efd083a88c7aaef390ec3b5c9774458aa8ff5db8055e188a737a30184a41` |
| `packaging/windows-store/assets/store-poster-art-720x1080.png` | `ec5893f4574003d64376c18a720596a5a55ff3039d408e52c10665f04d93e65f` |
| `packaging/windows-store/assets/store-box-art-1080x1080.png` | `243ea8dabc8371a18582aa451fb053b8235b679b670caffa5908350c904248f0` |
| `packaging/windows-store/assets/store-app-tile-300x300.png` | `cab7d95f5b6e8d20a39d6b60af33f034dc904409783adfb9eb2eb5bca57faf30` |
| `packaging/windows-store/assets/store-logo-150x150.png` | `796e08817f41658d05c25e1826ece092df73602b9072b31c76c00e85adb95979` |
| `packaging/windows-store/assets/store-logo-71x71.png` | `d7716fa64f2e7aafcb85d3ee6bb98647f874a5328500a3e140dddf42bfe4ab20` |
| `OtpHarbor.Tests/Fixtures/Qr/google-authenticator-10-account-export-1.png` | `8187e829ce68e29abed16a344ef9bcc3057e52f02f7e98348f921a2d8fa90640` |
| `OtpHarbor.Tests/Fixtures/Qr/google-authenticator-10-account-export-2.png` | `c0df83d925168869e81798a2974c7f90c4443dd31eabe8068759108076d130b3` |

Any replacement requires a new provenance record, license review, updated hashes, and visual/build validation.
