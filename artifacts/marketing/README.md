# Canonical v3 marketing assets

This directory owns **source captures**, generated comparisons, website derivatives, Store screenshots, and the fictional Debug-only dataset. `staging/` is excluded from Git. Do not copy old campaign artwork into `source/`: it predates the fictional-only policy.

## Current status

The 26-account dataset and 26 original geometric icons are generated and the `.otphicons` pack passes the app's format-v3 importer. The account names are fictional candidates, **not trademark-cleared names**. A preliminary exact-name web search found several collisions in the initial dataset, so it was replaced; see [TRADEMARK_REVIEW.md](TRADEMARK_REVIEW.md). A human must review each revised name and icon against likely marks before public use. The user supplied live-app captures; the canonical source tree now contains matching Light and Dark account lists for both platforms with the revised fictional names. Empty account-editor screens retain the app's ordinary issuer-placeholder examples, as explicitly accepted by the user; no screenshot uses a real service as a demo account or icon. Plain Windows Store gallery candidates and ten branded Dark-led campaign slides in each of four languages are generated from fixed-width app captures and documented composites. Publication remains blocked pending visual and legal review and Store candidate acceptance review.

## Prepare the demo apps

1. Run `./scripts/marketing/Prepare-DemoData.ps1`. This regenerates `demo-data/otp-harbor-demo-accounts.json`, `demo-data/otp-harbor-demo-icons.otphicons`, and the 512-pixel original icon masters deterministically. Python 3 and the pinned Pillow dependency in `scripts/marketing/requirements.txt` are required. Every account uses an `example.com` identity and a deterministic synthetic secret. The JSON is **only for disposable Debug vaults**; never ship it with a production package.
2. Verify that the Windows process comes from `artifacts/dev/avalonia-windows-debug/`, is unlocked, and is the intended disposable demo instance. The existing desktop vault path is shared with other local builds; do **not** run the reset script against a normal vault. `./scripts/testing/Clear-DesktopDevelopmentAccounts.ps1` creates an encrypted recovery snapshot and requires confirmation. The separate Debug CLI accepts `--debug-import-accounts <json>` and `--debug-import-icon-pack <otphicons>` after reset.
3. Verify the ADB serial and that `io.github.legends.otpharbor.debug` is installed and unlocked. `./scripts/testing/Clear-AndroidDevelopmentAccounts.ps1 -Device <serial>` creates an encrypted recovery snapshot and requires confirmation. The Android Debug receiver accepts `IMPORT_ACCOUNTS` and `IMPORT_ICON_PACK` from files under the app's private cache. Install a Debug build with `-p:EnableMarketingCapture=true` to allow ADB screen capture; Release builds reject that flag. Never use `pm clear` on a production package.
4. Check the account count is 26 and every issuer resolves to a distinct demo icon on **both** apps before any capture. Select Light and Dark explicitly. Keep account ordering the same on both platforms. Remove temporary JSON copies from Android cache after import.

## Capture and generate

The user will take the screenshots directly on both apps. Save each screenshot to a local PNG or JPEG and import it with, for example, `./scripts/marketing/Import-MarketingCapture.ps1 -Source C:\path\to\desktop-light.png -Platform desktop -Theme light -Screen account-list -Replace`. Use `-Platform android` for Android images. The import re-encodes pixels losslessly as PNG to strip EXIF and other identifying metadata without upscaling, checks for blank images, and records a hash of both the source and canonical file. `-Replace` is required to replace an existing capture. Inspect the image before importing: the script cannot detect personal information, notifications, real logos, or secrets.

For Desktop, capture the fixed-width Windows Debug application at its native size and at 100% display scale; do not enlarge the app or upscale its pixels to meet Store dimensions. The generator handles the Store canvas separately. For Android, use the installed Debug app on the designated phone and retain its full native resolution. Show the same 26 account names and icons in the same order, scroll the main list to the top, close Windows Hello or biometric prompts, and select the matching Light or Dark theme inside the app. Capture the Light and Dark account lists first. Then capture account editing, search, appearance, backups and other actually implemented screens in each theme; keep scanner views free of real QR payloads. Name `-Screen` values consistently, such as `account-list`, `account-editor`, `search`, `appearance`, and `backup`. Keep each platform's captures at matching dimensions and content. Linux uses the shared desktop UI, but any Linux-specific visual claim needs a Linux capture.

The existing `Capture-Desktop.ps1` and `Capture-Android.ps1` remain available as optional reproducible app capture tools. They are not required when the user supplies screenshots. After import, run `./scripts/marketing/Generate-MarketingAssets.ps1` and `./scripts/marketing/Validate-MarketingAssets.ps1`.

