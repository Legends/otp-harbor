# Android authentication setup and app-lock recovery

## Security review

- The setup screen presents an explicit, localized not-recommended warning on the no-app-lock choice. Selecting it immediately consumes the existing initial setup authorization grant and opens the app without a second confirmation. Android back navigation continues to select master-password protection; failed or cancelled no-lock enrollment leaves protection enabled.
- The vault encryption format, key derivation, platform key wrapping, and master-password recovery remain unchanged. No migration is required.
- The configured biometric or device-credential method is attempted first when it is usable. The existing authorization service still verifies the current platform wrapper and vault key. A failed platform attempt must not disable app lock.
- Password recovery remains available when platform authorization cannot be used. The UI explicitly explains this fallback; there is no automatic successful authorization.
- Diagnostics record configured/saved/selected methods and platform outcome categories, not passwords, OTP seeds, key aliases, or key material.
- The base revision already includes the password-to-biometric deactivation regression scenario. The reported physical-device failure has not been reproduced in this environment; the added diagnostics distinguish missing wrappers, missing adapters, missing or invalidated Android keys, and unsuccessful prompt results.

## Verification

Updated unit and headless tests cover single-tap initial no-lock selection, failure/cancellation safety, localized warnings, and the existing real-service authentication sequence. Run the affected mobile/headless tests and Android build on Windows. On a physical Android device, repeat a clean install, set a master password, enroll biometrics, and deactivate app lock. Check that fresh biometrics are requested and cancellation preserves the lock. If the password prompt appears, inspect the non-secret authorization diagnostics to establish the specific runtime cause. Automated test execution and physical-device verification are pending.

## Fixed-policy selection isolation

App-lock deactivation now captures the current authorization policy and passes that method to the operation explicitly. Hidden two-way selectors cannot change a fixed selection, and only an explicit password confirmation can take the password route. The trusted recovery transition is separate from the binding setter. Existing cryptographic verification, policy checks and cancellation remain intact. Added unit coverage for a synchronous stale password write-back and headless coverage exercising the actual Avalonia selectors, success, cancellation and password-required recovery. Physical-device reproduction and test execution remain unverified until validation.

## Initial setup navigation refinement

The redundant confirmation panel has been removed at the user's request. The first-install option retains its warning and uses the same one-time authorization grant; deactivation from Security settings still requires fresh configured authentication. Regression tests now assert that one pointer click or command invocation finishes the setup and opens the accounts view. Automated validation of this refinement is pending. The user confirmed the preceding authentication fix on Android.
