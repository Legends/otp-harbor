# Legacy DPAPI settings format

This document records the settings format in use before the portable authorization-envelope migration. It is an implementation inventory, not a proposal for the new format.

The unpublished development-era reader has been removed. No runtime compatibility path for this format is shipped; synthetic fixtures remain solely as security and format-design evidence.

## Storage location and override

The default Windows path is:

```text
%APPDATA%\TOTP-Manager\settings.totp
```

The retired Windows implementation derived this path from the roaming application-data directory. Its packaged `AppSettings:StorageFilePath` setting could override the default, with environment-variable expansion and full-path normalization.

Despite the `.totp` suffix, this file is not an account export or the encrypted account vault. It contains preferences and authorization key-wrapping metadata.

## Outer file format

The complete file is the raw byte array returned by:

```csharp
ProtectedData.Protect(jsonBytes, optionalEntropy: null, DataProtectionScope.CurrentUser)
```

There is no application-defined magic value, format version, length prefix, checksum, detached metadata, or outer encoding. The application passes no optional entropy and uses the default DPAPI flags and description.

Consequences:

- Windows DPAPI owns the opaque outer blob layout.
- Decryption requires the Windows current-user protection context. The format is not directly portable to another OS or user profile.
- DPAPI protection is not the same as the TPM-backed Windows Hello wrapper stored inside the JSON.
- A migration reader cannot identify the format from an application header; it must use trusted path/context and a guarded DPAPI-decrypt-and-parse attempt.

## Decrypted JSON format

The DPAPI plaintext is UTF-8 JSON produced by `System.Text.Json` with `WriteIndented = true`. No custom converters or naming policy are configured.

Therefore:

- property names use the PascalCase .NET names;
- enums are JSON numbers;
- byte arrays are Base64 strings;
- `TimeSpan` is a JSON string such as `"00:10:00"`;
- null properties and public read-only computed authorization properties are emitted;
- unknown properties are ignored during normal deserialization;
- missing properties retain the initializers/defaults of the newly constructed model.

An illustrative password-configured payload is shown below. Base64 values are placeholders, not real key material.

```json
{
  "CultureName": "en",
  "MinimumLogLevel": 2,
  "Authorization": {
    "Gate": 2,
    "PasswordSalt": "<16-byte Base64>",
    "ArgonIterations": 3,
    "ArgonMemorySize": 65536,
    "PasswordWrappedDek": "<AES-GCM ciphertext-and-tag Base64>",
    "DekNonce": "<12-byte Base64>",
    "HelloWrappedDek": null,
    "HelloKeyId": null,
    "IsConfigured": true,
    "IsPasswordSetup": true,
    "HasHelloSetup": false
  },
  "IdleTimeout": "00:10:00",
  "LockOnSessionLock": true,
  "LockOnMinimize": true,
  "ClearClipboardEnabled": true,
  "ClearClipboardSeconds": 15,
  "QrPreviewScaleFactor": 1.5,
  "ExportEncrypt": true,
  "OpenExportFileAfterExport": true,
  "HideSecretsByDefault": true
}
```

The computed `IsConfigured`, `IsPasswordSetup`, and `HasHelloSetup` values are ignored on read because they have no setters. They are redundant and must not be treated as authoritative migration input.

## Current fields

### Preferences

| JSON field | Type/default | Meaning |
| --- | --- | --- |
| `CultureName` | string, `en` | UI culture |
| `MinimumLogLevel` | numeric `AppLogLevel`, `Information` = 2 | application logging threshold |
| `IdleTimeout` | `TimeSpan`, 10 minutes | automatic lock timeout |
| `LockOnSessionLock` | boolean, `true` | lock after the platform reports session lock |
| `LockOnMinimize` | boolean, `true` | lock when the main window is minimized |
| `ClearClipboardEnabled` | boolean, `true` | enable delayed clipboard clearing |
| `ClearClipboardSeconds` | integer, 15 | clipboard-clear delay |
| `QrPreviewScaleFactor` | number, 1.5 | QR preview scale |
| `ExportEncrypt` | boolean, `true` | default export-encryption choice |
| `OpenExportFileAfterExport` | boolean, `true` | open unencrypted export after creation |
| `HideSecretsByDefault` | boolean, `true` | default secret visibility |

### Authorization metadata

