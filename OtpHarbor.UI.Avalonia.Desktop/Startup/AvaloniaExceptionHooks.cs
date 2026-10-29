using Avalonia.Threading;

namespace TOTP.Avalonia.Desktop.Startup;

internal sealed class AvaloniaExceptionHooks : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly AvaloniaExceptionBoundary _boundary;
    private bool _disposed;

    public AvaloniaExceptionHooks(
        Dispatcher dispatcher,
        AvaloniaExceptionBoundary boundary)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));

        _dispatcher.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dispatcher.UnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(
        object? sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        eventArgs.Handled = _boundary.TryHandleUiThread(eventArgs.Exception);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        _boundary.HandleDomain(eventArgs.ExceptionObject as Exception, eventArgs.IsTerminating);
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs eventArgs)
    {
        _boundary.HandleUnobservedTask(eventArgs.Exception);
        eventArgs.SetObserved();
    }
}
