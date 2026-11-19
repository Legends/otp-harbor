# Desktop App Lock

Settings → Security exposes the same Activate/Deactivate App Lock workflow as
Android. App Lock starts enabled. Changing it requires the current configured
authentication method, with the master password available as recovery when that
method is unavailable. Reactivation uses the saved preference from before
deactivation, rather than interpreting unattended startup as password verification.
Linux currently uses master-password verification because desktop quick unlock is
unavailable there. A missing or unavailable secure store cannot disable App Lock.

When disabled, OTP Harbor can open the vault for the signed-in OS user without a
fresh application authentication prompt. Manual, idle, minimize and OS-session
locking are suspended; their saved preferences are retained. Clipboard lifetime
and the authorization boundaries on sensitive actions remain independent.
Successful reactivation keeps the freshly verified session and Settings open.
Future manual/idle/minimize/session locking uses the restored protection. A failure
after the backend has already replaced unattended access and enabled protection
in memory still locks the shell conservatively.

Desktop startup always delegates configured vaults to the shared
`TryUnlockOnStartupAsync` policy, including when the session projects a Password
gate for an unattended wrapper. That projection does not imply a fresh password
verification and must not prevent unattended startup. With App Lock disabled,
only the enrolled OS-backed wrapper can recover the key; the shared service still
verifies that key against the vault before opening the session. A missing or
tampered key retains fail-closed password recovery. The saved biometric preference
remains available for authenticated reactivation; it is not a second unlock path
while the envelope contains only an unattended wrapper. No format or migration
change is required for this startup correction.

## Threat and data flow

Disabling App Lock deliberately gives anyone using the unlocked OS account access
to the codes. It does not make vault storage plaintext, remove the password
recovery wrapper, or make unattended unlock proof of user presence. Same-user
malware, an already compromised OS session, and secrets copied while access was
authorized are outside the protection offered by this mode.

The existing enrollment service first recovers or receives a freshly authenticated
vault key and verifies it against the vault. Only then does it create an OS-backed
wrapper, persist the authorization envelope, and save the disabled preference.
Failed enrollment/persistence retains protection and cleans up incomplete native
registrations where possible. The default platform adapter remains unavailable;
the desktop composition root explicitly registers each supported implementation.

| System | Unattended protection |
| --- | --- |
| Windows | A named RSA-2048 key in the current user's Microsoft Software Key Storage Provider, with private-key export disabled and no user-presence policy. The envelope contains RSA-OAEP-SHA256 ciphertext and its key reference. This is separate from Windows Hello's TPM provider. |
| macOS | A Keychain item with `WhenUnlockedThisDeviceOnly` accessibility and no user-presence flag, in a service distinct from authenticated quick unlock. It holds a random wrapping key. Unattended reads forbid authentication UI; the envelope contains the AES-256-GCM-encrypted vault key. |
| Linux | The existing Secret Service adapter stores a random wrapping key in the user's protected secret collection. The envelope contains the AES-256-GCM-encrypted vault key. A missing Secret Service runtime or denied store operation fails safely. |

The shared secret-store wrapper uses a fresh 32-byte key, a fresh 12-byte nonce
and a 16-byte GCM tag. Associated data binds the application context, provider and
key reference. Native secret stores never receive the actual vault key through
this adapter: a failed registration cleanup can leave only random wrapping
material, without its encrypted vault-key envelope. Tests cover that failure and
reject ciphertext moved between providers.

OS protection is still required: there is no plaintext-file fallback. Windows key
generation runs away from the UI thread. Recovered managed key buffers are cleared
after use; returned sensitive buffers have explicit ownership/disposal. macOS and
Linux share the reference adapter but retain their existing native-store boundaries.
Normal removal deletes the native key/item as well as removing its envelope
reference. Existing enrollment cleanup logs bounded failures if a now-unused
native item cannot be deleted; this does not grant it a new envelope reference.

