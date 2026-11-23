# F-Droid dependency audit

The Android graph currently contains **140 locked package ID/version pairs** across seven projects,
including the separate Mono and NativeAOT application lock files.
After a locked restore, `Test-AndroidDependencyLicenses.ps1` matches every cache metadata content
hash to the committed lock file, verifies the downloaded package archive against its cached SHA-512
record, requires the canonical NuGet source, and reads the package's NuGet license declaration.

The review updated on 2026-11-23 found:

- 87 `MIT AND Apache-2.0` declarations
- 38 `MIT` declarations
- 12 `Apache-2.0` declarations (including the reviewed Guava third-party notice)
- 1 `MIT AND Apache-2.0 AND BSD-3-Clause` declaration
- 1 `ISC` declaration (`libsodium`)
- 1 reviewed MIT license file (`Otp.NET` 1.4.1)

All of those licenses are free-software licenses compatible with this repository's GPL-3.0-only
distribution. The validation allowlist is intentionally exact; a new package, version, expression,
or license-file hash requires review rather than silently passing.

## What this evidence does not prove

NuGet license metadata is supplied by package publishers. It does not prove source correspondence,
reproducible compilation, absence of bundled binaries, or acceptance under F-Droid's build-server
rules. In particular, the AndroidX bindings, Avalonia/Skia native assets, .NET Android workload, and
other prebuilt NuGet artifacts still require F-Droid maintainer/scanner review and a viable source
build recipe. This audit therefore strengthens the FLOSS dependency record but does not clear the
toolchain gate.

The NativeAOT graph adds `Microsoft.DotNet.ILCompiler`,
`runtime.linux-arm64.Microsoft.DotNet.ILCompiler`, and
`runtime.linux-x64.Microsoft.DotNet.ILCompiler`, all at 10.0.15. Their cached,
hash-verified NuGet metadata declares MIT and points to the
[.NET source repository](https://github.com/dotnet/dotnet). These are compiler
toolchain packages; the compiler executables are not Android app payloads. This
review does not establish reproducibility or F-Droid acceptance of that toolchain.

## Dependency update procedure

1. Change package references intentionally and regenerate all affected Android lock files.
2. Restore with the pinned SDK and canonical NuGet source.
3. Run `Test-AndroidDependencyLicenses.ps1`; review any new license expression or license file.
4. Inspect source repository links, bundled native files, and transitive dependencies for the changed
   packages; do not rely on the SPDX expression alone.
5. Update the aggregate counts above and the package-specific exception only after review.
6. Run vulnerability, build, and F-Droid input validation before committing project and lock changes.

## Security and compatibility impact

- **Threat impact:** matching restored package hashes to committed locks reduces substitution risk;
  an exact license allowlist prevents unreviewed licensing drift. Neither control makes package code
  trustworthy by itself.
- **Data-flow impact:** validation reads lock files, cached public NuGet package metadata, package
  hashes, and license text. It does not read vaults, OTP seeds, passwords, backups, or signing keys.
- **Compatibility impact:** runtime behavior and file formats are unchanged. Dependency updates now
  require an explicit license-review step.
- **Verification evidence:** the Android CI job runs the audit after the locked development and
  NativeAOT builds, when packages from both graphs must be available in the local cache.
