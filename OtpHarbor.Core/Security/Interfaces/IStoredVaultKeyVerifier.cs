using FluentResults;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

/// <summary>
/// Loads the existing vault, if any, and verifies it with a candidate key
/// without changing vault contents or the active security context.
/// </summary>
public interface IStoredVaultKeyVerifier : IDisposable
{
    Task<Result<VaultKeyVerificationStatus>> VerifyAsync(
        ReadOnlyMemory<byte> candidateKey,
        CancellationToken cancellationToken = default);
}
