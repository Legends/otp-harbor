# Contributing to OTP Harbor

OTP Harbor is a security-sensitive .NET 10/Avalonia desktop application. Contributions should be small, testable, local-first, and explicit about failure behavior.

## Setup

Install Git and the .NET 10 SDK. Platform-specific UI or packaging work should also be tested on the affected operating system.

```powershell
git clone https://github.com/Legends/otp-harbor.git
cd otp-harbor
dotnet restore TOTP.sln --configfile NuGet.config
dotnet build TOTP.sln -c Debug
dotnet test TOTP.sln -c Debug
```

Run the desktop application with:

```powershell
dotnet run --project .\TOTP.UI.Avalonia.Desktop\TOTP.UI.Avalonia.Desktop.csproj
```

## Engineering rules

- Never persist or log OTP seeds, passwords, derived keys, vault keys, backup passwords, or signing material in plaintext.
- Keep MVVM boundaries strict. Business, persistence, and cryptographic policy do not belong in Avalonia code-behind.
- Use constructor injection and existing interfaces. Composition belongs in `TOTP.UI.Avalonia.Desktop/Startup/AvaloniaCompositionRoot.cs`.
- Use typed/result-based outcomes for expected failures and catch exceptions at I/O, OS, crypto, camera, update, and startup boundaries.
- Localize every user-visible string in every supported locale.
- Add behavioral tests for new behavior and regression tests for security fixes.
- Preserve compatibility unless a reviewed migration is part of the change.

The complete coding-agent and architecture contract is in [AGENTS.md](AGENTS.md).

## Project boundaries

| Project | Responsibility |
| --- | --- |
| `TOTP.Core` | Domain models, contracts, validation, and security abstractions |
| `TOTP.Infrastructure` | Security orchestration and concrete services |
| `TOTP.DAL` | Persistence and filesystem data access |
| `TOTP.UI.Avalonia.Shared` | Shared Avalonia controls and presentation contracts |
| `TOTP.UI.Avalonia.Desktop` | Desktop views, view models, startup, and UI adapters |
| `TOTP.Platform.*` | Operating-system integrations |
| `TOTP.Camera.OpenCv` | Camera capture and QR decoding boundary |
| `TOTP.Updater` | Windows update installation helper |
| `TOTP.Tests*` | Unit, integration, platform, and headless UI tests |

## Pull requests

By intentionally submitting a contribution for inclusion, you agree to license it under the
repository license in effect when it is accepted and certify that you have the authority to do so.
Copyright ownership is not transferred. Review [LICENSING.md](LICENSING.md) before contributing.

A pull request should explain:

- what changed and why;
- user-visible and failure-path behavior;
- security, data-flow, compatibility, and migration impact where relevant;
- tests performed and anything not verified physically.

Changes to password handling, key derivation, vault/envelope formats, imports/exports, backups, update verification, signing, logging/redaction, or quick unlock require explicit security-review notes.

## Release-sensitive changes

Release tags, package policies, signatures, manifests, and appcasts are product behavior. Do not bypass validation or commit private keys, certificates, tokens, real backups, or personal test artifacts. Start with the [documentation index](docs/README.md) and the relevant security runbook before modifying those paths.
