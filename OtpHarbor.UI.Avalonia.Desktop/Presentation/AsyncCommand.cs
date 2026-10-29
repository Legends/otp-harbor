using System.Windows.Input;

namespace TOTP.Avalonia.Desktop.Presentation;

internal sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        await execute();
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncCommand<T>(Func<T, Task> execute, Func<T, bool> canExecute) : ICommand
    where T : class
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        parameter is T value && canExecute(value);

    public async void Execute(object? parameter)
    {
        if (parameter is not T value || !CanExecute(value)) return;
        await execute(value);
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
