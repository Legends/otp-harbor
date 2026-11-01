# Android icon import session and progress review

Date: 2026-11-01.

## Requested behavior and implementation

- **T1 — DONE (automated):** Retain the existing localized, centered icon-import
  progress dialog. A recreated mobile view attached to an already initialized
  shell hides its startup overlay immediately, including while an import is
  busy. Native account hosts receive their reveal callback after attachment.
- **T2 — DONE (automated):** Make successful shell initialization idempotent.
  Android's activity-backed main-view factory can create another view around
  the same shell; this must not rerun authorization initialization or replace
  the active settings screen with startup/unlock. Failed startup remains
  retryable. Initial process startup still initializes and requires the normal
  authorization flow.
- **T3 — DONE (automated):** All 230 focused tests pass. Debug Android build
  succeeds with zero warnings/errors, producing
  `OtpHarbor.UI.Avalonia.Android/bin/Debug/net10.0-android/io.github.legends.otpharbor.debug-Signed.apk`.
- **Device UI acceptance — UNVERIFIED:** The changed APK has not been installed
  or exercised on the connected personal device. Automated view-layout/source
  checks do not establish actual Android file-picker or animation behavior.

## Security and compatibility

This change preserves an existing in-memory authorization session; it does not
persist credentials or unlock state, invoke automatic unlock on import, or
grant new authorization. A genuinely locked session remains locked when its
view is recreated. Process death still requires normal startup authorization.
Device/screen lock and the 30-second background grace policy remain unchanged,
including time spent outside the application in the system file picker.

No account import/export, encryption, vault, icon pack, PNG, archive validation,
or on-disk format changes are made. Import success, failure, exceptions, and
picker cancellation all leave the current foreground session and settings
navigation intact. The existing localized dialog text is reused in every
language; no new user-facing strings are introduced.

## Automated evidence

`dotnet test OtpHarbor.Tests/OtpHarbor.Tests.csproj -c Debug --filter
"FullyQualifiedName~MobileShellViewModelTests|FullyQualifiedName~MobileAccountInteractionTests|FullyQualifiedName~AndroidSplashResourceTests|FullyQualifiedName~MobileStringCatalogTests"`

230 tests pass. Regressions cover repeated initialization before/during/after
icon import, success/failure/exception/cancellation, long foreground processing,
account object and settings retention, no additional lock/logout/authorization
initialization, failed-startup retry, and recreation after a real lock. Existing
background-expiry, biometric, cold-start, splash, and localization tests pass.
The view regression checks that the original modal progress card remains wired
to its localized import state and that restored views hide the startup overlay.

## Device acceptance steps

1. Unlock, open icon settings, select a local `.otphicons` file, and return
   promptly from the picker. The centered import dialog should appear over the
   settings screen, with no startup animation or unlock request.
2. Repeat with a large valid pack, an invalid pack, and picker cancellation.
   Check localized progress/error text, preserved account navigation, and
   responsive presentation. Exercise activity recreation during import.
3. Lock the device or background beyond the configured grace period; verify
   normal locking still happens. Do not treat that intentional security lock
   as a successful session-preservation test.
4. Restart the process and verify normal startup authorization and startup
   presentation. A failed startup must still offer a usable retry.

## Third-party rights scope

The change adjusts application lifecycle/presentation only. It neither adds
artwork nor changes import attribution, legal notices, hosting, redistribution,
or brand usage. The existing disclaimer remains applicable. Upstream review:

- [Simple Icons license](https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md)
  and [disclaimer](https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md):
  collection licensing does not replace individual icon terms or brand guidance.
- [Aegis Icons credits and disclaimer](https://github.com/aegis-icons/aegis-icons#disclaimer):
  mixed asset rights and identification-only trademark guidance remain relevant.
- [Dashboard Icons license](https://github.com/homarr-labs/dashboard-icons/blob/main/LICENSE)
  and [legal guidance](https://github.com/homarr-labs/dashboard-icons#legal):
  attribution/redistribution terms and third-party trademark restrictions remain
  independent of format compatibility.

This review introduces no new asset-rights determination or endorsement claim.
