using TOTP.Avalonia.Desktop.Presentation.Dialogs;

namespace TOTP.Avalonia.Desktop.Platform;

public interface IAvaloniaDialogService
{
    Task<bool> ConfirmAsync(
        ConfirmationDialogRequest request,
        CancellationToken cancellationToken = default);

    Task<string?> PromptForPasswordAsync(
        PasswordDialogRequest request,
        CancellationToken cancellationToken = default);

    Task<ChoiceDialogResult> ChooseAsync(
        ChoiceDialogRequest request,
        CancellationToken cancellationToken = default);

    Task ShowMessageAsync(
        MessageDialogRequest request,
        CancellationToken cancellationToken = default);
}
