using Avalonia.Platform.Storage;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaFilePicker(
    AvaloniaWindowCoordinator windowCoordinator,
    IAvaloniaLocalizationService localization) : IAvaloniaFilePicker
{
    public async Task<INativeStorageFile?> PickImportFileAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = windowCoordinator.GetRequiredDialogOwner().StorageProvider;
        if (!provider.CanOpen) return null;

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localization.GetString(AvaloniaStringKeys.SelectTotpImportFile),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(localization.GetString(AvaloniaStringKeys.TotpFiles))
                {
                    Patterns = ["*.totp", "*.json", "*.txt", "*.csv"]
                }
            ]
        });
        cancellationToken.ThrowIfCancellationRequested();

        return files.Count == 1 ? new AvaloniaStorageFile(files[0]) : null;
    }

    public async Task<INativeStorageFile?> PickEncryptedExportFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        cancellationToken.ThrowIfCancellationRequested();
        var provider = windowCoordinator.GetRequiredDialogOwner().StorageProvider;
        if (!provider.CanSave) return null;

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = localization.GetString(AvaloniaStringKeys.ExportEncryptedTotpBackup),
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
            DefaultExtension = "totp",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType(localization.GetString(AvaloniaStringKeys.EncryptedTotpBackupFile))
                {
                    Patterns = ["*.totp"]
                }
            ]
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file is null ? null : new AvaloniaStorageFile(file);
    }

    public async Task<INativeStorageFile?> PickQrImageAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = windowCoordinator.GetRequiredDialogOwner().StorageProvider;
        if (!provider.CanOpen) return null;

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localization.GetString(AvaloniaStringKeys.SelectQrImage),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(localization.GetString(AvaloniaStringKeys.QrImageFiles))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"]
                }
            ]
        });
        cancellationToken.ThrowIfCancellationRequested();

        return files.Count == 1 ? new AvaloniaStorageFile(files[0]) : null;
    }
}
