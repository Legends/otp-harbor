# M7 release integrity and update policy

## Scope

M7.1 establishes a shared integrity contract for Avalonia release artifacts before credentialed native publication is enabled. CI now validates native dependency closure, generates target-specific artifact manifests, verifies every manifest hash and size, and fails on known vulnerable NuGet packages. These controls complement platform signing; they do not replace Authenticode, Developer ID, notarization, or Ed25519 update signatures.

The CI matrix also loads the real Avalonia application resources and main-window XAML under Avalonia Headless on all three supported runners. This detected and closed an Avalonia 12 custom-theme construction failure that ordinary compilation did not exercise.

## Artifact manifest

`New-ReleaseArtifactManifest.ps1` accepts only the selected Windows x64, macOS ARM64, and Linux x64 artifact names. Every entry records the target, format, byte length, lowercase SHA-256, installation ownership, and update policy. The manifest contains the full source commit and release version and deliberately omits timestamps so identical inputs produce stable content.

`Test-ReleaseArtifactManifest.ps1` rejects duplicate names, traversal, missing files, changed sizes, and changed hashes. Manifest generation also rejects direct-update artifacts above the client's 128 MiB safety limit. The manifest is audit metadata; the release workflow must still sign update payloads and the feed with Ed25519.

## Native dependency validation

The native package validator rejects foreign OpenCV runtimes on every target. Linux additionally runs `ldd` against the app host and OpenCV bridge and rejects unresolved dependencies. macOS runs `otool -L` against both Mach-O files. The packaged native runtime probe still executes separately, so static closure and actual loading are both tested on matching CI runners.

## Update selection policy

The portable updater accepts only a signed feed and signed payload over HTTPS. Portable clients require explicit `sparkle:os` and `sparkle:architecture` fields. The closed channel set is `stable` and `rc`: stable clients reject RC entries, while RC clients may select either a newer RC or the stable release. Unknown client or item channels fail closed. Avalonia stable file/appcast versions reserve numeric revision `65535`; RC numbers are limited to `1..65534`, so the stable release correctly sorts after every RC of the same base version. Because .NET assembly identity rejects revision `65535`, stable assemblies use revision `0` while the updater obtains its comparison value from the package's `AssemblyFileVersion` metadata.

Distribution ownership is also closed:

- `direct`: the application may check and download a matching signed artifact;
- `package-manager`: application self-update is disabled;
- `store`: application self-update is disabled;
- any unknown value: configuration failure, with no network request.

The Linux DEB is stamped `package-manager`; direct tar and DMG packages are stamped `direct`. RC packages are stamped `rc`. `TOTP_`-prefixed environment variables may override packaged configuration for controlled testing, using the standard double-underscore separator for nested keys.

GitHub stable direct packages use the latest stable release appcast. RC direct packages use the signed public RC endpoint on GitHub Pages. That endpoint selects the highest versioned published release containing an appcast, verifies the appcast signature against the client key, and mirrors the unchanged appcast/signature pair. Because RC clients also accept stable entries, the endpoint provides both RC-to-RC and RC-to-stable discovery without treating a prerelease as GitHub's latest stable release.

## Security review

- Threat impact: wrong-platform, wrong-channel, substituted, truncated, and package-manager-bypassing updates are rejected before installation. Known vulnerable NuGet graphs fail CI.
- Data flow: the only new persistent input is non-secret package policy in `appsettings.json`. Update artifacts remain `.part` in a current-user-restricted directory until complete and become `.ready` only after Ed25519 verification.
- Secret impact: no private signing material is introduced. Artifact manifests contain public release metadata and hashes only.
- Compatibility: the direct package default is stable. Linux DEB installs remain package-manager-owned. Avalonia uses the target-qualified `appcast-v2.xml` feed.
- Version flow: stable package/file metadata uses revision `65535` for update ordering, assembly identity uses the valid revision `0`, and the updater reads the file version through an injected provider. RC file and assembly revisions remain the bounded RC number.
- Recovery: interrupted or invalid downloads are deleted. Invalid distribution policy fails before network access. A missing or invalid manifest fails release validation.
- Windows install recovery: every destination file that already exists is copied to an isolated rollback directory before replacement. A failed or cancelled transaction restores prior files in reverse order and removes files newly introduced by the failed update. Incomplete rollback is surfaced as a distinct aggregate failure; it is never reported as success.

## Automated evidence

- stable/RC channel isolation;
- stable file-version selection over the lower assembly-identity version;
- wrong-OS artifact rejection;
- strict malformed and substituted appcast signature rejection;
- managed-package no-network behavior;
- interrupted and tampered download cleanup;
- artifact mutation detection after manifest generation;
- target-native dependency checks on matching CI runners;
- solution-wide NuGet vulnerability enumeration.
- real application-XAML loading on Windows, Ubuntu, and macOS CI runners;
- successful Windows file replacement plus injected mid-transaction failure restoration.

Physical clean-machine installation, Microsoft Store certification/signature checks, signed macOS notarization, and target update handoff remain explicit M7 acceptance gates.

## Conditional credentialed direct publication

The direct-publication implementation remains fail-closed but is not the active Windows stable channel. The previous SignPath Foundation application was not approved, so Windows direct stable archives cannot currently pass their Authenticode gate. Microsoft Store packaging is handled separately and becomes distributable only after Store certification and signing. macOS release artifacts still cannot be retained without a Developer ID certificate and a complete App Store Connect notarization API-key triplet. Linux direct and DEB artifacts are assembled on Ubuntu 24.04.

The native packaging matrix retains outputs without writing to GitHub Releases. Each publishing job runs only after every required package succeeds, downloads the complete retained set, rebuilds and validates one aggregate manifest, and uses pinned NetSparkle AppCast Generator 2.9.0 to sign each eligible direct payload, `appcast-v2.xml`, and the manifest. The Avalonia client's embedded public key must match the CI public key. The signing tool receives only a protected key-directory path; private key contents are not placed in process arguments. The complete asset set is first uploaded to a draft; only a successful upload makes the release visible. Release-candidate tags are explicitly prereleases and never become GitHub's latest stable release.

Release payload preparation removes only debug-symbol files below the resolved generated publish directory and rejects stale updater build/RID subtrees. This keeps direct artifacts within the existing 128 MiB client limit without increasing the download memory/denial-of-service boundary.

If a future direct Windows signing provider is approved, that conditional tag path may retain two Authenticode-signed ZIPs. The self-contained ZIP would be the single appcast-qualified update payload; the smaller framework-dependent `fast` ZIP would remain `manual-download`. This dormant design does not describe the Microsoft Store package.
