using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation.Dialogs;

public sealed record MessageDialogRequest(
    string Title,
    string Message,
    NotificationSeverity Severity,
    string CloseText);