An unattended adapter cannot be selected for authenticated enrollment or sensitive
authorization. Desktop and Android reactivation share the same operation, combining fresh platform verification with
enrollment of the actual vault key, avoiding a separate challenge/enrollment pair
of prompts. Windows Hello and Android verify once during registration; macOS must retrieve
and compare the newly registered key through its user-presence Keychain item.
Immediately before persisting the wrapper, a caller-owned guard checks that the
session, key, saved preference, lock policy and authorization generation remain
unchanged. Failed/cancelled verification or a changed session removes the new
registration and keeps the old unattended wrapper. The verified wrapper replaces
the unattended wrapper before the enabled preference is saved, so subsequent
failures remain fail closed. This scoped enrollment API does not grant a cached
or reusable authorization bypass. Both hosts keep the verified session open on
success, without a second enrollment or an immediate lock screen. Android renews
its background grace period after verification if the native prompt backgrounded
the app; subsequent background timeouts still lock normally.

Other sensitive operations while App Lock is off still verify through a random,
temporary 32-byte challenge, not the vault key. Windows Hello verifies
during registration; macOS additionally retrieves the challenge through its
user-presence Keychain item and compares the recovered bytes. Cancellation,
challenge mismatch or failed temporary-key cleanup cannot authorize a mutation.
The challenge, recovered copy and wrapper buffers are cleared, and the temporary
native item is removed before success. Android retains its existing authenticated
encryption challenge; it does not gain an extra prompt.

The desktop view model prevents duplicate requests and cancels pending operations
when settings closes, the session locks, or it is disposed. Password verification
and App Lock mutation have cancellation-aware facade overloads. The UI discards
late notifications and reconciles the authoritative preference; cancellation does
not promise to undo an operation that has already committed. Reactivation that
has enabled protection still requests a shell lock if persistence or enrollment
reports failure; successful reactivation keeps the verified session open.

## Compatibility

The envelope remains version 2, with three additional, strictly validated provider
identifiers. Android's provider shapes, vault encryption, password/KDF parameters,
portable exports and backup formats are unchanged. Unknown device-local providers
remain readable through the existing password-recovery path; unsupported wrappers
cannot be enrolled or silently treated as user-verified quick unlock. Older builds
cannot use the new unattended providers. Preserve the master password and enable
App Lock before downgrading or moving files to another device.

## Verification

Regression tests cover desktop provider metadata, secret-store enrollment/session
restart/removal, wrong references and binding tampering, partial registration
cleanup, cancellation, invalid recovered-key clearing, current/saved method
selection, password rejection, localizations, unavailable stores, duplicate taps,
late completion, single-prompt reactivation and fail-closed shell transitions.
The reactivation regression counts verification and registration calls through
the actual Windows quick-unlock adapter with a mocked OS gate; it checks no
temporary challenge or extra enrollment occurs. Windows tests create only a
synthetic temporary CNG key, check that private-key export is denied, verify a
round trip and tamper rejection, and delete the key. Policy tests cover disabled
idle/session/minimize/manual locking. Headless UI tests exercise enabled/disabled
states in all four languages at compact and regular constrained window sizes.

Cold-start regressions deactivate App Lock through the shared service, persist
preferences and envelopes in isolated temporary directories, dispose the first
session, and initialize a fresh desktop coordinator/service/session. They cover
valid, missing and tampered unattended keys, retained biometric preference and no
user-presence calls on restart. Windows uses a synthetic native CNG key that is
removed afterward; macOS/Linux use the actual reference adapter with a simulated
secret store. These tests do not restart the user's app or access its vault.

Native macOS Keychain and Linux Secret Service interaction, Windows Hello prompts,
and actual desktop startup/toggle workflows still require manual verification on
their respective systems. Automated tests do not claim these native checks passed.

API references: [Windows user key creation](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkeycreationparameters.keycreationoptions?view=net-10.0),
[CNG key storage providers](https://learn.microsoft.com/en-us/windows/win32/seccertenroll/cng-key-storage-providers),
[named key deletion](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkey.delete?view=net-10.0),
[Apple device-local accessibility](https://developer.apple.com/documentation/security/ksecattraccessiblewhenunlockedthisdeviceonly),
and [Apple access-control flags](https://developer.apple.com/documentation/security/secaccesscontrolcreateflags).
