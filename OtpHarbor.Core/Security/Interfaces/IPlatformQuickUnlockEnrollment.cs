using FluentResults;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

/// <summary>
/// Enables platform quick unlock after password recovery or fresh authorization
/// of an existing session key that verifies the vault. Retains password recovery.
/// </summary>
public interface IPlatformQuickUnlockEnrollment : IDisposable
{
    /// <summary>
    /// Registers the session key with fresh platform verification and checks the
    /// caller's session guard immediately before persisting the new wrapper.
    /// </summary>
    Task<Result> EnableWithPlatformVerificationAsync(
        ReadOnlyMemory<byte> vaultKey, PreferredUnlockMethod unlockMethod, Func<bool> canCommit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Fail("Platform-verified enrollment is unavailable."));

    /// <summary>Used by the authorization facade after fresh authentication; still verifies the vault key.</summary>
    Task<Result> EnableWithAuthenticatedKeyAsync(
        ReadOnlyMemory<byte> vaultKey, PreferredUnlockMethod unlockMethod,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Fail("Authenticated-key enrollment is unavailable."));

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
