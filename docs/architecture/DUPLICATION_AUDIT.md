# Cross-platform duplication audit

Reviewed 2026-11-19 for the common-localization migration. The inventory covered
504 production C# files across 16 project directories after extraction. The audit combined an
exact normalized C# block scan across production projects (excluding tests,
`bin`, `obj` and key-declaration boilerplate) with manual comparison of related
desktop/mobile/native workflows. The block scan found five groups containing at
least twelve meaningful matching lines; it is a candidate finder, not a complete
semantic equivalence analysis. The findings below identify concrete extraction
seams and preserve security and native-platform boundaries.

## Implemented: one localization owner

Desktop and mobile duplicated resource lookup, English fallback, supported-culture
selection and completeness checks. The updater had another resource loader.
They now delegate to [`OtpHarbor.Localization`](../../OtpHarbor.Localization).
All translations and desktop/mobile key definitions have moved there; identical
messages share one resource entry, while 48 wording differences and one compiled
line-ending difference remain explicit. Regional culture selection is now
consistent, including mobile `ApplyCulture("de-DE")`.

The migration removes 1,496 duplicate translated entries across four languages.
It preserves all 5,480 pre-migration compiled messages exactly. The independent
comparison used the previous applications' compiled resources so XML newline
normalization could not silently change the result. See
[common localization](COMMON_LOCALIZATION.md) for ownership and extension rules.

## Recommended extractions

These findings are recommendations; this migration does not also rewrite the
listed application workflows.

| Priority | Existing duplication | Suggested shared seam | Risk and required evidence |
| --- | --- | --- | --- |
| P1 | [`GroupAccountSelectionViewModel`](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/GroupAccountSelectionViewModel.cs) and [`MobileGroupAccountSelection`](../../OtpHarbor.UI.Avalonia.Mobile/Presentation/MobileGroupAccountSelection.cs) contain the same immutable account/brand fields and `IsSelected` notification. | One shared group-selection presentation item in `UI.Avalonia.Shared`, with compatibility wrappers if needed by compiled/native bindings. | Low. Preserve identity, selection notifications and native accessibility updates; cover desktop and native-picker binding contracts. |
| P1 | Desktop [`AsyncCommand`](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/AsyncCommand.cs) and mobile [`MobileAsyncCommand`](../../OtpHarbor.UI.Avalonia.Mobile/Presentation/MobileAsyncCommand.cs) implement generic and non-generic async `ICommand` adapters. Mobile adds an atomic execution guard. | Shared command execution/lifetime implementation; keep feature-specific `CanExecute` predicates at the call sites. | Medium. Do not replace the mobile atomic guard with desktop's weaker implementation. Test rapid taps, generic arguments, exception cleanup, re-entry and command notifications. |
| P1 | Desktop [`NotificationState`](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/NotificationState.cs) and mobile `SetTransientNotification`, `ClearNotificationAfterDelayAsync`, `CancelNotificationLifetime` in [`MobileShellViewModel`](../../OtpHarbor.UI.Avalonia.Mobile/Presentation/MobileShellViewModel.cs) repeat cancellation/replacement timers. | Shared presentation notification state with configurable duration/severity-reset behavior and existing UI adapters. | Medium. Desktop exposes `Shown` and persistent notifications; mobile resets severity on expiry. Preserve these differences and test replacement, cancellation, disposal and active-locale text refresh. |
| P1 | Desktop `ChangePreferredUnlockMethodAsync` in [`AuthorizationSettingsViewModel`](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/AuthorizationSettingsViewModel.cs) and mobile `PrepareSensitiveAuthenticationAsync`/`ApplyUnlockMethodChangeAsync` repeat current-method selection, recovery fallback and session-lifetime orchestration. | Common workflow/policy for interpreting authorization results and deciding when password recovery is allowed; presentation-specific prompts remain adapters. | High. The authenticated operation already belongs to `PortableAuthorizationService`; do not duplicate or bypass it. Review differences in `DisabledByPolicy`/unavailable fallback, pending-state projection, lock cancellation and late completion. Use matching desktop/mobile security regressions. |
| P2 | `SaveAccountAsync` in desktop [`AccountListViewModel`](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/AccountListViewModel.cs) and mobile [`MobileShellViewModel`](../../OtpHarbor.UI.Avalonia.Mobile/Presentation/MobileShellViewModel.cs) repeat issuer/account normalization, existing-account lookup, secret-preservation checks, duplicate checks and save-result handling. | A request/result-based account-edit workflow behind the current service boundary; leave field messages, navigation and native UI in the presentations. | High. Shared `SecretValidation`, `TotpConfigurationPolicy` and `TotpPeriodPolicy` already own validation rules. Reuse them, preserve old-secret authorization and buffer lifetimes, and cover new/edit/unchanged-secret/unauthorized-secret/save-failure paths. |
| P2 | Custom-icon sentinel handling and selected filename projections in desktop [`AccountListViewModel`](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/AccountListViewModel.cs) and mobile [`MobileShellViewModel`](../../OtpHarbor.UI.Avalonia.Mobile/Presentation/MobileShellViewModel.cs) repeat pending-picker state and `custom_` detection. | Shared icon-selection state and filename projections, building on existing shared branding services and `IconNameDisplayPolicy`. | Medium. Preserve cancellation/restoration and per-UI picker navigation. This is local selection state, not permission to merge provider acquisition/parsing or change icon licensing/disclosures. |
| P2 | `ScheduleRealizedAccountReport` and container-event wiring in desktop [`MainWindow.axaml.cs`](../../OtpHarbor.UI.Avalonia.Desktop/MainWindow.axaml.cs) and mobile [`MainView.axaml.cs`](../../OtpHarbor.UI.Avalonia.Mobile/Views/MainView.axaml.cs) repeat dispatcher coalescing and realized-item reporting. | Small shared Avalonia behavior that observes containers and reports typed visible items, with a caller-provided suspension predicate. | Medium. Mobile deliberately suppresses reporting during scrolling; desktop has visibility checks. Test attach/detach, scroll suspension, stale callbacks and avoiding offscreen code refresh. |
| P2 | `UpdateBrandAppearance` in Android [`NativeAccountRowView`](../../OtpHarbor.UI.Avalonia.Android/NativeAccountRowView.cs) and [`NativeGroupAccountPickerRecyclerView`](../../OtpHarbor.UI.Avalonia.Android/NativeGroupAccountPickerRecyclerView.cs) performs the same background-mode/color calculation. | One Android-native appearance calculation helper. Keep drawing, cache ownership and invalidation in each view. | Low–medium. Row rendering restricts template creation during scrolling; the picker does not. Test theme changes, embedded/solid backgrounds, fallback color and first-draw behavior. |
| P3 | Android [`AndroidPlatformQuickUnlock`](../../OtpHarbor.Platform.Android/AndroidPlatformQuickUnlock.cs) and [`AndroidPlatformUnattendedUnlock`](../../OtpHarbor.Platform.Android/AndroidPlatformUnattendedUnlock.cs) repeat Keystore loading, alias lookup/removal, AES-GCM cipher creation and associated-data buffer cleanup. | A narrowly scoped internal Keystore primitive helper, after security review. | High. Never unify authentication requirements, provider IDs, alias prefixes or associated-data contexts. Biometric/device-credential wrappers and unattended wrappers have intentionally different trust requirements. Keep invalidation, cancellation, cleanup and authenticated/unattended separation tests. |
| P2 | `CopyWindowsUpdaterBundleToOutput` and `CopyWindowsUpdaterBundleToPublish` in the [desktop project](../../OtpHarbor.UI.Avalonia.Desktop/OtpHarbor.UI.Avalonia.Desktop.csproj) repeat updater build properties, file selection and copying. | Shared MSBuild preparation/file-list target, followed by destination-specific copy targets. | Medium. Preserve version propagation, dependency/satellite files, signing inputs and correct publish layout. Verify fresh build and publish bundles rather than relying on stale output folders. |