| JSON field | Representation | Meaning |
| --- | --- | --- |
| `Gate` | numeric enum: None 0, Hello 1, Password 2 | preferred unlock gate |
| `PasswordSalt` | Base64 byte array; currently 16 bytes | Argon2id salt |
| `ArgonIterations` | integer; currently 3 | Argon2id passes |
| `ArgonMemorySize` | integer; currently 65536 | Argon2id memory in KiB |
| `PasswordWrappedDek` | Base64 byte array | 32-byte DEK encrypted with AES-256-GCM; NSec output includes the authentication tag |
| `DekNonce` | Base64 byte array; currently 12 bytes | AES-GCM nonce for the password wrapper |
| `HelloWrappedDek` | Base64 byte array; normally 256 bytes for the current RSA-2048 key | DEK encrypted by the Windows platform key using RSA-OAEP-SHA256 |
| `HelloKeyId` | string such as `TOTP_TPM_<guid>` | key name in the Microsoft Platform Crypto Provider |

The password KEK is derived from the UTF-8 master password with Argon2id. Degree of parallelism is fixed in code at 1 and is not persisted. Stored KDF parameters are validated before unwrap: passes 1-10, memory 8-262144 KiB, salt exactly 16 bytes, nonce exactly 12 bytes, and wrapped data at least one AES-GCM tag.

The password wrapper is the recovery path. The Hello wrapper is a device-local convenience path. Both currently wrap the same vault DEK and are then enclosed together by the outer DPAPI blob.

## Historical parse behavior

`AppSettingsDAL.LoadAsync` first attempts to deserialize the plaintext as `AppSettings`. It catches only `JsonException`; after such an exception it rewinds the stream and attempts to deserialize the same plaintext as a standalone `AuthorizationProfile`, then places that profile into a new `AppSettings` object.

This is not an explicit or reliable version discriminator:

- a standalone legacy `AuthorizationProfile` containing only valid fields such as `Gate` and `PasswordSalt` can be accepted as `AppSettings` with those unknown top-level fields ignored;
- in that case the fallback is never reached and authorization can appear unconfigured;
- the existing regression test forces the fallback by including an incompatible numeric `Authorization` property;
- there are no checked-in byte-for-byte historical fixtures yet.

The synthetic/historical fixture task must cover both the intended legacy profile and the exact payloads produced by released builds. Migration code must not retire the old file until the recovered authorization data and vault have been verified.

## Read, write, and failure behavior

### Read

1. A missing or zero-length file returns “no stored settings.”
2. The containing directory and existing file are restricted to the current Windows user before content is read.
3. The entire file is read into memory and passed to DPAPI `Unprotect` with current-user scope and no entropy.
4. The plaintext bytes are deserialized using the current-first, conditional-legacy behavior above.

DPAPI failures map to `AppSettingsDecryptFailed`; JSON failures map to `AppSettingsDeserializeFailed`; ACL/access and I/O failures have distinct result codes.

### Write

1. The full `IAppSettings` object is serialized to UTF-8 JSON in memory.
2. The JSON is DPAPI-protected in memory.
3. A same-directory file named `settings.totp.<guid>.tmp` is created with exclusive access.
4. The encrypted bytes are written and the temporary file is restricted to the current user.
5. `File.Move(temp, destination, overwrite: true)` replaces the destination.
6. Temporary-file deletion is attempted in `finally`.

This staging protects the previous destination when temporary-file creation, writing, or ACL hardening fails. The implementation does not currently flush the file and directory metadata to stable storage, keep a bounded migration backup, or verify a reread before replacement; those guarantees belong to later M2 tasks.

## Filesystem protection

`WindowsFileSecurity` rejects reparse points, disables inherited ACLs, sets the current user SID as owner, and grants only that SID full control on the settings directory and file. ACL hardening is fail-closed for the load/save operation.

DPAPI and ACLs are defense-in-depth controls. A process already executing as the same user can generally access the current-user DPAPI context and should remain in the threat model.

## Memory and migration cautions

The current DAL holds encrypted bytes, serialized JSON, and decrypted JSON in managed byte arrays. Those arrays are not explicitly zeroed, and disposing the `MemoryStream` does not clear its backing array. The future migration reader should minimize lifetimes and clear temporary plaintext/key-bearing buffers where practical.

Do not log decrypted JSON, Base64 authorization fields, DPAPI blobs, key identifiers beyond what is operationally necessary, or raw migration fixtures derived from a real user profile. All fixtures must use synthetic keys and salts.

## Historical implementation and retained evidence

- `OtpHarbor.DAL/Services/AppSettingsDAL.cs` (removed)
- `OtpHarbor.Core/Models/AppSettings.cs` (current non-secret settings shape; no legacy authorization member)
- `OtpHarbor.Core/Security/Models/AuthorizationProfile.cs` (removed; represented only by synthetic fixture DTOs in tests)
- `OtpHarbor.Infrastructure/Security/MasterPasswordService.cs`
- `OtpHarbor.Platform.Windows/Security/HelloGate.cs`
- `OtpHarbor.Platform.Windows/WindowsFileSecurity.cs`
- `OtpHarbor.Tests/Fixtures/LegacySettings` (synthetic evidence retained)
