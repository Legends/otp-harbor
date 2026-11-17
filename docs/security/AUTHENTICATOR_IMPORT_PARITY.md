# Ente Auth / 2FAS importer comparison

Checked 2026-11-14. Scope: major dedicated authenticators, existing Bitwarden
Password Manager support, and supported plaintext TOTP fields. The user explicitly
chose to audit encrypted formats while keeping foreign imports plaintext-only.
This is a source/schema review, not a claim of compatibility with every upstream
release or an export produced by every actual app installation.

## Evidence and inventory

Read Ente at commit `c2e571c599222ef6f8349dcc638bf23e0d885e21`, 2FAS Android
at `4ad83b2b94967993db55dd36dcb2e258646eb7b6`, 2FAS iOS at
`56bfaaaee1319cf257bec310a1f029152ce4c6cf`, and Bitwarden iOS at
`31b15f8ecb3caf0f9f4521dd63f4b55404c02617`.

Ente's [importer directory](https://github.com/ente/ente/tree/c2e571c599222ef6f8349dcc638bf23e0d885e21/mobile/apps/auth/lib/ui/settings/data/import)
contains Aegis, 2FAS, Bitwarden, LastPass, Proton, Google migration QR, plain URI,
Ente-encrypted, andOTP, Raivo, OTP Auth and Open Authenticator readers. These are
implementation evidence; their permissive parsing or fallbacks are not a
specification published by the originating app.

