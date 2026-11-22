# Android cold startup and deferred views

## Startup contract

The account list is revealed only after every account has been loaded, its initial
TOTP snapshot has been prepared, its local brand match has been resolved, and the
native list has completed its first layout. Accounts are not paged or incrementally
appended. Normal native view recycling remains enabled.

For unified `.otphicons` installations, account startup reads the bounded installed
index, checks issuer-index keys, duplicates and references, and materializes only
brands needed by the complete account list and explicit icon overrides. Each
selected brand uses the existing provenance, alias-completeness, asset-path and
runtime-appearance validators. PNG decoding and hash validation remain unchanged.
The full catalogue for the icon picker is initialized after startup, on a worker.
Older provider formats use their existing complete loading and matching behavior.

No new icon cache, account metadata file, storage format or migration is introduced.
The imported archive, provenance and notices are retained. Matching uses issuers
and explicit selections, never account names. Persisted selections accept canonical
hyphenated brand IDs as well as legacy IDs.

## Native Android startup handoff

Android holds its native splash through account preparation and the first native
account-list layout. The rows are made visible underneath it, followed by two
Avalonia rendering opportunities before the native surface is removed. The normal
Android launch does not show the Avalonia loading overlay or wait for its 360 ms
fade. Holding the exit-animation surface instead of blocking pre-draw allows the
underlying account list to lay out while the splash is present.

Unlock, onboarding and localized startup recovery are actionable destinations;
they release the native splash without waiting for account data that may require
user interaction. A startup failure keeps the existing app recovery overlay and
Retry action available. Retry and later post-unlock account reloads may use that
app overlay after the native splash has already gone away.

Automatic biometric prompts remain deferred until the native provider has been
removed and its removal has reached an Android rendering opportunity. A ready
presentation never marks the authorization session unlocked. A new activity
resets native-surface readiness; callbacks from an older view cannot release its
replacement's splash. Restored sessions keep their current workflow, while a
restored account screen still waits for any incomplete account snapshot/layout.

Secondary preloading starts after actual splash removal, keeping it off this
critical path. The fully-drawn report is emitted at that handoff. The additional
`destination-ready` diagnostic separates prepared content from visible content.

Native branding uses the existing Android resources: the Android 9-11 compat
splash artwork is static, while Android 12+ uses the animated vector. This follows
the platform splash implementations and adds no new artwork or visual theme.

## Secondary views

The lightweight Settings landing page remains available immediately. Heavier
settings sections, editors and dialogs use retained `DeferredContent` templates.
A requested section is constructed synchronously on the UI thread, independently
of the remaining preload queue. Once the startup reveal completes, sections are
created and measured one at a time at application-idle dispatcher priority, with
settings sections first. Input and rendering can run between sections. Detaching
the view cancels pending preloading; attaching it again can resume warming.

Preloading does not make controls visible or replace localization bindings. It
preserves state on subsequent navigation. Native group-picker and import-review
hosts are attached when their deferred content is created. Settings, authorization,
back navigation and keyboard-inset policies remain owned by their existing layers.

Idle priority cannot interrupt one expensive construction/layout callback. Actual
frame pacing and first-navigation latency must still be checked on the target
phone, especially when navigating immediately after a cold launch.

## Security and compatibility review

Vault encryption, password derivation, Android Keystore recovery, verification,
authorization, backup policy and screen-capture protection are unchanged. The new
lookup consumes only the existing local icon index and non-secret issuer values.
All loaded accounts and initial codes remain required before reveal. Unused brand
records are validated when the complete catalogue is initialized; selected records
are validated before use. Invalid optional icons fall back without blocking vault
access. No network access or upstream asset acquisition is added.

The native handoff changes presentation timing only. Authorization initialization,
vault verification and biometric eligibility remain owned by the existing
services/view model. Regression coverage verifies that pending automatic biometric
unlock cannot start merely because its unlock screen is ready; it starts only
after the actual native removal callback. Empty vaults, initial onboarding,
startup failures, localized recovery/retry and activity recreation are covered to
prevent a native splash from hiding required user interaction.

Existing icon notices, user responsibility wording and third-party links are
preserved. Reviewed upstream guidance: [Simple Icons disclaimer](https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md),
[Aegis Icons project/license](https://github.com/aegis-icons/aegis-icons), and
[Dashboard Icons license](https://www.dashboardicons.com/license). Collection
licenses do not override individual asset licenses, trademark rights or brand-use
terms; this startup change grants no additional rights or affiliation.

## Verification and phone measurements

Automated tests cover all 40 initial account codes and requested icon matches,
deferred full-catalog initialization, explicit overrides, unknown issuers, rejected
unsafe assets/corrupt alias references, retained view state, hidden preloading and
early Settings navigation. Headless checks exercise 360, 430 and 768 logical-pixel
viewports; they do not establish Android frame pacing.

Use `scripts/testing/android/Install-AndroidReleasePhone.ps1` for an ARM64 Release
Test build. `-BuildOnly` prepares and verifies the signed non-debuggable AOT APK
without a device; a normal run installs with `adb install -r` and preserves data.
The isolated `.releasetest` ID does not replace a production installation.

With the phone connected, compare repeated process-cold launches with the same
accounts, icon pack, theme and app-lock preference. Record the `OtpHarborStartup`
logcat stages (`services-ready`, `view-created`, `first-frame-ready`, `content-ready`)
and Android's fully-drawn timing. Stage times start at Application.OnCreate; Android's
system measurement also includes earlier process/runtime startup. Check immediate
Settings navigation and scrolling during preloading. Do not capture account codes,
seeds, passwords or screenshots of personal accounts in diagnostic artifacts.
