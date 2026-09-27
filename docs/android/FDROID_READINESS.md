# F-Droid readiness

OTP Harbor is **not currently available from the official F-Droid repository**. This document
tracks the evidence required before a submission can make that claim.

The authoritative external requirements are F-Droid's
[inclusion policy](https://f-droid.org/docs/Inclusion_Policy/),
[build metadata reference](https://f-droid.org/docs/Build_Metadata_Reference/),
[reproducible-build guidance](https://f-droid.org/docs/Reproducible_Builds/), and
[metadata and screenshot guidance](https://f-droid.org/docs/All_About_Descriptions_Graphics_and_Screenshots/).
They were last reviewed for this repository on 2026-09-27.

## Current gate status

| Gate | Status | Evidence or remaining work |
| --- | --- | --- |
| Public source and compatible license | Implemented | GPL-3.0-only source and notices are versioned in the repository. |
| Deterministic .NET SDK input | Implemented | `global.json` pins SDK `10.0.401` with roll-forward disabled. |
| Deterministic NuGet graph | Implemented baseline | Every project in `TOTP.Android.sln` has a committed `packages.lock.json`; Android CI and release jobs restore with `--locked-mode`. |
| Proprietary runtime dependency exclusion | Implemented baseline | `Test-AndroidFossBuild.ps1` rejects known proprietary service, analytics, advertising, and billing package families. This supplements, but does not replace, F-Droid's scanner and human review. |
| Locked NuGet license declarations | Implemented baseline | All 108 locked package ID/version pairs declare reviewed FLOSS licenses and cached package hashes must match the lock graph. Publisher metadata does not prove source correspondence or F-Droid acceptance. |
| F-Droid build-server-compatible toolchain | **Blocked** | The .NET Android workload and every prebuilt NuGet dependency must be accepted under F-Droid's FLOSS toolchain and binary-origin rules. A successful GitHub build does not establish this. |
| Reproducible APK evidence | **Blocked** | A tested comparator now checks non-signature APK entries by SHA-256. Same-machine clean-build evidence is only a baseline; two clean, independently provisioned environments and the proposed F-Droid build recipe must still match. |
| F-Droid metadata and screenshots | Implemented baseline | Localized listing text for all four supported languages and authentic synthetic-data screenshots are committed under `fastlane/metadata/android`. Final submission review remains required. |
| Signing and update-channel decision | Implemented policy | Initial acceptance uses F-Droid-managed signing. GitHub/Play signing material is never shared; switching signatures requires encrypted-backup migration. Activation remains blocked on the other gates. |
| Maintainer and F-Droid review | **Blocked** | Submission and acceptance must occur before public availability is claimed. |

## Locked Android build

Install the Android workload from the pinned SDK manifest, then restore only the committed graph:

```powershell
dotnet --version
dotnet workload install android --skip-manifest-update
dotnet restore TOTP.Android.sln --locked-mode --configfile NuGet.config
dotnet build TOTP.Android.sln -c Release --no-restore
./scripts/validation/Test-AndroidFossBuild.ps1
```

`dotnet --version` must print `10.0.401`. A package update is an explicit review operation: update
the project reference, regenerate the Android lock files with `--use-lock-file --force-evaluate`,
review resolved versions and content hashes, run the vulnerability and license checks, and commit
the project and lock changes together.

## APK payload comparison

Compare two APKs without treating ZIP timestamps, entry order, compression choices, JAR signature
records, or APK signing blocks as application payload differences:

```powershell
./scripts/validation/Compare-AndroidApkPayload.ps1 `
  -ReferenceApk path/to/first.apk `
  -CandidateApk path/to/second.apk
```

The comparator requires identical entry names, uncompressed lengths, and SHA-256 hashes for every
non-signature entry. Passing it demonstrates payload equivalence only. It does not prove that the
toolchain is acceptable to F-Droid, that two independent environments reproduce the payload, or that
signed APK bytes match. `Test-AndroidApkPayloadComparison.ps1` regression-tests these boundaries in CI.
Record each independent run with the
[F-Droid reproducibility evidence template](FDROID_REPRODUCIBILITY_RECORD_TEMPLATE.md); do not reuse
build outputs, caches, or generated intermediates between the two environments.

### Current local baseline

Clean same-machine builds from the same checkout were compared on Windows on 2026-09-27. These are
diagnostic results, not independent reproducibility evidence:

| Build profile | Non-signature payload result |
| --- | --- |
| Debug defaults | 36 embedded managed-assembly wrapper entries differed. |
| Release with deterministic CI flags | 238 Mono AOT native-library entries differed. |
| Release with `RunAOTCompilation=false` | Only the arm64 and x86_64 assembly-store entries differed. |
| Release with AOT and the assembly store disabled | Only the arm64 and x86_64 `_Microsoft.Android.Resource.Designer.dll` wrapper entries differed. |

[Microsoft's .NET Android build-property reference](https://learn.microsoft.com/dotnet/android/building-apps/build-properties#runaotcompilation)
documents that Mono AOT defaults to enabled for Release and disabled for Debug. Disabling
`AndroidUseDesignerAssembly` is not a viable workaround: the supported build fails with XA1034
because Avalonia.Android, HarfBuzzSharp, and SkiaSharp require the designer assembly. The comparator
therefore continues to treat these runtime entries as payload and the reproducibility gate remains
blocked. Do not add exclusions merely to turn this baseline green.

## Security and compatibility impact

- **Threat impact:** locked package hashes and a pinned SDK reduce dependency substitution and
  unreviewed toolchain drift. They do not prove that a package is trustworthy, reproducible, or
  acceptable to F-Droid.
- **Data-flow impact:** build validation processes source, project metadata, and public dependency
  metadata only. It does not read vaults, OTP seeds, passwords, signing keys, or backup files.
- **Compatibility impact:** application ID, Android version mapping, backup format, vault format,
  and signing identity are unchanged. SDK or dependency upgrades now require deliberate lock-file
  updates.
- **Verification evidence:** CI validates the pinned SDK declaration, lock-file completeness and
  content hashes, NuGet source restriction, prohibited package families, locked restore commands,
  localized Fastlane metadata and image constraints, and this non-availability disclosure.
