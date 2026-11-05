using FluentResults;
using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IDiagnosticLogEmailService
{
    Task<Result<DiagnosticLogEmailOutcome>> OpenDraftAsync(
        DiagnosticLogEmailRequest request,
        CancellationToken cancellationToken = default);
}
