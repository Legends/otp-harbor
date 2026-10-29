using FluentResults;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

/// <summary>
/// Enables platform quick unlock only after the persisted password-recovery
/// wrapper has recovered a key that verifies the existing vault.
/// </summary>
public interface IPlatformQuickUnlockEnrollment : IDisposable
{
    Task<Result> EnableAsync(
        string recoveryPassword,
        CancellationToken cancellationToken = default);

    Task<Result> EnableAsync(
        string recoveryPassword,
        PreferredUnlockMethod unlockMethod,
        CancellationToken cancellationToken = default) =>
        unlockMethod == PreferredUnlockMethod.PlatformQuickUnlock
            ? EnableAsync(recoveryPassword, cancellationToken)
            : Task.FromResult(Result.Fail("The requested unlock method is unavailable."));

    Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(
        PreferredUnlockMethod unlockMethod,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PlatformQuickUnlockAvailability.NotSupported);
}
