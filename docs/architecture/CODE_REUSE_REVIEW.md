# Application code reuse review

Reviewed 2026-11-19 against the current working tree, including the shared App
Lock reactivation fix. This extends the earlier
[localization duplication audit](DUPLICATION_AUDIT.md).

## Recommendation

Increase reuse by giving each policy and workflow one owner. Keep desktop and
Android shells, native rendering, OS authentication and lifecycle integration as
adapters. Prefer small composed models and request/result workflows over a common
application-shell base class or a generic platform framework.

The largest maintainability opportunity is the repeated account/settings workflow
code in desktop presentation and `MobileShellViewModel`. The latter currently has
7,505 lines in its main file; desktop `AccountListViewModel` has 3,136. Size alone
does not prove duplication, and moving methods into partial files would not create
reuse. Extract coherent behavior with tests and explicit lifetimes.

## Scope and evidence

The inventory covered all 16 production projects: 508 C# files and 25 Avalonia XAML
files, totaling 74,995 lines. Generated/build output and tests were excluded from
those counts. Project references, shared registrations, the earlier audit,
selected release/build configuration, related tests and representative feature
implementations were also examined.

A normalized C# scan found seven cross-project groups with at least twelve
matching meaningful lines, excluding imports, namespace declarations, braces and
localization-key declarations. Manual comparisons additionally identified
semantic duplication and duplication within a project, such as signature
verification. The scan is a candidate finder: short helpers, renamed code and
different orchestration of the same behavior need manual review. This is an
architecture/reuse assessment, not a line-by-line security audit or a runtime
performance benchmark. No runtime speed improvement is claimed below.

## Existing shared owners to retain

| Area | Current owner and decision |
| --- | --- |
| Domain and validation | **Core** owns account models, TOTP/secret/group/clipboard policies and wire contracts. Extend these policies rather than creating host-specific validation. |
| Crypto and authorization | **Infrastructure** owns `PortableAuthorizationService`, envelope lifecycle/session, enrollment and vault cryptography. Both hosts use the same service registration through `AddInfrastructure`. The reactivation fix now shares verification/enrollment/commit and failure handling. |
| Persistence | **DAL** owns account, preferences, envelope and backup-configuration stores. Keep persistence details out of presentation. Consolidate low-level write mechanics only with the safeguards described below. |
| Localization | **Localization** owns Common/Desktop/Mobile/Updater resources and culture resolution. No further resource-loader framework is needed. |
| Shared presentation | **UI.Avalonia.Shared** already owns branding resolution, ranked search, import review, About, storage locations, theme resources and reusable controls. This is the natural home for portable presentation models. |
| Desktop shell | **UI.Avalonia.Desktop** remains responsible for windows, dialogs, keyboard/focus, tray, minimize and desktop navigation. Thin adapters should call shared workflows. |
| Mobile shell | **UI.Avalonia.Mobile** remains responsible for mobile navigation and presentation state. Split out feature collaborators; avoid making it inherit desktop shell behavior. |
| Android host | **UI.Avalonia.Android** owns activities, intents, SAF documents, native lists, accessibility, insets and lifecycle. Reuse model/policy below the host and Android drawing helpers within the host. |
| Windows platform | **Platform.Windows** keeps Hello/CNG, Windows session integration and installation/mail adapters. It implements shared contracts. |
| macOS platform | **Platform.MacOS** keeps Keychain presence and unattended-store distinctions, session integration and native services. |
| Linux platform | **Platform.Linux** keeps Secret Service/runtime/session boundaries and its existing camera integration. |
| Unix platform | **Platform.Unix** already shares Unix filesystem/native support. ACLs and Unix modes should continue to have separate platform implementations. |
| Android platform | **Platform.Android** keeps Keystore, biometric/device-credential and unattended key contracts. Shared orchestration does not imply interchangeable authentication policies. |
| Camera | **Camera.OpenCv** owns desktop capture/preview/decode timing. Android camera activities have different lifetimes. Reuse QR validation/import policy, not their capture loops. |
| Updater | **Updater** remains a separate executable with staging, replacement, rollback and relaunch responsibilities. Do not make its file transaction depend on a running app session. |
| Installer | **Installer** keeps setup/repair/uninstall UI and its WXL assets. A different resource format alone does not justify coupling the installer to the main shell. |

