using FluentResults;
using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IQrAccountImportService
{
    Task<Result<QrAccountImportOutcome>> ImportAsync(
        string decodedPayload,
        Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>> resolveConflict,
        CancellationToken cancellationToken = default);

    Task<Result<QrAccountImportOutcome>> ImportWithConflictResolutionAsync(
        string decodedPayload,
        Func<QrAccountImportPreview, CancellationToken, Task<QrAccountImportResolution?>> resolveImport,
        CancellationToken cancellationToken = default);
}
