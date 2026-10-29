# OWASP Desktop Security Checklist (Concrete, Module-Based)

Status values:
- `Implemented`: control exists and is enforced in code/config.
- `Partial`: control exists but coverage/strictness is incomplete.
- `Missing`: control not present or not enforceable yet.

Modules:
- `UI`: `OtpHarbor.UI.Avalonia.Desktop` and `OtpHarbor.UI.Avalonia.Shared`
- `Core`: `OtpHarbor.Core`
- `Infra`: `OtpHarbor.Infrastructure`
- `DAL`: `OtpHarbor.DAL`
- `DevSecOps`: `.github`, `scripts`, `docs/security`

## Checklist
| ID | OWASP-aligned control | UI | Core | Infra | DAL | DevSecOps | Evidence |
|---|---|---|---|---|---|---|---|
| DS-01 | Sensitive local data encrypted at rest | Implemented | Implemented | Implemented | Implemented | Implemented | AES-GCM vault, authorization envelope v2, encrypted `.otphbp` export, plaintext-preference allowlist |
| DS-02 | Sensitive keys isolated in memory and cleared | Partial | Implemented | Implemented | Implemented | Implemented | `SecurityContext`, `SensitiveBuffer`, provider/camera/QR/updater temporary clearing; managed UI strings remain a platform limitation |
| DS-03 | Modern crypto + authenticated encryption (AEAD) | Implemented | Implemented | Implemented | Implemented | Implemented | AES-256-GCM, Argon2id, Ed25519 release verification |
| DS-04 | KDF parameters validated against abuse bounds | Implemented | Implemented | Implemented | Implemented | Implemented | Strict algorithm/iteration/memory/parallelism/salt/nonce bounds and hostile-parameter tests |
| DS-05 | Import parsers resistant to malformed/oversized files | Implemented | Implemented | Implemented | Implemented | Implemented | 5 MiB limit, bounded encrypted framing, deterministic JSON/CSV/TXT/TOTP fuzz tests |
| DS-06 | Least-privilege filesystem protection for secret/settings files | Implemented | Implemented | Implemented | Implemented | Implemented | Fail-closed Windows ACL and Unix 0700/0600/reparse/symlink policies on matching CI runners |
| DS-07 | Secure write pattern (atomic writes, temp file) | Implemented | Implemented | Implemented | Implemented | Implemented | Exclusive staged writes, harden-before-replace, bounded backups, rollback regression tests |
| DS-08 | Authorization required for sensitive operations | Implemented | Implemented | Implemented | Implemented | Implemented | Recovery-password authority, reviewed quick unlock, lock/session policy, authorized-shell and confirmation boundaries |
| DS-09 | Security logging without secret leakage | Implemented | Implemented | Implemented | Implemented | Implemented | Structured and rendered redaction plus exception-type-only startup/UI security boundaries |
| DS-10 | Build/release security gates (SAST/SCA/secrets) | Missing | Missing | Missing | Missing | Implemented | `.github/workflows/security-audit.yml`, `SECURITY_VERIFICATION.md` |
| DS-11 | Signed release artifacts and key custody | Missing | Missing | Missing | Missing | Partial | Store-only unsigned MSIX assembly and provenance checks are implemented without certificate secrets; Microsoft certification/signing and physical acceptance remain external gates; no SignPath Foundation certificate exists |
| DS-12 | Security tests for critical controls | Implemented | Implemented | Implemented | Implemented | Implemented | Crypto/envelope/store/import/update/logging/clipboard/platform failure and recovery suites |

## Remaining release risks

1. Complete independent penetration testing against exact signed release artifacts.
2. Inspect managed-process memory around string-backed password/secret UI bindings and decide whether a platform-specific secure-input redesign has sufficient benefit.
3. Complete physical platform, assistive-technology, native-store reset, clipboard-manager, camera, and clean-machine update acceptance.
4. Complete Microsoft Partner Center identity reservation, account/MFA review, Store certification, signed private-install verification, and physical acceptance.
