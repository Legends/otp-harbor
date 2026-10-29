# Large account import fixture

The generator creates `prominent-platforms-500.json` with 500 synthetic OTP
Harbor accounts for manual and automated load testing. Issuer names are
derived from the first 500 entries of the pinned Tranco research ranking list
`Y83YG`.

The generated file is deliberately written below the ignored `artifacts/`
directory and is not committed. This prevents secret scanners from treating
test-only Base32 values as repository credentials.

Every generated account identifier, Base32 secret, and `example.invalid`
account name is deterministic test data. The secrets are public and must never
be used for real accounts.

Generate the fixture with:

```powershell
.\scripts\testing\Generate-ProminentPlatformAccountFixture.ps1
```

This Tranco-based fixture intentionally uses ranked domains and is therefore
not suitable for verifying automatic brand-icon matching. To create 600
accounts whose issuers are guaranteed to resolve against a particular imported
OTP Harbor icon pack, run:

```powershell
.\scripts\testing\Generate-BrandAccountFixture.ps1 `
  -IconPackPath C:\path\to\otp-harbor-icons.otphicons `
  -Count 600
```

Import the same `.otphicons` pack into OTP Harbor before importing the generated
`artifacts/test-data/brand-accounts-600.json` account fixture. Brand names repeat
when the requested account count exceeds the number of brands in the pack;
account identifiers, names, and synthetic secrets remain unique.

The settings import UI can import the generated JSON directly. Debug builds
additionally support one-batch test helpers for importing the file and deleting
all accounts.
