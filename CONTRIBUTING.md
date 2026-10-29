# Contributing to OTP Harbor

OTP Harbor is a security-sensitive .NET 10/Avalonia desktop application. Contributions should be small, testable, local-first, and explicit about failure behavior.

## Setup

Start with the [developer guide](docs/DEVELOPMENT.md). It documents the required and optional tools, project boundaries, setup and test commands, platform workflows, and the complete PowerShell script catalog. Platform-specific UI or packaging work should also be tested on the affected operating system.

## Engineering rules

- Never persist or log OTP seeds, passwords, derived keys, vault keys, backup passwords, or signing material in plaintext.
- Keep MVVM boundaries strict. Business, persistence, and cryptographic policy do not belong in Avalonia code-behind.
- Use constructor injection and existing interfaces. Composition belongs in `OtpHarbor.UI.Avalonia.Desktop/Startup/AvaloniaCompositionRoot.cs`.
- Use typed/result-based outcomes for expected failures and catch exceptions at I/O, OS, crypto, camera, update, and startup boundaries.
- Localize every user-visible string in every supported locale.
- Add behavioral tests for new behavior and regression tests for security fixes.
- Preserve compatibility unless a reviewed migration is part of the change.

The complete coding-agent and architecture contract is in [AGENTS.md](AGENTS.md).

## Project boundaries

| Project | Responsibility |
| --- | --- |
| `OtpHarbor.Core` | Domain models, contracts, validation, and security abstractions |
| `OtpHarbor.Infrastructure` | Security orchestration and concrete services |
| `OtpHarbor.DAL` | Persistence and filesystem data access |
| `OtpHarbor.UI.Avalonia.Shared` | Shared Avalonia controls and presentation contracts |
| `OtpHarbor.UI.Avalonia.Desktop` | Desktop views, view models, startup, and UI adapters |
| `TOTP.Platform.*` | Operating-system integrations |
| `OtpHarbor.Camera.OpenCv` | Camera capture and QR decoding boundary |
| `OtpHarbor.Updater` | Windows update installation helper |
| `OtpHarbor.Tests*` | Unit, integration, platform, and headless UI tests |

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

Release tags, package policies, signatures, manifests, and appcasts are product behavior. Do not bypass validation or commit private keys, certificates, tokens, real backups, or personal test artifacts. Start with the [developer guide](docs/DEVELOPMENT.md), [documentation index](docs/README.md), and the relevant security runbook before modifying those paths.
