# Project asset provenance

This record covers every raster image embedded by `TOTP.UI.Avalonia.Desktop` and the public repository artwork. The files are project-owned outputs and are distributed under the repository's [MIT license](../../LICENSE.txt). No downloaded raster artwork is included.

## Application icon

The application icon was generated specifically for the project, now named OTP Harbor, on 2026-08-29 using OpenAI's built-in image-generation tool at the maintainer's request. It was generated without an input or reference image. [`Generate-AppIcon.ps1`](../../scripts/assets/Generate-AppIcon.ps1) resizes a supplied master with high-quality bicubic interpolation and packages independently resized PNG frames at 16, 24, 32, 48, 64, 128, and 256 pixels in the Windows ICO. The production outputs are retained below; discarded candidates remain available in Git history.

Final generation prompt:

> Use case: logo-brand. Asset type: square desktop application icon candidate. Create an original minimal symbol for a secure TOTP authenticator, combining a circular six-segment one-time-code ring with a small shield cutout. Use a crisp, flat, vector-like, geometric, modern native desktop software identity; a single centered mark; a strong silhouette; generous padding; and forms readable at 16x16 pixels. Use deep navy `#0C1C33`, violet `#7D7FF4`, a restrained bright cyan accent, and white only for small negative-space separation. Use a genuinely transparent background. Include no text, letters, digits, watermark, mockup, rounded-square container, 3D rendering, photorealism, padlock cliché, excessive detail, or thin fragile lines.

The generated image was visually reviewed for prohibited text, third-party branding, and small-size legibility before conversion.

## Language flags

`en.png` and `de.png` were rendered inside the project workspace on 2026-08-29 from geometric primitives. `fr.png` and `es.png` were rendered on 2026-09-03 by [`Generate-AdditionalLanguageFlags.ps1`](../../scripts/assets/Generate-AdditionalLanguageFlags.ps1), also from geometric primitives. They do not derive from downloaded image files:

- `en.png` is a project-rendered representation of the public-domain United States flag design, used for the English locale.
- `de.png` is a project-rendered representation of the public-domain German federal flag design, used for the German locale.
- `fr.png` is a project-rendered representation of the public-domain French flag design, used for the French locale.
- `es.png` is a project-rendered language indicator based on the public-domain Spanish flag layout, used for the Spanish locale.

## README screenshot

`docs/images/readme/app.png` is a maintainer-provided screenshot of the application populated only with synthetic `example.invalid` sample accounts. It was replaced on 2026-09-10 to document the current compact account layout with inline one-time passwords and countdown progress bars for prominent issuer labels. The screenshot was visually reviewed for private account data and third-party secrets before inclusion.

## Social preview

`docs/images/social/otp-harbor-social-preview.jpg` was generated specifically for the project on 2026-09-03 with OpenAI's built-in image-generation tool. The reviewed OTP Harbor icon and synthetic-account application screenshot were supplied as visual references. The output was checked for exact product spelling, synthetic-only account data, third-party branding, and the required 2:1 social-preview ratio. On 2026-09-04, the reviewed image was re-encoded as a 92-quality JPEG without resizing or content changes so it remains below GitHub's 1 MB social-preview upload limit.

Final generation prompt:

> Use case: GitHub social media preview. Create a professional, minimal 2:1 launch banner for the open-source app OTP Harbor. Preserve the supplied official OTP Harbor icon and the supplied authentic application screenshot; do not redesign, distort, or invent interface content. Use the app's deep navy background with restrained violet and cyan accents. Place the exact title “OTP Harbor” and the exact tagline “Local-first authentication. Your secrets stay yours.” in clear, highly legible typography. Balance the brand mark, product text, and a clean framed view of the app. Include no additional logos, badges, claims, ratings, people, devices, decorative clutter, or watermark.

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

[`New-WindowsStoreMarketingScreenshots.ps1`](../../scripts/assets/New-WindowsStoreMarketingScreenshots.ps1)
deterministically composites the canonical icon, reviewed localized marketing copy, and authentic
app captures onto that background. It creates five 1920 x 1080 outputs for each supported Store
locale (`en-US`, `de-DE`, `fr-FR`, and `es-ES`). The 20 outputs contain only synthetic accounts. The
Windows Hello visual combines the maintainer-provided empty-PIN prompt in
`windows-hello-prompt.png` with the authentic locked-app capture in `quick-unlock-screen.png`.
The import/export visual combines the authentic settings and QR camera/file workflows from
`import-export-settings.png` and `import-qr-camera.png`. No credential, biometric data, OTP secret,
QR code, or private account is present. Text remains editable in the generator and is rendered at
asset-build time; the generated screenshots themselves are not shipped inside the MSIX.

