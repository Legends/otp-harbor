# Development account snapshot workflow

These scripts are Debug-only test helpers. They copy OTP Harbor's encrypted vault and
portable password-authorization envelope; they never export account names, OTP seeds,
or codes as plaintext.

Run them from `scripts/testing` in this order:

```powershell
# Keep the desktop Debug app open and unlocked. This captures the source, then clears it.
.\Clear-DesktopDevelopmentAccounts.ps1

# Keep the Android Debug app open and unlocked. Add -Device "IP:Port" when needed.
.\Clear-AndroidDevelopmentAccounts.ps1 -Device "192.168.2.105:PORT"

# Close the desktop app first. This restores the exact encrypted desktop source.
.\Fill-DesktopDevelopmentAccountsFromSnapshot.ps1

# This restores the same source into the isolated Android Debug package.
.\Fill-AndroidDevelopmentAccountsFromDesktopSnapshot.ps1 -Device "192.168.2.105:PORT"
```

Snapshots are stored below
`%LOCALAPPDATA%\OTP-Harbor\DevelopmentAccountSnapshots` with an ACL restricted to the
current Windows user. Every restore verifies SHA-256 hashes. Each destructive script
creates a recovery/source snapshot before changing anything and supports `-WhatIf`.

After copying the desktop state to Android, use the desktop master password. A
platform-bound quick-unlock wrapper is not portable, so biometric unlock may need to
be enrolled again on Android.
