# Microsoft Store v2 → v3 upgrade acceptance

## Test v2 vault migration without a v2 installer

This is the direct data-migration test. It creates an encrypted `TVLT` v2 vault
using the released v2.0.1 layout and password-envelope contract, with the four
fictional accounts listed below. The installed v3 app performs the actual migration.

1. Close OTP Harbor and run **`SeedSyntheticV2.cmd`**. Existing raw/Store application
   directories are moved intact to uniquely named sibling directories ending in
   `.before-synthetic-v2-...`; nothing is deleted or overwritten. Their exact paths
   are printed and recorded in `synthetic-upgrade-state.json`. A separate encrypted
   baseline remains inside the kit.
2. Install the v3 update if applicable, then open the installed v3 app. Unlock with
   the public **test-only** password: `OTP-Harbor-v2-test-only!`. No account import
   action is required; the app should open the legacy vault and show four accounts.
3. Close the app and run **`VerifySyntheticV3.cmd`**. It checks the migrated v3 vault,
   all original IDs/account fields and the encrypted rollback files.
4. Restart, unlock and close v3, then run `VerifySyntheticV3.cmd` again.

No `Initialize.cmd`, `CaptureV2.cmd`, fresh Windows user or v2 installer is needed
for this synthetic flow. Do not use the test password or fictional accounts for
real authentication. Existing data is preserved, but this deliberately changes
which vault the app opens. To return to the preserved data, close the app, move
the synthetic application directory aside, then rename the recorded preserved
directory back to its original name. Preserve both directories; do not merge vaults.

This tests the **real app's vault migration**, including on an already installed v3
build. It does not establish Store package-update delivery from an installed v2
binary. That separate acceptance flow remains below and requires Store-signed v2.

This kit tests the actual installed Store application with four fictional accounts.
The verifier uses OTP Harbor's production authorization-envelope and vault readers;
the verification step does not invoke migration, install packages, or write application data. The **v3
app itself must perform the migration**. A successful verification therefore cannot
be caused by the verifier converting the vault first.

## Prepare the kit on the development machine

From the repository, using PowerShell 7 and the .NET 10 SDK:

```powershell
pwsh -File .\scripts/testing/windows\Test-StoreUpgradeTestKit.ps1
pwsh -File .\scripts/testing/windows\Prepare-StoreUpgradeTestKit.ps1
```

Preparation prints the new directory under `artifacts/windows/testing/store-v2-v3-...`.
Copy the **whole folder** to a disposable Windows VM or fresh Windows test user.
The verifier is self-contained for Windows x64; the target needs neither the source
repository nor a .NET SDK. Run the commands below from the copied kit directory,
or double-click the matching `.cmd` launchers. The launchers keep results visible.

Never use personal accounts in this test. Initialize refuses any existing
`TOTP-Manager` or `OTP-Harbor` directory in the known raw or Store-virtualized locations.
It does not delete or reset those directories. The run is bound to its Windows user
SID and machine. Use a new kit and a fresh profile for another run; do not edit the
state file to bypass checks. The kit's files inherit a current-user-only ACL when
initialized. Keep it outside the application's data directories.

Directory protection and SHA-256 hashing use .NET APIs directly; the target
launchers do not depend on `Set-Acl` or `Get-FileHash`. Every entry script first
checks Windows/PowerShell compatibility and explicitly loads its matching built-in
Management/Utility modules. Store actions also check the Windows Appx module and
the packaged verifier's runtime/native crypto dependencies before touching test data.
Preparation checks the .NET 10 SDK. The launchers use Windows PowerShell's explicit
system path and retain failure exit codes. No third-party PowerShell module is needed.

Built-in Windows modules are not Gallery packages. If one is actually missing or
cannot load, preflight stops with the required component and repair guidance; it
does not install an unrelated module with the same name or change machine settings.
Inherited PowerShell 7 module paths cannot redirect a Windows PowerShell launcher
to the wrong Utility/Management module.

To run only script/environment
checks on a development checkout, use `Test-StoreUpgradeTestKit.ps1 -ScriptsOnly`
with either Windows PowerShell 5.1 or PowerShell 7.

## Required packages

- The genuine **Store-signed v2** package, preferably v2.0.1, installed for the test user.
- The candidate **Store-signed v3** update, through a private Store flight or your
  archived Store-signed package delivery. The identity must remain
  `Legends77.OTPHarbor` with the same publisher/package family.

The public Store normally offers the latest build; this kit cannot obtain an old
Store version or sign a package as Microsoft. If the v2 package is unavailable,
record this test as blocked. A locally reconstructed/sideloaded build is useful for
development but is **not** evidence of a real Store update; this runner rejects it.
Do not change Store package identities or certificates to make the test pass.

## 1. Initialize before first launching v2

