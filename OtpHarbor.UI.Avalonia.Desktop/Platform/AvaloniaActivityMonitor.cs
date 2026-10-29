using Avalonia.Input;
using Avalonia.Interactivity;
using TOTP.Infrastructure.Services;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaActivityMonitor(IActivityHeartbeat activityHeartbeat)
{
    public IDisposable Attach(InputElement inputRoot)
    {
        ArgumentNullException.ThrowIfNull(inputRoot);
        return new ActivityRegistration(inputRoot, activityHeartbeat);
    }

    private sealed class ActivityRegistration : IDisposable
    {
        private readonly InputElement _inputRoot;
        private readonly EventHandler<KeyEventArgs> _keyHandler;
        private readonly EventHandler<PointerEventArgs> _pointerHandler;
        private readonly EventHandler<PointerPressedEventArgs> _pointerPressedHandler;
        private readonly EventHandler<PointerWheelEventArgs> _pointerWheelHandler;
        private bool _disposed;

        public ActivityRegistration(
            InputElement inputRoot,
            IActivityHeartbeat activityHeartbeat)
        {
            _inputRoot = inputRoot;
            _keyHandler = (_, _) => activityHeartbeat.RecordActivity();
            _pointerHandler = (_, _) => activityHeartbeat.RecordActivity();
            _pointerPressedHandler = (_, _) => activityHeartbeat.RecordActivity();
            _pointerWheelHandler = (_, _) => activityHeartbeat.RecordActivity();

            inputRoot.AddHandler(
                InputElement.KeyDownEvent,
                _keyHandler,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            inputRoot.AddHandler(
                InputElement.PointerMovedEvent,
                _pointerHandler,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            inputRoot.AddHandler(
                InputElement.PointerPressedEvent,
                _pointerPressedHandler,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            inputRoot.AddHandler(
                InputElement.PointerWheelChangedEvent,
                _pointerWheelHandler,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _inputRoot.RemoveHandler(InputElement.KeyDownEvent, _keyHandler);
            _inputRoot.RemoveHandler(InputElement.PointerMovedEvent, _pointerHandler);
            _inputRoot.RemoveHandler(InputElement.PointerPressedEvent, _pointerPressedHandler);
            _inputRoot.RemoveHandler(InputElement.PointerWheelChangedEvent, _pointerWheelHandler);
        }
    }
}
