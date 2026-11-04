# OTP Harbor threat model

## 1. Scope
- Product: OTP Harbor (Avalonia cross-platform desktop and Android clients)
- In scope:
  - Local secret handling (OTP seeds, export/import files, passwords, keys in memory)
  - Settings and security workflows
  - Build and release pipeline
- Out of scope:
  - Third-party services not controlled by this repository

## 2. Architecture Summary
- Client: Avalonia Windows/macOS/Linux desktop application
- Data stores:
  - Plaintext allowlisted non-secret preferences (`preferences.json`)
  - Password-wrapped authorization envelope (`authorization-envelope.bin`)
  - AES-GCM encrypted account vault
  - Local encrypted export files (`.otphbp`, plus legacy `.totp` imports)
  - Versioned automatic recovery copies (`.otphbackup`) in a user-selected folder or Android document tree
- Security services:
  - Recovery-password authorization and platform quick unlock
  - Versioned key wrapping, envelope activation, and vault-key verification

## 3. Assets and Trust Boundaries
- Assets:
  - OTP secrets
  - Master password
  - Data Encryption Key (DEK)
  - Password-wrapper and platform quick-unlock metadata
  - Exported encrypted backup files
  - Signing/release credentials in CI
  - Signed update feeds, release payloads, and artifact manifests
- Trust boundaries:
  - User input boundary (UI and file dialog)
  - Process memory boundary
  - Filesystem boundary
  - Windows Hello/TPM provider boundary
  - macOS LocalAuthentication/data-protection Keychain boundary
  - Linux session D-Bus/Secret Service boundary
  - CI/CD boundary (GitHub Actions, secrets)

## 4. STRIDE Threat Analysis
| Threat | Example | Current Mitigation | Gap / Action |
|---|---|---|---|
| Spoofing | Unattended or injected input reaches import/export workflows | Authorized-shell boundary, owned native dialogs, password-protected export/import, explicit conflict/confirmation paths | Validate native dialog ownership physically |
| Tampering | Modified encrypted export payload | Bounded authenticated decryption, wrong-password/tamper result, round-trip and fuzz regressions | External parser/crypto review |
| Tampering | Modified, partial, or rolled-back automatic backup generation | Bounded strict package codec, encrypted-vault authentication, SHA-256 publication check, unique immutable generation names, write/read verification, and multi-generation retention | User-selected providers can still delete or roll back every generation; keep an independent copy for high-value recovery |
| Tampering | Replaced, corrupted, or rolled-back authorization envelope | Strict bounded codec, authenticated password wrapper, candidate-key vault verification, atomic replacement, one bounded backup | Add explicit rollback-version policy before multi-device synchronization |
| Tampering | Substituted update feed, payload, or target package | Ed25519 feed/payload verification, target/channel matching, signed manifest, platform signing, bounded transactional replacement and rollback | Physical signed handoff plus external review |
| Repudiation | No trace of security-sensitive actions | Allowlisted startup/security status and typed failure logging with centralized rendered-text redaction | Release-log review; never add secret-bearing audit fields |
| Information Disclosure | Secrets/passwords retained in memory | Owned clearable buffers, short-lived copies, pinned DEK storage, lock/disposal zeroing, and presentation cleanup | Managed string bindings remain; perform external memory inspection |
| Information Disclosure | Authorization material accidentally enters plaintext preferences | Allowlisted `AppPreferencesV1` mapper and strict unknown-field rejection; authorization has a separate encrypted envelope | Require data-classification review for every new preference field |
| Information Disclosure | Platform-store secret leaks through helper process arguments or logs | macOS uses in-process Security.framework; Linux sends clearable base64 only through `secret-tool` stdin and bounds stdout; logs contain status/type only | Physical memory/log review remains a release gate |
| Spoofing | Focus loss or unrelated D-Bus traffic is treated as a session lock | macOS reads OS session lock state; Linux accepts only selected ScreenSaver `ActiveChanged` signals | Validate selected desktops physically and report unsupported environments explicitly |
| Denial of Service | Malformed import or update payload crashes workflow | Size/item/KDF bounds, DTD prohibition, streamed downloads, error mapping, deterministic fuzz/adversarial tests | Continue fuzz corpus expansion |
| Elevation of Privilege | Weak CI/release controls | Required security workflows, vulnerability audit, secret scan, mandatory target signing/notarization, signed aggregate metadata | Verify repository branch rules and credential rotation operationally |

## 5. Attack Surfaces
- Import file parsing (`.otphbp`, legacy `.totp`, `.json`, `.txt`, `.csv`)
- QR parsing (`otpauth://` and bounded Google Authenticator `otpauth-migration://` payloads)
- Export path handling
- Automatic-backup folder/document-provider permissions, generation rotation, and restore parsing
- Password prompt and validation flows
- Authorization-envelope and preferences file replacement
- Windows Hello/TPM and macOS Keychain/LocalAuthentication registration, reset, and unlock
- Linux Secret Service and desktop session-lock signal handling
- CI/CD pipeline and release artifacts
- Dependency supply chain
- Windows updater process and temporary staging/rollback directories
- macOS bundle/notarization and Linux package-manager ownership boundaries

