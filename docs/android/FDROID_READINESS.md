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
| F-Droid build-server-compatible toolchain | **Blocked** | The .NET Android workload and every prebuilt NuGet dependency must be accepted under F-Droid's FLOSS toolchain and binary-origin rules. A successful GitHub build does not establish this. |
| Reproducible APK evidence | **Blocked** | Build the same tagged source in two clean, independently provisioned environments and compare the unsigned APK payload. Then reproduce it using the proposed F-Droid build recipe. |
| F-Droid metadata and screenshots | Planned | Review localized descriptions and place authentic, synthetic-data screenshots in the source layout accepted by F-Droid. Existing Google Play artwork is not automatically treated as submitted F-Droid metadata. |
| Signing and update-channel decision | Planned | Decide between F-Droid signing and a verified reproducible upstream-signed APK. Keep this independent from GitHub's protected signing credentials. |
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
  and this non-availability disclosure.
