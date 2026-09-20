# AGENTS.md

This file is the working contract for humans and coding agents contributing to `otp-harbor`.

It is intentionally opinionated. The repo is security-sensitive, cross-platform, and already has clear architectural direction. Contributions should reinforce that direction, not dilute it.

> **Efficiency:** Minimize redundant tool calls and context consumption. Reuse
> repository knowledge acquired during the current task, use narrowly scoped
> searches and file reads, and stop investigating once the implementation
> location and relevant tests are known. Do not sacrifice correctness,
> verification, security review, or test coverage to reduce tool usage.

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

All platform-specific applications must present one coherent OTP Harbor design. Keep semantic colors, typography, spacing, iconography, control states, and interaction feedback consistent across Windows, macOS, Linux, and Android. Introduce a platform-specific visual difference only when an established platform convention or technical constraint makes the shared design inappropriate; document the reason and add cross-platform regression coverage where practical.

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

### Project layout

- `TOTP.Core`
  - domain models, enums, common primitives, contracts, security abstractions
- `TOTP.Infrastructure`
  - concrete implementations for logging, crypto orchestration, security, settings, account management, export, QR generation
- `TOTP.DAL`
  - persistence and filesystem-facing data access
- `TOTP.Camera.OpenCv`
  - OpenCV camera capture and QR scanning
- `TOTP.Platform.Windows`
  - Windows-specific platform implementation
- `TOTP.Platform.Linux`
  - Linux-specific platform implementation
- `TOTP.Platform.MacOS`
  - macOS-specific platform implementation
- `TOTP.Platform.Unix`
  - shared Unix-specific implementation
- `TOTP.UI.Avalonia.Shared`
  - portable presentation contracts and shared Avalonia-facing workflows
- `TOTP.UI.Avalonia.Desktop`
  - Avalonia views, view models, commands, bootstrap, platform UI adapters, and assets
- `TOTP.Installer`
  - installer-related functionality
- `TOTP.Tests`
  - unit, regression, security-adjacent, and integration tests
- `TOTP.Tests.Avalonia.Headless`
  - Avalonia headless UI and interaction tests
- `TOTP.Tests.Unix`
  - Unix/platform-specific tests
- `TOTP.Updater`
  - updater/install support UI and logic
- `TOTP.UI.Avalonia.Mobile`, `TOTP.UI.Avalonia.Android`, and `TOTP.Platform.Android`
  - adjacent shared mobile UI, Android host, and Android platform projects built by the Android workflow rather than `TOTP.sln`
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
- desktop and Android tags run shared validation but publish only their own platform artifacts
- appcast generation/signing is integrated when secrets are configured

### Release tag format

The workflow expects independent stable release tags:

```text
v<major>.<minor>.<patch>
android-v<major>.<minor>.<patch>
```

Desktop tags publish Linux/source assets and create the Partner Center MSIX input. Android tags
publish only the signed APK and Android integrity metadata. Historical RC tags remain immutable,
but new v2 releases are stable-only. Do not change release versioning casually; it affects package
identity, published assets, and update metadata.

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
- use narrow searches, focused file slices, and bounded command output
- avoid repeated discovery or validation when inputs have not changed
- keep progress updates and final summaries concise unless detail is requested or a risk needs explanation
- prefer small, reversible changes
- keep edits aligned with current architecture
- add tests with behavior changes
- do not "simplify" by removing security boundaries
- optimize tool calls and test scope without reducing security, localization, regression coverage, or release confidence

### Codex Efficiency and Repository Navigation

This is a multi-project .NET/Avalonia repository. Work efficiently and minimize
unnecessary repository reads, searches, builds, and test runs while preserving
correctness. Use the project map in **Current Repo Shape** before searching
broadly.

#### Repository inspection rules

Do not rediscover repository structure repeatedly during the same task.

1. Reuse information already obtained during the current task when the relevant
   files have not changed.
2. Do not repeatedly run nearly identical `rg`, `Get-Content`, Git, or other
   search commands against unchanged files.
3. Before a repository-wide search, determine which project or directory is
   most likely to contain the implementation.
4. Scope searches as narrowly as practical. Prefer:

   ```powershell
   rg -n "SymbolName" TOTP.UI.Avalonia.Desktop TOTP.Tests
   ```

   over:

   ```powershell
   rg -n "SymbolName" .
   ```

5. Combine related symbols into one targeted search where practical:

   ```powershell
   rg -n "SymbolA|SymbolB|SymbolC" <relevant-directories>
   ```

6. Once a symbol has been located, inspect its known file directly instead of
   searching the repository for the same symbol again.