## Prioritized opportunities

P1 means a good next extraction, not a claim that the current code has a security
vulnerability. Effort and risk are relative; each change should be independently
reviewable.

| ID | Priority | Scope | Benefit / cost |
| --- | --- | --- | --- |
| R1 | P1 | Async commands | One re-entry/notification contract; small change, medium behavior risk. |
| R2 | P1 | Group selection item | Identical model; small change, low risk. |
| R3 | P1 | Notification and copy-feedback lifetime | One cancellation/replacement implementation; medium change/risk. |
| R4 | P1 | Diagnostic log discovery and snapshot preparation | Remove filesystem orchestration from both UI models; medium change/risk. |
| R5 | P1 | Authentication selection and recovery decisions | Prevent future desktop/Android drift; high value, high security review burden. |
| R6 | P1 | Account-edit workflow | One edit/save policy; high value, larger change and security review. |
| R7 | P1 | FAQ membership/catalog | Keep shared FAQ items in sync by construction; small/medium change, low risk. |
| R8 | P2 | Token presentation/countdown state | One rollover/notification policy; medium change, performance-sensitive. |
| R9 | P2 | Local icon-selection state | One pending selection/cancel contract; medium change/risk. |
| R10 | P2 | Avalonia realization and focus helpers | Remove duplicate view plumbing; medium change, lifecycle/performance-sensitive. |
| R11 | P2 | Android brand appearance calculation | Identical internal drawing decision; small change, low/medium risk. |
| R12 | P2 | Appcast signature primitive | Reuse the existing payload verifier; small change, security-sensitive. |
| R13 | P3 | Atomic storage write mechanics | Useful only if rollback/verification contracts stay explicit; larger change, high risk. |
| R14 | P3 | JSON duplicate-property primitive | Small saving, differing current scopes; small change, compatibility-sensitive. |
| R15 | P2 | Updater bundle MSBuild targets | One file list/build preparation; small/medium change, release-sensitive. |
| R16 | P3 | Dependency version declarations | Lower update maintenance cost; configuration migration, no current drift found. |

### R1–R3: small shared presentation building blocks

**R1.** [Desktop AsyncCommand](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/AsyncCommand.cs)
and [MobileAsyncCommand](../../OtpHarbor.UI.Avalonia.Mobile/Presentation/MobileAsyncCommand.cs)
have generic/non-generic variants of the same `ICommand` adapter. Mobile already
uses an atomic execution guard; desktop relies on its supplied predicate. Move
the execution contract into Shared and preserve the stronger guard. Keep command
predicates at call sites. Decide exception routing explicitly; a shared command
must not silently swallow failures or add unsolicited dialogs. Test rapid taps,
generic arguments, synchronous exceptions, async exceptions, guard cleanup and
`CanExecuteChanged`. The expected benefit is consistent execution, not speed.

**R2.** [GroupAccountSelectionViewModel](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/GroupAccountSelectionViewModel.cs)
and [MobileGroupAccountSelection](../../OtpHarbor.UI.Avalonia.Mobile/Presentation/MobileGroupAccountSelection.cs)
have identical account/brand fields and `IsSelected` notification. A single Shared
selection item is sufficient. Migrate compiled bindings and Android native picker
subscriptions together; temporary wrappers are justified only for compatibility.
Verify identity, unchanged-value notification suppression, selection round trips
and native accessibility updates.

**R3.** [NotificationState](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/NotificationState.cs)
and mobile `SetTransientNotification`/`ClearNotificationAfterDelayAsync` duplicate
replacement, cancellation and expiry. Copy confirmation repeats the same lifetime
pattern in both account presentations. Share a small lifetime component or
notification model using an injected `TimeProvider`; retain persistent messages,
desktop `Shown` events and mobile severity reset. Keep displayed text localized
and do not capture secret-bearing objects in timer callbacks. Extend existing
`NotificationStateTests` with replacement, clear/dispose, repeated identical text,
locale changes and deterministic expiry for both adapters.

### R4–R7: shared feature workflows

