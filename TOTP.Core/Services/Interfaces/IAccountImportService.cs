using FluentResults;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IAccountImportService
{
    Task<Result<AccountImportOutcome>> ImportAsync(
        IReadOnlyList<Account> importedAccounts,
        ImportConflictStrategy conflictStrategy,
        Func<AccountImportPreview, CancellationToken, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default);

    Task<Result<AccountImportOutcome>> ImportWithConflictResolutionAsync(
        IReadOnlyList<Account> importedAccounts,
        Func<AccountImportPreview, CancellationToken, Task<AccountImportResolution?>> resolveAsync,
        CancellationToken cancellationToken = default);
}
