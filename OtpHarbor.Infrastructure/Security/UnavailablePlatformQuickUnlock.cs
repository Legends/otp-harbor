using FluentResults;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed class UnavailablePlatformQuickUnlock : IPlatformQuickUnlock
{
    public const string UnavailableProvider = "unavailable";

    public string ProviderId => UnavailableProvider;

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
        return Task.FromResult(Result.Fail<PlatformQuickUnlockWrapperV2>(UnavailableError()));
    }

    public Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Fail<PlatformQuickUnlockAttempt>(UnavailableError()));
    }

    public Task<Result> RemoveAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Fail(UnavailableError()));
    }

    private static PlatformQuickUnlockError UnavailableError() => new(
        PlatformQuickUnlockErrorCode.Unavailable,
        "Platform quick unlock is unavailable on this application host.");
}
