using TOTP.Avalonia.Desktop.Presentation;

namespace TOTP.Avalonia.Desktop.Platform;

public interface IAvaloniaCameraScannerDialogService
{
    Task ShowAsync(
        CameraScannerViewModel scanner,
        CancellationToken cancellationToken = default);
}
