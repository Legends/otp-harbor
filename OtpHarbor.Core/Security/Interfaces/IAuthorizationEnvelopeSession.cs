using FluentResults;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IAuthorizationEnvelopeSession : IDisposable
{
    AuthorizationEnvelopeSessionState State { get; }

    Task<Result<AuthorizationEnvelopeSessionState>> InitializeAsync(
        CancellationToken cancellationToken = default);

    Task<Result<AuthorizationResult>> TryUnlockWithPasswordAsync(
        string password,
        CancellationToken cancellationToken = default);

    Task<Result<AuthorizationResult>> TryUnlockWithPlatformAsync(
        CancellationToken cancellationToken = default);
}
