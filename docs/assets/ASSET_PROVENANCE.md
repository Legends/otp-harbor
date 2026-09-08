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

`docs/images/readme/app.png` is a maintainer-provided screenshot of the application populated only with synthetic `example.invalid` sample accounts. It was replaced on 2026-09-08 to document the current compact account layout with inline one-time passwords and countdown progress bars. The screenshot was visually reviewed for private account data and third-party secrets before inclusion.

## Social preview

`docs/images/social/otp-harbor-social-preview.jpg` was generated specifically for the project on 2026-09-03 with OpenAI's built-in image-generation tool. The reviewed OTP Harbor icon and synthetic-account application screenshot were supplied as visual references. The output was checked for exact product spelling, synthetic-only account data, third-party branding, and the required 2:1 social-preview ratio. On 2026-09-04, the reviewed image was re-encoded as a 92-quality JPEG without resizing or content changes so it remains below GitHub's 1 MB social-preview upload limit.

Final generation prompt:

> Use case: GitHub social media preview. Create a professional, minimal 2:1 launch banner for the open-source app OTP Harbor. Preserve the supplied official OTP Harbor icon and the supplied authentic application screenshot; do not redesign, distort, or invent interface content. Use the app's deep navy background with restrained violet and cyan accents. Place the exact title “OTP Harbor” and the exact tagline “Local-first authentication. Your secrets stay yours.” in clear, highly legible typography. Balance the brand mark, product text, and a clean framed view of the app. Include no additional logos, badges, claims, ratings, people, devices, decorative clutter, or watermark.

## Windows installer artwork

`scripts/release/installer/InstallerDialog.bmp` and `InstallerBanner.bmp` were rendered in the project workspace on 2026-09-08 from geometric shapes and the reviewed project-owned `app-1024.png`. They provide the branded WiX welcome/license and banner surfaces. No downloaded or third-party artwork is included. Both bitmaps were visually reviewed at their exact Windows Installer dimensions.

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
| `docs/images/readme/app.png` | `3730253e1d8579bae7fb49d173ad83b91ad7a2f90f5f5a759a29f048abe1e657` |
| `docs/images/social/otp-harbor-social-preview.jpg` | `2ca1ebc4d4dabbb5f8061013432c4d3dc5708efa03ab746b20e4e83686de6725` |
| `scripts/release/installer/InstallerDialog.bmp` | `be89b19fbb5e0c3abcf6e6d6df916e8f79fa9c3528a3c2cdf5881f5b419ddacc` |
| `scripts/release/installer/InstallerBanner.bmp` | `7ba45f2c67dafb51e370d97cc31ac7555d986703ea1dcada4403b458108e8a58` |

Any replacement requires a new provenance record, license review, updated hashes, and visual/build validation.
