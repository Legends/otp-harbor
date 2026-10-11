# Optional local brand icons

OTP Harbor releases do not contain Simple Icons artwork or any other third-party service logos. Account rows use deterministic initial tiles until the user explicitly imports a local provider-neutral `.otphicons` file produced by the separate OTP Harbor Icon Pack Builder.

## Third-party brand assets

OTP Harbor intentionally does not distribute third-party brand assets. It does not bundle, host, provide, or automatically download third-party brand logos or icon packs.

Users may independently obtain image files or compatible icon packs and import them into OTP Harbor for local use. Such files remain user-provided content and are stored and processed locally.

OTP Harbor does not grant any license or other rights to third-party trademarks, logos, artwork, or other intellectual property. Users are responsible for ensuring that their acquisition and use of imported assets is permitted under the applicable licenses, trademark rules, copyright rules, and brand guidelines.

References to third-party product, service, or company names are used solely for identification and interoperability purposes. They do not imply sponsorship, endorsement, certification, authorization, or affiliation with OTP Harbor.

OTP Harbor is not affiliated with or endorsed by the owners of any third-party trademarks referenced by the application.

Third-party licenses and notices supplied with user-imported packages are preserved where technically applicable. Their presence does not constitute or imply authorization by the relevant trademark or copyright owner.

OTP Harbor does not upload issuer names, account names, imported icons, or other icon-matching data for the purpose of resolving brand assets.

OTP Harbor ships a versioned, logo-free resolver database containing only canonical pack identifiers and textual issuer aliases. These names are used solely for local identification and interoperability. The database contains no artwork, asset URLs, download locations, or claim that a referenced service endorses OTP Harbor.

The OTP Harbor release process includes automated checks intended to prevent third-party brand assets or automatic brand-asset download functionality from being included in official releases, installers, update artifacts, backups, exports, repository artwork, store listings, test fixtures, or other tracked visual media.

Putting an icon pack in another OTP Harbor-owned repository would still make the project a distributor of that pack. OTP Harbor therefore does not publish, mirror, endorse, or designate an official mixed-logo pack. Any deliberate change to this policy requires documented asset-by-asset legal and provenance review.

## Data flow

1. The user independently runs the separate OTP Harbor Icon Pack Builder. The builder owns provider acquisition, provider-specific parsing, source selection, SVG normalization, and package production for the current workflow. OTP Harbor does not reference the builder implementation, and its normal import UI does not accept provider archives directly.
2. The builder creates `otp-harbor-icons.otphicons`, containing selected canonical SVGs, background colors, authoritative issuer aliases, source provenance, and referenced licenses/notices.
3. The user selects that local file in **Settings → Icon packs → Import OTP Harbor icon pack**.
4. OTP Harbor treats the file as untrusted input. It bounds compressed and expanded resources, rejects unsafe or ambiguous paths, and validates `pack.json`, its complete reference graph, every selected SVG, licenses, provenance, canonical IDs, and the authoritative alias index before activation. Validation is all-or-nothing.
5. Valid SVGs, provenance, notices, and a generated `brand-index.json` are copied beneath the current user's application-data directory in `BrandIcons/packs/`. The completed staging directory and installed-pack registry are activated only after full validation succeeds.
6. Automatic resolution uses only the mandatory issuer and requires an exact entry in the pack's authoritative normalized alias index. A lookup miss remains a miss: OTP Harbor does not use account names, substring, prefix, slug-derived, or fuzzy matching. The account editor may store an explicit stable brand ID as a display-only override.
7. The shared Avalonia resolver caches path data, brushes, layers, and resolved presentations. OTP timer updates do not repeat issuer resolution or access icon files.

Existing vault and backup formats are unchanged. Automatic matches and absolute asset paths are not persisted. Explicit account choices are stored separately in `BrandIcons/account-brand-settings.json` as an account GUID to stable brand-id mapping. That local display-only file contains no issuer, account name, OTP secret, icon path, or artwork and is excluded from vault backups and exports. Removing a pack restores the generic fallback; reimporting a pack containing the selected identifier restores the explicit icon.

## Privacy and legal scope

Import is entirely local. OTP Harbor does not download icon packs, request favicons, call logo APIs, or transmit issuer/account metadata. The separate builder may download supported upstream sources only when the user runs it. Imported artwork and generated packs are not copied into OTP Harbor releases, installers, backups, or exports.

Resolver entries may be added only when the target identifier matches a supported local pack identifier and the alias is specific enough to avoid misleading matches. Conflicting aliases fail validation. A resolver match produces a logo only when the user has independently imported a local pack containing that identifier; otherwise the generic fallback remains in use.

Simple Icons' [CC0 license](https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md) covers its project/database but does not necessarily grant rights to every underlying trademark or artwork; its [disclaimer](https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md) calls this out explicitly. Dashboard Icons and Aegis Icons likewise have their own project licenses, notices, and trademark boundaries. OTP Harbor preserves referenced license and provenance files from the imported pack; users remain responsible for confirming that their acquisition, local use, and any sharing are authorized. Brand display does not imply sponsorship, affiliation, certification, or endorsement.

To update icons, import a newer complete `.otphicons` file. A rejected replacement cannot damage the active installation.

The settings page also provides **Remove imported icons** when a pack is installed. Deletion runs away from the UI thread, removes the local imported pack, and restores deterministic placeholder tiles; it does not modify accounts, secrets, backups, exports, or display-only account mappings. **Show issuer logo** controls whether either branded or placeholder tiles are displayed and is enabled by default. That branding-only preference is stored under `BrandIcons/display-settings.json`, outside the strict portable `preferences.json` contract used by released versions.
