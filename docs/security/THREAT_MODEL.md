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
  - Local encrypted export files (`.totp`)
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
- Import file parsing (`.totp`, `.json`, `.txt`, `.csv`)
- QR parsing (`otpauth://` and bounded Google Authenticator `otpauth-migration://` payloads)
- Export path handling
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

## 8. Review Cadence
- Update this model:
  - On every security-significant feature
  - On new external dependency introduction
  - Before each production release tag
