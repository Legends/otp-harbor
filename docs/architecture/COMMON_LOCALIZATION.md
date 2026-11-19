# Common localization

`OtpHarbor.Localization` owns all translated application text for desktop, mobile
and the updater. It targets plain `net10.0`, references only the .NET base class
library, and has no Avalonia, WPF, Android, OS-service or domain-project dependency.
Windows, Linux and macOS use the desktop presentation; Android uses the mobile
presentation. A future iOS or other .NET host can reference the same library
without depending on either UI project. There is no iOS application host in this
repository today; this change does not introduce one.

## Resource ownership

All English, German, French and Spanish `.resx` files are under
[`OtpHarbor.Localization/Resources`](../../OtpHarbor.Localization/Resources).

| Resource group | Purpose |
| --- | --- |
| `Common` | Identical messages shared across presentations. |
| `Desktop` | Desktop-only messages and wording that differs from mobile. |
| `Mobile` | Mobile-only messages and wording that differs from desktop. |
| `Updater` | Update/install workflow messages. |

These are presentation contexts, not operating-system checks. Choose the context
for the workflow being presented; do not detect the device OS inside localization.
For example, a shared mobile UI on iOS can use `Mobile` in the same way as Android.

The migration preserves all 5,480 existing translated messages, including
placeholders, Unicode and compiled line endings. The invariant catalogs initially
contained 750 desktop keys, 597 mobile keys and 23 updater keys. There are now
373 common messages, 377 desktop-specific messages, 224 mobile-specific messages
and 22 updater-specific messages per language: 1,496 duplicate translated entries
were removed across the four languages. Forty-eight desktop/mobile key families
had wording differences, and one additional family had different compiled line
endings. They retain explicit context-specific entries. Identical wording under
different key names was not automatically merged: semantic equivalence needs
review, especially for security messages and formatting arguments.

## Lookup and live language changes

```csharp
using System.Globalization;
using TOTP.Localization;

var catalog = new LocalizationCatalog();
var cancel = catalog.Get(CommonStringKeys.Cancel, CultureInfo.GetCultureInfo("de-DE"));

var desktop = new LocalizationCatalog(LocalizationScope.Desktop);
var backup = desktop.Get("BackupSection", CultureInfo.GetCultureInfo("en"));
```

`LocalizationCulture.Resolve` consistently maps supported regional cultures such
as `de-AT`, `fr-CA` and `es-MX` to their neutral translated language. Unsupported,
blank or invalid culture names resolve to English. Lookup checks the context's
resources and then common resources; .NET `ResourceManager` supplies English
fallback. An unknown key returns the key, keeping missing messages visible.
Resource managers are shared and culture is passed explicitly; lookups do not
change process-wide culture and are safe for concurrent language requests.

The desktop `AvaloniaStringCatalog` and mobile `MobileStringCatalog` remain small
compatibility adapters. Desktop's localization service updates Avalonia dynamic
resources and raises its existing culture-change event. Mobile retains its
instance culture and view-model property notifications. `UpdaterText` uses the
same catalog with the updater context and continues formatting values with the
caller's current UI culture.

`CommonStringKeys` and `UpdaterStringKeys` are defined in the common library.
Existing `AvaloniaStringKeys` and `MobileStringKeys` types also live there, under
their existing namespaces to avoid changing consumers. Their shared constants
alias `CommonStringKeys`; context-specific constants and expected-key lists
remain explicit. UI assemblies forward the old key types. Those legacy namespace
names do not introduce a UI dependency into the library. Existing shared
import/storage/about key contracts retain their domain-aware enum mappings and
alias the common constants where applicable.

## Adding or changing translations

1. Put wording shared by multiple presentations in `Resources/Common.resx` and
   add its typed constant to `CommonStringKeys` and its `All` list. Keep a
   context-specific resource only when wording or formatting genuinely differs.
2. Update the same key in `.de.resx`, `.fr.resx` and `.es.resx` in that group.
   Each group must be complete; English fallback must not hide missing supported
   translations. Preserve composite-format indexes and format specifications.
3. Add keys to the consuming compatibility facade/expected-key list when that
   presentation uses them. Shared domain-aware string-key helpers may alias the
   common key rather than repeat its literal value.
4. Avoid incidental line-ending changes. Explicit `&#xA;`, `&#xD;` and `&#x9;`
   entities preserve runtime text independently of source-file line endings.
5. Run `scripts/validation/Test-LocalizationResources.ps1` and the localization
   tests. The validator checks every group's keys, values and placeholders and
   rejects context entries that redundantly copy a common value in all locales.

To add a language, provide a resource file for every group, add it to
`LocalizationCulture.Supported`, update both presentations' language-selection
adapters and extend validation/tests. Do not add a second resource-loading engine
or put translated strings back in the UI project assemblies.

## Packaging and verification

Both solution files include the common library. The Android locked dependency
graph includes it as an eighth project; it adds no NuGet packages. Normal desktop,
updater and Android builds must carry `TOTP.Localization.dll` and its German,
French and Spanish satellite assemblies. Android release checks must also inspect
the assembly store after trimming/AOT; checking that `.resx` source files exist is
insufficient. Keep updater dependency files alongside the updater executable.
Build desktop and Android compilations sequentially, or isolate their shared
`bin`/`obj` directories, to avoid contention over common assemblies and symbols.

Use `scripts/validation/Test-LocalizationPackaging.ps1 -PublishDirectory <output>`
to check managed publish directories (including the updater when present), and
`-AndroidApkPath <apk>` to check the managed assembly-store names in an Android
release APK. These checks prove inclusion; native language-switch interaction
still requires platform execution.

`CommonLocalizationTests` cover neutral/regional/unsupported cultures, exact-locale
completeness, format-argument contracts, intentional presentation differences,
missing-key behavior, type forwarding, satellite ownership and concurrent lookup.
Existing desktop/mobile/updater localization and headless UI tests remain in use.
Native iOS/macOS/Linux execution requires the corresponding host and toolchain.

This is a resource-ownership and lookup refactor. It changes no authentication,
password/KDF, vault, backup, update-verification or storage format. Existing legal
disclosures and upstream links are preserved verbatim. Their notices were checked
against the [Simple Icons disclaimer](https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md),
[Aegis Icons disclaimer](https://github.com/aegis-icons/aegis-icons#disclaimer)
and [Dashboard Icons legal notice](https://github.com/homarr-labs/dashboard-icons#legal).
No third-party assets or additional asset rights are introduced by this move.
