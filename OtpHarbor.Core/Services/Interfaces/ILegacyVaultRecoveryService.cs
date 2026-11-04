using FluentResults;
using TOTP.Core.Models;

namespace TOTP.Core.Services.Interfaces;

public interface ILegacyVaultRecoveryService
{
    bool HasConflict();

    Task<Result<IReadOnlyList<Account>>> ReadOlderAccountsAsync(
        string masterPassword,
        CancellationToken cancellationToken = default);
}