## Android website marketing campaign

`packaging/android/marketing/source/android-marketing-background.png` was generated on 2026-09-12
with OpenAI's built-in image-generation tool without an input image. It is a background-only source
for authentic Android screenshots and contains no app UI, text, logo, device, QR code, user data, or
third-party artwork.

Final generation prompt:

> Use case: ads-marketing. Asset type: reusable 16:9 website campaign backdrop for OTP Harbor Android. Create an original premium futuristic cybersecurity background suitable for compositing authentic Android phone screenshots and editable HTML-generated marketing copy later. Use a deep navy abstract digital space with restrained cyan and violet flowing light ribbons, subtle encrypted-data particles, and a softly illuminated dimensional stage. Keep a dark calm copy-safe area across the left 42 percent and a brighter cyan-violet halo and product stage across the right 58 percent, with generous safe margins and no important details at the edges. The mood should be a calm, high-trust security product. Background only: no text, letters, numbers, logos, brands, icons, shields, locks, fingerprints, QR codes, app UI, devices, phones, monitors, people, watermarks, or borders.

The capture plan and reviewed synthetic account roster are documented in
[`packaging/android/marketing/README.md`](../../packaging/android/marketing/README.md). Final
composites must use authentic Android app/system captures. No generated or reconstructed UI may be
presented as an application screenshot.

## Microsoft Store screenshots

The four-image Store upload set under `packaging/windows-store/screenshots/en-US` was captured from
the current Windows desktop build on 2026-09-10. Before capture, the existing synthetic-only vault was
cleared and repopulated with eight deterministic synthetic accounts using prominent issuer labels
and reserved `example.invalid` account names. The screenshots were visually reviewed to confirm
that they contain no real accounts, OTP seeds, personal paths, desktop content, or OS notifications.
One-time codes and the QR workflow, when shown, derive only from synthetic fixture material.

`04-quick-unlock-2.png` is a maintainer-provided capture of the same synthetic-only build with an
empty Windows Security PIN prompt. `04-quick-unlock-transparent.png` is a deterministic derivative
that preserves the application and Windows Security pixels while removing only the surrounding
desktop area into a real PNG alpha channel. No entered PIN, account secret, or personal desktop
content is present.

## Reviewed file hashes

