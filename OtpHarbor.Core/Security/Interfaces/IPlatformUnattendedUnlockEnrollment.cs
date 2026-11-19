using FluentResults;

namespace TOTP.Core.Security.Interfaces;

public interface IPlatformUnattendedUnlockEnrollment : IDisposable
{
    /// <summary>Used by the authorization facade after fresh authentication; still verifies the vault key.</summary>
    Task<Result> EnableWithAuthenticatedKeyAsync(
        ReadOnlyMemory<byte> vaultKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Fail("Authenticated-key enrollment is unavailable."));

    Task<Result> EnableAsync(
        string recoveryPassword,
        CancellationToken cancellationToken = default);

    Task<Result> DisableAsync(CancellationToken cancellationToken = default);
}
