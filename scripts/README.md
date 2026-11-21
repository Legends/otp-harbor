# Repository scripts

Run scripts from any working directory using their full paths, or use the paths
below from the repository root. Relative dependencies are resolved from the script
location; repository discovery also supports source archives without `.git`.

```text
scripts/
  assets/                 app-owned artwork and screenshot tooling
  dev/windows/            local Windows publish/run
  marketing/              capture, provenance, generation, local website preview
  release/
    android/              APK versioning/signing, F-Droid repository
    linux/                Linux packaging
    macos/                macOS packaging
    windows/              MSIX/MSI/setup, Store metadata, installer resources
    shared/               appcasts, integrity manifests, policy, release summaries
  security/               security validation entry points
  testing/
    android/              device installation and synthetic installer tests
    linux/                Linux VM setup/sync helpers
    windows/              Store migration, desktop fixtures and previews
    shared/               repository/path discovery helpers
    fixtures/             reviewed synthetic fixtures and generators
  validation/             deterministic repository/packaging/localization checks
```

Add platform-specific scripts to the relevant task/platform folder when needed;
do not add empty implementations for a platform. iOS has no current host.

Common commands:

```powershell
./scripts/dev/windows/Publish-And-Run-AvaloniaWindows.ps1
./scripts/testing/android/Install-AndroidReleasePhone.ps1 -BuildOnly
./scripts/testing/android/Test-AndroidReleaseInstaller.ps1
./scripts/release/windows/Save-StoreReleaseArtifact.ps1 -ReleaseTag v3.0.4
./scripts/validation/Test-TestingScriptLayout.ps1
python -m unittest discover -s scripts/validation -p 'test_*.py'
```

The layout check parses all PowerShell scripts and validates script-relative
dependencies, documentation/workflow references and repository discovery.
See [local output locations](../artifacts/README.md) for artifact rules.

Scripts that install, launch, clear development accounts, contact signing services
or publish releases retain their own explicit opt-in parameters. Layout validation
does not execute those operations or access a live vault.