`source/desktop/{light,dark}/` and `source/android/{light,dark}/` contain genuine app captures. `composites/` contains explicitly labeled designed images, including the Desktop Light/Dark account-list comparison, scanner QR overlays, and transparent Windows Security prompt cutouts. `store/windows/screenshots/` contains Windows-only gallery candidates: when a genuine window capture is below Store dimensions, the generator centers the exact native app pixels on a neutral frame and records `kind: framed-store-screenshot-candidate`, native app bounds and the source crop. `store/windows/promotional/<culture>/` contains the **v3 branded campaign set** in English, German, French and Spanish: ten 1920 × 1080 slides per locale using the existing Store campaign background and app icon. Slides 02–07 feature genuine Desktop captures or documented composites; a rounded corner mask removes only captured pixels beyond the app window border. Slides 08–11 use OTP Harbor's own groups/favourites, icon-pack, backup/migration and Add accounts illustrations. These four are labeled conceptual promotions in `manifest.json`, with no app capture placements. Light is represented in the retained comparison, while Dark leads the campaign. `store/android/` contains eight website campaign images, six phone screenshots, the feature graphic and high-resolution icon derived from the reviewed v3 Android captures. All generated files record source paths and hashes; the validator checks resized capture interiors, illustration provenance and prompt-cutout provenance. `website/optimized/{platform}/{theme}/` contains 18 cleaned app captures: Desktop captures are trimmed by three pixels on each edge and gain transparent native corners; Android captures lose only system status and navigation bars. `website/optimized/framed/` contains 20 separate 1920 × 1080 designed presentations using the established blue Windows and Android backgrounds, with Android captures inside a phone frame. The core screens are the main view, Desktop context menu in both themes, and settings; separate marketing cards show supported file imports, QR scanning, and backup/restore. Android backup artwork combines its genuine Settings capture with a labeled OTP Harbor concept illustration. `manifest.json` records source hashes, crops, frame placements, and app pixel provenance. The source scanner captures stay unchanged. Derived Desktop and Android QR-scanning cards contain a visibly labeled `GITHUB DEMO QR` inside the camera area; the code decodes to `https://github.com/Legends/otp-harbor` in both themes. Biometric captures remain excluded from the website gallery.

Run `./scripts/marketing/Create-RequestedComposites.ps1` to recreate the four scanner composites and two transparent prompt cutouts. The QR generator encodes exactly `https://legends.github.io/otp-harbor/` and decodes all four results as a check. These are **designed composites**, not genuine camera views or Store screenshots. The transparent cutouts preserve the original Windows Security prompt pixels, while removing the surrounding loading window. The full resolution source stays under `source/`. The Desktop scanner displays a third-party authenticator name, and the Windows Security prompt carries OS branding; both conflict with the fictional-only marketing rule and are **review-only assets**, excluded from published galleries. The Android scanner composites also need manual review before use.

The static website's screenshot gallery loads `website/gallery.json` and `website/optimized/framed/` through the Pages workflow. `Generate-WebsiteShowcase.py` writes the cleaned and framed sets from canonical source captures; `Generate-MarketingAssets.py` invokes it after the other derivatives. Core cards show only plain screen headings, and gallery captions use short titles such as `Account List` and `Settings`; the former `SYNTHETIC DEMO DATA` badge and platform/theme/capture caption suffixes are omitted. The scanner marketing cards retain a `GITHUB DEMO QR` badge to identify their added QR. The Pages workflow copies the gallery only when `manifest.json` says `approved for website publication`, after the human release review; until then the gallery stays hidden. Platform and theme selectors and the image dialog use the site's existing HTML/CSS/JavaScript stack. No source capture is manually copied into `site/`.

For a private approval preview, run `./scripts/marketing/Publish-LocalPreview.ps1`. It validates the assets, assembles the same static site into ignored `staging/local-site/`, includes the candidate gallery locally, and starts a server bound to `127.0.0.1:8765`. Review both `http://127.0.0.1:8765/` and `http://127.0.0.1:8765/android/`. The Android page defaults to genuine Dark captures, with a Light switch and keyboard-accessible enlarged viewing. This local preview does not alter the public Pages approval gate or publish any release.

Microsoft's [MSIX screenshot requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images) require PNG, at least 1366 × 768 pixels for Desktop, at most 50 MB, and advise against added marketing messages or logos. Use up to ten Desktop images; four or more are recommended. The existing published Store listing uses branded campaign slides, so a matching **v3 campaign candidate set** is generated in `store/windows/promotional/` with actual v3 UI and clearly distinguished conceptual illustrations. These branded files meet the technical dimensions but need manual policy and Partner Center acceptance review before they can replace the published v1/v2 art; prior Store acceptance does not guarantee new acceptance. `storeCampaignOrder` lists ten slides per locale; `01-light-account-list.png` was removed, with Dark shown first. The quick-unlock slide uses the actual Windows Security PIN prompt cutout and explains that fingerprint depends on device support. The QR slide uses the documented website QR composite and labels that code as an example. Slides 08–11 use only repo-owned onboarding artwork and verified feature/format descriptions; format names indicate compatibility and use no third-party logos. The separate `storeUploadOrder` contains conservative gallery candidates: main Light, main Dark, settings Light, settings Dark, account actions and Imports. Their app UI was not enlarged, repainted or replaced; a neutral frame brings each file to the size minimum. Captions must be 200 characters or less. The optional `store/windows/icons/otp-harbor-tile-300.png` is generated from the application's 1024-pixel original icon, with its source path and hash recorded in the manifest. Submit nothing without explicit authorization.

