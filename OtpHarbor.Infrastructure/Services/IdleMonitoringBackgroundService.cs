using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class IdleMonitoringBackgroundService : BackgroundService, IActivityHeartbeat
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private readonly IAuthorizationService _authService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<IdleMonitoringBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    private bool _wasUnlocked;
    private long _lastActivityTimestamp;

    public event EventHandler? ApplicationLocked;

    public IdleMonitoringBackgroundService(
        IAuthorizationService authService,
        ISettingsService settingsService,
        ILogger<IdleMonitoringBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        _authService = authService;
        _settingsService = settingsService;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastActivityTimestamp = _timeProvider.GetTimestamp();
    }

    public void RecordActivity() =>
        Interlocked.Exchange(ref _lastActivityTimestamp, _timeProvider.GetTimestamp());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var profileResult = await _settingsService.LoadAsync();
            if (profileResult.IsFailed)
            {
                _logger.LogWarning("Failed to load settings for idle monitoring. Using in-memory defaults.");
            }

            using var timer = new PeriodicTimer(CheckInterval, _timeProvider);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                EvaluateIdlePolicy();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Idle monitoring service canceled.");
        }
    }

    internal void EvaluateIdlePolicy()
    {
        var isUnlocked = _authService.State.IsUnlocked;

        if (isUnlocked && !_wasUnlocked)
        {
            RecordActivity();
            _wasUnlocked = true;
            return;
        }

        _wasUnlocked = isUnlocked;
        if (!isUnlocked)
        {
            return;
        }

        var timeout = _settingsService.Current?.IdleTimeout ?? AppSettings.DefaultIdleTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        var lastActivity = Interlocked.Read(ref _lastActivityTimestamp);
        if (_timeProvider.GetElapsedTime(lastActivity) < timeout)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Idle timeout reached ({Timeout}). Locking app.", timeout);
            _authService.Lock();
            _wasUnlocked = false;
            ApplicationLocked?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                "Failed to lock application after the idle timeout. Exception type: {ExceptionType}.",
                exception.GetType().FullName);
        }
    }
}

public interface IActivityHeartbeat
{
    void RecordActivity();
}
