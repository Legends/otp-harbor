using System.Windows.Input;

namespace TOTP.Avalonia.Mobile.Presentation;

internal sealed class MobileAsyncCommand(
    Func<Task> execute,
    Func<bool> canExecute) : ICommand
{
    private int _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        Volatile.Read(ref _isExecuting) == 0 && canExecute();

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)
            || Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            return;
        }

        NotifyCanExecuteChanged();
        try
        {
            await execute();
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class MobileAsyncCommand<T>(
    Func<T, Task> execute,
    Func<T, bool> canExecute) : ICommand
    where T : class
{
    private int _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        parameter is T value
        && Volatile.Read(ref _isExecuting) == 0
        && canExecute(value);

    public async void Execute(object? parameter)
    {
        if (parameter is not T value
            || !CanExecute(value)
            || Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            return;
        }

        NotifyCanExecuteChanged();
        try
        {
            await execute(value);
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