## 6. Security Controls Baseline
- Centralized password validation service
- Mandatory recovery-password wrapper independent of platform quick unlock
- Candidate DEK verification against the vault before envelope activation
- Bounded parsing, atomic envelope replacement, and fail-closed authorization results
- Sensitive-data cleanup for prompt workflows
- Exception handling/logging in workflow boundaries
- Automated CI checks for:
  - SAST (CodeQL)
  - SCA (NuGet vulnerability/deprecation scan)
  - Secret scanning (Gitleaks)
  - Optional DAST (ZAP baseline) for externally reachable endpoints
  - target-native dependency closure and runtime loading
  - signed artifact/feed/manifest verification
  - cross-platform real-XAML headless loading

## 7. Residual Risks
- Manual penetration testing still required for release confidence
- Desktop runtime hardening (host OS, malware resistance) is environment-dependent
- A local attacker able to run as the user can replace or roll back application files; authenticated unwrap and vault verification prevent silent key substitution but do not provide a monotonic anti-rollback counter
- A valid older authorization envelope can be replayed by a same-user local attacker; strict version/crypto validation prevents format downgrade but there is no trusted monotonic envelope counter
- The Windows updater can restore files after ordinary copy failure/cancellation, but power loss or hostile interference during rollback still requires manual re-extraction of a signed package
- Loss or reset of Windows Hello/TPM or macOS Keychain state disables quick unlock; recovery remains dependent on the master password
- Linux desktop D-Bus and Secret Service behavior varies by distribution/session; unsupported or misconfigured environments remain password/manual-lock capable and are reported explicitly
- Third-party dependency risk remains ongoing and must be continuously monitored
- Automatic backups share the vault master password's recovery boundary. Loss of that password, loss of all selected-provider generations, or a hostile provider that deletes or rolls back every generation remains unrecoverable.

## 8. Review Cadence
- Update this model:
  - On every security-significant feature
  - On new external dependency introduction
  - Before each production release tag

## 9. Desktop backup/recovery workflow review (2026-09-25)

- **Threat impact:** Encrypted exports remain password-protected and now use the `.otphbp` filename extension. The restore action accepts only `.otphbp`, legacy `.totp`, or automatic `.otphbackup` selections, rejects other extensions before decoding, validates the password through authenticated import, and requires explicit conflict confirmation before account mutation. Exported location text contains only the user-selected path or a provider-managed-location notice; no password, seed, or decrypted payload is displayed or logged. The folder launcher is offered only when the provider supplies a local path.
- **Data-flow impact:** Export streams account data through `IExportService` to the native save destination, then hardens local files for the current user. Restore streams the selected file through password validation and encrypted import, then delegates account changes to the existing pre-import-backup workflow. Provider-backed files remain under the document provider's ownership and are not passed through local ACL hardening.
- **Compatibility/migration impact:** The encrypted backup payload, vault format, account schema, and generic import formats are unchanged. New manual exports use `.otphbp`; existing `.totp` exports remain readable, so no backup-data migration is required.
- **Verification evidence:** Full solution Debug build completed with zero warnings and errors. The full solution test run passed 1,034 core tests, 110 Avalonia headless tests, and 13 Unix tests (five expected platform skips). Focused backup/recovery coverage includes 16 `NativeFilePickerViewModelTests` and dedicated settings UI structure coverage.

## 10. Automatic backup workflow review (2026-10-18)

