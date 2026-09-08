# AGENTS.md

This file is the working contract for humans and coding agents contributing to `otp-harbor`.

It is intentionally opinionated. The repo is security-sensitive, cross-platform, and already has clear architectural direction. Contributions should reinforce that direction, not dilute it.

## Mission

Build OTP Harbor as a trustworthy cross-platform desktop TOTP authenticator that is:

- local-first
- secure by default
- polished enough for daily personal use
- maintainable under long-term iteration
- releaseable with high confidence

The product aspiration is not "just a code generator". It is a serious desktop authenticator with:

- encrypted local storage
- reliable unlock and authorization flows
- import/export and backup safety
- robust update delivery
- strong regression coverage around security-sensitive paths

If a proposed change improves convenience but weakens trust, auditability, or recovery confidence, reject it or redesign it.

## Product Direction

Use these as prioritization rules when tradeoffs are unclear.

### 1. Security first

Protect OTP seeds, passwords, derived keys, exported backups, and update/release credentials as first-class assets.

Expected posture:

- no plaintext secret persistence
- no accidental secret logging
- short-lived sensitive buffers where feasible
- explicit authorization for sensitive actions
- documented security impact for meaningful changes

### 2. Desktop quality matters

This is a native Avalonia desktop app, not a web wrapper. UX should feel stable, native, responsive, and respectful of user attention on Windows, macOS, and Linux.

Priorities:

- fast startup
- reliable single-instance behavior
- predictable lock/unlock behavior
- low-friction account CRUD
- smooth QR workflows
- safe update/install flow

### 3. Architecture remains intentional

The repository already points toward strict layering, MVVM, DI, and testable workflows. Preserve those boundaries.

### 4. Shipping discipline

Releases, auto-update metadata, signatures, and CI behavior are part of the product. Treat them as product code, not afterthoughts.

## Current Repo Shape

### Solution layout

- `TOTP.Core`
  - domain models, enums, common primitives, contracts, security abstractions
- `TOTP.Infrastructure`
  - concrete implementations for logging, crypto orchestration, security, settings, account management, export, QR generation
- `TOTP.DAL`
  - persistence and filesystem-facing data access
- `TOTP.UI.Avalonia.Shared`
  - portable presentation contracts and shared Avalonia-facing workflows
- `TOTP.UI.Avalonia.Desktop`
  - Avalonia views, view models, commands, bootstrap, platform UI adapters, and assets
- `TOTP.Tests`
  - unit, regression, security-adjacent, and integration tests
- `TOTP.Updater`
  - updater/install support UI and logic
- `scripts`
  - release, security, and local update/testing helpers
- `docs/security`
  - threat model, verification notes, branch protection, signing/update documentation

### Platform/runtime

- Windows, macOS, and Linux desktop targets
- .NET 10
- Avalonia desktop UI
- CI runs platform-specific jobs on Windows, macOS, and Linux

### Key libraries already in play

- `FluentResults`
- `Serilog`
- `Otp.NET`
- `QRCoder`
- `OpenCvSharp`
- `Avalonia`
- `xUnit v3`
- `Moq`
- `Moq.AutoMock`
- `FluentAssertions`

## Non-Negotiable Engineering Rules

### Security rules

- Never log OTP seeds, master passwords, DPAPI payloads, export secrets, or raw secret material.
- Never introduce plaintext-at-rest shortcuts for debugging or convenience.
- Never commit real secrets, private keys, certificates, or personal backup artifacts.
- Any change to crypto, KDF parameters, password handling, authorization flow, storage format, import/export format, or update verification requires explicit security review thinking.
- If sensitive data must briefly exist in memory, minimize lifetime and clear temporary buffers where practical.

### Architecture rules

- Keep MVVM boundaries strict.
- Keep business/security decisions out of Avalonia code-behind.
- Prefer interface-driven services and constructor injection.
- Keep desktop composition rooted in `TOTP.UI.Avalonia.Desktop/Startup/AvaloniaCompositionRoot.cs`.
- Do not add service-locator-style resolution inside feature code unless there is already a clear repo pattern and no cleaner seam.

### Error handling rules

- Use return-based/result-based control flow for expected failures.
- Catch exceptions at boundaries: file I/O, OS integration, crypto, update/install, camera/scanner interaction, startup orchestration.
- Make user-visible failure modes recoverable and explicit.
- Add tests for failure branches, not only happy paths.

### Localization rules

