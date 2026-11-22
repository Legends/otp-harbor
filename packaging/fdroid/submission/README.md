# Official F-Droid submission draft

This folder publishes a review draft for `fdroiddata`, separate from OTP
Harbor's maintainer-operated binary repository. The existing
[packaging request, RFP #4758](https://gitlab.com/fdroid/rfp/-/work_items/4758),
is open; this metadata draft has not been submitted as a `fdroiddata` merge
request or accepted. The build is explicitly disabled while policy and build
checks remain open. No signing keys or credentials are required for this review.

RFP status checked on 2026-11-22: opened on 2026-11-07, with labels `fdroid-bot`
and `git-url`. The maintainer supplied their comment asking about .NET Android
toolchain compatibility and the permitted origin of NuGet binary dependencies.
No reviewer response has been verified. Use the existing request for follow-up. A
[technical update draft](RFP_UPDATE.md) summarizes the evidence and open question;
it has not been posted.

The proposed file to copy into a dedicated branch of a `fdroiddata` fork is
[`metadata/io.github.legends.otpharbor.yml`](metadata/io.github.legends.otpharbor.yml).
It targets immutable Android release `android-v3.0.3`, commit
`c801e73224f3d6abed33396f858c54866b8e70f2`, with version code `30000399`.
That tag pins SDK **10.0.402**; the current development branch pins **10.0.404**.
Do not silently change the tagged SDK or Android version mapping. A newer initial
submission should use a new reviewed Android tag, with matching lock files and
updated recipe/toolchain hashes.

## Review decisions still required

| Item | Status | Required evidence |
| --- | --- | --- |
| .NET Android toolchain | Blocked | F-Droid packagers must accept its provenance or provide a source-built/approved compiler recipe. The draft pins Microsoft's Linux SDK archive with SHA-512, but that does not establish policy acceptance. |
| NuGet packages and native libraries | Blocked | Review [the tagged dependency inventory](NUGET_DEPENDENCIES.json): 137 package/version pairs across seven Android solution projects. Source correspondence and an accepted acquisition/build strategy are still required. SDK/workload packs are outside this inventory. |
| Buildserver recipe | Unverified | Clean Linux build with the specified SDK/workload, Java 21, SDK dependencies and locked restore. Confirm the unsigned APK filename, manifest identity/version and scanner result. |
| Automatic updates | Disabled | The Android version is injected by CI. Default tag scanning cannot infer CI-injected release versions from the csproj; do not enable the pasted `Version`/`Tags` example without a tested extraction strategy for the existing version-code formula. |
| Signing | Decided | F-Droid-managed signing, as documented in [the channel policy](../../../docs/android/FDROID_SIGNING_AND_UPDATES.md). Cross-signature installation requires encrypted-backup migration. |
| Listing and images | Existing baseline | Use all four locales under `fastlane/metadata/android` at the chosen tag. The draft omits top-level Summary/Description fields so they do not override upstream translations. Review screenshots and release changelog before submission. |

The recipe intentionally has no `Binaries`, upstream signing-key allowlist,
`scanignore` or `scandelete` shortcuts. Its `prebuild` phase bootstraps the pinned
SDK outside the source tree, installs the Android workload and dependencies, and
restores the locked graph. Its `build` phase publishes from source with explicit
package/version properties. The unsigned APK sibling is selected; neither
upstream production signing material nor the debug-signed sibling is submitted.
The bootstrap and NuGet strategy are proposals requiring packager review.
The dependency-install target also sets `RestoreLockedMode=true`, so its implicit
restore cannot bypass the committed lock graph. The unsigned output is under
`artifacts/android/release/fdroid/apk/`, following the platform-first artifact layout.

## Validation before submission

Inside the dedicated `fdroiddata` branch, with a current compatible `fdroidserver`:

```bash
fdroid readmeta
fdroid rewritemeta io.github.legends.otpharbor
fdroid lint io.github.legends.otpharbor
```

Only after the policy blockers are resolved and the recipe is updated should its
`disable` reason be removed in the review branch and the real build be tested:

```bash
fdroid build --server --verbose io.github.legends.otpharbor
```

Check the resulting APK's package name, version name/code, unsigned output and
runtime dependency scan. Keep F-Droid's own checks enabled. Do not use a passing
GitHub build or the separate signed binary feed as source-build evidence.

## Validation evidence (2026-11-22)

Validation used `fdroidserver 2.4.5` (the same version pinned by the repository's
F-Droid workflow), Python 3.12.3 and Androguard 3.3.5 in an isolated WSL
environment. Windows downloaded/built the required wheels; WSL installed them
offline. No global network configuration or production signing material was used.

| Check | Result |
| --- | --- |
| `fdroid readmeta` | Passed with the actual parser. |
| `fdroid rewritemeta` | Passed; the draft uses its canonical output, with metadata semantics compared before copying it back. |
| `fdroid lint` | Passed using the official `fdroiddata` category keys. A temporary copy without the build-disable marker also passed lint; the review draft stays disabled. |
| Tagged source scan | Zero fatal findings and no reported warnings/errors, using the public SUSS signatures. This scanned the untouched release source before workload installation, package restoration or compilation. |
| Static recipe checks | Passed: immutable tag/SHA, SDK pin and published archive checksum, version mapping, signing boundaries and bash syntax for all three phases. |
| Application dependency inventory | All 137 package/version pairs have consistent SHA-512 content hashes across the seven tagged projects. This does not prove source correspondence or policy acceptance. |
| Full buildserver, restored dependencies and APK scan | Unverified. These require the accepted toolchain, dependency strategy and a complete build. |
| APK signature verification | Unverified; the metadata-only environment has no `apksigner`. Its warning was retained in the logs. |

The [published validation snapshot](validation/validation-results.json) includes
tool results and links to the captured text logs. The
[source-scan report](validation/source-scan.json) records its scope and findings.
The original local outputs remain under
`artifacts/shared/testing/fdroid-submission/20261122/`; those generated files are
not required to read this published packet.
Local .NET Android target inspection supports the expected unsigned APK naming
and publication of APK siblings; it does not substitute for a tag-specific Linux
build or signature verification. Passing these preparatory checks does not mean
the application is accepted or buildable on the official service.
This preparation created no new GitLab fork, issue or merge request. RFP #4758
already exists and was supplied by the maintainer.

Security scope: this disabled draft handles public source and toolchain artifacts
only. It preserves package identity, existing signing separation, vault formats
and network-permission policy. The archive checksum and locked restore constrain
substitution; they do not resolve the dependency-origin review. No production
key, password, vault or backup is read or supplied to F-Droid.

Authoritative references reviewed on 2026-11-22:

- [Submission guide](https://f-droid.org/docs/Submitting_to_F-Droid_Quick_Start_Guide/)
- [Inclusion policy](https://f-droid.org/docs/Inclusion_Policy/)
- [Build metadata reference](https://f-droid.org/docs/Build_Metadata_Reference/)
- [Official category configuration](https://gitlab.com/fdroid/fdroiddata/-/raw/master/config/categories.yml)
- [Microsoft .NET 10 SDK release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
- [Repository readiness evidence](../../../docs/android/FDROID_READINESS.md)
