# Large account import fixture

`prominent-platforms-500.json` contains 500 synthetic OTP Harbor accounts for
manual and automated load testing. Issuer names are derived from the first 500
entries of the Tranco research ranking. The checked-in fixture was generated
from list `Y83YG` on 2026-10-02.

Every account identifier, Base32 secret, and `example.invalid` account name is
deterministic test data. The secrets are public and must never be used for real
accounts.

Regenerate the fixture with:

```powershell
.\scripts\testing\Generate-ProminentPlatformAccountFixture.ps1
```

The settings import UI can import the JSON directly. Debug builds additionally
support one-batch test helpers for importing the file and deleting all accounts.