Install v2, close it if installation launched it, and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Invoke-StoreUpgradeTest.ps1 -Phase Initialize
```

If v2 has already created application data, use a fresh profile. Do not clear a real
profile. Start v2 and create a new **test-only master password**. Import
`synthetic-v2-accounts.txt` using the v2 TXT import action. This is the native v2
pipe-delimited format (`issuer|account_name|secret|id|period_seconds`), not a URI list.
Expect exactly:

| Issuer | Account | Period |
| --- | --- | --- |
| Upgrade Alpha | alpha@example.invalid | 30 s |
| Upgrade Beta | beta@example.invalid | 60 s |
| Upgrade München | jörg@example.invalid | 15 s |
| Upgrade Delta | delta@example.invalid | 30 s |

All use SHA-1 and six digits, the fields supported by the released v2.0.1 account
model. SHA-256/512 and eight-digit support introduced later cannot be tested as
pre-existing v2.0.1 data. The synthetic seeds are public test data and have no value.

Confirm that all four accounts generate codes. If the machine supports Windows
Hello, enroll it, then test both password and Hello unlock **before** capture.
Create a password-protected external backup through v2 and retain it privately in
the test environment. Do not include backups or passwords in a bug report.
Restart v2, unlock with the password, and close it completely.

## 2. Capture the baseline

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Invoke-StoreUpgradeTest.ps1 -Phase CaptureV2
```

Enter the original test password at the hidden console prompt. It is never passed
as a command argument, stored in a report, or echoed. Capture checks the installed
Store v2 identity/version and reads both raw Roaming AppData and the package's
`LocalCache\Roaming` locations. Missing or multiple vaults fail explicitly.

Only after the vault authenticates and exactly matches the four synthetic accounts
does capture copy its encrypted bytes, authorization envelope and existing rotating
backups into `baseline`. A baseline cannot be overwritten. No plaintext accounts
are saved from the vault. `upgrade-state.json` records package versions, hashes and
results, not passwords, OTPs or account contents; it does contain local paths/SID,
so redact those before sharing it outside the test environment.

Take a VM checkpoint now. Do not change accounts, password or Hello registration
after capture. Updating these would intentionally invalidate the baseline.

## 3. Perform the real update

Install/update to Store v3 **without uninstalling v2, resetting the app, clearing
data, or restoring/importing a backup**. Leave the kit in place. Start v3 and unlock
using the original master password. Verify all four accounts appear and generate
codes, then close the app.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Invoke-StoreUpgradeTest.ps1 -Phase VerifyV3
```

Enter the same test password. PASS requires:

- A newer Store v3 package with the original package family and publisher.
- A `TVL2` vault produced by the app, readable with the original password/key.
- The exact four accounts, original GUIDs, issuer/account names including Unicode,
  seeds, periods, algorithm, digits, favorite state and group memberships.
- A byte-identical authorization envelope; no password/key re-enrollment was needed.
- A byte-identical `pre-upgrade-rollback` vault, envelope and every captured rotating
  backup, with the rollback vault also decryptable using the original password.
- No application-file changes caused by the verifier.

Restart v3, unlock again and close it, then **repeat VerifyV3**. Both successful
checks are appended to `upgrade-state.json`. Wrong passwords and corrupted or
changed inputs fail with redacted errors; the scripts do not repair or overwrite
anything. Investigate a failure in the disposable environment before release.

## Manual checks and recovery

- Try an incorrect password in the app; it must not unlock or replace the vault.
- Confirm existing Hello unlock still works, or falls back safely to the original
  password and permits re-enrollment. Record which occurred. Run the automated
  verification before deliberately changing Hello enrollment.
- On a separate VM checkpoint/clone, test restoration of the **external encrypted
  backup made by v2** into v3, using the app's normal backup restore flow. Confirm
  all four accounts, periods and code generation after another restart.
- For an actual v2 rollback drill, revert the whole VM checkpoint. The rollback
  snapshot alone is not a Store downgrade procedure. Do not uninstall the live app
  or copy encrypted files between real users to simulate recovery.
- Record Windows version, v2/v3 full package versions, package delivery source,
  both automated verification outcomes and the manual Hello/backup outcomes.

The self-test covers synthetic positive and failure branches. It is not a substitute
for this Store-signed installation test. Do not mark upgrade acceptance complete
until both automated phases and the manual checks have actually passed.

## Security and compatibility scope

No application storage, crypto format, KDF policy, authorization or update trust
boundary changes are introduced by the kit. Password unwrap and authenticated vault
decoding use existing production code. Sensitive byte buffers are cleared, inputs
are bounded, path junctions are rejected and crypto failures are redacted. Plaintext
fixture seeds are deliberately public synthetic values; decrypted vault contents are
never written. The original test password briefly exists in memory because the
production password API accepts a string, but is never passed through the shell.
The kit's encrypted baseline is separate from application-managed recovery files.

Regression coverage includes a released-v2-shaped payload, exact account/ID/field
preservation, repeated read-only verification, wrong passwords, changed envelopes
and rollback copies, corrupt/unconverted vaults, missing/unrelated accounts and
oversized inputs. Script checks cover existing-data refusal, raw/virtualized paths,
duplicate/missing vaults, Store identity, state serialization and junction rejection.
