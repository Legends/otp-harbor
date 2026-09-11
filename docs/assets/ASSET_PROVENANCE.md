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

Any replacement requires a new provenance record, license review, updated hashes, and visual/build validation.
