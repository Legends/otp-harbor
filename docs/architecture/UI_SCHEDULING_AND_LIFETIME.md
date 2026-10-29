# UI scheduling and application lifetime

## Scope

UI-thread scheduling and application termination are exposed through contracts in `OtpHarbor.Core`:

- `IUiScheduler` provides framework-neutral access checks, queued posts, and awaited synchronous/asynchronous invocation.
- `IApplicationLifetime` provides graceful shutdown and explicit process-exit operations.

The Avalonia desktop host implements these contracts with `AvaloniaUiScheduler` and `AvaloniaApplicationLifetime`, registered at its composition root.

## Scheduling semantics

- `Post` queues work without waiting on the Avalonia dispatcher. It is intended for property-change and UI-refresh notifications.
- `InvokeAsync(Action)` schedules synchronous UI work and completes after the action runs.
- `InvokeAsync(Func<Task>)` schedules asynchronous UI work and unwraps its task so failures and completion propagate to the caller.
- `CheckAccess` reports whether the caller may touch UI-owned state directly.
- The Avalonia adapter queues posts at its native `Normal` priority and awaited work at `Background` priority, matching the portable contract without exposing Avalonia dispatcher types.

## Lifetime semantics

- View models and workflows request shutdown only through `IApplicationLifetime`.
- `AvaloniaApplicationLifetime` marshals graceful shutdown to the Avalonia dispatcher and ignores duplicate requests once shutdown begins.
- `ExitProcess` remains an explicit emergency/final-close operation that flushes Serilog before terminating the process.
- Direct exit calls that occur before dependency injection exists remain confined to the executable startup and unhandled-exception boundaries in `Program` and `AvaloniaExceptionBoundary`.

## Compatibility impact

- Debounce, QR scanner state delivery, main-view warmup/timer updates, and auto-update UI callbacks retain their prior UI-thread behavior.
- Auto-update shutdown handoff and main-window close use the lifetime boundary rather than a framework singleton.
- No vault, settings, backup, export, or update-state formats change.

## Reliability and security impact

- Central scheduling makes thread-affinity behavior testable and prevents portable workflows from acquiring UI-framework dependencies.
- Awaited scheduler calls propagate exceptions rather than losing them in fire-and-forget dispatcher work.
- Central lifetime handling reduces inconsistent shutdown paths that could skip cleanup or logging flushes.
- These contracts do not authorize feature code to terminate the process; callers still use shutdown only at existing fatal or explicit-close boundaries.
