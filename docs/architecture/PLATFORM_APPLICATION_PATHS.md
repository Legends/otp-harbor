# Platform application paths

## Status

`IPlatformApplicationPaths` is the source of application filesystem locations. Windows, macOS, and Linux adapters implement the policy below without creating directories or weakening filesystem permissions. The Avalonia composition root selects the host implementation and registers it as the single application-wide instance.

## Path policy

| Asset | Windows | macOS | Linux | Android |
|---|---|---|---|---|
| Packaged configuration | Executable directory, `appsettings.json` | Application bundle/resource directory | Executable/resource directory | Packaged application configuration |
| Vault | `%AppData%\OTP-Harbor\master.vault` | `~/Library/Application Support/TOTP-Manager/master.vault` | `$XDG_DATA_HOME/totp-manager/master.vault` | Private app files, `TOTP-Manager/master.vault` |
| Authorization envelope | `%AppData%\OTP-Harbor\authorization-envelope.bin` | `~/Library/Application Support/TOTP-Manager/authorization-envelope.bin` | `$XDG_DATA_HOME/totp-manager/authorization-envelope.bin` | Private app files, `TOTP-Manager/authorization-envelope.bin` |
| Preferences | `%AppData%\OTP-Harbor\preferences.json` | `~/Library/Application Support/TOTP-Manager/preferences.json` | `$XDG_CONFIG_HOME/totp-manager/preferences.json` | Private app files, `TOTP-Manager/preferences.json` |
| Rotating vault backups | Beside the vault (`master.vault.bak1`…`.bak5`) | Beside the vault (`master.vault.bak1`…`.bak5`) | Beside the vault (`master.vault.bak1`…`.bak5`) | Beside the vault (`master.vault.bak1`…`.bak5`) |
| Logs | `%AppData%\OTP-Harbor\Logs` | `~/Library/Logs/TOTP-Manager` | `$XDG_STATE_HOME/totp-manager/logs` | Private app cache, `Logs` |
| Update state | `%AppData%\OTP-Harbor\autoupdate-state.json` | `~/Library/Application Support/TOTP-Manager/autoupdate-state.json` | `$XDG_STATE_HOME/totp-manager/autoupdate-state.json` | Private app files, `TOTP-Manager/autoupdate-state.json` |

Linux adapters must use these fallbacks when the corresponding absolute XDG variable is absent:

- `XDG_CONFIG_HOME`: `~/.config`
- `XDG_DATA_HOME`: `~/.local/share`
- `XDG_STATE_HOME`: `~/.local/state`

Relative XDG locations are invalid and must not be accepted as application storage roots.

## Compatibility and migration

- macOS, Linux, and Android retain established `TOTP-Manager` or `totp-manager` directory components as compatibility identifiers, not current display branding.
- Windows startup moves the legacy `%AppData%\TOTP-Manager` directory to `%AppData%\OTP-Harbor` only when the current directory is absent or empty; it never merges or overwrites two populated directories.
- Existing configuration overrides for the vault path remain supported and take precedence over adapter defaults.
- Default-path installations migrate `master.totp` and its `.bak1`…`.bak5` generations to `master.vault` on first startup. The encrypted file format is unchanged. Migration hardens each source file before a same-directory atomic rename, never overwrites an existing destination, and leaves a legacy file in place when its corresponding destination already exists.
- Rotating vault backups remain adjacent to the configured vault on every platform. The platform `BackupDirectory` property is reserved for future backup-policy extraction and must not be presented as the current rotating-backup location.
- A future Windows-to-portable migration must discover the existing roaming-data files before creating a new layout. It must use atomic copy/verification and leave rollback data intact.
- Path comparisons and migration fixtures must not contain real secrets.

## Security impact

- The filename migration changes neither vault encryption nor the storage format.
- Platform adapters must fail closed when a required trusted user directory cannot be resolved. They must never fall back to the current working directory or plaintext storage.
- Directory creation and permission hardening remain filesystem boundary operations. OS-specific ACL policy sits behind `IPlatformFileSecurity`; Unix adapters must follow [the platform file-security policy](../security/PLATFORM_FILE_SECURITY.md).
- Logs remain outside secret-bearing data files and must continue through the existing redaction pipeline.
