using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using AppLifetime = TOTP.Core.Services.Interfaces.IApplicationLifetime;

namespace TOTP.Avalonia.Desktop.Startup;

public sealed class AvaloniaExceptionBoundary(
    IAuthorizationService authorizationService,
    AppLifetime applicationLifetime,
    ILogger<AvaloniaExceptionBoundary> logger)
{
    private const int FatalExitCode = 1;

    public bool TryHandleUiThread(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        LogExceptionType("Avalonia UI thread fault", exception);
        TryLockAuthorization();

        try
        {
            applicationLifetime.Shutdown(FatalExitCode);
            return true;
        }
        catch (Exception shutdownException)
        {
            LogExceptionType("Avalonia fatal shutdown fault", shutdownException);
            return false;
        }
    }

    public void HandleDomain(Exception? exception, bool isTerminating)
    {
        if (exception is not null)
            LogExceptionType("Avalonia application-domain fault", exception, isTerminating);
        else
            TryLogCritical(
                "Avalonia application-domain fault contained a non-exception value. Terminating: {IsTerminating}.",
                isTerminating);

        TryLockAuthorization();
    }

    public void HandleUnobservedTask(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        LogExceptionType("Avalonia unobserved task fault", exception);
    }

    private void TryLockAuthorization()
    {
        try
        {
            authorizationService.Lock();
        }
        catch (Exception lockException)
        {
            LogExceptionType("Avalonia fail-closed authorization fault", lockException);
        }
    }

    private void LogExceptionType(string stage, Exception exception, bool? isTerminating = null)
    {
        if (isTerminating.HasValue)
        {
            TryLogCritical(
                "{Stage} with exception type {ExceptionType}. Terminating: {IsTerminating}.",
                stage,
                exception.GetType().FullName,
                isTerminating.Value);
            return;
        }

        TryLogCritical(
            "{Stage} with exception type {ExceptionType}.",
            stage,
            exception.GetType().FullName);
    }

    private void TryLogCritical(string message, params object?[] arguments)
    {
        try
        {
            logger.LogCritical(message, arguments);
        }
        catch
        {
            // Exception reporting must never replace the original fatal path.
        }
    }
}
