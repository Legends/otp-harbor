using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class UpdateCheckViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IPortableUpdateService _updates;
    private readonly IUpdateInstallerLauncher _installer;
    private readonly IAvaloniaLocalizationService _localization;
    private readonly AsyncCommand _checkCommand;
    private readonly AsyncCommand _downloadCommand;
    private readonly AsyncCommand _installCommand;
    private readonly AsyncCommand _cancelCommand;
    private PortableUpdateOffer? _offer;
    private PortableUpdatePackage? _package;
    private CancellationTokenSource? _operationLifetime;
    private string _messageKey = AvaloniaStringKeys.UpdateReadyToCheck;
    private object[] _messageArguments = [];
    private string _version = string.Empty;
    private string _releaseNotes = string.Empty;
    private int _progressPercentage;
    private bool _isProgressIndeterminate;
    private bool _isDownloading;
    private bool _isBusy;
    private bool _disposed;

    public UpdateCheckViewModel(
        IPortableUpdateService updates,
        IUpdateInstallerLauncher installer,
        IAvaloniaLocalizationService localization,
        TimeSpan? transientMessageDuration = null)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        Notification = new NotificationState(transientMessageDuration);
        Notification.ShowForSeverity(LocalizeMessage(), NotificationSeverity.Information);
        _localization.CultureChanged += LocalizationCultureChanged;
        _checkCommand = new AsyncCommand(CheckAsync, () => !_disposed && !IsBusy);
        _downloadCommand = new AsyncCommand(
            DownloadAsync,
            () => !_disposed && !IsBusy && _offer is not null);
        _installCommand = new AsyncCommand(
            InstallAsync,
            () => !_disposed && !IsBusy && _package is not null);
        _cancelCommand = new AsyncCommand(
            CancelAsync,
            () => !_disposed && IsBusy && _operationLifetime is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CheckCommand => _checkCommand;
    public ICommand DownloadCommand => _downloadCommand;
    public ICommand InstallCommand => _installCommand;
    public ICommand CancelCommand => _cancelCommand;

    public NotificationState Notification { get; }
    public string Message => Notification.Text;

    public string Version
    {
        get => _version;
        private set
        {
            if (!SetField(ref _version, value)) return;
            OnPropertyChanged(nameof(HasOffer));
            NotifyActionVisibility();
        }
    }

    public string ReleaseNotes
    {
        get => _releaseNotes;
        private set
        {
            if (!SetField(ref _releaseNotes, value)) return;
            OnPropertyChanged(nameof(HasReleaseNotes));
        }
    }

    public NotificationSeverity MessageSeverity => Notification.Severity;

    public int ProgressPercentage
    {
        get => _progressPercentage;
        private set => SetField(ref _progressPercentage, Math.Clamp(value, 0, 100));
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetField(ref _isProgressIndeterminate, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            NotifyCommands();
            NotifyActionVisibility();
        }
    }

    public bool HasOffer => Version.Length > 0;
    public bool HasReleaseNotes => ReleaseNotes.Length > 0;
    public bool IsInstallReady => _package is not null;
    public bool InstallerSupported => _installer.IsSupported;
    public bool ShowCheckAction => !IsBusy && !HasOffer && !IsInstallReady;
    public bool ShowDownloadAction => !IsBusy && HasOffer && !IsInstallReady;
    public bool ShowInstallAction => !IsBusy && IsInstallReady;
    public bool ShowCancelAction => IsBusy;
    public bool IsDownloadInProgress => _isDownloading;

    public async Task CheckAsync()
    {
        if (_disposed || IsBusy) return;
        ResetOffer();
        using var operation = BeginOperation();
        try
        {
            SetMessage(AvaloniaStringKeys.UpdateChecking, NotificationSeverity.Information);
            var result = await _updates.CheckAsync(operation.Token);
            if (result.IsFailed)
            {
                var verificationFailed = result.Errors
                    .OfType<PortableUpdateError>()
                    .Any(error => error.Code == PortableUpdateErrorCode.FeedVerificationFailed);
                SetFailure(verificationFailed
                    ? AvaloniaStringKeys.UpdateFeedVerificationFailed
                    : AvaloniaStringKeys.UpdateCheckFailed);
                return;
            }

            switch (result.Value.Status)
            {
                case PortableUpdateCheckStatus.Disabled:
                    SetMessage(AvaloniaStringKeys.UpdateDisabled, NotificationSeverity.Information);
                    break;
                case PortableUpdateCheckStatus.NoUpdate:
                    SetMessage(AvaloniaStringKeys.UpdateNoneAvailable, NotificationSeverity.Success);
                    break;
                case PortableUpdateCheckStatus.UpdateAvailable when result.Value.Offer is not null:
                    _offer = result.Value.Offer;
                    Version = result.Value.Offer.Version.ToString();
                    ReleaseNotes = result.Value.Offer.ReleaseNotes;
                    SetMessage(AvaloniaStringKeys.UpdateAvailable, NotificationSeverity.Success, Version);
                    break;
                default:
                    SetFailure(AvaloniaStringKeys.UpdateResponseIncomplete);
                    break;
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetMessage(AvaloniaStringKeys.UpdateCheckCancelled, NotificationSeverity.Information);
        }
        catch (Exception)
        {
            SetFailure(AvaloniaStringKeys.UpdateCheckFailed);
        }
    }

    public async Task DownloadAsync()
    {
        if (_disposed || IsBusy || _offer is null) return;
        _package = null;
        OnPropertyChanged(nameof(IsInstallReady));
        NotifyActionVisibility();
        using var operation = BeginOperation();
        _isDownloading = true;
        OnPropertyChanged(nameof(IsDownloadInProgress));
        ProgressPercentage = 0;
        IsProgressIndeterminate = true;
        try
        {
            SetMessage(AvaloniaStringKeys.UpdateDownloading, NotificationSeverity.Information);
            var progress = new Progress<PortableUpdateDownloadProgress>(value =>
            {
                IsProgressIndeterminate = value.Percentage is null;
                if (value.Percentage is { } percentage) ProgressPercentage = percentage;
            });
            var result = await _updates.DownloadAsync(_offer, progress, operation.Token);
            if (result.IsFailed)
            {
                SetFailure(AvaloniaStringKeys.UpdatePackageVerificationFailed);
                return;
            }

            _package = result.Value;
            ProgressPercentage = 100;
            IsProgressIndeterminate = false;
            OnPropertyChanged(nameof(IsInstallReady));
            NotifyActionVisibility();
            _installCommand.NotifyCanExecuteChanged();
            SetMessage(
                _installer.IsSupported
                    ? AvaloniaStringKeys.UpdateReadyToInstall
                    : AvaloniaStringKeys.UpdateInstallerUnsupported,
                _installer.IsSupported
                    ? NotificationSeverity.Success
                    : NotificationSeverity.Warning);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetMessage(AvaloniaStringKeys.UpdateDownloadCancelled, NotificationSeverity.Information);
        }
        catch (Exception)
        {
            SetFailure(AvaloniaStringKeys.UpdateDownloadFailed);
        }
        finally
        {
            _isDownloading = false;
            OnPropertyChanged(nameof(IsDownloadInProgress));
            IsProgressIndeterminate = false;
        }
    }

    public async Task InstallAsync()
    {
        if (_disposed || IsBusy || _package is null) return;
        using var operation = BeginOperation();
        try
        {
            var result = await _installer.LaunchAsync(_package, operation.Token);
            if (result.IsFailed)
            {
                SetMessage(AvaloniaStringKeys.UpdateInstallerStartFailed, NotificationSeverity.Error);
                return;
            }

            SetMessage(AvaloniaStringKeys.UpdateInstallerStarted, NotificationSeverity.Success);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetMessage(AvaloniaStringKeys.UpdateInstallCancelled, NotificationSeverity.Information);
        }
        catch (Exception)
        {
            SetFailure(AvaloniaStringKeys.UpdateInstallerFailedSafely);
        }
    }

    public Task CancelAsync()
    {
        _operationLifetime?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localization.CultureChanged -= LocalizationCultureChanged;
        _operationLifetime?.Cancel();
        _operationLifetime?.Dispose();
        _operationLifetime = null;
        Notification.Dispose();
        ResetOffer();
        NotifyCommands();
    }

    private OperationLease BeginOperation()
    {
        _operationLifetime?.Dispose();
        _operationLifetime = new CancellationTokenSource();
        IsBusy = true;
        return new OperationLease(this, _operationLifetime);
    }

    private void EndOperation(CancellationTokenSource lifetime)
    {
        if (!ReferenceEquals(_operationLifetime, lifetime)) return;
        _operationLifetime = null;
        lifetime.Dispose();
        IsBusy = false;
    }

    private void ResetOffer()
    {
        _offer = null;
        _package = null;
        Version = string.Empty;
        ReleaseNotes = string.Empty;
        ProgressPercentage = 0;
        IsProgressIndeterminate = false;
        _isDownloading = false;
        OnPropertyChanged(nameof(IsDownloadInProgress));
        OnPropertyChanged(nameof(IsInstallReady));
        NotifyActionVisibility();
        NotifyCommands();
    }

    private void SetFailure(string key)
    {
        SetMessage(key, NotificationSeverity.Error);
    }

    private void SetMessage(string key, NotificationSeverity severity, params object[] arguments)
    {
        _messageKey = key;
        _messageArguments = arguments;
        Notification.ShowForSeverity(LocalizeMessage(), severity);
    }

    private string LocalizeMessage()
    {
        var template = _localization.GetString(_messageKey);
        return _messageArguments.Length == 0
            ? template
            : string.Format(template, _messageArguments);
    }

    private void LocalizationCultureChanged(object? sender, EventArgs e)
    {
        if (Notification.HasMessage)
            Notification.ShowForSeverity(LocalizeMessage(), Notification.Severity);
    }

    private void NotifyCommands()
    {
        _checkCommand.NotifyCanExecuteChanged();
        _downloadCommand.NotifyCanExecuteChanged();
        _installCommand.NotifyCanExecuteChanged();
        _cancelCommand.NotifyCanExecuteChanged();
    }

    private void NotifyActionVisibility()
    {
        OnPropertyChanged(nameof(ShowCheckAction));
        OnPropertyChanged(nameof(ShowDownloadAction));
        OnPropertyChanged(nameof(ShowInstallAction));
        OnPropertyChanged(nameof(ShowCancelAction));
        OnPropertyChanged(nameof(IsDownloadInProgress));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class OperationLease(
        UpdateCheckViewModel owner,
        CancellationTokenSource lifetime) : IDisposable
    {
        private bool _disposed;
        public CancellationToken Token => lifetime.Token;
        public bool IsCancellationRequested => lifetime.IsCancellationRequested;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.EndOperation(lifetime);
        }
    }
}