| File | SHA-256 |
| --- | --- |
| `packaging/android/marketing/source/android-marketing-background.png` | `282b99c342a4713b830cd905035eb579f81c05a5b92914b50adf8151d6cd7383` |
| `TOTP.UI.Avalonia.Desktop/Assets/Icons/app-1024.png` | `66748954507b3f9f9cff87dc23c97134c1d7d029e8275de179b9f3872f2d12b4` |
| `TOTP.UI.Avalonia.Desktop/Assets/Icons/app-128.png` | `26fe7fe9a91c7f2e939c7d794cbade4d1e22090ef3c40a59b8ae9ffb3c9aaf88` |
| `TOTP.UI.Avalonia.Desktop/Assets/Icons/app.ico` | `7a71a423982499c438177e3b58126f003c3ece9a66cb2b91c07dc50a812ab81e` |
| `TOTP.UI.Avalonia.Desktop/Assets/flags/en.png` | `1c2bcc20e5985e5f03a3a440f198b5d08a4ac609e9cebba00b639b0e50fba8fc` |
| `TOTP.UI.Avalonia.Desktop/Assets/flags/de.png` | `2c8f253f3401d18df0a47bd7906102cf78ea7e4a2caac9e4c6f4efebc906de0a` |
| `TOTP.UI.Avalonia.Desktop/Assets/flags/fr.png` | `b962887c6a6317b8e60a1c0b33ae5fed16b453a83e9026043480ccfd3cc3a340` |
| `TOTP.UI.Avalonia.Desktop/Assets/flags/es.png` | `d45958f491e9cf1d10c0e6de74970c5fed11e826e702c14c9009197842e6d1bd` |
| `docs/images/readme/app.png` | `f35c44c51e29e7561053cbfe7f6593465df60924feaa3a84d194f2d8b4a07ce6` |
| `docs/images/social/otp-harbor-social-preview.jpg` | `2ca1ebc4d4dabbb5f8061013432c4d3dc5708efa03ab746b20e4e83686de6725` |
| `scripts/release/installer/InstallerDialog.bmp` | `be89b19fbb5e0c3abcf6e6d6df916e8f79fa9c3528a3c2cdf5881f5b419ddacc` |
| `scripts/release/installer/InstallerBanner.bmp` | `7ba45f2c67dafb51e370d97cc31ac7555d986703ea1dcada4403b458108e8a58` |
| `packaging/windows-store/assets/source/store-abstract-background.png` | `9a45a1d07a9b3912af600cd0156847117b2cf2f9367665cf5c439df8324eb102` |
| `packaging/windows-store/assets/source/store-marketing-screenshot-background.png` | `2a08328ae20abe713330235b08071262ce00827838966cd474009a23b1e2bef4` |
| `packaging/windows-store/assets/source/windows-hello-quick-unlock.png` | `6ff8db7a3f08c4b34d047538148071b50b6541d8cb63b2820fdc4914a9a32a41` |
| `packaging/windows-store/assets/source/windows-hello-prompt.png` | `f86d932878865889c094690ad73ad55c4b14f19e0f0f0075813c1af31f946672` |
| `packaging/windows-store/assets/source/quick-unlock-screen.png` | `75730074e7e3bc43c01c6aa86a6734eaf944f68aebaf63371d5eb23d382b985d` |
| `packaging/windows-store/assets/source/import-export-settings.png` | `58a46d85bb904bd96c9905d2dbb804fb8067e9a370d80e7a688e43d36a4c09d0` |
| `packaging/windows-store/assets/source/import-qr-camera.png` | `ba4e39868cc899841bd288977832fe8e23d76489642a8c2ebced91638cff1ff2` |
| `packaging/windows-store/assets/store-super-hero-1920x1080.png` | `1df8efd083a88c7aaef390ec3b5c9774458aa8ff5db8055e188a737a30184a41` |
| `packaging/windows-store/assets/store-poster-art-720x1080.png` | `ec5893f4574003d64376c18a720596a5a55ff3039d408e52c10665f04d93e65f` |
| `packaging/windows-store/assets/store-box-art-1080x1080.png` | `243ea8dabc8371a18582aa451fb053b8235b679b670caffa5908350c904248f0` |
| `packaging/windows-store/assets/store-app-tile-300x300.png` | `cab7d95f5b6e8d20a39d6b60af33f034dc904409783adfb9eb2eb5bca57faf30` |
| `packaging/windows-store/assets/store-logo-150x150.png` | `796e08817f41658d05c25e1826ece092df73602b9072b31c76c00e85adb95979` |
| `packaging/windows-store/assets/store-logo-71x71.png` | `d7716fa64f2e7aafcb85d3ee6bb98647f874a5328500a3e140dddf42bfe4ab20` |
| `packaging/windows-store/screenshots/en-US/01-account-dashboard.png` | `f35c44c51e29e7561053cbfe7f6593465df60924feaa3a84d194f2d8b4a07ce6` |
| `packaging/windows-store/screenshots/en-US/02-search-accounts.png` | `70eee4716ccfbc278789e92bb71b3864573e821a0888a1431f44a852c014592f` |
| `packaging/windows-store/screenshots/en-US/03-add-account.png` | `f2a6594983eddb8955fd3275e6ddbb2063579a139d182c7c2931e9f3f2211861` |
| `packaging/windows-store/screenshots/en-US/04-quick-unlock.png` | `c80dd15aad7c584c444784fa29ec559c1e4bf38fd32df1632bfdfbb4b6947c3c` |
| `packaging/windows-store/screenshots/en-US/04-quick-unlock-2.png` | `13db0d0da561c511c6a94acf5213f68a31ed859c0f0e82a7310265211f0dd88c` |
| `packaging/windows-store/screenshots/en-US/04-quick-unlock-transparent.png` | `6ff8db7a3f08c4b34d047538148071b50b6541d8cb63b2820fdc4914a9a32a41` |
| `packaging/windows-store/screenshots/de-DE/marketing/01-local-vault.png` | `1ef8cb148b6f18b8083d3f1e7f1483fc301debd1fbaf5b57c82dd67b2e3c3c0f` |
| `packaging/windows-store/screenshots/de-DE/marketing/02-search-and-copy.png` | `0190328c0e3a042a1ac2b955269427f3cf597bfea9dba37b796adf0568fcc6c4` |
| `packaging/windows-store/screenshots/de-DE/marketing/03-add-and-import.png` | `9cae1c0573688f0bfedaa22a056b3c1560776e5c2d2523f9c9e397e4b8c85e5b` |
| `packaging/windows-store/screenshots/de-DE/marketing/04-windows-hello.png` | `d62c6518ec516da09d47591893c5b27ca80389ecd20fa82f2e04bb520cf4e898` |
| `packaging/windows-store/screenshots/de-DE/marketing/05-import-export.png` | `8a163c3925a2547948f680d378fdef1691128778bb62a8372b77cb049e70c66c` |
| `packaging/windows-store/screenshots/en-US/marketing/01-local-vault.png` | `cb51058dfeb6064133558ceaac6dff559f0242c1a2f29fd25b46306d5679a778` |
| `packaging/windows-store/screenshots/en-US/marketing/02-search-and-copy.png` | `79ccd68cbca820f11957dfa7c62ccbbe5b912508a83ea2313b95c6cd42d62040` |
| `packaging/windows-store/screenshots/en-US/marketing/03-add-and-import.png` | `41cfcb9322386dfc8a0a3ecf4bf2f76e0bf67ad426e63850603ae43bd13fbd68` |
| `packaging/windows-store/screenshots/en-US/marketing/04-windows-hello.png` | `6ff17afe947a5e47635b65763f3237747a4fc715465ba645779cff4d0b2c687f` |
| `packaging/windows-store/screenshots/en-US/marketing/05-import-export.png` | `d6de618621fa1fdf550d2126dbcb43b4c90293d4764cda42c5fc3bc2e8161264` |
| `packaging/windows-store/screenshots/es-ES/marketing/01-local-vault.png` | `059092199174042a0b42f22f8197a83594585f683681d59171af0656f6d4fed6` |
| `packaging/windows-store/screenshots/es-ES/marketing/02-search-and-copy.png` | `3e682a7f58e90d0195001d24f68d7a12dd42ac797cbf625cc7037aeb89530ecb` |
| `packaging/windows-store/screenshots/es-ES/marketing/03-add-and-import.png` | `71cc9dd13f6ab647a21790a8a95054a789ffedc09d134106c1db19e0e133684d` |
| `packaging/windows-store/screenshots/es-ES/marketing/04-windows-hello.png` | `7b1e84462a0323910a4e12a4760898d17f8cca27b7ac69127194e60c4615875c` |
| `packaging/windows-store/screenshots/es-ES/marketing/05-import-export.png` | `0bb82cdaa4014c268967843449acf3fc42d2b57589dedd5c0f73c9986c3f9bb1` |
| `packaging/windows-store/screenshots/fr-FR/marketing/01-local-vault.png` | `5142ea36d83266cd9434d86563a20d776084cb64bab08d1cfa3d6f9cf852521a` |
| `packaging/windows-store/screenshots/fr-FR/marketing/02-search-and-copy.png` | `559287d3e48e8c06bbdfc2d24bde70cf67aa1e193bd650698011a1290a87c0d0` |
| `packaging/windows-store/screenshots/fr-FR/marketing/03-add-and-import.png` | `2a5ed9a1b1d12845b3a23fc661e2c75cbac58cbdb99faefa3e8b76dcbdfe0d52` |
| `packaging/windows-store/screenshots/fr-FR/marketing/04-windows-hello.png` | `885d9e38ff04dadc724ac055fd8f20dae754ac5101c336b68078c9ab3bafe4a5` |
| `packaging/windows-store/screenshots/fr-FR/marketing/05-import-export.png` | `242772834189dba90a966ee59995a348241b33bc54aab6f2f801c2fe730ef82d` |

Any replacement requires a new provenance record, license review, updated hashes, and visual/build validation.