2FAS Android's [source selector](https://github.com/twofas/2fas-android/blob/4ad83b2b94967993db55dd36dcb2e258646eb7b6/feature/externalimport/src/main/java/com/twofasapp/feature/externalimport/domain/ImportType.kt)
lists Google, Aegis, Raivo, LastPass, Authenticator Pro, andOTP, Bitwarden, Proton,
1Password, Ente and OTP Auth. Its own backup reader is separate from that list.
The iOS [parsers](https://github.com/twofas/2fas-ios/blob/56bfaaaee1319cf257bec310a1f029152ce4c6cf/TwoFAS/Data/Interactors/ImportFromFileInteractor%2BParsers.swift)
were also checked. Supporting the union of those lists is not this change's scope:
new small-provider and password-manager integrations remain outside the agreed
priority. No third-party code, logos, sample seeds or assets were copied.

## Provider-owned evidence and results

| Provider | Official specification / exporter evidence | Comparison and OTP Harbor result |
| --- | --- | --- |
| Aegis | [Official vault specification](https://github.com/beemdevelopment/Aegis/blob/master/docs/vault.md) | Plain outer v1 / database v1–3 TOTP layout already matched. Added rejection of ambiguous duplicate fields in nested provider JSON. Empty issuer fallback and SHA-1/256/512, 6/8 digits and periods remain covered. HOTP/Steam/MOTP/Yandex remain outside the supported OTP subset. |
| 2FAS | [Legacy iOS schema](https://github.com/twofas/2fas-ios/blob/56bfaaaee1319cf257bec310a1f029152ce4c6cf/TwoFAS/Data/MainRepository/DataTypes/ExchangeData.swift), [v3/v4 model](https://github.com/twofas/2fas-ios/blob/56bfaaaee1319cf257bec310a1f029152ce4c6cf/TwoFAS/Data/MainRepository/DataTypes/ExchangeData2.swift), [iOS exporter](https://github.com/twofas/2fas-ios/blob/56bfaaaee1319cf257bec310a1f029152ce4c6cf/TwoFAS/Data/MainRepository/MainRepositoryImpl%2BExport.swift), [Android model](https://github.com/twofas/2fas-android/blob/4ad83b2b94967993db55dd36dcb2e258646eb7b6/data/services/src/main/java/com/twofasapp/data/services/domain/BackupService.kt) | The official iOS importer explicitly handles schemas 1/2 and 3/4. Added missing plaintext schema 1 to the existing parser; schemas 2–4 remain supported. Nullable/default OTP fields retained. Blank encryption markers agree with the Android model's `isEncrypted` rule; nonblank markers still fail closed even alongside plaintext services. Added final issuer/account length and nonempty-issuer checks plus nested duplicate-field rejection. No standalone format document/schema was found among the checked GitHub repository files; these models/exporters are the authoritative implementation evidence used. |
| Ente Auth | [Official exporter](https://github.com/ente/ente/blob/c2e571c599222ef6f8349dcc638bf23e0d885e21/mobile/apps/auth/lib/ui/settings/data/export_widget.dart), [code storage/export](https://github.com/ente/ente/blob/c2e571c599222ef6f8349dcc638bf23e0d885e21/mobile/apps/auth/lib/store/code_store.dart), [display schema](https://github.com/ente/ente/blob/c2e571c599222ef6f8349dcc638bf23e0d885e21/mobile/apps/auth/lib/models/code_display.dart) | Public plaintext export writes standard URI lines. Its data can contain `codeDisplay.trashed`; the Android 2FAS importer explicitly filters that flag. OTP Harbor previously ignored it, potentially reactivating deleted codes. Deleted entries are now excluded and counted separately before any account merge. Active core TOTP fields are preserved; notes/tags/icons/pinned display metadata are not imported. Malformed or contradictory deletion metadata fails safely instead of guessing. |
| Bitwarden Authenticator | [Official iOS exporter](https://github.com/bitwarden/ios/blob/31b15f8ecb3caf0f9f4521dd63f4b55404c02617/AuthenticatorShared/Core/Vault/Services/ExportItemsService.swift), [official iOS JSON models](https://github.com/bitwarden/ios/blob/31b15f8ecb3caf0f9f4521dd63f4b55404c02617/AuthenticatorShared/Core/Vault/Models/Data/CipherLike.swift), Android exporter referenced in [the previous audit](AUTHENTICATOR_EXPORT_FORMAT_AUDIT.md) | JSON's `encrypted: false`, item type 1 and login fields match. iOS uses the full eleven-column CSV header and may insert blank separators; an additional regression verifies those separators are handled. Android's six-header/nine-value variant remains supported. Malformed, unescaped CSV rows are rejected rather than repaired by guessing. |
| Bitwarden Password Manager | [Official export documentation](https://bitwarden.com/help/export-your-data/), [official CLI](https://github.com/bitwarden/clients/blob/main/apps/cli/src/vault.program.ts) | Existing JSON/CSV TOTP-only import retained. Explicit `passwordProtected: true` now also classifies the input as encrypted, even if combined with a contradictory plaintext flag. No password/notes/passkey or encrypted-vault import added. |
| Proton Authenticator | [Official plaintext exporter](https://github.com/protonpass/proton-pass-common/blob/main/proton-authenticator/src/entry/exporter.rs), [password exporter](https://github.com/protonpass/proton-pass-common/blob/main/proton-authenticator/src/entry/password_exporter.rs), [crypto wrapper](https://github.com/protonpass/proton-pass-common/blob/main/proton-authenticator/src/crypto.rs) | Version-1 `entries/content/entry_type/uri` TOTP layout already matched. Existing tests preserve URI parameters and reject unknown versions, Steam and encrypted envelopes. Display name/note data is intentionally not substituted for the URI's TOTP identity. |
| LastPass Authenticator | No public vendor export specification or exporter repository found; [Ente](https://github.com/ente/ente/blob/c2e571c599222ef6f8349dcc638bf23e0d885e21/mobile/apps/auth/lib/ui/settings/data/import/lastpass_import.dart), [2FAS](https://github.com/twofas/2fas-android/blob/4ad83b2b94967993db55dd36dcb2e258646eb7b6/feature/externalimport/src/main/java/com/twofasapp/feature/externalimport/domain/LastPassImporter.kt) and [Proton](https://github.com/protonpass/proton-pass-common/blob/main/proton-authenticator/src/parser/lastpass/json.rs) supply interoperability evidence | Their issuer/user/secret/algorithm/digits/time-step fields match the implemented compatible JSON v1 reader. Some readers ignore version metadata; OTP Harbor continues to require its tested v1 schema rather than claiming arbitrary or unversioned LastPass compatibility without vendor evidence. |
| Google Authenticator | [Official key URI format](https://github.com/google/google-authenticator/wiki/Key-Uri-Format) | Standard TOTP URIs already supported. Separate Google migration QR/protobuf v1–2 and multipart tests remain; neither Ente's nor 2FAS's protobuf reader substitutes for a current official transfer-wire specification. |

Microsoft and Authy remain migration guidance rather than claimed native portable
importers; the [previous audit](AUTHENTICATOR_EXPORT_FORMAT_AUDIT.md) records the
official evidence and limits. Broader upstream support for HOTP, Steam, other
algorithms/digit counts, groups or custom display information is intentional scope
variation, not permission to silently convert those entries into supported TOTP.

Ente's public exporter also offers a presentation-oriented HTML option; OTP Harbor
does not parse HTML and users should choose its plaintext TXT export. Ente's own
plaintext import code additionally accepts an `items`/`rawData`/`display` JSON shape.
That reader is not evidence that an arbitrary `items` object is a documented Ente
export; the checked public TXT exporter writes URI lines, and no speculative JSON
recognizer was added. Likewise, permissive readers that accept missing versions or
silently discard malformed entries do not override our explicit version checks
and whole-file validation policy.

## Encrypted-format audit only

| Format | Provider-owned evidence / relevant requirements | Status |
| --- | --- | --- |
| Aegis | Official vault document describes password slots wrapping the master key, scrypt parameters, AES-256-GCM slot and database nonces/tags. Ente implements a slot-unwrapping reader. A future implementation must bound file-supplied KDF work, authenticate the key and database and reject unknown versions/slots safely. | Rejected; no decryption implemented. |
| 2FAS | [Backup cipher](https://github.com/twofas/2fas-android/blob/4ad83b2b94967993db55dd36dcb2e258646eb7b6/core/cipher/src/main/java/com/twofasapp/cipher/backup/BackupCipherImpl.kt), [PBKDF2 parameters](https://github.com/twofas/2fas-android/blob/4ad83b2b94967993db55dd36dcb2e258646eb7b6/core/cipher/src/main/java/com/twofasapp/cipher/backup/internal/BackupKeyGeneratorPbkdf.kt), [AES wrapper](https://github.com/twofas/2fas-android/blob/4ad83b2b94967993db55dd36dcb2e258646eb7b6/core/cipher/src/main/java/com/twofasapp/cipher/internal/CipherAes.kt): separate encrypted services/reference, Base64 components, PBKDF2-HMAC-SHA256 with 10,000 iterations/256-bit key and AES-GCM/128-bit tag. Ente has a foreign reader. | Rejected; no decryption implemented. |
| Ente | [Official encrypted-export specification](https://github.com/ente/ente/blob/c2e571c599222ef6f8349dcc638bf23e0d885e21/docs/docs/auth/migration/export.md): JSON v1, Argon2id parameters and salt, XChaCha20-Poly1305 data/nonce; the password belongs to the export. A future reader must bound untrusted memory/operations and validate/authenticate every envelope field. | Rejected; no decryption implemented. |
| Proton | Official password exporter derives a 32-byte Argon2id v0x13 key with 19 MiB, two iterations and one lane from a 16-byte salt. The crypto wrapper uses AES-256-GCM, a prepended 12-byte nonce and AAD `proton.authenticator.export.v1`. Ente implements a matching reader; 2FAS Android's checked foreign importer rejects it. | Rejected; no decryption implemented. |
| Bitwarden | Official exports distinguish account-restricted encrypted JSON and password-protected portable exports; these have different recovery/portability requirements. Checked Ente/2FAS Bitwarden readers do not provide a general encrypted Bitwarden importer. | Rejected; no decryption implemented. |

Do not weaken OTP Harbor's own KDF or copy foreign crypto parameters into native
vault policy. Its own encrypted `.otphbp`, legacy `.totp` and `.otphbackup` readers
and password/recovery behavior remain unchanged.

## Security, data flow and regression evidence

`TrashedCount` is transient import diagnostic metadata, not a new Account or vault
field. Only active decoded accounts reach the unchanged AccountImportService.
Deleted entries appear in the selected-file attention count and review exclusion
count/text; they prevent the clean/duplicate-only shortcuts from hiding exclusions.
An all-deleted export produces a localized acknowledgement on both platforms with
no preview/backup/commit. Invalid/unsupported active entries still reject the whole
file; decoded counts never authorize a partial import. Current vault data changes
only through explicit accepted existing merge operations.

No secrets or raw metadata enter UI bindings/logs; Ente display JSON is temporary,
depth-bounded and inspected only for its deletion marker. Duplicate provider JSON
fields and ambiguous deletion markers are rejected. Existing byte limits, strict
UTF-8, off-thread parsing, temporary-buffer cleanup and password cancellation remain.
Source help never controls parsing. No new packages, encryption implementation,
file persistence, update or authorization changes were introduced.

`ProviderFormatParityTests` uses independently generated synthetic schema examples:
2FAS v1–4/defaults/identity bounds/markers, nested duplicates, iOS Bitwarden CSV,
Ente active/deleted/malformed/mixed files and encoded metadata containing query
delimiters and Unicode. Presentation tests check exact EN/DE/FR/ES messages/counts
on desktop and mobile; host tests verify all-deleted files cannot write. These are
schema-based regression tests, not golden exports collected from every actual app
version. Existing provider, import/backup/restore/conflict and headless suites must
remain passing.