## Pre-publication gate

The refreshed Desktop scanner slide (`07-qr-import.png`) shows the supplied
1400 × 1050 live-app captures side by side in Light and Dark. All four campaign
locales label both themes and identify the QR as a website example. The generated
code fits inside the captured scan target; the composite preserves the original
window pixels outside that rectangle. The two website cards use the same capture
pair with the separately labeled repository QR. Neither demo QR holds an account
or an OTP seed.

### Website preview delivery

`python scripts/marketing/Generate-WebsiteShowcase.py --previews-only` derives 1200 × 675
quality-95 WebP previews from the existing reviewed 1920 × 1080 campaign PNGs.
It never rewrites canonical captures or full-resolution
frames. A normal generator run also creates these delivery assets. Both modes record
hashes/provenance in the manifest and reset publication approval for human review.

The README reuses the four existing Desktop and Android account-list captures.
Its HTML tables supply padding and a `#F3F4F5` CSS background where inline styles
are supported. GitHub sanitizes those styles and uses its own themed table
background and spacing. No separate bordered screenshot files are generated.

Gallery schema version 2 separates `preview` and `full` assets. Cards and the
build-selected hero use previews; enlargement alone requests the original PNG.
Pages runs marketing validation before copying approved assets, selects the hero
in the generated HTML (without a second JavaScript source swap), and removes the
Android fallback campaign from approved HTML before browsers discover its PNGs.
Social-preview PNG URLs and the unapproved fallback remain available.

The shared static loader waits for page load, two animation frames and an idle
callback (300 ms fallback), then uses two download slots with interleaved Desktop
Light / Android Dark / Android Light work. On normal desktop connections it also
finishes initially lazy cards through the queue, so returning to Desktop Dark is
prepared without loading all six cards during critical rendering. It pauses queued speculative work in
hidden pages and preempts background requests for user selections. Save-Data,
2G and devices reporting at most 2 GB memory get no speculative work; 3G gets two
previews, and mobile viewports get four. Desktop caches all four groups; mobile
keeps two, with one on the most constrained devices. These limits bound retained
image nodes, while the browser controls its own decoded-image memory. A complete
decoded group replaces the previous gallery atomically. Full-size dialogs show a
loading/error state until decoding completes and retain at most one full-size image
(released on close on constrained devices).

Compression comparison for all 19 images, using Pillow/Lanczos/method 6:

| Asset | Total bytes | Reduction from PNG |
| --- | ---: | ---: |
| Original PNGs | 19,370,245 | — |
| WebP quality 85 | 890,660 | 95.4% |
| WebP quality 90 | 1,124,234 | 94.2% |
| **WebP quality 95 (selected)** | **1,583,976** | **91.8%** |
| WebP quality 100 | 2,120,780 | 89.1% |

Quality 95 preserves small interface detail in visual comparisons without the
extra 34% payload of quality 100. All four scanner preview QR codes decode to the
same project demonstration URL. Validation checks dimensions, hashes, provenance,
visual error against the resized full image, QR decoding, and unchanged README
capture pixels inside the neutral frame.

Install test-only dependencies from
`scripts/validation/gallery-browser-requirements.txt`, install Playwright Chromium,
then run `python scripts/validation/test_gallery_browser.py --report gallery-performance.json`.
Pages runs this test before deployment. It checks all four groups, cached node reuse,
no extra downloads/decoding on repeated switches, the decode barrier, rapid selection,
retry after failures, hidden-page scheduling, two-worker concurrency, constrained
budgets, fallbacks, responsive widths, and keyboard/full-resolution dialog behavior.

Local Chromium measurements (Windows, 1440 × 1000 viewport; three cold runs at
10 Mbit/s with 40 ms latency, medians) compared the previous site with these assets:

| Measurement | Previous | Optimized |
| --- | ---: | ---: |
| Window load | 6.47 s | 1.56 s |
| LCP | 1.13 s | 1.15 s |
| Images requested before window load, transfer | 5.43 MiB | 0.54 MiB |
| Image transfer by 7 s after load | 9.55 MiB | 1.92 MiB |
| Full-resolution gallery requests by that point | 11 | 0 |

LCP is effectively unchanged in this local sample; the measurements are not field
data or a guarantee on other browsers/devices. Unthrottled prepared switching took
3–18 ms through the next rendered frame (under the 100 ms target), with no new
requests or decode calls across twelve switches. Local preview decode calls took
0.2–11 ms. Real Android hardware and Safari/Firefox still require manual checks.

### Human approval

Run the local validation script, then manually inspect *every* image at full resolution for real brands, personal details, status-bar notifications, QR payloads, clip/truncation, correct theme, and icon assignment. Review fictional names/icons for trademark conflicts. Check ownership or permission for every graphical element. Compare the Windows and Android issuer lists and icon IDs. Inspect links and supported-platform claims. Do not call any image approved solely because the automated checks pass.
