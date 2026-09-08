# Microsoft Store release

Microsoft Store is the primary Windows distribution channel for OTP Harbor. GitHub remains the
source repository and provides explicitly marked preview archives for advanced testing. Direct
GitHub builds discover updates through the project's Ed25519-signed appcast; Store builds leave
update discovery and installation to Microsoft Store.

The Store path uses MSIX. Microsoft signs an MSIX/AppX package after it passes Store certification,
so the Partner Center submission does not require a CA-trusted project certificate. This does not
make an unsigned MSIX safe for sideloading: the package created by this repository is a submission
input and must not be attached to a GitHub Release or distributed directly.

## Partner Center prerequisites

The maintainer must complete these account-owned steps before the first submission package can be
built with its real identity:

1. Register a Microsoft Store developer account.
2. Reserve the product name `OTP Harbor` as an MSIX/PWA app.
3. Copy the case-sensitive package `Identity/Name`, `Publisher`, and publisher display name from
   Partner Center.
4. Configure English, German, French, and Spanish Store listings and complete the age-rating,
   privacy, support, availability, and pricing sections.

Reviewed draft copy for all four listings is in
[`packaging/windows-store/STORE_LISTING.md`](../../packaging/windows-store/STORE_LISTING.md).

Do not guess the identity values. Partner Center rejects packages whose manifest identity does not
match the reserved product.

## Build the submission package

Every supported release tag (`v<major>.<minor>.<patch>` and
`v<major>.<minor>.<patch>-rc<number>`) automatically builds a Partner Center MSIX from the same
validated self-contained Windows payload used by that release. The GitHub release is published
only after this package succeeds. Download the 90-day Actions artifact named
`otp-harbor-microsoft-store-<release-version>` from the release workflow run. The unsigned Store
input remains intentionally separate from the public GitHub Release assets.

The Store's four-part numeric version cannot contain a prerelease suffix and reserves its fourth
component. OTP Harbor therefore maps the release tag as follows: the first component is the SemVer
major; the second is `minor * 1000 + patch`; the third is the RC number, or `65535` for a stable
release; and the fourth is `0`. For example, `v2.0.0-rc14` becomes `2.0.14.0`, `v2.0.0` becomes
`2.0.65535.0`, and the next patch preview `v2.0.1-rc1` becomes `2.1.1.0`. This keeps previews below
their matching stable release and later patch releases above it.

For an ad-hoc rebuild, use the reviewed workflow **Build Microsoft Store MSIX**, or run locally on
Windows with the Windows SDK installed:

```powershell
.\scripts\release\New-MicrosoftStoreMsix.ps1 `
  -IdentityName '<Partner Center Identity Name>' `
  -Publisher '<Partner Center Publisher, such as CN=...>' `
  -PublisherDisplayName '<Partner Center publisher display name>' `
  -Version '2.0.0.0'
```

The fourth version component is reserved by Microsoft and must remain `0`. The output under
`artifacts/store` contains:

- the unsigned x64 MSIX for Partner Center only;
- `store-package.json` with the identity, version, distribution policy, and hash; and
- `SHA256SUMS` for maintainer-side handoff verification.

The packager publishes a self-contained Windows build, sets the distribution mode to `store`,
disables application-owned updates, removes the standalone updater, generates deterministic Store
tiles from the reviewed app icon, validates the manifest with MakeAppx, and then removes staging
files. Store-managed updates are the only update mechanism for this package.

## Certification and release

1. Verify the workflow commit and downloaded artifact hash against `store-package.json`.
2. Upload only the MSIX to the reserved Partner Center product.
3. Complete all four localized listings and upload screenshots containing synthetic accounts only.
4. Run the current Windows App Certification Kit where possible and resolve every failure.
5. Submit for certification with manual publishing selected for the first release.
6. After certification, install from the private Store link and verify package identity, launch,
   account CRUD, QR scanning, encrypted backup/restore, Windows Hello enrollment, lock behavior, and
   Store-managed update behavior.
7. Publish only after physical acceptance succeeds.

On an installed Store build, this command should report `SignatureKind` as `Store`:

```powershell
Get-AppxPackage | Where-Object Name -Like '*OtpHarbor*' |
  Select-Object Name, Publisher, Version, SignatureKind
```

## Migration from a GitHub preview

The Store package has a Windows package identity and must be treated as a separate installation.
Before switching, export and test an encrypted `.totp` backup from the existing app. Import that
backup into the Store build using the master password, verify several synthetic codes, and then
re-enroll Windows Hello. Do not copy live vault or quick-unlock files between installations.

Keep the GitHub preview installed only long enough to confirm the migration. The Store listing must
explain this first-release migration path before public availability.

## Microsoft references

- [Choose a Windows app distribution path](https://learn.microsoft.com/windows/apps/package-and-deploy/choose-distribution-path)
- [Microsoft Store app certification process](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-certification-process)
- [Reserve an app name and obtain package identity](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name)
- [Create a Store submission](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/create-app-submission)
- [MSIX package requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements)
