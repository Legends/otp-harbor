using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TOTP.Core.Platform;
using TOTP.Core.Security.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class SessionLockPolicyBackgroundService(
    IPlatformSessionEventSource sessionEvents,
    IAuthorizationService authorizationService,
    ISettingsService settingsService,
    ILogger<SessionLockPolicyBackgroundService> logger) : BackgroundService
{
    private int _isStarted;

    public event EventHandler? ApplicationLocked;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isStarted, 1) != 0)
        {
            return;
        }

        sessionEvents.SessionChanged += OnSessionChanged;
        try
        {
            sessionEvents.Start();
            await base.StartAsync(cancellationToken);
        }
        catch
        {
            sessionEvents.SessionChanged -= OnSessionChanged;
            Interlocked.Exchange(ref _isStarted, 0);
            throw;
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Delay(Timeout.Infinite, stoppingToken);

    private void OnSessionChanged(object? sender, PlatformSessionChangedEventArgs args)
    {
        if (args.State != PlatformSessionState.Locked
            || !settingsService.Current.LockOnSessionLock
            || !authorizationService.State.IsUnlocked)
        {
            return;
        }

        try
        {
            logger.LogInformation("Platform session locked. Locking application.");
            authorizationService.Lock();
            ApplicationLocked?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                "Failed to lock application after the platform session was locked. Exception type: {ExceptionType}.",
                exception.GetType().FullName);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isStarted, 0) == 0)
        {
            return;
        }

        try
        {
            sessionEvents.Stop();
        }
        finally
        {
            sessionEvents.SessionChanged -= OnSessionChanged;
            await base.StopAsync(cancellationToken);
        }
    }
}
