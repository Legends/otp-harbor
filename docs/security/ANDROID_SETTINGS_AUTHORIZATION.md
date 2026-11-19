# Protected settings and initial quick unlock

## Desktop preferred unlock method

Desktop uses the same authenticated settings boundary as Android when changing
from an enrolled platform method to master-password unlock. It calls
`ConfigureUnlockMethodWithPlatformAsync` with the **current** platform method and
Password as the destination; the native platform prompt must succeed before the
preference is saved. It does not request the destination password merely because
Password was selected. OS cancellation, failed verification and exhausted retries
retain the previous preference and radio selection. Only an unavailable/policy-blocked
provider or a PasswordRequired result permits explicit master-password recovery.

Enabling quick unlock while Password is current always verifies the master
password through enrollment, even when a platform wrapper already exists. Desktop
no longer calls `SetGateAsync` to reselect that wrapper without authentication.
Password fallback uses the existing localized dialogs and the service's combined
password-verification/change API. Master-password rotation still requires the
current recovery password through its existing password lifecycle.

Settings authorization has a cancellation lifetime for locking, closing settings,
disposal and shutdown. Tokens reach the platform authorization service and dialogs;
late success cannot update a closed settings surface. Concurrent clicks do not start
overlapping requests. Existing service generation/key checks and temporary-buffer
cleanup remain authoritative. No cryptographic algorithm, KDF, envelope format,
recovery wrapper, platform adapter, stored-data migration or diagnostic logging
changes are introduced. Switching to Password retains the recovery wrapper and
does not enroll a new platform wrapper.

Regression evidence: `AuthorizationSettingsViewModelTests.CurrentMethod` covers
both platform-method projections, native success/rejection/cancellation, localized
password recovery in all four supported languages, password verification when
reselecting quick unlock, repeated clicks and cancellation on session/settings
closure. `PortableSensitiveAuthorizationTests.ChangeToPassword` checks fresh
platform verification before saving, save failure, preserved recovery/enrollment
boundaries and clearing copied keys. Native Windows Hello/macOS/Linux prompt
interaction remains a manual verification requirement.

## Android protected settings

Desktop App Lock uses the same mutation facade with desktop OS-backed unattended
adapters. Its storage, policy integration, cancellation and compatibility review
are documented in [Desktop App Lock](DESKTOP_APP_LOCK.md).

Android uses the configured vault unlock method to authorize enabling or disabling app lock,
changing the unlock method, and revealing an existing account's secret for editing.
Master-password authorization remains available as recovery. Passwords needed to
encrypt or decrypt portable backups remain independent of device authentication.
Changing the recovery master password continues to require the current master
password through the existing password-wrapper lifecycle. The method chooser
does not replace that recovery-password verification or the new-password fields.

The Android app-lock toggle also authenticates immediately with its configured
platform method, without an authentication-method chooser. Deactivation uses the
current enrolled method. Reactivation uses the saved preference from before
deactivation, even though unattended session state projects Password. A configured
password or unavailable platform method shows only master-password entry; another
available platform method is never substituted. Cancellation or failed authorization
does not apply the toggle. Reactivation shares the desktop authorization operation:
the native adapter verifies once while registering the actual vault key, then a
session/key/preference/generation guard allows the wrapper commit. Successful
activation keeps the verified session and Settings open. Partial activation
failures still lock. Any pre-verification background time is excluded from the
newly enabled background grace period; future background timeouts still lock.
Duplicate taps are blocked,
session closure cancels pending platform authorization, and late availability
results cannot open a prompt after lock/disposal. This changes presentation and
method selection and scoped reactivation orchestration: the shared facade authorizes and persists the
mutation, keeps the recovery wrapper, and preserves the saved unlock preference.
There is no vault, KDF, wrapper-format or migration change. App-lock toggle tests
cover both platform methods, password recovery, active-locale prompts, failures,
unavailable methods, repeated taps and session closure.

Android initially shows a single action to open the master-password editor. Opening
or cancelling that inline editor performs no password change. Its expanded Change
action retains the existing validation and `ChangePasswordAsync` boundary. Cancel,
success, leaving Security, locking and disposal clear all three fields and collapse
the editor; password controls are concealed between openings. Validation and service
failures keep the editor open with cleared fields for retry. Change/Cancel are
disabled during a pending change, and a late completion cannot reopen a locked UI.
The vertical reveal measures natural content height, clips during animation and
hides collapsed fields from focus/accessibility. This presentation change does not
alter cryptography, storage/recovery formats or current-password verification.

## Authorization boundary and threat impact

`PortableAuthorizationService` owns authorization and mutation. The mobile view
model offers choices and invokes this boundary; a UI selection never grants access.
When app lock is enabled, only the enrolled, currently configured platform method
can authorize a protected change. The existing envelope session performs a fresh
platform unlock. An available but unenrolled biometric cannot bypass a password gate.

Selecting a different unlock-method radio immediately invokes the **previously
configured** platform method before enrolling or applying the requested method.
This workflow has no secondary authentication-method chooser or extra Apply step
when current platform verification is available. The selected destination does not
authorize its own enrollment. A cancelled/failed prompt leaves the configured
method unchanged; the UI returns to that authoritative selection. Concurrent taps
do not start another change. A lock cancels the pending operation, and late
completion cannot show success in the locked UI.

