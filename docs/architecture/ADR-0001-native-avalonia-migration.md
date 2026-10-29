# ADR-0001: Migrate the desktop client to native Avalonia UI

- **Status:** Accepted
- **Date:** 2026-07-16
- **Decision owners:** OTP Harbor maintainers

## Outcome (2026-08-28)

The side-by-side phase is complete. Avalonia is the sole desktop client on `master`; the legacy WPF project, its release job, and its presentation-only regression tests were retired before `v2.0.0-rc6`. Historical v1 tags and synthetic compatibility fixtures remain the rollback and format-compatibility evidence. The Windows updater helper remains a separate implementation detail of the verified Avalonia update handoff.

## Context

At the time of this decision, OTP Harbor—then named TOTP Manager—was a Windows WPF application with a local encrypted vault, Windows Hello and DPAPI integration, QR workflows, encrypted import/export, and a signed automatic-update feed. The product needed Windows, macOS, and Linux support without weakening recovery, storage, authorization, or update verification.

The existing domain, security, persistence, and workflow code should remain the source of truth. Rewriting the product or splitting clients into separate repositories would increase compatibility risk around vault formats and security fixes.

The immutable WPF baseline for this migration is tag `v1.0.0` at commit `23395c48832e029051f0a7f6cac21c3b755ec251`.

## Decision at adoption

1. Use native Avalonia UI for the cross-platform desktop client.
2. Keep WPF and Avalonia in this repository during migration.
3. Add Avalonia as a separate client; do not convert the WPF project in place.
4. Extract portable contracts and platform adapters before porting most screens.
5. Preserve the current encrypted vault format unless a separately reviewed security change requires a versioned migration.
6. Keep the master password as the universal recovery mechanism. Platform biometrics remain optional quick-unlock mechanisms.
7. Keep `master` releasable and merge migration work through small, short-lived branches.
8. Maintain WPF 1.x from `release/1.x` once migration work begins on `master`. Security and storage-format fixes must be forward-ported immediately.
9. Target `2.0.0` for the first supported Avalonia release.

## Initial platform policy

The first desktop validation matrix is:

- Windows 10 22H2 and Windows 11, x64.
- macOS 14 or later, ARM64. M3 excluded macOS x64 from the initial support policy after the version-aligned OpenCvSharp 4.13 x64 package restored and published but failed its first native call on GitHub's current macOS Intel runner; ARM64 passed the identical packaged probe. Intel support may be reconsidered after upstream provides a validated runtime/CI path or the project accepts ownership of a separately maintained native build.
- Ubuntu 24.04 LTS, x64, as the Linux reference platform. Other distributions remain best-effort until native dependency and packaging tests establish a wider support contract.

These became the initial validation targets; current distribution status is maintained in [AVALONIA_DESKTOP_DISTRIBUTION.md](AVALONIA_DESKTOP_DISTRIBUTION.md).

The initial direct-distribution packaging direction is:

- Windows: retain the existing fast and self-contained artifacts until an installer decision is reviewed.
- macOS: signed and notarized app bundle in a DMG or PKG.
- Linux: portable tarball or AppImage plus DEB for Ubuntu/Debian users. Additional formats require demonstrated demand and a maintained update policy.

Store distribution and macOS App Store sandboxing are out of scope for the first cross-platform release.

## Security consequences

- DPAPI-protected settings cannot be opened directly on macOS or Linux. A versioned key-envelope migration with rollback tests is required before cross-platform release.
- Platform secret storage must fail closed; no provider may fall back to plaintext persistence.
- Update artifacts must remain signed and selected by operating system and architecture.
- Compatibility fixtures must use synthetic secrets and cover historical WPF formats.
- Historical WPF tags remain available as recovery evidence; current releases contain only the Avalonia client.

## Delivery consequences

- `OtpHarbor.Core` remains UI-neutral.
- Portable infrastructure must compile without Windows or WPF assemblies.
- OS integration belongs behind injected platform contracts.
- The WPF client was required to build throughout the side-by-side phase and was removed after the Avalonia cutover decision.
- Avalonia is now the default startup and only release artifact.

## Alternatives rejected

### Separate Avalonia repository

Rejected because it would create version skew, duplicate security policy, and make storage-format changes difficult to land atomically.

### Avalonia XPF

Rejected as the primary strategy because compatibility licensing and retained WPF assumptions would weaken control over the long-term portable architecture.

### Uno Platform or .NET MAUI

Rejected for the initial desktop migration because native Avalonia better matches the desktop-first Windows, macOS, and Linux scope. Mobile remains a later product phase.

### Immediate screen rewrite

Rejected because platform paths, file security, authorization persistence, and lifecycle behavior must be isolated and verified before UI parity work.

## Historical validation gates

This decision must be revisited if the M3 vertical slice cannot demonstrate:

- Correct password unlock against migrated data.
- Reliable account listing and TOTP refresh.
- Acceptable startup, memory, and rendering performance.
- Working platform adapters on Windows, macOS, and the Linux reference platform.
- A credible accessible replacement for the current account grid.
- No regression in secret handling or recovery behavior.

## References

- [Maintained documentation index](../README.md)
- [Desktop distribution policy](AVALONIA_DESKTOP_DISTRIBUTION.md)
- [`docs/security/THREAT_MODEL.md`](../security/THREAT_MODEL.md)
- [`docs/security/SECURITY_VERIFICATION.md`](../security/SECURITY_VERIFICATION.md)
