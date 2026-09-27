# F-Droid reproducibility evidence record

Use one copy of this record for each independently provisioned build environment. Two records may
be compared only when they use the same immutable source commit and proposed F-Droid build recipe.
A same-machine rebuild is diagnostic evidence, not an independent result.

Do not include signing keys, passwords, tokens, real vaults, OTP seeds, user paths, or private
runner details. Store APKs and full logs in access-controlled release evidence when they cannot be
published safely.

## Candidate identity

| Field | Value |
| --- | --- |
| UTC date | |
| Operator or CI identity | |
| Source repository | |
| Immutable commit SHA | |
| Candidate tag, if any | |
| Proposed F-Droid recipe revision | |
| Clean worktree confirmed | Pass / Fail |

## Independent environment

| Field | Value |
| --- | --- |
| Environment identifier | A / B |
| Provisioning source or image digest | |
| Host OS and architecture | |
| Container or VM runtime/version | |
| .NET SDK (`dotnet --version`) | Must be `10.0.401` |
| Installed Android workload manifest versions | |
| Java runtime/version | |
| Android SDK packages and versions | |
| NuGet cache state | Empty / newly provisioned |
| Network/source restrictions | |

## Locked source build

Run from a clean checkout. Do not copy `bin`, `obj`, NuGet caches, workload packs, or generated
artifacts between environments.

```powershell
dotnet --version
dotnet workload install android --skip-manifest-update
dotnet restore TOTP.Android.sln --locked-mode --configfile NuGet.config
dotnet build TOTP.Android.sln -c Release --no-restore
./scripts/validation/Test-AndroidFossBuild.ps1
```

| Check | Result | Sanitized evidence location |
| --- | --- | --- |
| Pinned SDK selected | Pass / Fail | |
| Locked restore succeeded | Pass / Fail | |
| Release build succeeded | Pass / Fail | |
| FOSS validation succeeded | Pass / Fail | |
| Build produced the expected unsigned APK | Pass / Fail | |

## Artifact record

Record hashes before moving or comparing the artifact.

| Field | Value |
| --- | --- |
| APK filename | |
| APK byte length | |
| APK SHA-256 | |
| Retained evidence location | |

## Cross-environment payload comparison

After both artifacts are retained, run the repository comparator without adding exclusions:

```powershell
./scripts/validation/Compare-AndroidApkPayload.ps1 `
  -ReferenceApk path/to/environment-a.apk `
  -CandidateApk path/to/environment-b.apk `
  -ReportPath path/to/payload-comparison.json
```

| Field | Value |
| --- | --- |
| Reference environment/artifact | |
| Candidate environment/artifact | |
| Comparator exit code | |
| Non-signature payload match | Pass / Fail |
| Sanitized comparator output | |
| JSON comparison report and SHA-256 | |
| Differing entries, if any | |

## Review conclusion

- Toolchain acceptance is independently confirmed by F-Droid review: Yes / No
- Two independently provisioned environments match: Yes / No
- Proposed F-Droid recipe produces the matching payload: Yes / No
- Reproducible-build gate may be marked complete: Yes / **No unless every item above is Yes**
- Reviewer and UTC review date:
- Follow-up issue or decision record:

Passing this record is reproducibility evidence only. It does not approve dependency provenance,
metadata, signing policy, maintainer identity, or official F-Droid inclusion.