When the current method is the master password, its existing password-entry
confirmation remains necessary. If current platform verification requires password
recovery or becomes unavailable, the workflow exposes that password-entry fallback
without an authentication-method dropdown. The existing password service still
performs the actual recovery verification before applying a change.

When app lock is disabled, the UI offers the master password and available Android
strong biometrics/device credential. A silent unattended unlock is **not** proof of
user presence. Instead, the selected Android adapter registers an ephemeral random
32-byte challenge through its existing authenticated encryption prompt. The challenge
contains no vault data, is never persisted, and its temporary Keystore alias is
removed before authorization succeeds. Failed cleanup fails authorization and is
retried; an orphaned alias can contain only a random challenge, never a vault key.

Device credential means the device's existing PIN, pattern, or password; OTP Harbor
does not create or store a separate PIN. Capability checks and Android version
restrictions remain in the existing adapters. Android describes these authenticator
and Keystore boundaries in its [biometric authentication documentation](https://developer.android.com/identity/sign-in/biometric-auth).

The facade captures the unlocked session key and generation before prompting and
checks both again afterward. Locking cancels pending authorizations and invalidates
the initial-setup capability. A late native completion cannot revive a locked
session. A changed key fails closed and locks the session. Neither prompt success
nor a choice alone is sufficient to change vault protection.

## Enrollment data flow and compatibility

After fresh verification, the facade supplies a temporary copy of the session DEK
to the existing enrollment implementation. Enrollment verifies this key against
the stored vault, validates provider metadata, and persists the updated envelope
before removing an old platform registration. Failed persistence removes the new
registration. Cancellation after registration prevents persistence and cleans up.
Owned key/challenge/wrapper buffers are cleared in `finally` blocks. No secrets or
authentication input are added to logs.

The authenticated-key enrollment APIs are internal application service capabilities,
not an external authentication endpoint: callers must use the authorization facade.
They avoid retaining/re-entering a master password after platform authorization.
Existing password APIs retain their behavior. Password recovery,
KDF settings, encryption algorithms, vault/envelope formats, and backup semantics
are unchanged. Disabling app lock remains an explicit warned transition to an
unattended Android wrapper; preference persistence failures retain the existing
password recovery/fail-closed rollback behavior.

After new master-password creation, a single-use in-memory capability allows an
optional quick-unlock enrollment prompt. The UI offers only available methods,
does not enroll automatically, clears setup password fields, and allows skipping
or cancelling. The offer is not repeated for existing vaults or subsequent launches.
An enrollment failure leaves password recovery usable. All new text is localized
in English, German, French, and Spanish.

## First-password setup on Android and desktop

Creating and confirming the master password is mandatory before any optional
device-authentication offer. Only the successful password-creation workflow
triggers the offer; ordinary startup, later unlocks, settings navigation, and
onboarding previews do not. Availability checks never enroll a method. Desktop
shows a confirmation for one available method or a choice for two; Android shows
only its available method buttons. Desktop uses the existing platform adapters,
so the OS controls its supported biometric/PIN mechanisms. An unavailable method
is not advertised, and no app-specific PIN or pattern is introduced.

Both presentations call the same single-use initial-setup capability without
retaining the setup password. Enrollment still verifies the session key against
the vault and preserves the password-recovery wrapper. Skipping consumes the
capability without changing vault protection. Cancellation, adapter failure or
registration failure leaves master-password recovery available.

Desktop awaits the post-password workflow, cancels its dialog/enrollment token on
lock or shutdown, and ignores late success after the authorized shell closes.
Its setup dialogs stack wrapped actions to keep long translations usable at the
minimum window width. Android offers the choice before account loading and loads
accounts after the choice; its native account views are excluded while the modal
is visible. The Android startup transition treats this offer as ready content,
so its account-loading overlay cannot obscure the offer or wait for that choice
to load accounts. Locking or disposing during Android capability checks cannot display
a late offer. These are presentation changes to the existing capability and do
not change the KDF, vault format, recovery format or platform authentication rules.

## Regression evidence

- `PortableSensitiveAuthorizationTests`: current-method enforcement, fresh temporary
  authorization with app lock off, cleanup failure, cancellation/locking/key changes, no-write
  failures, and single-use initial setup.
- `AuthenticatedKeyEnrollmentTests`: both enrollment paths verify the supplied key,
  preserve the password wrapper, clear owned buffers, and roll back failed saves
  and cancellation before persistence.
- `MobileShellViewModelTests`: available-method choices, no forced master password,
  cancellation/failure, background-lock/disposal cancellation of direct secret
  authorization, device-credential-only setup, skip/back and prompt-once.
- `MobileShellViewModelTests.UnlockMethodChanges`: both platform methods authorize
  switching to each other or password directly, no chooser/Apply UI, password-only
  fallback, cancelled/failed prompts, overlapping taps, lock and localized prompts.
- Mobile catalog and interaction tests: localization completeness, shared chooser,
  conditional password fields, and scrollable optional setup UI.
- `MainWindowViewModelTests`: desktop password-first sequence, available-only
  choices, skipping, failed password setup/enrollment, active-locale messages,
  locking during a prompt/enrollment, and no repeated offer on subsequent unlock
  or startup.
- `FirstSetupQuickUnlockOffer_KeepsLocalizedActionsWithinViewport`: desktop setup
  actions fit compact widths and constrained heights in all supported locales.

Physical-device verification remains necessary for the operating-system prompts,
OEM biometric behavior, and touch layout; automated tests do not emulate Android's
secure hardware or user authentication.
