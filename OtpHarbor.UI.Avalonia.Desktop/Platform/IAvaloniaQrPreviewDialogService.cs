using Avalonia.Media;

namespace TOTP.Avalonia.Desktop.Platform;

public interface IAvaloniaQrPreviewDialogService
{
    Task ShowAsync(
        IImage image,
        string title,
        double requestedImageSize,
        CancellationToken cancellationToken = default);

    void Close();
}
