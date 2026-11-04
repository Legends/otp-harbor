# OTP Harbor recovery guide

Use synthetic data when rehearsing these steps. Before changing any application-data file, close every OTP Harbor process and copy the complete data directory to a separate protected location. Never send a vault, authorization envelope, password, OTP seed, exported backup, Keychain item, or TPM material with a support request.

## Recovery principles

- The master password is the authoritative recovery method. Windows Hello and macOS quick unlock are conveniences, not replacements for it.
- An encrypted `.otphbp` export plus its separate export password is the preferred migration and disaster-recovery backup. Legacy `.totp` exports remain importable.
- Local `master.vault.bak1` through `.bak5` files protect against some recent local write/corruption failures. They are stored beside the vault and are not a substitute for an external backup.
- There is no password bypass, recovery key, cloud copy, or plaintext fallback. Losing both the master password and a usable external encrypted backup is unrecoverable by design.
- Do not install an unsigned build or bypass Gatekeeper/Authenticode to recover data. Preserve the files and use a verified compatible release.

## Restore an encrypted export

1. Verify that the application came from the expected signed release and start it normally.
2. Create or unlock the destination vault with its master password.
3. Open Settings, choose Import / Export, and use **Restore encrypted backup** to select the encrypted `.otphbp` export through the native file picker. Older `.totp` exports are accepted as well.
4. Enter the export password when requested and review the conflict policy before confirming.
5. Confirm that expected synthetic accounts are present and generate a code from a non-production test account.
6. Keep the original backup unchanged until a restart and second unlock have succeeded.

A wrong password, modified file, unsupported format, oversized payload, or failed pre-import backup must leave the current account set unchanged.

## Recover from a rotating local vault backup

Use this only when the current `master.vault` is unreadable and no newer tested encrypted export is available.

1. Close OTP Harbor and make a protected copy of the entire application-data directory.
2. Locate the vault directory for the platform:

   - Windows: `%APPDATA%\OTP-Harbor`
   - macOS: `~/Library/Application Support/TOTP-Manager`
   - Linux: `$XDG_DATA_HOME/totp-manager`, or `~/.local/share/totp-manager` when `XDG_DATA_HOME` is unset

   The macOS and Linux directory names are compatibility identifiers retained after the OTP Harbor rebrand.

3. Preserve `master.vault`, `authorization-envelope.bin`, and every `master.vault.bak*` file. If a pre-migration `master.totp` or `master.totp.bak*` remains, preserve that too. Do not edit the authorization envelope.
4. Starting with `master.vault.bak1`, copy one candidate to a temporary file named `master.vault`, preserving the original files outside the directory. On Unix, keep the file readable/writable only by the current user.
5. Start the same or a newer compatible signed application and unlock with the master password. If the candidate fails, close the app, restore the protected directory copy, and try the next generation.
6. After a successful unlock, immediately create and test a new external encrypted `.otphbp` export.

Local backups remain encrypted with the vault DEK. Resetting or replacing the authorization envelope cannot make an old vault decryptable and may permanently remove the password path to it.

## Upgrade from an older vault and return to an older release

During upgrade, current OTP Harbor reads a supported legacy `TVLT` vault only to convert it. The first authenticated account load writes the newer `TVL2` vault format before accounts are shown. Version-1 automatic backups remain valid restore inputs; older releases cannot read `TVL2` or version-2 automatic backups. Keep a tested external encrypted export before upgrading, especially on Android where app-private files are not normally accessible for manual recovery.

Before renaming a default `master.totp` vault, or converting an existing `TVLT` vault at `master.vault` or a configured custom path, the app creates and verifies `pre-upgrade-rollback` beside that vault. The folder contains an encrypted `master.totp` copy, the authorization envelope when present, and up to five encrypted rotating backups. It is private to the current user and remains unchanged after conversion. If copying, permission hardening, decryption, or the verified atomic write fails, the v2 vault is not converted and account loading fails. A missing authorization envelope is not reconstructed by this copy.

To use an older signed desktop release after a format upgrade, close every OTP Harbor process and first preserve the entire current data and configuration directories. Restore the matching `master.totp`, authorization envelope, and any desired backup generations from `pre-upgrade-rollback` to the paths expected by that older release. For v2.0.1 on Windows, the older data directory is `%APPDATA%\TOTP-Manager`; newer releases use `%APPDATA%\OTP-Harbor`. The snapshot contains only the files named above, so preserve other older-release configuration and device-bound quick-unlock state separately if needed. Unlock with the original master password and verify a synthetic account before relying on the restored vault. Do not point an older binary at a `TVL2` `master.vault` or a version-2 `.otphbackup` package. If direct file restoration is unavailable, install a current signed release and restore a compatible encrypted export through the application.

If both Windows data directories already contain active vaults, OTP Harbor leaves both directories in place and asks whether to continue with the current vault or review the older accounts. Reviewing requires unlocking the current vault, then entering the older vault's master password. The app reads the older `TVLT` vault and authorization envelope without replacing either file and lists the older account names. Accounts match by stable ID first; a different ID with the same normalized issuer and account name is treated as a logical collision and also enters conflict review. Exact unchanged matches are kept automatically and require no decision. When new accounts remain, the app asks whether to import them. Only matching accounts whose data differs require conflict handling and final confirmation. Replacing a logical collision preserves the current account ID while applying the selected older account data. A verified recovery checkpoint is required before committing changes to the current vault. Cancelling or failing to unlock the older vault leaves the current account set unchanged. The older directory remains available for later recovery; the choice is offered again at a future launch while both vaults exist. If the current vault lacks its authorization envelope, startup blocks new-vault setup rather than overwriting it.

## Quick unlock is missing, cancelled, or reset

- Choose the master-password path. Missing TPM/Hello or Keychain state must fall back without modifying the vault.
- After password unlock succeeds, disable/re-enable quick unlock from Settings if the platform capability is available.
- On macOS, deleting/resetting the Keychain item invalidates only that quick-unlock wrapper. On Windows, TPM/Hello reset or account changes can make the device key unavailable.
- Linux uses the master password; Secret Service availability does not enable silent vault unlock.

Do not repeatedly recreate the vault or authorization envelope in response to a native prompt failure.

## Preferences or startup failure

The non-secret Avalonia preference file is separate from the encrypted vault and authorization envelope. If support diagnostics identify only a corrupt `preferences.json`, close the app, preserve the full data/config directories, and move that one preference file aside so reviewed defaults can be recreated. Do not move or delete `master.vault`, a legacy `master.totp`, or `authorization-envelope.bin` as a startup troubleshooting step.

For a failed binary update, leave the data directory untouched and re-extract the exact signed self-contained package into a clean user-writable application directory. The Windows updater attempts transactional rollback, but interruption by power loss or hostile filesystem software can still require manual re-extraction.

## Safe support evidence

Useful non-secret evidence includes:

- release tag, commit, package filename, and SHA-256;
- OS version, architecture, desktop/session type, and display scaling;
- capability states from Support Diagnostics;
- exception type/error code and stage name after redaction;
- whether recovery succeeded with a synthetic vault.

Never attach application-data files or unreviewed logs. Search copied diagnostics for passwords, seeds, OTP codes, clipboard values, local user names, home paths, bearer values, query secrets, and signing credentials before sharing.
