# Android application foundation

Android shares the repository and core security architecture with the desktop application. It keeps
a dedicated solution for platform tooling and is validated by supported CI. Once the protected
production-signing environment is configured, tagged builds package it as a separate signed APK in
the same versioned GitHub Release as the desktop applications.

## Implemented development MVP

The current Android application provides:

- password-based creation and unlocking of the production encrypted vault
- account listing, manual creation, editing, and deletion
- account search by issuer or account name
- offline QR capture through the system camera, including Google Authenticator bulk-migration QR codes, explicit conflict handling, and account QR display
- encrypted backup import and export through Android's system document picker
- current TOTP code and countdown display inside every visible account row
- tap-to-copy account rows and thresholded swipe actions for QR display, editing, and confirmed
  deletion
- conditional clipboard clearing without retaining the copied code as adapter state
- a 30-second background grace period for normal app switching, with immediate code removal and
  immediate locking on device lock
- optional strong-biometric quick unlock backed by a non-exportable Android Keystore key, with
  automatic prompting after startup or a background lock, master-password confirmation during
  enrollment, and password recovery at all times
- English, German, French, and Spanish localization with resource-completeness and placeholder checks
- private Android application storage with verified owner-only Unix permissions
- disabled Android backup and cleartext network traffic
- screenshot and recent-app preview protection through `FLAG_SECURE` while the current account list
  and its short-lived TOTP codes are visible
- a focused two-destination mobile shell: codes and security settings

Account rows contain identifiers, display metadata, and short-lived current OTP codes. OTP seeds
remain in the encrypted vault and are loaded only through the existing authorization-aware TOTP
service. The presentation projection is cleared immediately on backgrounding, explicit locking,
device locking, and disposal.

## Mobile product scope

The Android application follows a deliberately smaller mobile surface than the desktop app. Its
primary workflow is opening the app, finding an account, and using a code. Security configuration
is kept out of the code list, while the master password remains a visible recovery option.

The maintained mobile scope is:

- TOTP account capture through the camera or manual entry
- fast account search, code display, and copy
- encrypted local storage, explicit locking, and biometric quick unlock
- encrypted backup import and export through Android's system document picker
- account editing and deletion with recoverable, localized failures

The following desktop concerns are intentionally excluded rather than ported:

- log-folder and diagnostics-folder controls
- desktop minimize and system-session-lock settings
- interface scaling and QR-preview scaling controls
- opening export folders after export
- an in-app desktop installer or updater
- Windows-specific authorization and platform guidance

Android owns lifecycle locking and display scaling. Updates will be delivered by the selected app
distribution channel. Developer diagnostics remain available through Android tooling rather than
as end-user settings.

The following maintained mobile capability remains deliberately deferred until its platform adapter
is complete:

- QR import from an existing image

## Build and install

Use the dedicated `TOTP.Android.sln` to open the Android app, mobile UI, Android adapters, and their
shared Core/Infrastructure dependencies together in Visual Studio. Keeping platform entry points in
separate solution files avoids requiring the Android workload for ordinary desktop development; the
shared release workflow still validates both graphs.

Build the Android solution explicitly:

```powershell
dotnet restore .\TOTP.Android.sln --configfile .\NuGet.config
dotnet build .\TOTP.Android.sln -c Debug
```

Or build the application project directly:

```powershell
dotnet restore .\TOTP.UI.Avalonia.Android\TOTP.UI.Avalonia.Android.csproj --configfile .\NuGet.config
dotnet build .\TOTP.UI.Avalonia.Android\TOTP.UI.Avalonia.Android.csproj -c Debug --no-restore
```

To install on one authorized Android device and launch the app:

```powershell
.\scripts\testing\Install-AndroidDevelopmentBuild.ps1
```

The script performs an in-place development APK install and does not clear app data. USB or paired
wireless debugging must be enabled, exactly one device must be connected, and the computer must be
authorized on that device. Development APKs embed their managed assemblies and therefore do not
depend on IDE-specific Android Fast Deployment state.

## Release policy

1. Never publish development APKs as production artifacts. Debug builds use the isolated
   `io.github.legends.otpharbor.debug` ID.
2. Keep the long-lived Android app-signing key outside the repository and require reviewed CI access.
3. Publish the signed universal APK as a separate asset in the shared GitHub Release; do not place it
   inside desktop packages and do not present an AAB as an end-user download.