**R4.** `TryGetCurrentLogFilePath` is repeated in
[SettingsPageViewModel](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/SettingsPageViewModel.cs)
and [MobileShellViewModel](../../OtpHarbor.UI.Avalonia.Mobile/Presentation/MobileShellViewModel.cs).
`CopySnapshotAsync` and expired snapshot cleanup are also repeated in
[WindowsDiagnosticLogEmailService](../../OtpHarbor.Platform.Windows/WindowsDiagnosticLogEmailService.cs)
and [AndroidDiagnosticLogEmailService](../../OtpHarbor.UI.Avalonia.Android/AndroidDiagnosticLogEmailService.cs).
Create a filesystem-facing diagnostic-log preparation service in Infrastructure
behind a Core contract. It should own discovery, filename validation, snapshot
creation and retention; the OS adapter should open the mail/share draft. Preserve
concurrent log-writer sharing, unique `CreateNew` snapshots, path/reparse checks,
permissions and ownership after a draft opens. Keep user review/disclosure and
environment formatting at their existing boundaries. Test missing directories,
rolling-log selection, locked files, cancellation, cleanup failures and native
draft lifetime. Existing diagnostics tests are a starting point.

**R5.** Desktop `ChangePreferredUnlockMethodAsync` and mobile
`PrepareSensitiveAuthenticationAsync`/`ApplyUnlockMethodChangeAsync` interpret
saved/current methods, availability and recovery results independently. Desktop
currently includes `DisabledByPolicy` among fallback outcomes; mobile branches
depend on the operation and `fromMethodSelection`. Treat these as observable
differences to resolve explicitly, not proof of an exploitable defect. Extract a
typed decision policy or scoped authorization workflow that distinguishes the
operation and returns the next action. Keep native prompting in adapters and all
mutations in `PortableAuthorizationService`. The new shared
`ReactivateAppLockWithPlatformAsync` is the existing owner for reactivation; do not
add another implementation. Use the same scenario matrix for desktop/mobile:
saved versus projected method, cancel, unavailable, policy-disabled, password
recovery, lock/dispose/close, stale results and partial commit. Never introduce a
cached authorization token merely to reduce prompts.

**R6.** Both `SaveAccountAsync` implementations repeat identity trimming,
existing-account lookup, unchanged-secret preservation, secret replacement checks,
duplicate detection, TOTP validation, persistence and secondary icon-save outcome.
Desktop validates some fields before loading accounts; mobile validates later.
Desktop also applies the currently selected group to a new account. A shared
account-edit workflow can return typed field errors and partial-success outcomes,
with explicit input for intended group membership. Put persistence orchestration
in Infrastructure behind a Core contract and portable editor state in Shared.
Keep navigation and message placement in each UI. Build on existing Core validation and `AccountManager`;
avoid duplicating their validation or creating a second persistence route. Treat
secret authorization as a scoped workflow state, not an arbitrary reusable
caller-supplied boolean. Preserve unchanged legacy secrets, clear edited inputs,
and do not expand secret lifetime through a long-lived request cache. Test new and
existing accounts, stale IDs, duplicate identities, unauthorized replacement,
unchanged/blank secret, algorithms/digits/periods, groups, persistence failure,
icon-save failure and locking while work is pending. Relevant tests already live
in `AccountListViewModelTests`, mobile editor tests and validation/service tests.

**R7.** The FAQ translations are shared, but membership/order and nested content
remain independently declared in
[SettingsWindow.axaml](../../OtpHarbor.UI.Avalonia.Desktop/SettingsWindow.axaml)
and [MainView.axaml](../../OtpHarbor.UI.Avalonia.Mobile/Views/MainView.axaml).
A typed Shared FAQ catalog can supply stable IDs, ordering, question/answer keys
and explicit platform applicability. Preserve rich/nested content with bounded
templates keyed by item ID; a plain string list would lose existing content.
Keep desktop accordion and mobile layout choices in their views. Add a parity
test comparing shared IDs and explicit platform exceptions, alongside all-locale
and compact-layout tests. This addresses the user's synchronization requirement
more durably than another pair of manually maintained lists.

### R8–R11: presentation performance and native drawing