- Localize every user-visible string. This includes headings, labels, buttons, tooltips, placeholders, validation text, errors, confirmations, empty states, transient progress/status messages, notifications, accessibility names, and platform-specific guidance.
- Do not hard-code user-visible text in views, view models, code-behind, dialog services, or platform adapters. Resolve it through the localization resources used by that UI project.
- Add every new localization key to all supported language resources in the same change. Do not assemble a message from localized and hard-coded fragments, because that produces mixed-language UI and makes translation grammatically unsafe.
- Use stable typed/string-key references rather than duplicating resource-key literals throughout feature code.
- When behavior selects or composes localized messages, add regression coverage that proves the complete displayed message comes from the active locale. Also keep resource-completeness tests passing.
- Logs and developer diagnostics are not UI and should remain stable, structured, non-secret-bearing English unless an existing subsystem requires otherwise.

### Testing rules

- New behavior should come with tests.
- Security fixes must come with regression tests.
- Prefer targeted, deterministic tests near the changed workflow.
- Avoid tests that only mirror implementation details with no behavioral value.
- Keep validation rules consistent between desktop and Android by default. Put shared
  policy in a common non-UI layer and cover both presentations with matching behavioral
  tests. Diverge only when a platform constraint or workflow makes the shared rule
  inappropriate, and document the reason in the change.

## Architectural Intent By Layer

### `TOTP.Core`

Owns:

- domain concepts
- contracts/interfaces
- security abstractions
- cross-layer primitives and error codes

Should not own:

- desktop UI concerns
- filesystem details
- concrete infrastructure wiring

### `TOTP.Infrastructure`

Owns:

- implementations behind core contracts
- security orchestration
- logging/redaction behavior
- settings/export/account services

Should not become:

- a second UI layer
- a dumping ground for unrelated helpers

### `TOTP.DAL`

Owns:

- persistence mechanics
- local file handling
- low-level storage mapping

Should not own:

- product policy
- authorization decisions
- UI messaging

### `TOTP.UI.Avalonia.Desktop`

Owns:

- Avalonia views
- view models
- commands
- app startup
- orchestration between services and user interaction

Should not own:

- raw crypto policy
- persistence internals
- secret-handling shortcuts for binding convenience

## What Good Changes Look Like

Good contributions usually have these properties:

- they improve one workflow clearly
- they keep dependencies explicit
- they reduce ambiguity around failures
- they preserve or improve testability
- they do not leak security details into presentation logic
- they leave logging safer, not noisier
- they fit the current release/update model instead of bypassing it

Examples:

- tightening authorization around export/import
- improving startup reliability without weakening diagnostics
- adding regression coverage for lock/unlock edge cases
- improving QR scanning robustness and test seams
- hardening logging redaction
- clarifying release automation and appcast generation

## What To Avoid

- "quick fixes" in code-behind that bypass view-model or service boundaries
- hidden static state when DI would be cleaner
- catch-and-ignore error handling
- adding dependencies without clear need
- generic abstractions with no current payoff
- premature cloud/backend assumptions in a local-first app
- weakening update verification, signing, or release metadata handling
- storing secret-bearing values in immutable strings longer than necessary when a safer pattern exists

## Repo-Specific Workflows

### Restore

```powershell
dotnet restore TOTP.sln --configfile NuGet.config
```

### Build

```powershell
dotnet build TOTP.sln -c Debug
```

### Run tests

Full:

```powershell
dotnet test TOTP.sln -c Debug
```

Fast PR-like subset:

```powershell
dotnet test TOTP.sln -c Release --no-build --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~IdleMonitoringBackgroundServiceTests&FullyQualifiedName!~UserActivityServiceTests"
```

### Run locally

```powershell
dotnet run --project .\TOTP.UI.Avalonia.Desktop\TOTP.UI.Avalonia.Desktop.csproj
```

### Debug auto-update locally

Use the scripts under `scripts/release` and the guidance in
[`docs/security/AUTO_UPDATE.md`](docs/security/AUTO_UPDATE.md).

Relevant helpers include:

- `scripts/release/Generate-AvaloniaAppcast.ps1`
- `scripts/release/Test-AvaloniaAppcast.ps1`
- `scripts/release/Set-PackageUpdatePolicy.ps1`

## CI / Release Reality

The repo already treats release engineering seriously. Match that standard.

### Current CI expectations

GitHub Actions currently builds/tests on push and PR, and publishes on version tags.

