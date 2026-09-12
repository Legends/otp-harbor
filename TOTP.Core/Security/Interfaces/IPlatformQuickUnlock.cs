using FluentResults;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

/// <summary>
/// Provides user-verified, device-local quick unlock without replacing the
/// master-password recovery path.
/// </summary>
public interface IPlatformQuickUnlock
{
    string ProviderId { get; }

    PreferredUnlockMethod UnlockMethod => PreferredUnlockMethod.PlatformQuickUnlock;

    Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<Result<PlatformQuickUnlockWrapperV2>> RegisterAsync(
        ReadOnlyMemory<byte> vaultKey,
        CancellationToken cancellationToken = default);

    Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes platform-managed material for the wrapper. Removing an absent
    /// or already-reset key is successful.
    /// </summary>
    Task<Result> RemoveAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default);
}
