using Avalonia.Threading;
using TOTP.Avalonia.Desktop.Dialogs;
using TOTP.Avalonia.Desktop.Presentation;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaCameraScannerDialogService(
    AvaloniaWindowCoordinator windows,
    AvaloniaActivityMonitor activityMonitor) : IAvaloniaCameraScannerDialogService
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);

    public async Task ShowAsync(
        CameraScannerViewModel scanner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        await _dialogGate.WaitAsync(cancellationToken);
        try
        {
            var owner = windows.GetRequiredDialogOwner();
            var dialog = new CameraScannerDialogWindow { DataContext = scanner };
            EventHandler? opened = null;
            opened = (_, _) => Dispatcher.UIThread.Post(
                () => scanner.StartCommand.Execute(null),
                DispatcherPriority.Background);
            scanner.CloseRequested += Close;
            dialog.Opened += opened;
            using var ownership = windows.RegisterOwnedDialog(dialog);
            using var activity = activityMonitor.Attach(dialog);
            using var cancellation = cancellationToken.Register(
                () => Dispatcher.UIThread.Post(dialog.Close));

            try
            {
                await dialog.ShowDialog(owner);
            }
            finally
            {
                dialog.Opened -= opened;
                scanner.CloseRequested -= Close;
                scanner.Clear();
                dialog.DataContext = null;
            }

            void Close(object? sender, EventArgs args) =>
                Dispatcher.UIThread.Post(dialog.Close);
        }
        finally
        {
            _dialogGate.Release();
        }
    }
}
