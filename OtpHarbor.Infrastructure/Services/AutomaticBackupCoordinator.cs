using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Backup;
using TOTP.Core.Security;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class AutomaticBackupCoordinator : IAutomaticBackupCoordinator
{
    private const int LatestGenerationCount = 5;
    private const int DailyGenerationCount = 7;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);

    private readonly IAutomaticBackupConfigurationStore _configurationStore;
    private readonly IAutomaticBackupSnapshotService _snapshotService;
    private readonly IAutomaticBackupDestinationAdapter _destinationAdapter;
    private readonly IAccountDAL _accountDal;
    private readonly AuthorizationState _authorizationState;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutomaticBackupCoordinator> _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly SynchronizationContext? _synchronizationContext;
    private CancellationTokenSource? _pendingBackup;
    private AutomaticBackupConfigurationV1? _configuration;
    private AutomaticBackupStatus _status = AutomaticBackupStatus.NotConfigured;
    private bool _initialized;
    private bool _disposed;

    public AutomaticBackupCoordinator(
        IAutomaticBackupConfigurationStore configurationStore,
        IAutomaticBackupSnapshotService snapshotService,
        IAutomaticBackupDestinationAdapter destinationAdapter,
        IAccountDAL accountDal,
        AuthorizationState authorizationState,
        TimeProvider timeProvider,
        ILogger<AutomaticBackupCoordinator> logger)
    {
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _destinationAdapter = destinationAdapter ?? throw new ArgumentNullException(nameof(destinationAdapter));
        _accountDal = accountDal ?? throw new ArgumentNullException(nameof(accountDal));
        _authorizationState = authorizationState ?? throw new ArgumentNullException(nameof(authorizationState));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _synchronizationContext = SynchronizationContext.Current;
        _authorizationState.Changed += AuthorizationStateChanged;
    }

    public event EventHandler? StatusChanged;

    public AutomaticBackupStatus Status
    {
        get
        {
            lock (_stateLock) return _status;
        }
    }

    public async Task<Result> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return Result.Ok();
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return Result.Ok();
            var loaded = await _configurationStore.LoadAsync(cancellationToken);
            if (loaded.IsFailed)
            {
                SetStatus(AutomaticBackupStatus.NotConfigured with { Kind = AutomaticBackupStatusKind.Failed });
                return Result.Fail(loaded.Errors);
            }

            _configuration = loaded.Value;
            _initialized = true;
            if (_configuration is null)
            {
                SetStatus(AutomaticBackupStatus.NotConfigured);
                return Result.Ok();
            }

            if (!string.Equals(_configuration.DestinationKind, _destinationAdapter.Kind, StringComparison.Ordinal))
            {
                await RecordFailureAsync(AutomaticBackupFailureKind.DestinationUnavailable,
                    AutomaticBackupDestinationAccess.Unavailable,
                    AutomaticBackupStatusKind.DestinationUnavailable,
                    cancellationToken);
                return Result.Ok();
            }

            var listed = await _destinationAdapter.ListAsync(ToDestination(_configuration), cancellationToken);
            if (listed.IsFailed)
            {
                await RecordFailureAsync(AutomaticBackupFailureKind.DestinationUnavailable,
                    AutomaticBackupDestinationAccess.Unavailable,
                    AutomaticBackupStatusKind.DestinationUnavailable,
                    cancellationToken);
                return Result.Ok();
            }

            _configuration = _configuration with
            {
                DestinationAccess = AutomaticBackupDestinationAccess.Accessible
            };
            if (_configuration.LastSuccessfulFileName is { } expected
                && !listed.Value.Any(file => string.Equals(file.Name, expected, StringComparison.Ordinal)))
            {
                await RecordFailureAsync(AutomaticBackupFailureKind.RecoveryPointMissing,
                    AutomaticBackupDestinationAccess.Accessible,
                    AutomaticBackupStatusKind.Failed,
                    cancellationToken);
            }
            else
            {
                SetStatus(CreateStatus(AutomaticBackupStatusKind.Ready));
            }

            return Result.Ok();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<Result<AutomaticBackupRunResult>> ConfigureAsync(CancellationToken cancellationToken = default)
    {
        var initialized = await InitializeAsync(cancellationToken);
        if (initialized.IsFailed) return Result.Fail<AutomaticBackupRunResult>(initialized.Errors);

        var selected = await _destinationAdapter.SelectAsync(cancellationToken);
        if (selected.IsFailed) return Result.Fail<AutomaticBackupRunResult>(selected.Errors);
        if (selected.Value is null)
            return Result.Ok(new AutomaticBackupRunResult(AutomaticBackupRunStatus.NotConfigured));

        var configuration = new AutomaticBackupConfigurationV1
        {
            DestinationKind = selected.Value.Kind,
            DestinationLocation = selected.Value.Location,
            DestinationDisplayName = selected.Value.DisplayName,
            DestinationAccess = AutomaticBackupDestinationAccess.Accessible
        };
        var saved = await _configurationStore.SaveAsync(configuration, cancellationToken);
        if (saved.IsFailed) return Result.Fail<AutomaticBackupRunResult>(saved.Errors);

        _configuration = configuration;
        SetStatus(CreateStatus(AutomaticBackupStatusKind.Ready));
        return await BackupCoreAsync(force: true, cancellationToken);
    }

    public async Task<Result> DisableAsync(CancellationToken cancellationToken = default)
    {
        CancelPendingBackup();
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var deleted = await _configurationStore.DeleteAsync(cancellationToken);
            if (deleted.IsFailed) return deleted;
            _configuration = null;
            _initialized = true;
            SetStatus(AutomaticBackupStatus.NotConfigured);
            return Result.Ok();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<Result<AutomaticBackupRunResult>> BackupNowAsync(CancellationToken cancellationToken = default)
    {
        var initialized = await InitializeAsync(cancellationToken);
        return initialized.IsSuccess
            ? await BackupCoreAsync(force: false, cancellationToken)
            : Result.Fail<AutomaticBackupRunResult>(initialized.Errors);
    }

    public Task<Result<AutomaticBackupRunResult>> EnsureCurrentCheckpointAsync(
        CancellationToken cancellationToken = default) => BackupNowAsync(cancellationToken);

    public void RequestBackup()
    {
        if (_disposed) return;
        CancellationTokenSource pending;
        lock (_stateLock)
        {
            _pendingBackup?.Cancel();
            _pendingBackup?.Dispose();
            _pendingBackup = new CancellationTokenSource();
            pending = _pendingBackup;
        }
        _ = RunPendingBackupAsync(pending);
    }

    private async Task RunPendingBackupAsync(CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(DebounceDelay, _timeProvider, pending.Token);
            await BackupNowAsync(pending.Token);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Queued automatic backup failed unexpectedly.");
            await RecordFailureAsync(AutomaticBackupFailureKind.UnexpectedFailure,
                _configuration?.DestinationAccess ?? AutomaticBackupDestinationAccess.Unknown,
                AutomaticBackupStatusKind.Failed,
                CancellationToken.None);
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_pendingBackup, pending)) _pendingBackup = null;
            }
            pending.Dispose();
        }
    }

    private async Task<Result<AutomaticBackupRunResult>> BackupCoreAsync(
        bool force,
        CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (_configuration is null)
                return Result.Ok(new AutomaticBackupRunResult(AutomaticBackupRunStatus.NotConfigured));
            if (!string.Equals(_configuration.DestinationKind, _destinationAdapter.Kind, StringComparison.Ordinal))
            {
                await RecordFailureAsync(AutomaticBackupFailureKind.DestinationUnavailable,
                    AutomaticBackupDestinationAccess.Unavailable,
                    AutomaticBackupStatusKind.DestinationUnavailable,
                    cancellationToken);
                return Failure(AutomaticBackupErrorCode.DestinationUnavailable,
                    "The configured automatic-backup destination is unavailable.");
            }

            SetStatus(CreateStatus(AutomaticBackupStatusKind.BackingUp, isBusy: true));
            var accounts = await _accountDal.GetAllAsync();
            if (accounts.IsFailed)
            {
                await RecordFailureAsync(AutomaticBackupFailureKind.VaultUnavailable,
                    _configuration.DestinationAccess,
                    AutomaticBackupStatusKind.Failed,
                    cancellationToken);
                return Result.Fail<AutomaticBackupRunResult>(accounts.Errors);
            }

            if (accounts.Value.Count == 0)
            {
                var healthy = await RecordSuccessAsync(cancellationToken);
                if (healthy.IsFailed) return Result.Fail<AutomaticBackupRunResult>(healthy.Errors);
                SetStatus(CreateStatus(AutomaticBackupStatusKind.UpToDate));
                return Result.Ok(new AutomaticBackupRunResult(AutomaticBackupRunStatus.UpToDate));
            }

            var snapshotResult = await _snapshotService.CreateAsync(cancellationToken);
            if (snapshotResult.IsFailed)
            {
                await RecordFailureAsync(AutomaticBackupFailureKind.VaultUnavailable,
                    _configuration.DestinationAccess,
                    AutomaticBackupStatusKind.Failed,
                    cancellationToken);
                return Result.Fail<AutomaticBackupRunResult>(snapshotResult.Errors);
            }

            using var snapshot = snapshotResult.Value;
            var destination = ToDestination(_configuration);
            if (!force && string.Equals(snapshot.Fingerprint, _configuration.LastFingerprint, StringComparison.Ordinal))
            {
                var listed = await _destinationAdapter.ListAsync(destination, cancellationToken);
                if (listed.IsFailed)
                {
                    await RecordFailureAsync(AutomaticBackupFailureKind.DestinationUnavailable,
                        AutomaticBackupDestinationAccess.Unavailable,
                        AutomaticBackupStatusKind.DestinationUnavailable,
                        cancellationToken);
                    return Result.Fail<AutomaticBackupRunResult>(listed.Errors);
                }

                if (_configuration.LastSuccessfulFileName is { } expected
                    && listed.Value.Any(file => string.Equals(file.Name, expected, StringComparison.Ordinal)))
                {
                    var healthy = await RecordSuccessAsync(cancellationToken);
                    if (healthy.IsFailed) return Result.Fail<AutomaticBackupRunResult>(healthy.Errors);
                    SetStatus(CreateStatus(AutomaticBackupStatusKind.UpToDate));
                    return Result.Ok(new AutomaticBackupRunResult(
                        AutomaticBackupRunStatus.UpToDate,
                        _configuration.LastSuccessfulUtc));
                }
            }

            var createdUtc = _timeProvider.GetUtcNow();
            var fileName = $"otp-harbor-auto-{createdUtc:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}.otphbackup";
            var written = await _destinationAdapter.WriteAsync(destination, fileName, snapshot.Payload, cancellationToken);
            if (written.IsFailed)
            {
                await RecordFailureAsync(AutomaticBackupFailureKind.WriteFailed,
                    _configuration.DestinationAccess,
                    AutomaticBackupStatusKind.Failed,
                    cancellationToken);
                return Result.Fail<AutomaticBackupRunResult>(written.Errors);
            }

            var updatedConfiguration = _configuration with
            {
                LastFingerprint = snapshot.Fingerprint,
                LastSuccessfulUtc = createdUtc,
                LastSuccessfulFileName = fileName,
                DestinationAccess = AutomaticBackupDestinationAccess.Accessible,
                LastFailureKind = null,
                LastFailureUtc = null
            };
            var saved = await _configurationStore.SaveAsync(updatedConfiguration, cancellationToken);
            if (saved.IsFailed)
            {
                SetInMemoryFailure(AutomaticBackupFailureKind.ConfigurationFailed,
                    AutomaticBackupDestinationAccess.Accessible,
                    AutomaticBackupStatusKind.Failed);
                return Result.Fail<AutomaticBackupRunResult>(saved.Errors);
            }

            _configuration = updatedConfiguration;
            await ApplyRetentionAsync(destination, cancellationToken);
            SetStatus(CreateStatus(AutomaticBackupStatusKind.Succeeded));
            return Result.Ok(new AutomaticBackupRunResult(AutomaticBackupRunStatus.Created, createdUtc));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic backup failed unexpectedly.");
            await RecordFailureAsync(AutomaticBackupFailureKind.UnexpectedFailure,
                _configuration?.DestinationAccess ?? AutomaticBackupDestinationAccess.Unknown,
                AutomaticBackupStatusKind.Failed,
                CancellationToken.None);
            return Result.Fail<AutomaticBackupRunResult>(new AutomaticBackupError(
                AutomaticBackupErrorCode.UnexpectedFailure,
                "Automatic backup failed unexpectedly.",
                ex));
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<Result> RecordSuccessAsync(CancellationToken cancellationToken)
    {
        if (_configuration is null) return Result.Ok();
        var updated = _configuration with
        {
            DestinationAccess = AutomaticBackupDestinationAccess.Accessible,
            LastFailureKind = null,
            LastFailureUtc = null
        };
        if (updated == _configuration) return Result.Ok();

        var saved = await _configurationStore.SaveAsync(updated, cancellationToken);
        if (saved.IsFailed)
        {
            SetInMemoryFailure(AutomaticBackupFailureKind.ConfigurationFailed,
                AutomaticBackupDestinationAccess.Accessible,
                AutomaticBackupStatusKind.Failed);
            return saved;
        }
        _configuration = updated;
        return Result.Ok();
    }

    private async Task RecordFailureAsync(
        AutomaticBackupFailureKind failureKind,
        AutomaticBackupDestinationAccess destinationAccess,
        AutomaticBackupStatusKind statusKind,
        CancellationToken cancellationToken)
    {
        if (_configuration is null)
        {
            SetStatus(AutomaticBackupStatus.NotConfigured with { Kind = statusKind });
            return;
        }

        _configuration = _configuration with
        {
            DestinationAccess = destinationAccess,
            LastFailureKind = failureKind,
            LastFailureUtc = _timeProvider.GetUtcNow()
        };
        var saved = await _configurationStore.SaveAsync(_configuration, cancellationToken);
        if (saved.IsFailed) _logger.LogWarning("Automatic backup health could not be persisted.");
        SetStatus(CreateStatus(statusKind));
    }

    private void SetInMemoryFailure(
        AutomaticBackupFailureKind failureKind,
        AutomaticBackupDestinationAccess destinationAccess,
        AutomaticBackupStatusKind statusKind)
    {
        if (_configuration is not null)
        {
            _configuration = _configuration with
            {
                DestinationAccess = destinationAccess,
                LastFailureKind = failureKind,
                LastFailureUtc = _timeProvider.GetUtcNow()
            };
        }
        SetStatus(CreateStatus(statusKind));
    }

    private async Task ApplyRetentionAsync(AutomaticBackupDestination destination, CancellationToken cancellationToken)
    {
        var listed = await _destinationAdapter.ListAsync(destination, cancellationToken);
        if (listed.IsFailed)
        {
            _logger.LogWarning("Automatic backup retention could not enumerate existing generations.");
            return;
        }

        var ordered = listed.Value.OrderByDescending(file => file.CreatedUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToList();
        var keep = ordered.Take(LatestGenerationCount)
            .Select(file => file.Identifier)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var daily in ordered.GroupBy(file => file.CreatedUtc.UtcDateTime.Date)
                     .OrderByDescending(group => group.Key)
                     .Take(DailyGenerationCount)
                     .Select(group => group.First()))
            keep.Add(daily.Identifier);

        foreach (var obsolete in ordered.Where(file => !keep.Contains(file.Identifier)))
        {
            var deleted = await _destinationAdapter.DeleteAsync(destination, obsolete, cancellationToken);
            if (deleted.IsFailed)
            {
                _logger.LogWarning("An obsolete automatic backup generation could not be removed.");
                break;
            }
        }
    }

    private AutomaticBackupStatus CreateStatus(AutomaticBackupStatusKind kind, bool isBusy = false) =>
        _configuration is null
            ? AutomaticBackupStatus.NotConfigured
            : new AutomaticBackupStatus(
                true,
                isBusy,
                _configuration.DestinationDisplayName,
                _configuration.LastSuccessfulUtc,
                kind,
                _configuration.DestinationAccess,
                _configuration.LastFailureKind,
                _configuration.LastFailureUtc);

    private static AutomaticBackupDestination ToDestination(AutomaticBackupConfigurationV1 configuration) => new(
        configuration.DestinationKind,
        configuration.DestinationLocation,
        configuration.DestinationDisplayName);

    private static Result<AutomaticBackupRunResult> Failure(AutomaticBackupErrorCode code, string message) =>
        Result.Fail<AutomaticBackupRunResult>(new AutomaticBackupError(code, message));

    private void SetStatus(AutomaticBackupStatus status)
    {
        lock (_stateLock) _status = status;
        var handler = StatusChanged;
        if (handler is null) return;
        if (_synchronizationContext is null) handler(this, EventArgs.Empty);
        else _synchronizationContext.Post(_ => handler(this, EventArgs.Empty), null);
    }

    private void AuthorizationStateChanged(object? sender, EventArgs e)
    {
        if (_authorizationState.IsUnlocked) RequestBackup();
    }

    private void CancelPendingBackup()
    {
        lock (_stateLock)
        {
            _pendingBackup?.Cancel();
            _pendingBackup = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _authorizationState.Changed -= AuthorizationStateChanged;
        CancelPendingBackup();
    }
}