**R8.** [AccountListItemViewModel](../../OtpHarbor.UI.Avalonia.Desktop/Presentation/AccountListItemViewModel.cs)
and [MobileAccountItem](../../OtpHarbor.UI.Avalonia.Mobile/Presentation/MobileAccountItem.cs)
repeat countdown, expiring/next-code transitions and dirty-binding handling.
TOTP generation already has one owner, `AccountTotpService`; share only the
remaining small presentation state machine. Retain mobile loading state and
native adapter needs. Avoid a per-tick allocation/event-forwarding layer or one
timer per row. Test rollover, empty/invalid code clearing, next-code preference,
offscreen deferral and first visible refresh. Measure allocations, notifications
and frame behavior before/after using large lists; existing behavior tests alone
do not establish a performance gain.

**R9.** The two editors duplicate selected icon filename projections, pending
custom selection and restoration/cancel state. Move that local state into a
Shared editor collaborator, using existing `IconNameDisplayPolicy` and branding
contracts. Keep document pickers and navigation in their hosts. Test replacement,
cancel, save failure, clearing pending buffers and selection after reload. This
does not require changing the underlying asset/import implementation.

**R10.** Desktop and mobile code-behind repeat
`ScheduleRealizedAccountReport`, container wiring and
`RefocusAccountPeriodAfterClear`/`PostInputFocus`. A small attachable Avalonia
behavior can coalesce realization reports with explicit suspension/visibility
predicates; a separate focus helper can cover the identical focus operation.
Mobile intentionally suppresses updates during scrolling and limits full-list
code refresh at 50 items. Desktop also flushes dirty visible bindings. Preserve
those differences, callback cancellation and detach behavior. Test recycling,
scroll suspension, stale queued callbacks, attach/detach and focus after clear.
Do not centralize native RecyclerView scrolling through Avalonia container logic.

**R11.** Android `UpdateBrandAppearance` in
[NativeAccountRowView](../../OtpHarbor.UI.Avalonia.Android/NativeAccountRowView.cs)
and [NativeGroupAccountPickerRecyclerView](../../OtpHarbor.UI.Avalonia.Android/NativeGroupAccountPickerRecyclerView.cs)
computes the same embedded-background flag and fallback color. An internal
Android appearance helper can return these values. Keep drawing, cache lifetimes,
invalidation and the row's scrolling constraints in the respective views. Verify
both themes, embedded/solid backgrounds, malformed color fallback and recycled
rows. A cross-platform drawing abstraction would add more cost than this helper.

### R12–R16: infrastructure and build consistency

**R12.** [SignedAppcastVerifier](../../OtpHarbor.Infrastructure/Services/SignedAppcastVerifier.cs)
privately repeats Base64 decoding, Ed25519 key/signature size checks, raw key
import, verification and buffer cleanup already provided by
[SignedPayloadVerifier](../../OtpHarbor.Infrastructure/Services/SignedPayloadVerifier.cs).
Inject/reuse `ISignedPayloadVerifier` for the signature primitive. Preserve
appcast size limits, signature-before-XML-parse order, prohibited DTD resolution,
target/channel selection and failure classification. Keep format-specific policy
in `SignedAppcastVerifier`. Test valid/tampered/wrong-key payloads, malformed
Base64, empty/oversized input and invalid XML. Do not merge update installation
with signature validation or change the signed bytes during extraction.

**R13.** DAL stores repeat staging files, disk flush, permission restriction,
replacement, verification and cleanup. However, envelope storage retains a
`.previous` file and verifies the committed result; preferences and account
storage have rollback paths; backup configuration follows a different contract.
Do not replace them with a generic `SaveJsonAsync`. A small staged-write primitive
may be useful after documenting each transaction contract. Keep serialization,
staged/final verification, rollback ownership, cancellation commit point and
error mapping explicit. Require fault-injection tests at every write/replace/
permission/verify boundary and recovery tests before changing these stores.
Avoid extra whole-file copies or weaker flush behavior. Defer this until the
higher-value presentation extractions are established.

**R14.** Five Core codecs implement `HasDuplicateProperty`: envelope,
preferences, automatic-backup configuration, backup V1 and backup V2. They are
not currently identical: preferences inspect root properties, while the envelope
recurses through objects/arrays; depth and size limits also differ. An internal
JSON helper is reasonable only with an explicit traversal scope and unchanged
format-specific validation/limits. Nested duplicate rejection would be a behavior
change, not merely reuse. Retain case sensitivity and each codec's error mapping;
test root/nested/array duplicates, case differences and depth/size boundaries.
Given the small code saving and compatibility burden, this is low priority.

