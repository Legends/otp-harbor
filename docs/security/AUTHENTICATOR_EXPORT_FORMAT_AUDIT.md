# Authenticator export-format audit

Checked 2026-11-13. Scope: major dedicated authenticators first, as requested.
Existing Bitwarden Password Manager TOTP import is retained. New 1Password and
KeePassXC-specific parsers are outside this change. Provider selection remains
optional help; the reader dispatches by supported schema/content. This is an
interoperability review, not an affiliation or market-share claim.

## Major providers

| Provider | Primary repository evidence / specification | Verified format and OTP Harbor coverage |
| --- | --- | --- |
| Aegis | [Vault specification](https://github.com/beemdevelopment/Aegis/blob/master/docs/vault.md) | Formal UTF-8 JSON specification. Outer `version`, `header`, `db`; database entries carry issuer/name and OTP info. OTP Harbor reads plaintext outer v1/database v1–3; encrypted slots are a different format and remain unsupported. |
| 2FAS | [BackupContent](https://github.com/twofas/2fas-android/blob/develop/data/services/src/main/java/com/twofasapp/data/services/domain/BackupContent.kt), [BackupService](https://github.com/twofas/2fas-android/blob/develop/data/services/src/main/java/com/twofasapp/data/services/domain/BackupService.kt), [cipher](https://github.com/twofas/2fas-android/blob/develop/core/cipher/src/main/java/com/twofasapp/cipher/backup/BackupCipherImpl.kt) | Checked the actual serialization models; current source declares schema 4. Services contain a seed and nested OTP parameters. Password protection uses separate encrypted services/reference data. OTP Harbor supports unencrypted schemas 1–4; encrypted exports remain unsupported. No standalone repository format specification was found in this check. |
| Bitwarden Authenticator | [Actual Android exporter](https://github.com/bitwarden/android/blob/main/authenticator/src/main/kotlin/com/bitwarden/authenticator/data/authenticator/repository/AuthenticatorRepositoryImpl.kt), [official export instructions](https://bitwarden.com/help/authenticator-import-export/) | Exporter defines JSON and CSV. JSON uses `encrypted: false` and `items`. Android CSV writes six header names but nine values, including repeated issuer/period/digits. OTP Harbor now tests and supports that specific variant and normal Bitwarden-compatible CSV. No claim that synced Password Manager entries are included in a local Authenticator export. |
| Bitwarden Password Manager (existing support) | [Official export documentation](https://bitwarden.com/help/export-your-data/), [official CLI export formats](https://github.com/bitwarden/clients/blob/main/apps/cli/src/vault.program.ts) | JSON/CSV and encrypted variants are distinct upstream options. OTP Harbor accepts plaintext login TOTP fields only; passwords, notes, cards, passkeys and encrypted exports are not imported. |
| Ente Auth | [Repository export specification](https://github.com/ente/ente/blob/main/docs/docs/auth/migration/export.md), [actual plaintext exporter](https://github.com/ente/ente/blob/main/mobile/apps/auth/lib/ui/settings/data/export_widget.dart) | Plaintext is a newline-separated TOTP URI list and is supported. Encrypted JSON v1 describes KDF parameters, encrypted data and nonce, using Argon2id and XChaCha20-Poly1305. That encrypted reader is not implemented in OTP Harbor. |
| Proton Authenticator | [Serialization/export source](https://github.com/protonpass/proton-pass-common/blob/main/proton-authenticator/src/entry/exporter.rs), [password export source](https://github.com/protonpass/proton-pass-common/blob/main/proton-authenticator/src/entry/password_exporter.rs) | Actual v1 JSON has an entries array whose content distinguishes TOTP from Steam and contains a URI. OTP Harbor supports plaintext TOTP entries. Password-protected v1 uses a different salt/content envelope; encrypted exports and Steam are unsupported. Source inspected at common-library tree `b1a9edc622a5a8e0b284326a5586e6c5f08153b5`. |
| LastPass Authenticator | [Ente interoperability reader](https://github.com/ente/ente/blob/main/mobile/apps/auth/lib/ui/settings/data/import/lastpass_import.dart), [Proton interoperability schema and iOS tests](https://github.com/protonpass/proton-pass-common/blob/main/proton-authenticator/src/parser/lastpass/json.rs) | No public vendor application repository/specification was found. Two independent open-source readers agree on plaintext JSON v1 with accounts and issuer/user/seed/algorithm/digits/time-step fields. OTP Harbor adds this compatible v1 schema with synthetic fixtures; it is not described as a vendor-published specification. Missing issuer may use a nonempty display name as issuer, following the existing import fallback policy. |
| Google Authenticator | [Official Key URI specification](https://github.com/google/google-authenticator/wiki/Key-Uri-Format) | This documents standard `otpauth://` URIs, which are supported. OTP Harbor separately supports Google migration QR payload versions 1–2 and multipart batches. The checked public repository/wiki does not provide a current official specification for that migration protobuf; do not conflate the Key URI document with the transfer wire format. |
| Microsoft Authenticator | [Official backup documentation](https://support.microsoft.com/en-us/authenticator/back-up-your-accounts-in-microsoft-authenticator), [alternative authenticator setup](https://support.microsoft.com/en-us/authenticator/how-to-add-your-accounts-to-microsoft-authenticator) | No public native-app export repository or portable TOTP interchange specification was found. Official documentation describes app/cloud recovery and new registration. OTP Harbor provides re-enrolment guidance and standard TOTP QR entry; it does not claim Microsoft cloud-backup, push or passwordless-credential import. |
| Authy | [Official token export/import policy](https://help.twilio.com/hc/en-us/articles/19753420684059-Export-or-Import-Tokens-in-the-Authy-app-Not-Supported) | Vendor explicitly states token import/export is unsupported. OTP Harbor adds localized guidance for registering standard TOTP again at each service and verifying replacement access. No unofficial extraction or proprietary Authy-token compatibility is claimed. |

## Smaller providers checked as references

- [Stratum's formal backup specification](https://github.com/stratumauth/app/blob/master/doc/BACKUP_FORMAT.md)
  is a useful example. It defines an Authenticators JSON array, numeric OTP-type
  and algorithm identifiers, optional usernames, and separate strong/legacy
  encrypted envelopes. Strong encryption uses Argon2id/AES-GCM; legacy uses
  PBKDF2/AES-CBC. Stratum permits OTP types and digit counts beyond OTP Harbor's
  supported TOTP subset. No Stratum parser was added in this major-provider change.
- [2FAGuard's native backup exporter](https://github.com/timokoessler/2FAGuard/blob/main/Guard.WPF/Core/Export/Exporter/BackupExporter.cs)
  implements a password-protected native envelope. Its source is available, but
  native-format support is not added. Standard URI exports can use generic import.
- [2fast's backup reader](https://github.com/2fast-team/2fast/blob/master/Project2FA.Shared/Services/Importer/TwofastBackupImportService.cs)
  reads an encrypted data-file model with version-dependent key derivation. It is
  not a plain TOTP interchange schema; no native parser is added. “Fast 2FA” was
  checked against this identifiable project, without assuming it is the same as
  every similarly named store app.

## Implementation and test rules

Use provider-owned specifications first, actual serialization source second, and
independent interoperability readers only when labelled as such. Pin fixtures to
an inspected schema; generate fresh synthetic account data rather than copying
public or private users' seeds. Branch links may change after this review.

All readers produce the common Account model and preserve only supported fields.
Foreign IDs are not native vault identity. No provider icons, notes, passwords,
arbitrary metadata or unsupported OTP types are imported. Detecting a provider
never authorizes an account write; preview, explicit conflict decisions, recovery
backup and atomic commit remain in the existing AccountImportService pipeline.
Encrypted foreign formats require their own implementation and security tests;
the OTP Harbor backup password prompt does not imply support for them.

The LastPass-compatible reader has positive and negative synthetic tests covering
renamed extensions, SHA-1/256/512, optional names, digit/period validation,
malformed seeds, unsupported versions, encrypted rejection and safe diagnostics.
Existing providers retain their dedicated regression and content-detection tests.

Rechecked 2026-11-14 against Ente and 2FAS Android/iOS: see the
[source comparison](AUTHENTICATOR_IMPORT_PARITY.md). Legacy 2FAS schema 1 is now
covered, Ente deleted entries are explicitly excluded and reported, and foreign
encryption remains unsupported by the user's explicit scope choice.
