# Local outputs

Generated outputs are ignored by Git. Keep source, fixtures and build instructions
in their tracked project, `packaging/`, `site/` and `scripts/` directories.

```text
artifacts/
  android/
    testing/             development APKs, installer tests, device evidence
    release/             retained Android release inputs
  linux/
    testing/
    release/
  macos/
    testing/
    release/
  ios/                   reserved; no iOS host or release workflow currently
    testing/
    release/
  windows/
    testing/             development publishes, MSI/upgrade/VM experiments
    release/
      v3.0.4/            Partner Center MSIX, SHA256SUMS, store-package.json
  shared/
    testing/             cross-platform fixtures and verification evidence
    release/             cross-platform release verification evidence
    tools/               downloaded developer tools
  marketing/             reviewed captures, provenance, gallery/Store derivatives
  website/               complete assembled website; never versioned by release
```

## Windows release files

Store submission files belong directly in `windows/release/<desktop-tag>/`.
They are unsigned Partner Center inputs, not end-user installers. GitHub retains
them as the Actions artifact `otp-harbor-microsoft-store-<version>` for 90 days.
They are not attached to the public stable GitHub release.
The retrieved `release-origin.json` identifies the successful tag workflow and
source commit. Hash checks establish consistency with that authenticated CI
output; they do not turn an unsigned submission package into a signed installer.

Use `scripts/release/windows/Save-StoreReleaseArtifact.ps1 -ReleaseTag v3.0.4`
to retrieve the retained CI output into this location. It checks the successful
tag build, package identity/version and hashes; it never installs or submits it.
Linux packages and signed update metadata already available on the public release
do not need another local release bundle.

Local Store builds use the same directory through
`scripts/release/windows/New-CurrentSourceMicrosoftStoreMsix.ps1`.
The lower-level packager uses `v<product-version>` when supplied, otherwise
`msix-<package-version>` for manual Store-only builds.

## Website locations

- Source: `site/`.
- Canonical images and provenance: `artifacts/marketing/`.
- Derived gallery images/data: `artifacts/marketing/website/`.
- Complete assembled output: `artifacts/website/` (replaced on refresh).
- Published site: <https://legends.github.io/otp-harbor/>.

Build the complete local site using
`scripts/marketing/Publish-LocalPreview.ps1 -NoServe -PythonExecutable <pinned-python>`.
Omit `-NoServe` to serve it on localhost. This local review includes candidate
gallery assets; public Pages publication retains its approval and integrity gates.

Older experiments are retained under their platform's `testing/` directory.
No vaults, credentials, signing material or personal backups belong in source control.
