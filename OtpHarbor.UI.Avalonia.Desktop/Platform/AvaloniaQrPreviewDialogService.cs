using Avalonia.Media;
using Avalonia.Threading;
using TOTP.Avalonia.Desktop.Dialogs;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaQrPreviewDialogService(
    AvaloniaWindowCoordinator windows,
    AvaloniaActivityMonitor activityMonitor) : IAvaloniaQrPreviewDialogService
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly object _gate = new();
    private QrPreviewDialogWindow? _currentDialog;

    public async Task ShowAsync(
        IImage image,
        string title,
        double requestedImageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await _dialogGate.WaitAsync(cancellationToken);
        try
        {
            var owner = windows.GetRequiredDialogOwner();
            var screen = owner.Screens.ScreenFromWindow(owner);
            var screenScale = screen?.Scaling is > 0 ? screen.Scaling : 1;
            var maximumWidth = screen is null
                ? 948
                : (screen.WorkingArea.Width / screenScale) - 64;
            var maximumHeight = screen is null
                ? 1020
                : (screen.WorkingArea.Height / screenScale) - 64;
            var maximumImageSize = Math.Max(
                256,
                Math.Min(maximumWidth - 48, maximumHeight - 120));
            var imageSize = Math.Clamp(requestedImageSize, 256, maximumImageSize);
            var dialog = new QrPreviewDialogWindow
            {
                DataContext = new QrPreviewDialogViewModel(title, image),
                Width = imageSize + 48,
                Height = imageSize + 120
            };
            lock (_gate) _currentDialog = dialog;
            using var ownership = windows.RegisterOwnedDialog(dialog);
            using var activity = activityMonitor.Attach(dialog);
            using var cancellation = cancellationToken.Register(Close);
            try
            {
                await dialog.ShowDialog(owner);
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_currentDialog, dialog)) _currentDialog = null;
                }
                dialog.DataContext = null;
            }
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    public void Close()
    {
        QrPreviewDialogWindow? dialog;
        lock (_gate) dialog = _currentDialog;
        if (dialog is null) return;

        if (Dispatcher.UIThread.CheckAccess())
            dialog.Close();
        else
            Dispatcher.UIThread.Post(dialog.Close);
    }
}
