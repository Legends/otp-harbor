using FluentResults;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed class UnavailablePlatformUnattendedUnlock : IPlatformUnattendedUnlock
{
    public string ProviderId => "unavailable-unattended-unlock";

    public Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PlatformQuickUnlockAvailability.NotSupported);
    }

    public Task<Result<PlatformQuickUnlockWrapperV2>> RegisterAsync(
        ReadOnlyMemory<byte> vaultKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Fail<PlatformQuickUnlockWrapperV2>(
            "Unattended unlock is unavailable on this platform."));
    }

    public Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Ok(
            PlatformQuickUnlockAttempt.WithoutKey(
                PlatformQuickUnlockStatus.NotAvailable)));
    }

    public Task<Result> RemoveAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Ok());
    }
}
