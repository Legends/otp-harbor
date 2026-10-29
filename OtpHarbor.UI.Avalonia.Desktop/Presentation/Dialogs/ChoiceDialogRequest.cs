using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation.Dialogs;

public enum ChoiceDialogResult
{
    Cancel,
    Primary,
    Secondary
}

public sealed record ChoiceDialogRequest(
    string Title,
    string Message,
    NotificationSeverity Severity,
    string PrimaryText,
    string SecondaryText,
    string CancelText);
