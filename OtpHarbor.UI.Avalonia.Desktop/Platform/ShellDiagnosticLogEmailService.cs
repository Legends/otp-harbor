using System.Diagnostics;
using FluentResults;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class ShellDiagnosticLogEmailService : IDiagnosticLogEmailService
{
    public Task<Result<DiagnosticLogEmailOutcome>> OpenDraftAsync(
        DiagnosticLogEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (string.IsNullOrWhiteSpace(request.Recipient)
                || string.IsNullOrWhiteSpace(request.Subject)
                || string.IsNullOrWhiteSpace(request.Body))
            {
                return Task.FromResult(Result.Fail<DiagnosticLogEmailOutcome>(
                    "The diagnostic email fields are incomplete."));
            }

            var uri = $"mailto:{Uri.EscapeDataString(request.Recipient)}"
                + $"?subject={Uri.EscapeDataString(request.Subject)}"
                + $"&body={Uri.EscapeDataString(request.Body)}";
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return Task.FromResult(Result.Ok(
                DiagnosticLogEmailOutcome.ManualAttachmentRequired));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Fail<DiagnosticLogEmailOutcome>(
                new ExceptionalError("The diagnostic email draft could not be opened.", ex)));
        }
    }
}
