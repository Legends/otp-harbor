# Documentation

This index lists maintained documentation. Git history preserves completed migration plans and superseded implementation notes; they are intentionally not retained as parallel sources of truth.

## Users and contributors

- [Recovery guide](RECOVERY.md)
- [Contribution guide](../CONTRIBUTING.md)
- [Desktop test commands](testing/DESKTOP_APP_TEST_COMMANDS.md)
- [Android development preview](android/FOUNDATION.md)
- [Microsoft Store release](release/MICROSOFT_STORE.md)
- [Website indexing](release/WEBSITE_INDEXING.md)
- [Community platform testing](testing/COMMUNITY_PLATFORM_TESTING.md)

## Architecture

- [Native Avalonia decision](architecture/ADR-0001-native-avalonia-migration.md)
- [Desktop distribution policy](architecture/AVALONIA_DESKTOP_DISTRIBUTION.md)
- [Platform application paths](architecture/PLATFORM_APPLICATION_PATHS.md)
- [Platform session and lifecycle events](architecture/PLATFORM_SESSION_AND_LIFECYCLE_EVENTS.md)
- [Single instance and activation](architecture/SINGLE_INSTANCE_AND_ACTIVATION.md)
- [UI interaction boundaries](architecture/UI_INTERACTION_BOUNDARIES.md)
- [UI scheduling and lifetime](architecture/UI_SCHEDULING_AND_LIFETIME.md)
- [Notification policy](architecture/AVALONIA_NOTIFICATIONS.md)
- [Platform adapters](architecture/M6_PLATFORM_ADAPTERS.md)
- [Release integrity](architecture/M7_RELEASE_INTEGRITY.md)
- [Hardening audit](architecture/M8_HARDENING_AUDIT.md)

## Security and release operations

- [Threat model](security/THREAT_MODEL.md)
- [Security verification](security/SECURITY_VERIFICATION.md)
- [Authorization envelope v2](security/AUTHORIZATION_ENVELOPE_V2.md)
- [Clipboard security](security/CLIPBOARD_SECURITY.md)
- [Platform file security](security/PLATFORM_FILE_SECURITY.md)
- [Automatic updates](security/AUTO_UPDATE.md)
- [Branch protection](security/BRANCH_PROTECTION.md)
- [Signing-key rotation](security/SIGNING_KEY_ROTATION.md)
- [SignPath onboarding](security/SIGNPATH_FOUNDATION_ONBOARDING.md)
- [Penetration-test plan](security/PENTEST_PLAN.md)
- [OWASP desktop checklist](security/OWASP_DESKTOP_CHECKLIST.md)
- [Legacy DPAPI format reference](security/LEGACY_DPAPI_SETTINGS_FORMAT.md)

Acceptance-record templates are under [`architecture/evidence`](architecture/evidence/).
Use [`architecture/M6_PHYSICAL_ACCEPTANCE.md`](architecture/M6_PHYSICAL_ACCEPTANCE.md) for target-host checks and [`architecture/evidence/FINAL_RELEASE_ACCEPTANCE_TEMPLATE.md`](architecture/evidence/FINAL_RELEASE_ACCEPTANCE_TEMPLATE.md) for a release decision.
