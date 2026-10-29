using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class AccountImportedEventArgs(
    Guid accountId,
    QrAccountImportStatus status,
    string message) : EventArgs
{
    public Guid AccountId { get; } = accountId;
    public QrAccountImportStatus Status { get; } = status;
    public string Message { get; } = message;
}