4. Test same-key upgrades before every public Android release.

The permanent production application ID is `io.github.legends.otpharbor`. Signing, Android developer
verification, GitHub environment setup, and the future Google Play path are documented in the
[Android release guide](../release/ANDROID.md).

## Security review notes

- **Threat impact:** the host prevents ordinary backup and cleartext network traffic. Android applies
  `FLAG_SECURE` only while the current account list and its short-lived TOTP codes are visible,
  including while an account-list overlay is open. Setup, unlock, account editor, settings,
  and import/export views deliberately permit screenshots and recent-app previews. A generated
  account QR remains part of the account-list surface and therefore stays protected while that
  surface is visible. Unprotected views can expose revealed passwords, OTP seeds, or backup details
  if the user or another capture-capable process records them; this is an explicit usability
  tradeoff and not a confidentiality guarantee. An explicit `EnableMarketingCapture=true` build
  may relax the secure-window flag only for the separate Debug application ID and synthetic test
  data; the Android project rejects that property for Release builds. The primary unlock method is
  an explicit user choice between strong biometrics, Android's secure device credential (PIN,
  password, or pattern as configured by the operating system), and the master password. OTP Harbor
  never creates or stores a separate app PIN. Biometrics remain the first-run default when available,
  while the master password always remains a recovery fallback. Biometric and device-credential
  modes use separate provider identifiers, aliases, associated-data contexts, and Keystore policies.
  Their non-exportable AES-256 keys are usable only for one second after the selected system
  authentication. Biometric keys are invalidated when biometric enrollment changes; device-
  credential keys are not coupled to biometric enrollment. That minimal time window is an
  Android-documented compatibility path for
  devices whose KeyMint implementation rejects an authentication-per-use `CryptoObject`. Code
  executing as the app's UID during that second shares the authorization window; the app does not
  claim to resist a rooted, injected, or otherwise compromised process or device.
- **Data-flow impact:** the existing authorization, encryption, vault, account, settings, TOTP, and
  clipboard services now process production data on Android. Files stay below the private app data
  root. Copied codes enter the system clipboard, are marked sensitive on Android 13 and newer, and
  are conditionally cleared according to the existing setting. A normal app switch retains the
  in-memory vault key for no more than the 30-second grace period while immediately removing all
  displayed codes. While foregrounded, the UI holds one current short-lived OTP string per visible
  account so the primary authenticator list can show and copy codes without a second secret lookup.
  It never receives the underlying seeds. The foreground transition re-evaluates the deadline using monotonic time because
  Android may suspend background execution. With the default app lock enabled, device lock,
  explicit lock, and process termination do not receive this grace period. Enabling quick unlock unwraps the vault key only after recovery
  password verification, then encrypts it with AES-256-GCM inside the authenticated Keystore flow.
  Unlock first completes the selected `BIOMETRIC_STRONG` or `DEVICE_CREDENTIAL` system prompt
  without a `CryptoObject`; the cipher is
  created and completed synchronously in the success callback, within the one-second authorization
  window. The password field remains available if the prompt is cancelled or recovery is required.
  The envelope stores only the Keystore alias, nonce, authenticated ciphertext, and reviewed
  provider metadata; it stores neither the biometric nor an exportable platform key.
  Changing the primary method verifies the master password and registers the replacement Keystore
  wrapper before the envelope is swapped; the prior alias is removed only after persistence succeeds.
  Disabling the app lock is a separate, explicitly warned opt-out that requires the recovery
  password. It replaces any biometric wrapper with a non-exportable Android Keystore AES key whose
  policy permits access without user verification, while retaining the mandatory password wrapper.
  Startup still verifies the recovered DEK against the encrypted vault before exposing codes. With
  this opt-out active, background and device-lock transitions clear displayed code/QR state but keep
  authorization available; anyone using the unlocked device can therefore access the codes. The
  selected interactive method remains in preferences while app lock is off. Re-enabling requires
  the master password when that method is biometric or device credential, recreates its protected
  wrapper, and locks immediately; failure leaves the app lock enabled and falls back safely to the
  master password.
  QR capture delegates still-image acquisition to the installed system camera and decodes only the
  returned in-memory preview with the embedded ZXing decoder. The release manifest requests neither
  network nor camera permission for this flow, the app does not write a captured image, accepts only
  QR payloads, and validates bounded `otpauth://` or Google Authenticator
  `otpauth-migration://` payloads before persistence. Google migration protobuf bytes are decoded
  only in memory and cleared after conversion; multi-account writes require a recovery backup.
  Generated account QR images and their sensitive PNG buffers are disposed when hidden, when the
  selection changes, and whenever the app leaves the foreground.
  Backup export passes account data directly to the existing encrypted stream format and never
  offers plaintext export. Backup passwords are removed from bound state before document I/O.
  Import decrypts only after document selection, requires an explicit count/conflict confirmation,
  creates the existing recovery backup before writes, and skips matching accounts instead of
  overwriting them. An incomplete failed export is removed from its document provider when that
  provider permits deletion.
