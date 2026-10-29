using FluentResults;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

/// <summary>
/// Stores device-local quick-unlock secret material behind operating-system
/// protection. Callers must not use this store as the sole vault recovery path.
/// </summary>
public interface IPlatformSecretStore
{
    string ProviderId { get; }

    Task<PlatformSecretStoreAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<Result> StoreAsync(
        string secretReference,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a caller-owned buffer, or a successful null result when the
    /// reference does not exist. The caller must dispose a returned buffer.
    /// </summary>
    Task<Result<SensitiveBuffer?>> RetrieveAsync(
        string secretReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a reference. Deleting an absent reference is successful.
    /// </summary>
    Task<Result> DeleteAsync(
        string secretReference,
        CancellationToken cancellationToken = default);
}
