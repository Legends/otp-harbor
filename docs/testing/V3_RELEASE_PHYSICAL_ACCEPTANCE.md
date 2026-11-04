# OTP Harbor v3 physical release acceptance

Use only a disposable profile and the synthetic files in this kit. Never record a password,
generated OTP, secret, vault, authorization envelope, Windows Hello material, or secret-bearing
screenshot. Record the exact artifact hash, package version, OS/device version, result, and sanitized
diagnostics.

## Expected fixture results

| File | Expected import |
| --- | --- |
| `release-import-uris.txt` | `Release URI Alpha` and `Release URI Beta` |
| `release-aegis.json` | one `Release Aegis` account |
| `release-twofas.2fas` | one `Release 2FAS` account |
| `release-native.csv` | one `Release CSV` account |
| two Google Authenticator PNGs | one ten-account, two-page transfer completed without duplicates |
| `otp-harbor-demo-icons.otphicons` | local pack imports and supplies the fictional demo icons |

The mixed-account fixtures must classify `Scenario Exact` as unchanged, `Scenario Collision` as a
changed logical match, and `Scenario Older New` as new. Keeping or replacing the collision must
leave exactly four scenario accounts; replacement preserves current ID
`22222222-2222-4222-8222-222222222222`.

## Windows Store flight — required

1. Record the downloaded workflow artifact and MSIX SHA-256. Confirm `store-package.json` names the
   same source commit and package version `3.0.65535.0`.
2. With Store v2.0.1 installed, create a synthetic vault, enable Windows Hello, create an encrypted
   external backup, restart, and prove password and Hello unlock both work.
3. Install the Store-signed v3 flight/update without uninstalling v2 or clearing application data.
   Unlock with the original password. Confirm every account is present, the v2 vault was migrated,
   a rollback snapshot exists, and a second restart/unlock succeeds.
4. Confirm Windows Hello either remains usable or fails safely to the master password and can be
   re-enrolled. Lock the Windows session and verify OTP/QR output is cleared and the app relocks.
5. Complete the mixed-account older-vault scenario in
   `docs/testing/MIXED_ACCOUNT_CONFLICT_SCENARIO.md`, once keeping and once replacing the collision.
   Confirm cancellation writes nothing, the older vault remains unchanged, and an accepted import
   creates a recovery checkpoint.
6. Import each format in the table into a clean disposable v3 vault. For the two Google QR pages,
   scan/import in order and verify the final account count without duplicates.
7. Export a password-protected v3 `.otphbp`, delete one synthetic account, restore the backup, then
   restart and unlock again. Also restore the v2 export created in step 2 into a separate clean v3
   vault.
8. Exercise account add/edit/delete, groups, favourites, search, copy and timed clipboard clearing,
   account QR display, camera denied/no-device/success paths, icon-pack import/remove, Light/Dark
   themes, English/German/French/Spanish, minimum desktop size, and common display scaling.
9. From a clean Windows user/profile, install v3 from the Store flight and repeat setup, restart,
   single-instance activation, protocol/file activation, backup export/restore, and uninstall.
10. Verify `Get-AppxPackage` reports version `3.0.65535.0`, the reserved publisher, and
    `SignatureKind` equal to `Store`. Inspect sanitized logs for secret-bearing values.

## Android — required only for `android-v3.0.0`

1. Review all eight `packaging/android/marketing/en-US` campaign images, all six Fastlane phone
   screenshots, the feature graphic, and icon at full resolution. Confirm no personal data,
   unapproved mark, clipping, misleading UI claim, or wrong theme. Backup/restore is a labeled
   concept composition, not a literal backup screen; the scanner QR is a labeled demonstration.
2. Install the production-signed v3 APK as an update over production v2 without clearing data.
3. Confirm the v2 account survives migration and a second restart/unlock.
4. Repeat the mixed-account scenario and every import fixture above in the isolated Debug package
   or a disposable production profile.
5. Verify biometric/device-credential/password fallback, background and device-lock behavior,
   screenshot policy, camera and file picker, Light/Dark themes, all four languages, compact phone
   width, font scaling, account-list scrolling, icon-pack import/remove, backup export/restore, and
   app restart.
6. In the target Android store console, verify the six screenshot order, feature graphic, icon,
   localized text, privacy/support URLs, content rating, data-safety declarations, signing identity,
   rollout controls, and release notes before submission.

## Partner Center and publication — required

1. Review every one of the ten Store images in all four locales at full resolution. Confirm no
   personal data, real account identity, unapproved mark, clipping, mistranslation, or misleading UI
   claim. Slides 08–11 are conceptual promotions.
2. Obtain the required human trademark/legal review for all fictional names and geometric marks.
3. Run the latest Windows App Certification Kit against the exact candidate.
4. Verify Partner Center package identity, capabilities, age rating, privacy/support URLs,
   availability, localized listing copy, v3 release notes, certification notes, and manual publish
   or flight/gradual-rollout settings.
5. After certification, repeat the Windows checks against the Store-signed private package. Publish
   only after every required row passes; then verify the public Store page before marking v3 public
   in repository metadata.