- **Compatibility impact:** the Android projects remain outside `TOTP.sln`. The infrastructure
  dependency on `NSec.Cryptography` was upgraded from 25.4.0 to 26.4.0 for current Android native
  runtime and 16 KB page-size support; the existing version-2 authorization envelope gains an
  optional, strictly validated Android provider wrapper without changing the password wrapper or
  encrypted-vault format. The optional `appLockEnabled` preference defaults to `true` for existing
  files. Older builds reject the unattended provider for quick unlock and fall back to the still-valid
  password wrapper.
  Strong-biometric quick unlock currently requires Android 11 (API 30) or newer. Earlier supported
  Android versions retain password unlock rather than silently accepting a weaker biometric class.
  Desktop and Android exchange the existing encrypted `.totp` backup format; private live-vault
  files are not exposed as an interchange format.
- **Recovery impact:** the master password remains an independent recovery and authorization method.
  A missing or invalidated Keystore key falls back to password unlock and never bypasses vault-key
  verification. Android backup is intentionally unavailable; deleting app data deletes the local
  vault. Users must not treat this development build as the sole copy of an authenticator account.
- **Test evidence:** localized resource completeness and mobile startup, immediate device locking,
  background grace-period expiry, account projection, and account validation behavior have
  automated coverage. Debug and Release Android builds plus
  desktop regression tests must remain green. Private storage, NSec runtime behavior, lifecycle
  transitions, multi-account code projection, code clearing, and clipboard clearing have automated
  regression coverage. Private storage and NSec runtime behavior have initial physical-device
  evidence and still require a
  broader supported-device matrix before release. Biometric provider metadata, enrollment,
  automatic-prompt cancellation, recovery fallback, and successful unlock have regression
  coverage; enrollment-change invalidation still requires physical-device verification.
  App-lock opt-out startup, background behavior, preference compatibility, provider-policy
  validation, and localized warnings have regression coverage; Keystore persistence across process
  restart remains a physical-device verification gate.
  Mobile screen-capture policy transitions between starting, unlock, account-list, settings,
  editor, and locked states have regression coverage; Android `FLAG_SECURE` behavior and recent-app
  previews remain a physical-device verification gate.
  Mobile navigation, search, unavailable-scanner fallback, localized QR-import outcomes, and
  disposal of generated QR images and sensitive PNG buffers have regression coverage.
  Encrypted-only backup export, immediate password-field clearing, explicit import confirmation,
  and skip-existing conflict policy have regression coverage.

## Physical-device evidence

An Android 16 ARM64 device was used for the first development-MVP verification on 2026-09-02:

- password setup, unlock, process restart, and encrypted account persistence succeeded
- owner-only `0700` directory and `0600` vault-file permissions were confirmed without reading file
  contents
- a public test seed produced the same TOTP as an independent Otp.NET calculation
- conditional clipboard clearing succeeded after 15 seconds
- a 10-second app switch preserved the session, a 35-second switch locked it, and device locking
  caused an immediate app lock
- on 2026-09-03, a Xiaomi/Poco Android 16 device with a Class 3 fingerprint enrolled the
  Keystore-backed wrapper, persisted `PlatformQuickUnlock` as the preferred method, and successfully
  unlocked the existing encrypted vault; the strict authentication-per-use flow returned
  `UserNotAuthenticatedException`, while Android's one-second time-based flow succeeded
- the app remained free of Android crash-buffer entries throughout the completed workflow
- the focused codes/settings navigation, account search field, and QR actions rendered in the
  Android accessibility tree after an in-place wireless-debugging upgrade

This evidence applies to the development APK and does not replace release-signing, biometric
enrollment-change invalidation, camera, import/export, upgrade, or broader device-matrix
verification.
