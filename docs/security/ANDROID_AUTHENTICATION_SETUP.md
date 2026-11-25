# Android authentication setup and app-lock recovery

## Security review

- The setup screen now requires an explicit second confirmation before disabling app lock. Cancellation and Android back navigation retain password protection and the initial setup grant.
- The vault encryption format, key derivation, platform key wrapping, and master-password recovery remain unchanged. No migration is required.
- The configured biometric or device-credential method is attempted first when it is usable. The existing authorization service still verifies the current platform wrapper and vault key. A failed platform attempt must not disable app lock.
- Password recovery remains available when platform authorization cannot be used. The UI explicitly explains this fallback; there is no automatic successful authorization.
- Diagnostics record configured/saved/selected methods and platform outcome categories, not passwords, OTP seeds, key aliases, or key material.
- The base revision already includes the password-to-biometric deactivation regression scenario. The reported physical-device failure has not been reproduced in this environment; the added diagnostics distinguish missing wrappers, missing adapters, missing or invalidated Android keys, and unsuccessful prompt results.

## Verification

Updated unit and headless tests cover the initial no-lock confirmation, cancellation/back navigation, localized warnings, and the existing real-service authentication sequence. Run the affected mobile/headless tests and Android build on Windows. On a physical Android device, repeat a clean install, set a master password, enroll biometrics, and deactivate app lock. Check that fresh biometrics are requested and cancellation preserves the lock. If the password prompt appears, inspect the non-secret authorization diagnostics to establish the specific runtime cause. Automated test execution and physical-device verification are pending.
