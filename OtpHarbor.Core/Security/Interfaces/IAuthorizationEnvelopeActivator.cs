using FluentResults;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

/// <summary>
/// Verifies a proposed v2 envelope and its candidate vault key before
/// atomically persisting the envelope.
/// </summary>
public interface IAuthorizationEnvelopeActivator : IDisposable
{
    Task<Result> ActivateAsync(
        AuthorizationEnvelopeV2 envelope,
        ReadOnlyMemory<byte> candidateVaultKey,
        string recoveryPassword,
        CancellationToken cancellationToken = default);
}
