using Avalonia.Controls;
using Avalonia.Input;
using Moq;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Avalonia.Platform;

public sealed class AvaloniaActivityMonitorTests
{
    [Fact]
    public void Attach_RecordsHandledKeyboardInputUntilRegistrationIsDisposed()
    {
        var heartbeat = new Mock<IActivityHeartbeat>();
        var inputRoot = new Border();
        var monitor = new AvaloniaActivityMonitor(heartbeat.Object);
        var registration = monitor.Attach(inputRoot);

        inputRoot.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.A,
            Handled = true
        });

        heartbeat.Verify(value => value.RecordActivity(), Times.Once);

        registration.Dispose();
        inputRoot.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.B
        });
        heartbeat.Verify(value => value.RecordActivity(), Times.Once);
    }
}
