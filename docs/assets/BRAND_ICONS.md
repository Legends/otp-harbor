# Optional local brand icons

OTP Harbor releases do not contain Simple Icons artwork or any other third-party service logos. Account rows use deterministic initial tiles until the user explicitly imports a local icon-pack ZIP that they obtained independently. The ZIP may be a Simple Icons release or a filename-indexed collection of SVG files.

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

1. The user selects a local `.zip` file in **Settings → Import / Export → Optional brand icons**.
2. OTP Harbor treats the archive as untrusted input. It bounds compressed and expanded sizes, rejects unsafe or ambiguous paths, parses JSON and XML with restricted readers, and accepts either a Simple Icons release with `data/simple-icons.json` metadata or a manifest-free filename-indexed pack.
3. A filename-indexed pack may place SVGs in nested folders, but every SVG filename must be a canonical ASCII brand identifier matching `[A-Za-z0-9_]+.svg`. Filenames are indexed case-insensitively, and duplicate identifiers, unsafe names, malformed SVGs, oversized files, or ambiguous archive entries reject the complete import. For example, `microsoft.svg`, `github.svg`, and `google.svg` become the local identifiers `microsoft`, `github`, and `google`.
4. Valid SVG files, relevant upstream notice files, and a compact generated `brand-index.json` are copied beneath the current user's application-data directory in `BrandIcons/packs/`. A small `current.json` pointer activates the fully written pack atomically. Generic-pack notice files named `LICENSE`, `LICENCE`, `COPYING`, `NOTICE`, or `DISCLAIMER`, optionally ending in `.md` or `.txt`, are retained in the pack's local `notices/` directory.
5. The resolver matches the issuer against exact titles/slugs, normalized titles/slugs, upstream aliases, and the embedded versioned alias database in `TOTP.Infrastructure/Branding/issuer-resolver.v1.json`. Exact aliases take precedence over broader prefixes, and an ambiguous alias is not assigned to whichever icon happened to be indexed first. It never examines the OTP secret or infers a brand from the account's email domain.
6. When an icon pack is installed, the account editor offers **Automatic** plus the pack's locally indexed names. A user may select a stable pack identifier for that account; automatic matching remains the default.
7. The shared Avalonia resolver caches path data, brushes, and resolved presentations. OTP timer updates do not repeat issuer resolution or access icon files.

Existing vault and backup formats are unchanged. Automatic matches and absolute asset paths are not persisted. Explicit account choices are stored separately in `BrandIcons/account-brand-settings.json` as an account GUID to stable brand-id mapping. That local display-only file contains no issuer, account name, OTP secret, icon path, or artwork and is excluded from vault backups and exports. Removing a pack restores the generic fallback; reimporting a pack containing the selected identifier restores the explicit icon.

## Privacy and legal scope

Import is entirely local. OTP Harbor does not download icon packs, request favicons, call logo APIs, or transmit issuer/account metadata. Imported artwork is not copied into application releases, installers, backups, or exports.

Resolver entries may be added only when the target identifier matches a supported local pack identifier and the alias is specific enough to avoid misleading matches. Conflicting aliases fail validation. A resolver match produces a logo only when the user has independently imported a local pack containing that identifier; otherwise the generic fallback remains in use.

Simple Icons' [CC0 license](https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md) covers its project/database but does not necessarily grant rights to every underlying trademark or artwork; its [disclaimer](https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md) calls this out explicitly. OTP Harbor preserves the imported pack's `LICENSE.md` and `DISCLAIMER.md`; users remain responsible for confirming that their local use is authorized. Brand display does not imply sponsorship, affiliation, certification, or endorsement.

To update icons, import a newer complete pack ZIP. The previous complete pack remains inactive in the local pack directory so a failed replacement cannot damage the active installation.

The settings page also provides **Remove imported icons** when a pack is installed. Deletion runs away from the UI thread, removes the local imported pack, and restores deterministic placeholder tiles; it does not modify accounts, secrets, backups, exports, or display-only account mappings. **Show issuer logo** controls whether either branded or placeholder tiles are displayed and is enabled by default. That branding-only preference is stored under `BrandIcons/display-settings.json`, outside the strict portable `preferences.json` contract used by released versions.