7. Read only the relevant portion of large files. Prefer bounded ranges such as:

   ```powershell
   Get-Content path/to/File.cs | Select-Object -Skip 400 -First 120
   ```

   Avoid dumping a complete large source file unless it is genuinely required.
8. Do not reread an unchanged file merely to refresh context. Reread it only
   when it changed, a precise detail must be verified, previous output was
   incomplete, or correctness requires confirmation.
9. Treat command output as part of the context budget. Avoid commands producing
   hundreds or thousands of irrelevant lines.
10. Prefer exact symbol, class, method, property, XAML control name, test name,
    or error-message searches over broad keyword searches.

#### Investigation workflow

For implementation tasks, normally use this order:

1. Identify the likely project from the repository map.
2. Locate the primary implementation with one targeted search.
3. Locate directly related tests with one targeted search.
4. Read only the relevant implementation and test sections.
5. Form an implementation plan.
6. Make the change.
7. Inspect the Git diff for changed files.
8. Run the smallest relevant test or build command.
9. Escalate to broader tests or builds only when justified.

Do not restart repository discovery after step 4 unless new information makes
it necessary.

#### Editing and verification

After editing, prefer reviewing the change with:

```powershell
git diff -- path/to/changed/file
```

or:

```powershell
git diff --stat
```

Use a small contextual reread around an edited method only when needed to
verify surrounding code. Do not repeatedly inspect a file before and after
every small edit when the diff already provides sufficient verification.

#### Build efficiency

Do not build the complete solution after every change. Prefer the narrowest
applicable project build first, for example:

```powershell
dotnet build TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj
```

Build the full solution when:

- shared APIs used by several projects changed
- project references changed
- package or configuration changes may affect multiple projects
- release-level verification is requested
- narrower builds are insufficient

Do not repeat an identical successful build unless relevant source or build
configuration changed afterward.

#### Test efficiency

Run the smallest scope that meaningfully validates the change. Prefer the
relevant test project, test class, or filtered set before the complete suite.
Examples:

```powershell
dotnet test TOTP.Tests/TOTP.Tests.csproj --filter "FullyQualifiedName~AccountListViewModel"
```

```powershell
dotnet test TOTP.Tests.Avalonia.Headless/TOTP.Tests.Avalonia.Headless.csproj
```

Expand to broader tests when shared behavior changed, targeted tests reveal
related failures, the change crosses project boundaries, or final verification
warrants it. Do not rerun an unchanged test command after it passed unless
subsequent changes could affect the result.

#### Search-result reuse

Maintain a working map during the task. If a previous command established a
class location, method location, test class, or project responsibility, reuse
that information. For example, after establishing:

```text
AccountListViewModel
  -> TOTP.UI.Avalonia.Desktop/Presentation/AccountListViewModel.cs

AccountListViewModelTests
  -> TOTP.Tests/Avalonia/Presentation/AccountListViewModelTests.cs
```

do not search the entire repository for those types again during the same task
without a specific reason.

#### Avoid speculative searching

Do not search for many possible implementations "just in case." Start with the
most likely location based on project architecture, namespaces, file names,
referenced symbols, compile errors, stack traces, bindings, and existing tests.
Broaden only when the targeted search fails.

#### XAML and Avalonia tasks

For desktop UI behavior, inspect in this approximate order when applicable:

1. Relevant `.axaml` view.
2. Its `.axaml.cs` code-behind when interaction, focus, or input is involved.
3. Relevant presentation or view-model class.
4. Shared Avalonia code only when behavior is shared.
5. Existing Avalonia/headless tests.

Do not automatically scan all UI projects for desktop-only changes. For
keyboard, focus, pointer, routing, window, or desktop-shell behavior, prioritize
`TOTP.UI.Avalonia.Desktop` and corresponding Avalonia tests.

#### Documentation tasks

Search only the relevant documentation directory and exact terms first. Do not
scan source projects merely to verify wording unless a documentation claim must
be validated against implementation.

#### Git usage

Use Git as the primary way to understand modifications made during the current
task:

```powershell
git status --short
git diff --stat
git diff -- <file>
```

Do not use repeated repository-wide source reads to determine what changed when
the Git diff already answers the question.

#### Quality rule

Efficiency must not replace verification. Do not skip relevant tests,
compilation checks, security-sensitive validation, persistence or encryption
correctness checks, platform-boundary checks, or inspection of directly affected
code merely to reduce tool usage. The goal is to remove redundant exploration,
not necessary engineering work.

#### Stop condition for investigation

Once the implementation location, related dependencies, and relevant tests are
known, stop searching and begin implementation. Every additional repository
search must answer a concrete unresolved question; do not continue searching
merely to gather more context.

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
