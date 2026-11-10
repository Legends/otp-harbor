# TOTP configuration and compatibility review

Desktop and Android share `AccountTotpService`, `AccountListSnapshotService`,
`OtpNetTotpGenerator`, `TotpConfigurationPolicy`, and the account/import/export/vault
implementations. Supported configurations are SHA1, SHA256, or SHA512 with exactly
6 or 8 digits. Both editors expose these under advanced options. The issuer's
provisioning settings determine the correct configuration; changing them arbitrarily
will produce codes the issuer cannot verify. Existing configurable periods remain
bounded by `TotpPeriodPolicy` (default 30 seconds).

Generation uses the .NET `HMACSHA1`, `HMACSHA256`, and `HMACSHA512` APIs, the
RFC-defined big-endian time counter and dynamic truncation, and invariant fixed-width
decimal formatting. The decoded seed and temporary HMAC output are cleared. A shared
clock controls current and next codes consistently. There is no algorithm fallback,
HOTP support, network resolution, or new cryptographic dependency.

## Data flow and threat impact

`Account.algorithm` / `Account.digits` and the private encrypted `VaultAccountPayload`
carry the configuration. Favorites, group changes, import replacement/keep-both,
QR migration, and account editing preserve it. Configuration-only edits retain the
stored secret. Import conflicts include configuration changes even when the seed is
identical. Validation occurs before imports or account mutations commit unsupported
configurations; generation and QR export also reject invalid settings.

No key derivation, authorization, vault envelope, encryption, associated data, or backup
wrapper changes are made. Metadata stays inside the authenticated encrypted account
section. No OTP, seed, or QR payload logging is added. The security concern addressed
is generating a plausible but incorrect code after silently losing metadata.

Missing metadata defaults to SHA1/6 in legacy native JSON and both legacy/current
vault account payloads. Explicit invalid metadata is not converted to a valid default.
No automatic data rewrite or secret rotation is required. Existing encrypted backups
remain readable. Older OTP Harbor versions that only support SHA1/6 cannot correctly
use accounts newly configured with other settings; restore those accounts with a
version supporting their configuration.

## Format coverage

Secrets use one shared decoder-compatible Base32 rule on Desktop and Android: after
normalizing case, spaces, hyphens and optional trailing padding, the input must contain
only Base32 alphabet characters and decode to at least one byte. No minimum bit
length or entropy claim is imposed on provider-issued secrets. This applies to manual
creation or secret changes, QR/URI scans, third-party imports, native account files,
and manual/automatic backup restoration through the validated import workflow.
There is no decode/re-encode equality requirement: unused trailing bits are handled
by the established decoder. Inputs such as `TEST` and `ASDF` therefore retain their
existing decoded key bytes instead of becoming unrestorable. Empty decoded keys and
invalid alphabet characters still fail. Existing file, archive and migration-payload
size bounds are unchanged.

This interoperability behavior follows the lenient decoding used by
[2FAS Android 6.0.3](https://github.com/twofas/2fas-android/blob/6.0.3/core/otp/src/main/java/com/twofasapp/otp/OtpAuthenticator.kt)
and [Google's published Android decoder](https://github.com/google/google-authenticator-android/blob/master/java/com/google/android/apps/authenticator/util/Base32String.java).
Google's public implementation is archived; this is not a claim about every current
store version or every input quirk of those applications.

This is an interoperability policy, not a statement that every accepted secret has
adequate entropy or meets RFC 4226's minimum key-strength requirement. OTP Harbor
does not issue these seeds and cannot strengthen one without breaking its match to
the provider. Stored accounts are not rewritten or rotated. All validation entry
points now use the same decoder-compatible rule; validation buffers continue to be
cleared, and no seed/OTP logging is added.

| Format | Configuration behavior |
| --- | --- |
| `otpauth://totp/`, single QR and URI-list text import | `algorithm=SHA1/SHA256/SHA512`, `digits=6/8`; omitted values default to SHA1/6; invalid values fail |
| Exported account QR | Includes algorithm, digits, and period |
| Google Authenticator migration QR | Maps protobuf algorithms 1/2/3 and digit counts 1/2; unspecified, MD5 and HOTP fail; the format has no custom period, so 30 seconds remains |
| Unencrypted Aegis vault, outer v1 / database v1–3 | Preserves required `info.algo`, `info.digits`, `info.period`; encrypted Aegis remains unsupported |
| 2FAS JSON / `.2fas`, schema v2–4 | Preserves `otp.algorithm`, `otp.digits`, `otp.period`; existing optional defaults retained; encrypted 2FAS remains unsupported |
| Native JSON | Additive `algorithm` and `digits` account fields |
| Native CSV / pipe-delimited text | Adds trailing `algorithm,digits` / `algorithm\|digits`; original rows and headers still import with defaults |
| Manual encrypted `.otphbp` / legacy `.totp` import | Existing authenticated wrapper; configuration preserved by the enclosed account format |
| Encrypted vault and automatic `.otphbackup` recovery | Same authenticated envelope/sections and portable restoration path; additive account metadata |

The algorithm follows [RFC 6238](https://www.rfc-editor.org/rfc/rfc6238) and
[RFC 4226](https://www.rfc-editor.org/rfc/rfc4226). URI fields follow the
[Google Key URI format](https://github.com/google/google-authenticator/wiki/Key-Uri-Format).
No new third-party export format or encrypted third-party backup support is claimed.

## Verification

`TotpConfigurationTests` uses all 18 official RFC 6238 Appendix B vectors at eight
digits and the corresponding six-digit truncations, including leading zeros. Shared
cases cover every configuration in account generation/snapshots, Desktop/mobile
formatting, native export round trips, encrypted backup round trips, vault persistence,
account copies, QR imports, and invalid metadata. Legacy current vaults are exercised
with metadata genuinely absent from the encrypted payload.

`AutomaticBackupSnapshotServiceTests` verifies portable snapshot create/read with every
configuration. External parser conformance tests cover Aegis, 2FAS and Google migration.
Both editor test suites exercise selection, persistence, resetting defaults, unchanged
seeds, localized errors, and mobile targeted code refresh. Native Android sizing tests
cover eight-digit grouping at compact widths; Avalonia headless tests cover Desktop
row layout at 360, 430 and 640 logical pixels. Physical Android screen/input and
cross-platform desktop rendering remain manual release checks.
