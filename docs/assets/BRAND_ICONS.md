# Optional local brand icons

OTP Harbor releases do not contain Simple Icons artwork or any other third-party service logos. Account rows use deterministic initial tiles until the user explicitly imports a Simple Icons release ZIP that they obtained independently.

## Data flow

1. The user selects a local `.zip` file in **Settings → Import / Export → Optional brand icons**.
2. OTP Harbor treats the archive as untrusted input. It bounds compressed and expanded sizes, rejects unsafe or ambiguous paths, parses JSON and XML with restricted readers, and accepts only the expected `data/simple-icons.json`, `icons/<slug>.svg`, `LICENSE.md`, and `DISCLAIMER.md` content.
3. Valid SVG files, the upstream notices, and a compact generated `brand-index.json` are copied beneath the current user's application-data directory in `BrandIcons/packs/`. A small `current.json` pointer activates the fully written pack atomically.
4. The resolver matches the issuer against exact titles/slugs, normalized titles/slugs, upstream aliases, and a small conservative known-alias table. It never examines the OTP secret or infers a brand from the account's email domain.
5. The shared Avalonia resolver caches path data, brushes, and resolved presentations. OTP timer updates do not repeat issuer resolution or access icon files.

Existing vault and backup formats are unchanged. No absolute asset path or detected brand identity is persisted on an account. A future explicit picker can pass a stable brand id to the existing resolver API without a storage-format dependency on icon locations.

## Privacy and legal scope

Import is entirely local. OTP Harbor does not download icon packs, request favicons, call logo APIs, or transmit issuer/account metadata. Imported artwork is not copied into application releases, installers, backups, or exports.

Simple Icons' [CC0 license](https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md) covers its project/database but does not necessarily grant rights to every underlying trademark; its [disclaimer](https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md) calls this out explicitly. OTP Harbor preserves the imported pack's `LICENSE.md` and `DISCLAIMER.md`; users remain responsible for their local use of third-party marks. Brand display does not imply sponsorship, affiliation, or endorsement.

To update icons, import a newer complete Simple Icons release ZIP. The previous complete pack remains inactive in the local pack directory so a failed replacement cannot damage the active installation.

The settings page also provides **Remove imported icons** when a pack is installed. Deletion runs away from the UI thread, removes the local imported pack, and restores deterministic placeholder tiles; it does not modify accounts, secrets, backups, or exports. **Show issuer logo** controls whether either branded or placeholder tiles are displayed and is enabled by default. That branding-only preference is stored under `BrandIcons/display-settings.json`, outside the strict portable `preferences.json` contract used by released versions.
