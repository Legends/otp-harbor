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

The settings import UI can import the generated JSON directly. Debug builds
additionally support one-batch test helpers for importing the file and deleting
all accounts.