Observed workflow expectations:

- restore from `NuGet.config`
- build in `Release`
- PR test runs are filtered for speed
- push/tag runs execute the fuller test set
- tagged releases publish `fast` and `portable` artifacts
- appcast generation/signing is integrated when secrets are configured

### Release tag format

The workflow expects:

```text
v<major>.<minor>.<patch>
v<major>.<minor>.<patch>-rc<nr>
```

Do not change release versioning casually. It affects published assets and appcast metadata.

### Auto-update

Auto-update is a product feature, not an ops detail.

Guardrails:

- do not weaken signature validation
- do not hardcode private material into repo files
- preserve compatibility between published binaries and appcast metadata
- verify version fields when changing publish flow

## Startup, Logging, and Diagnostics

Startup is performance- and reliability-sensitive. The current app already records startup stages and uses early logging.

When touching startup code:

- preserve single-instance behavior
- preserve splash/startup sequencing unless intentionally redesigning it
- avoid blocking the UI thread unnecessarily
- preserve or improve startup diagnostics
- treat logging redaction as mandatory

Key files:

- [`TOTP.UI.Avalonia.Desktop/Program.cs`](TOTP.UI.Avalonia.Desktop/Program.cs)
- [`TOTP.UI.Avalonia.Desktop/Startup/AvaloniaCompositionRoot.cs`](TOTP.UI.Avalonia.Desktop/Startup/AvaloniaCompositionRoot.cs)
- [`TOTP.Infrastructure/Logging/LoggingConfigurator.cs`](TOTP.Infrastructure/Logging/LoggingConfigurator.cs)
- [`TOTP.Infrastructure/Logging/SensitiveTextRedactor.cs`](TOTP.Infrastructure/Logging/SensitiveTextRedactor.cs)

## Security Review Triggers

Treat the following as mandatory review triggers:

- password setup/unlock flow changes
- Argon2id parameter changes
- DPAPI handling changes
- vault/encryption/storage format changes
- import/export schema or cryptographic wrapper changes
- update signing/appcast validation changes
- logging/redaction changes
- Windows Hello integration changes
- backup/restore behavior changes

When making these changes, document:

- threat impact
- data-flow impact
- compatibility or migration impact
- test evidence

## Guidance For AI Agents

### Default posture

- read before writing
- prefer small, reversible changes
- keep edits aligned with current architecture
- add tests with behavior changes
- do not "simplify" by removing security boundaries

### Before editing

Review the relevant local context first:

- feature code
- associated interfaces/contracts
- DI registrations
- existing tests
- related security docs when applicable

### When editing

- preserve naming and file organization patterns already used nearby
- prefer extending an existing service/workflow over creating parallel logic
- route all user-visible text through the relevant localization service/resources and update every supported locale
- keep code comments sparse and useful
- avoid speculative refactors unless they directly unblock the change

### After editing

- build the affected projects when feasible
- run the most relevant tests
- mention any unverified risk explicitly

## Suggested Near-Term Aspirations

These are consistent with the current repo direction and should guide future decisions:

- make the app a high-trust personal authenticator for Windows
- continue tightening secret-handling and redaction discipline
- deepen regression coverage around authorization, update, and import/export flows
- improve recovery and migration confidence without sacrificing local-first design
- keep release automation reproducible and auditable
- keep the UX polished while preserving explicit security boundaries

## Source Documents Worth Reading First

- [`readme.md`](readme.md)
- [`CONTRIBUTING.md`](CONTRIBUTING.md)
- [`docs/README.md`](docs/README.md)
- [`docs/security/THREAT_MODEL.md`](docs/security/THREAT_MODEL.md)
- [`docs/security/SECURITY_VERIFICATION.md`](docs/security/SECURITY_VERIFICATION.md)
- [`docs/security/PENTEST_PLAN.md`](docs/security/PENTEST_PLAN.md)
- [`docs/security/AUTO_UPDATE.md`](docs/security/AUTO_UPDATE.md)
- [`docs/security/BRANCH_PROTECTION.md`](docs/security/BRANCH_PROTECTION.md)

## Bottom Line

Contribute as if this repository is trying to become a trustworthy desktop security product, because that is what the codebase already signals.

Optimize for:

- trust
- correctness
- maintainability
- safe iteration
- release confidence

Do not optimize for:

- shortcuts
- hidden magic
- superficial velocity
- convenience that weakens security posture
