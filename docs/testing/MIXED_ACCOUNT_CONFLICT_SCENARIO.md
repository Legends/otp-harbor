# Mixed account conflict scenario

This scenario verifies the same shared account matching and conflict policy on Windows desktop and Android. Use only a disposable test profile or device state. The fixtures contain synthetic `example.invalid` accounts and synthetic TOTP seeds. Preserve an encrypted recovery snapshot before changing an existing development vault.

## Expected classification

| Older fixture account | Current fixture relationship | Expected result |
| --- | --- | --- |
| Scenario Exact | Same ID and payload | Keep automatically; no conflict choice |
| Scenario Collision | Different ID, same issuer and account name, different secret | Show conflict resolution |
| Scenario Older New | Different ID and identity | Import as new |

`Scenario Current Only` remains in the current vault. If the collision is replaced, OTP Harbor keeps current ID `22222222-2222-4222-8222-222222222222`, applies the older synthetic secret, and does not add older collision ID `33333333-3333-4333-8333-333333333333`.

## Windows desktop rehearsal

Use a disposable Windows user or VM because v2 and v3 use the profile's roaming application-data directories.

1. Preserve encrypted snapshots of both app-data directories and close every OTP Harbor process.
2. In v3, import `scripts/testing/fixtures/mixed-account-current-v3.json` into an empty test vault.
3. In the Store v2 app, import `scripts/testing/fixtures/mixed-account-older.json` into an empty older test vault.
4. Close v2 and start v3. Choose **Review older vault** and unlock the older vault.
5. Verify the review reports one unchanged match, one new account, and one changed collision requiring a decision.
6. Choose to keep or use the older collision, confirm the import, and verify the four expected account labels.
7. Verify the older encrypted vault is unchanged and the current vault has a recovery checkpoint.

## Android rehearsal

Use the isolated Debug package or another disposable test installation. Copy the fixtures with:

```powershell
.\scripts\testing\Copy-MixedAccountConflictScenarioToAndroid.ps1 -Device <adb-device-id>
```

1. Unlock an empty v3 test vault and import `mixed-account-current-v3.json` from Downloads.
2. Import `mixed-account-older.json` from Downloads.
3. Verify Android reports one new account, one unchanged match, and one account requiring review.
4. Verify the conflict card compares `Scenario Collision` and offers per-account keep/replace controls plus apply-to-all controls.
5. Confirm the import and verify four accounts remain: Exact, Collision, Current Only, and Older New.
6. Repeat once with **Keep current** and once from a restored encrypted snapshot with **Use older**.

The automated shared-service and Android presentation tests additionally verify exact IDs: replacement retains the current collision ID, the older collision ID is absent, and the genuinely new account retains its older-vault ID.

Never capture or share generated OTP values. Record only account labels, classifications, selected conflict actions, and pass/fail results.