**R15.** `CopyWindowsUpdaterBundleToOutput` and
`CopyWindowsUpdaterBundleToPublish` in the
[desktop project](../../OtpHarbor.UI.Avalonia.Desktop/OtpHarbor.UI.Avalonia.Desktop.csproj)
repeat updater build properties, exclusions and recursive file lists. Share build
preparation and the item list, retaining destination-specific targets. Confirm
MSBuild property context and target ordering rather than depending on the
existing incidental double build. Verify fresh Build/Publish directories,
satellite/dependency files, version metadata and packaging/signing inputs. Keep
desktop and Android release identities, tags, signing and artifact flows separate.

**R16.** Repeated package versions are currently consistent in the inspected
projects: e.g. FluentResults in seven projects, logging abstractions in six,
Avalonia Fluent in three. Central package version declarations could reduce
upgrade maintenance, but this is not a current version-drift defect. Treat a
`Directory.Packages.props` migration separately with lockfile regeneration,
conditional-target review, desktop/Android restore/build and publish validation.
Keep `Directory.Build.props` for shared product metadata; do not centralize
different target frameworks or runtime settings simply because the XML is similar.

## Boundaries where more reuse would be counterproductive

- Windows Hello, macOS Keychain and Android Keystore have different presence
  semantics. Registration verifies on Windows/Android; macOS verifies by reading
  the protected item. Share orchestration and results, retain native operations.
- Authenticated and unattended wrappers must keep separate policy, provider,
  key-reference and associated-data contracts. Shared internal primitives are
  possible, but collapsing these into a mode flag throughout the app would make
  security review harder. Existing `SecretStoreUnattendedUnlock` already shares
  the appropriate desktop store orchestration.
- Android SAF permissions/content URIs, desktop filesystem destinations and OS
  mail/share launchers are different adapters. Common backup and diagnostic
  preparation should not assume all destinations are ordinary writable paths.
- Versioned backup codecs and different external parser formats need distinct
  validation and recovery paths. Extend existing import/review services instead
  of merging formats into an opaque generic parser.
- Native Android rows and Avalonia controls should share data and policy, not
  layout/drawing implementations. Preserve native accessibility and scrolling.
- Desktop minimize/session rules and Android background rules are different
  lifecycle inputs. Their shared authorization mutation does not require one
  universal lifecycle state machine.
- Installer/updater/bootstrap code must retain independent startup, recovery and
  packaging behavior. A large shared UI dependency would increase their coupling
  and failure surface.
- Similar `SetField` methods, tiny exception wrappers and simple DTO declarations
  alone do not justify a large inheritance hierarchy or new dependencies.

## Suggested implementation order and acceptance gates

1. Extract R2 and R1 in separate small changes. Preserve public/binding contracts
   and establish shared behavior tests with both presentation adapters.
2. Extract R3/R4 and R7. These give visible consistency benefits without touching
   vault formats. Include lifecycle, locale and responsive UI checks.
3. Extract R5 and R6 independently under explicit security review. Reuse existing
   shared authorization and validation; cover every failure/cancellation branch
   in matching desktop/mobile scenarios. No authorization bypass or migration.
4. Extract R8/R10/R11 only with before/after allocation and scrolling measurements.
   Implement R9 alongside account-editor work if it removes a real second owner.
5. Handle R12/R15 as focused security/release changes. Consider R13/R14/R16 later
   if maintenance evidence justifies their migration cost.

Each extraction should remove the second implementation, not leave a shared helper
plus a legacy copy. Prefer constructor injection and composition. Keep platform
exceptions explicit and localized resources in the existing catalog. Test shared
behavior once, then test each adapter's observable contract; retain native smoke
checks where mocks/headless tests cannot prove OS behavior. Stop an extraction if
it adds branches, allocations or coupling without eliminating meaningful policy
duplication.

## Current verification baseline

Before this analysis, the reactivation/FAQ fixes passed 2,643 main tests and 288
headless UI tests, with six existing opt-in tests skipped. Release solution build,
Android Debug `Compile`, EN/DE/FR/ES resource validation and whitespace checks
passed. Native authentication prompts and interactive device workflows remain
manual verification items. This review introduces recommendations only; it does
not claim that any proposed extraction or performance improvement is implemented.
