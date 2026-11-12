using TOTP.Avalonia.Desktop.Presentation.Dialogs;

namespace TOTP.Avalonia.Desktop.Platform;

public interface IAvaloniaDialogService
{
    Task<bool> ConfirmImportFileAsync(TOTP.Avalonia.Shared.Import.AuthenticatorImportViewModel model,
        TOTP.Core.Services.Models.AccountImportReadSummary summary, string fileName,
        CancellationToken cancellationToken = default);

    Task<TOTP.Core.Services.Models.AccountImportResolution?> ReviewImportAsync(
        TOTP.Avalonia.Shared.Import.ImportReviewViewModel review,
        CancellationToken cancellationToken = default);

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