## Boundaries to retain

- Cryptography, KDFs, vault verification, account persistence and backup formats
  already belong to Core/Infrastructure/DAL. Presentation duplication does not
  justify moving those decisions into UI code or into the localization project.
- `WindowsPlatformQuickUnlock`, `MacOSPlatformQuickUnlock` and Android Keystore
  adapters represent different OS credential mechanisms. Share contracts and
  outcome policy, not native callbacks, storage handles or user-verification rules.
- [`WindowsFileSecurity`](../../OtpHarbor.Platform.Windows/WindowsFileSecurity.cs)
  and [`UnixFileSecurity`](../../OtpHarbor.Platform.Unix/UnixFileSecurity.cs) have
  different ACL/mode semantics. They are not interchangeable implementations just
  because their public contracts match.
- Android native RecyclerView drawing and Avalonia desktop view layouts need
  different UI implementations. Reuse presentation data, policy and resources;
  retain native focus, insets, accessibility and scrolling behavior.
- `INotifyPropertyChanged`/`SetField` boilerplate alone is not a reason to add a
  large common view-model inheritance tree. Extract behavior with observable
  value, not every similar property declaration.
- Existing shared import, branding and storage components already provide useful
  seams. Extend those rather than adding parallel cross-platform frameworks.

## Suggested follow-up order

Start with the identical group-selection model and the async command adapters,
then the notification lifetime state. Those are bounded extractions with direct
behavioral tests. Address authorization coordination and account editing only as
separate reviewed security changes. Leave native Keystore consolidation until its
trust boundaries have dedicated regression evidence. Each extraction should
remove an actual second owner of behavior, preserve platform contracts and land
with tests for both presentations.
