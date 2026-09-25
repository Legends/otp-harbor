# OTP Harbor recovery guide

Use synthetic data when rehearsing these steps. Before changing any application-data file, close every OTP Harbor process and copy the complete data directory to a separate protected location. Never send a vault, authorization envelope, password, OTP seed, exported backup, Keychain item, or TPM material with a support request.

## Recovery principles

- The master password is the authoritative recovery method. Windows Hello and macOS quick unlock are conveniences, not replacements for it.
- An encrypted `.totp` export plus its separate export password is the preferred migration and disaster-recovery backup.
- Local `master.totp.bak1` through `.bak5` files protect against some recent local write/corruption failures. They are stored beside the vault and are not a substitute for an external backup.
- There is no password bypass, recovery key, cloud copy, or plaintext fallback. Losing both the master password and a usable external encrypted backup is unrecoverable by design.
- Do not install an unsigned build or bypass Gatekeeper/Authenticode to recover data. Preserve the files and use a verified compatible release.

## Restore an encrypted export

1. Verify that the application came from the expected signed release and start it normally.
2. Create or unlock the destination vault with its master password.
3. Open Settings, choose Import / Export, and use **Restore encrypted backup** to select the encrypted `.totp` export through the native file picker.
4. Enter the export password when requested and review the conflict policy before confirming.
5. Confirm that expected synthetic accounts are present and generate a code from a non-production test account.
6. Keep the original backup unchanged until a restart and second unlock have succeeded.

A wrong password, modified file, unsupported format, oversized payload, or failed pre-import backup must leave the current account set unchanged.

## Recover from a rotating local vault backup

Use this only when the current `master.totp` is unreadable and no newer tested encrypted export is available.

1. Close OTP Harbor and make a protected copy of the entire application-data directory.
2. Locate the vault directory for the platform:

   - Windows: `%APPDATA%\TOTP-Manager`
   - macOS: `~/Library/Application Support/TOTP-Manager`
   - Linux: `$XDG_DATA_HOME/totp-manager`, or `~/.local/share/totp-manager` when `XDG_DATA_HOME` is unset

   These legacy directory names are intentionally retained after the OTP Harbor rebrand so existing vaults remain discoverable without migration.

3. Preserve `master.totp`, `authorization-envelope.bin`, and every `master.totp.bak*` file. Do not edit the authorization envelope.
4. Starting with `master.totp.bak1`, copy one candidate to a temporary file named `master.totp`, preserving the original files outside the directory. On Unix, keep the file readable/writable only by the current user.
5. Start the same or a newer compatible signed application and unlock with the master password. If the candidate fails, close the app, restore the protected directory copy, and try the next generation.
6. After a successful unlock, immediately create and test a new external encrypted `.totp` export.

Local backups remain encrypted with the vault DEK. Resetting or replacing the authorization envelope cannot make an old vault decryptable and may permanently remove the password path to it.

## Quick unlock is missing, cancelled, or reset

- Choose the master-password path. Missing TPM/Hello or Keychain state must fall back without modifying the vault.
- After password unlock succeeds, disable/re-enable quick unlock from Settings if the platform capability is available.
- On macOS, deleting/resetting the Keychain item invalidates only that quick-unlock wrapper. On Windows, TPM/Hello reset or account changes can make the device key unavailable.
- Linux uses the master password; Secret Service availability does not enable silent vault unlock.

Do not repeatedly recreate the vault or authorization envelope in response to a native prompt failure.

## Preferences or startup failure

The non-secret Avalonia preference file is separate from the encrypted vault and authorization envelope. If support diagnostics identify only a corrupt `preferences.json`, close the app, preserve the full data/config directories, and move that one preference file aside so reviewed defaults can be recreated. Do not move or delete `master.totp` or `authorization-envelope.bin` as a startup troubleshooting step.

For a failed binary update, leave the data directory untouched and re-extract the exact signed self-contained package into a clean user-writable application directory. The Windows updater attempts transactional rollback, but interruption by power loss or hostile filesystem software can still require manual re-extraction.

## Safe support evidence

Useful non-secret evidence includes:

- release tag, commit, package filename, and SHA-256;
- OS version, architecture, desktop/session type, and display scaling;
- capability states from Support Diagnostics;
- exception type/error code and stage name after redaction;
- whether recovery succeeded with a synthetic vault.

Never attach application-data files or unreviewed logs. Search copied diagnostics for passwords, seeds, OTP codes, clipboard values, local user names, home paths, bearer values, query secrets, and signing credentials before sharing.
