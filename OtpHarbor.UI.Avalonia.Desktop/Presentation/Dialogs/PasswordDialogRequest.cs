namespace TOTP.Avalonia.Desktop.Presentation.Dialogs;

public sealed record PasswordDialogRequest(
    string Title,
    string Message,
    string ConfirmText,
    string CancelText,
    string RequiredMessage,
    string ValidationFailureMessage,
    Func<string, CancellationToken, Task<string?>>? ValidateAsync = null,
    bool RequireConfirmation = false,
    string ConfirmationRequiredMessage = "",
    string MismatchMessage = "");
