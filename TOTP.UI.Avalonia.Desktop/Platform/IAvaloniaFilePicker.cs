namespace TOTP.Avalonia.Desktop.Platform;

public interface IAvaloniaFilePicker
{
    Task<INativeStorageFile?> PickImportFileAsync(CancellationToken cancellationToken = default);

    Task<INativeStorageFile?> PickQrImageAsync(CancellationToken cancellationToken = default);

    Task<INativeStorageFile?> PickEncryptedExportFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}
