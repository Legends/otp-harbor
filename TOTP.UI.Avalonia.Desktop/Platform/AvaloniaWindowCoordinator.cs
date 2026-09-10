using Avalonia.Controls;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaWindowCoordinator
{
    private readonly object _gate = new();
    private Window? _mainWindow;
    private readonly List<Window> _ownedDialogs = [];
    private int _nativePromptCount;

    public Window? CurrentActivationTarget
    {
        get
        {
            lock (_gate)
            {
                return _ownedDialogs.Count > 0 ? _ownedDialogs[^1] : _mainWindow;
            }
        }
    }

    public void RegisterMainWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_gate)
        {
            if (_mainWindow is not null && !ReferenceEquals(_mainWindow, window))
                throw new InvalidOperationException("A main window is already registered.");
            _mainWindow = window;
        }
    }

    public void UnregisterMainWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_gate)
        {
            if (ReferenceEquals(_mainWindow, window)) _mainWindow = null;
        }
    }

    public Window GetRequiredMainWindow()
    {
        lock (_gate)
        {
            return _mainWindow
                ?? throw new InvalidOperationException("The main window is not available for dialog ownership.");
        }
    }

    public Window GetRequiredDialogOwner()
    {
        lock (_gate)
        {
            return _ownedDialogs.Count > 0
                ? _ownedDialogs[^1]
                : _mainWindow
                    ?? throw new InvalidOperationException(
                        "The main window is not available for dialog ownership.");
        }
    }

    public IDisposable RegisterOwnedDialog(Window dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        lock (_gate)
        {
            if (_mainWindow is null)
                throw new InvalidOperationException("A dialog cannot be registered before the main window.");
            if (_ownedDialogs.Contains(dialog))
                throw new InvalidOperationException("The dialog is already registered.");
            _ownedDialogs.Add(dialog);
        }

        return new Registration(this, dialog);
    }

    public void ActivateCurrent()
    {
        if (_nativePromptCount > 0) return;
        var target = CurrentActivationTarget;
        if (target is null) return;

        if (target.WindowState == WindowState.Minimized)
            target.WindowState = WindowState.Normal;
        target.Show();
        target.Activate();
    }

    public IDisposable BeginNativePrompt()
    {
        var owner = CurrentActivationTarget;
        if (owner is null || !owner.IsVisible)
            throw new InvalidOperationException("A visible owner is required for a native prompt.");
        if (_nativePromptCount != 0)
            throw new InvalidOperationException("A native prompt is already active.");

        ActivateCurrent();
        var wasTopmost = owner.Topmost;
        owner.Topmost = false;
        _nativePromptCount++;
        return new NativePromptRegistration(this, owner, wasTopmost);
    }

    private sealed class NativePromptRegistration(
        AvaloniaWindowCoordinator coordinator, Window owner, bool wasTopmost) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            coordinator._nativePromptCount--;
            // A cancelled shutdown must never resurrect a closed owner.
            if (!owner.IsVisible || !ReferenceEquals(coordinator.CurrentActivationTarget, owner)) return;
            owner.Topmost = wasTopmost;
            coordinator.ActivateCurrent();
        }
    }

    private void UnregisterOwnedDialog(Window dialog)
    {
        lock (_gate)
        {
            _ownedDialogs.Remove(dialog);
        }
    }

    private sealed class Registration(
        AvaloniaWindowCoordinator coordinator,
        Window dialog) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            coordinator.UnregisterOwnedDialog(dialog);
        }
    }
}
