namespace TOTP.Core.Services.Interfaces;

public interface IUiScheduler
{
    bool CheckAccess();
    void Post(Action action);
    Task InvokeAsync(Action action);
    Task InvokeAsync(Func<Task> action);
}
