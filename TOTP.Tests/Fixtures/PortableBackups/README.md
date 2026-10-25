# Synthetic portable-backup fixture

`synthetic-ui-test-backup.totp` is a legacy-extension encrypted backup created
exclusively from synthetic accounts for manual compatibility import and restore
testing. New fixtures and exports use `.otphbp`; the encrypted payload format is
unchanged.

Rules for this fixture:

- It must never contain a real account, OTP seed, password, user name, or other
  personal data.
- Do not replace it with a backup from a production vault.
- Its decryption password is intentionally not stored in the repository.
- Automated tests should create purpose-specific temporary exports unless they
  explicitly require a stable compatibility fixture.