- **Threat impact:** Automatic backups are opt-in and are written only to a desktop folder or Android Storage Access Framework document tree explicitly selected by the user. A generation is one ZIP container with an encrypted account section, encrypted profile section, portable master-password wrapper, and strict manifest. A unique backup ID and section hashes prevent accidental mixing, while matching revision IDs inside both independently authenticated sections reject deliberate cross-generation substitution. Platform quick-unlock, biometric, device-credential, and unattended-unlock wrappers are deliberately stripped. Package/configuration parsing is bounded and strict, ambiguous or extra content fails closed, completed writes are read back and hash-verified, and filenames are unique so an interrupted write cannot overwrite the last known-good generation. The destination path or URI is non-secret configuration, protected for the current user where the platform permits, and is never included in diagnostic messages.
- **Data-flow impact:** Successful account mutations and master-password changes enqueue a one-second coalesced snapshot. Unlock retries pending state safely. Vault writes independently encrypt account secrets and profile metadata with distinct AEAD domains but the same random revision ID. The coordinator fingerprints both encrypted sections plus the portable envelope, skips unchanged content only after confirming that the exact last successful generation is still present, retains the five newest generations plus the newest generation from each of the seven newest UTC days, and records success only after publication and configuration persistence. Android retention deletes provider-owned generations through `DocumentsContract.DeleteDocument`; a provider rejection stops that retention pass after the first failure, preserving the remaining generations and bounding failure work and logging. Destination access, the last typed failure, and its timestamp are persisted without provider exception text or paths and are exposed with a manual retry. Before account deletion, a synchronous checkpoint verifies the current vault revision; a failed checkpoint produces a separate destructive confirmation rather than making external storage a hard dependency. Restore requires the master password that protected the selected generation, verifies and recombines the linked sections in memory, decrypts into an independent account collection, and then enters the existing preview/conflict/recovery-backup import workflow; it never replaces the active vault file directly.
- **Compatibility/migration impact:** Current vault writes use the split `TVL2` account/profile format. Version-1 `.otphbackup` packages remain valid restore inputs. A legacy `TVLT` vault is read solely for one-time conversion to `TVL2` during the first authenticated account load, before accounts are shown. Authorization-envelope and manual encrypted-backup payload formats are unchanged; new manual exports use `.otphbp` and legacy `.totp` exports remain readable. Existing configurations without health or recovery-file identity are accepted and create a fresh verified generation on the next backup attempt. Older OTP Harbor versions cannot read `TVL2` vaults or version-2 automatic backup containers; users must not downgrade after the first migrated vault write without restoring a compatible earlier generation. Disabling the feature deletes only its destination configuration and never deletes previously created recovery generations.
- **Legacy upgrade rollback hardening (2026-11-02):** Before a default legacy filename move, startup creates a private `pre-upgrade-rollback` directory beside the vault. For a legacy-header vault at the current or a configured custom path, the first account load verifies or creates this copy before an atomic `TVL2` write. It contains only encrypted vault and rotating-backup bytes plus the authorization envelope when present. Source and snapshot paths receive current-user file restrictions, copies are flushed and hash-verified, and a staged directory is published only after verification. Existing snapshots are checked before reuse, including a partially completed backup filename move; mismatch, reparse points, or permission failures stop migration. This adds an encrypted local copy to the data flow and increases local storage use, but does not create a plaintext export or relax vault authentication. It does not make `TVL2` readable by older binaries. Windows application-data directory migration now follows single-instance ownership so another running process cannot race the move. The recovery guide documents manual rollback and the v2.0.1 Windows path distinction. Filename-migration and snapshot regressions cover intact, tampered, interrupted, custom-path, and permission-failure cases; a physical Android migration and a full downgrade rehearsal remain manual release checks.
- **V2 vault conversion review (2026-11-02):** After password or platform unlock verifies the vault key, the first account load decrypts a `TVLT` vault in memory, verifies or creates its private encrypted rollback copy, then encrypts and atomically commits `TVL2` before returning accounts to the UI. The old on-disk bytes remain if authentication, decryption, snapshot validation, or staged commit fails. The encrypted input and output buffers are cleared after use; no plaintext migration file is written. Direct import writes also verify the rollback copy before replacing a legacy active vault. This changes the timing of format migration from a later account mutation to the first authenticated load; v2 binaries still require manual restoration of the preserved old vault and matching envelope. Focused DAL, filename, vault-codec, backup, and rollback tests cover conversion, no-op, custom/separate paths, wrong key, tampering, permission failure, and failed atomic commit. Physical Android and real-v2-binary downgrade tests remain release checks.
- **Windows side-by-side legacy-vault recovery (2026-11-03):** When both the old `%APPDATA%\TOTP-Manager` vault and current `%APPDATA%\OTP-Harbor` vault exist, startup leaves both directories untouched and asks which source to review. The current authorization boundary remains mandatory; reading old accounts additionally requires the old master password to unwrap the old envelope and authenticate the `TVLT` vault. The old encrypted files are size-bounded, checked for reparse points, and cleared from temporary buffers after reading. The existing restore workflow previews conflicts and requires a verified recovery checkpoint before any current-vault mutation. Cancellation, invalid password, malformed files, and failed recovery backup preserve both on-disk vaults. This adds read-only access to the older Windows directory but no plaintext file, new cryptographic format, or automatic overwrite. Unit and headless regressions cover the read boundary, localization, startup choice, and compact dialog layout; a Store-packaged v2-to-v3 rehearsal remains a release check.
- **Verification evidence:** Codec regressions cover ZIP round trip, required entries, profile tampering, unexpected content, ambiguous legacy JSON, and rejection of device-specific wrappers. Snapshot regressions verify linked multi-group restoration, wrapper stripping, legacy package restore, wrong-password failure, and independent vault-key use. Vault regressions cover old single-group migration, overlapping memberships, section authentication, and revision matching. Coordinator regressions cover publication, verified deduplication, missing-generation recreation, inaccessible-destination health, and failed-write state. Account-deletion regressions cover the failed-checkpoint second-confirmation boundary on desktop and Android. Account-manager regressions verify that only successful mutations request a backup. Localized desktop/Android UI structure and resource-completeness tests cover setup, health, retry, backup-now, and disable actions. Android receives a best-effort `Documents` initial-location hint and localized subfolder guidance; document providers may ignore the hint. Persisted SAF permission behavior and lifecycle recovery still require physical-device validation.
