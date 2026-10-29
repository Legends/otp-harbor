using Avalonia.Threading;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Platform;

internal sealed class AvaloniaUiScheduler(Dispatcher dispatcher) : IUiScheduler
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        dispatcher.Post(action, DispatcherPriority.Normal);
    }

    public async Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action, DispatcherPriority.Background);
    }

    public async Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (dispatcher.CheckAccess())
        {
            await action();
            return;
        }

        await dispatcher.InvokeAsync(action, DispatcherPriority.Background);
    }
}
