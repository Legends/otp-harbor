using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class SettingsPageViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IAvaloniaLocalizationService? _localization;
    private readonly AvaloniaStringCatalog _fallbackLocalization = new();
    private readonly IPlatformApplicationPaths? _applicationPaths;
    private readonly IPlatformFolderLauncher? _folderLauncher;
    private readonly AsyncCommand _openLogFolderCommand;
    private readonly TimeSpan _autoSaveDelay;
    private CancellationTokenSource? _autoSaveCts;
    private int _idleTimeoutMinutes;
    private bool _lockOnMinimize;
    private bool _lockOnSessionLock;
    private bool _clearClipboardEnabled;
    private int _clearClipboardSeconds;
    private decimal _qrPreviewScaleFactor;
    private InterfaceScaleOption? _selectedInterfaceScale;
    private bool _openExportFileAfterExport;
    private AppLogLevel _minimumLogLevel;
    private bool _isBusy;
    private bool _isReloading;
    private bool _saveRequested;
    private bool _disposed;
    private LanguageOption? _selectedLanguage;

    public SettingsPageViewModel(
        ISettingsService settingsService,
        IAvaloniaLocalizationService? localization = null,
        IPlatformApplicationPaths? applicationPaths = null,
        IPlatformFolderLauncher? folderLauncher = null,
        TimeSpan? autoSaveDelay = null,
        TimeSpan? transientMessageDuration = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _localization = localization;
        _applicationPaths = applicationPaths;
        _folderLauncher = folderLauncher;
        _autoSaveDelay = autoSaveDelay ?? TimeSpan.FromMilliseconds(200);
        SettingsNotification = new NotificationState(transientMessageDuration);
        LogFolderNotification = new NotificationState(transientMessageDuration);
        Languages = localization?.SupportedLanguages ?? [];
        LogLevels = Enum.GetValues<AppLogLevel>();
        InterfaceScales = CreateInterfaceScaleOptions();
        _selectedLanguage = localization?.CurrentLanguage;
        if (_localization is not null)
            _localization.CultureChanged += LocalizationCultureChanged;
        _openLogFolderCommand = new AsyncCommand(
            OpenLogFolderAsync,
            () => !_isBusy && _applicationPaths is not null && _folderLauncher is not null);
        VersionText = typeof(SettingsPageViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? typeof(SettingsPageViewModel).Assembly.GetName().Version?.ToString()
            ?? Localize(AvaloniaStringKeys.Unknown);
        Reload();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsSaved;

    public IReadOnlyList<LanguageOption> Languages { get; }
    public IReadOnlyList<AppLogLevel> LogLevels { get; }
    public IReadOnlyList<InterfaceScaleOption> InterfaceScales { get; }
    public bool IsInterfaceScaleAvailable => OperatingSystem.IsLinux();
    public string VersionText { get; }
    public ICommand OpenLogFolderCommand => _openLogFolderCommand;
    public NotificationState SettingsNotification { get; }
    public NotificationState LogFolderNotification { get; }

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetField(ref _selectedLanguage, value) || value is null) return;
            _localization?.ApplyCulture(value.CultureName);
            QueueAutoSave();
        }
    }

    public int IdleTimeoutMinutes
    {
        get => _idleTimeoutMinutes;
        set
        {
            if (!SetField(ref _idleTimeoutMinutes, value)) return;
            QueueAutoSave();
        }
    }

    public bool LockOnMinimize
    {
        get => _lockOnMinimize;
        set
        {
            if (!SetField(ref _lockOnMinimize, value)) return;
            QueueAutoSave();
        }
    }

    public bool LockOnSessionLock
    {
        get => _lockOnSessionLock;
        set
        {
            if (!SetField(ref _lockOnSessionLock, value)) return;
            QueueAutoSave();
        }
    }

    public bool ClearClipboardEnabled
    {
        get => _clearClipboardEnabled;
        set
        {
            if (!SetField(ref _clearClipboardEnabled, value)) return;
            QueueAutoSave();
        }
    }

    public int ClearClipboardSeconds
    {
        get => _clearClipboardSeconds;
        set
        {
            if (!SetField(ref _clearClipboardSeconds, value)) return;
            QueueAutoSave();
        }
    }

    public decimal QrPreviewScaleFactor
    {
        get => _qrPreviewScaleFactor;
        set
        {
            if (!SetField(ref _qrPreviewScaleFactor, value)) return;
            QueueAutoSave();
        }
    }

    public InterfaceScaleOption? SelectedInterfaceScale
    {
        get => _selectedInterfaceScale;
        set
        {
            if (!SetField(ref _selectedInterfaceScale, value)) return;
            QueueAutoSave();
        }
    }

    public bool OpenExportFileAfterExport
    {
        get => _openExportFileAfterExport;
        set
        {
            if (!SetField(ref _openExportFileAfterExport, value)) return;
            QueueAutoSave();
        }
    }

    public AppLogLevel MinimumLogLevel
    {
        get => _minimumLogLevel;
        set
        {
            if (!SetField(ref _minimumLogLevel, value)) return;
            QueueAutoSave();
        }
    }

    public string Message => SettingsNotification.Text;
    public NotificationSeverity MessageSeverity => SettingsNotification.Severity;
    public string LogFolderMessage => LogFolderNotification.Text;
    public NotificationSeverity LogFolderMessageSeverity => LogFolderNotification.Severity;

    public void Reload()
    {
        if (_saveRequested || _isBusy) return;

        _isReloading = true;
        try
        {
            IdleTimeoutMinutes = (int)Math.Clamp(
                Math.Round(_settingsService.Current.IdleTimeout.TotalMinutes),
                0,
                1440);
            LockOnMinimize = _settingsService.Current.LockOnMinimize;
            LockOnSessionLock = _settingsService.Current.LockOnSessionLock;
            ClearClipboardEnabled = _settingsService.Current.ClearClipboardEnabled;
            ClearClipboardSeconds = _settingsService.Current.ClearClipboardSeconds;
            QrPreviewScaleFactor = (decimal)_settingsService.Current.QrPreviewScaleFactor;
            SelectedInterfaceScale = InterfaceScales.FirstOrDefault(
                option => option.Percent == _settingsService.Current.InterfaceScalePercent)
                ?? InterfaceScales[0];
            OpenExportFileAfterExport = _settingsService.Current.OpenExportFileAfterExport;
            MinimumLogLevel = _settingsService.Current.MinimumLogLevel;
            SettingsNotification.Clear();
            LogFolderNotification.Clear();
        }
        finally
        {
            _isReloading = false;
        }
    }

    public async Task SaveAsync()
    {
        if (IdleTimeoutMinutes is < 0 or > 1440
            || ClearClipboardSeconds is < 1 or > 300
            || QrPreviewScaleFactor is < 1.0m or > 6.0m
            || QrPreviewScaleFactor * 2 != decimal.Truncate(QrPreviewScaleFactor * 2)
            || SelectedInterfaceScale is null) return;

        CancelAutoSaveDelay();
        if (_isBusy)
        {
            _saveRequested = true;
            return;
        }

        _isBusy = true;
        _saveRequested = false;
        _openLogFolderCommand.NotifyCanExecuteChanged();
        var previous = TOTP.Core.Models.AppPreferencesMapper.FromSettings(_settingsService.Current);
        try
        {
            _settingsService.Current.IdleTimeout = TimeSpan.FromMinutes(IdleTimeoutMinutes);
            _settingsService.Current.LockOnMinimize = LockOnMinimize;
            _settingsService.Current.LockOnSessionLock = LockOnSessionLock;
            _settingsService.Current.ClearClipboardEnabled = ClearClipboardEnabled;
            _settingsService.Current.ClearClipboardSeconds = ClearClipboardSeconds;
            _settingsService.Current.QrPreviewScaleFactor = (double)QrPreviewScaleFactor;
            _settingsService.Current.InterfaceScalePercent = SelectedInterfaceScale.Percent;
            _settingsService.Current.OpenExportFileAfterExport = OpenExportFileAfterExport;
            _settingsService.Current.MinimumLogLevel = MinimumLogLevel;
            if (SelectedLanguage is not null)
                _settingsService.Current.CultureName = SelectedLanguage.CultureName;
            var result = await _settingsService.SaveAsync();
            if (result.IsSuccess)
            {
                if (previous.InterfaceScalePercent != SelectedInterfaceScale.Percent)
                {
                    ShowTransientMessage(
                        Localize(AvaloniaStringKeys.InterfaceScaleRestartRequired),
                        NotificationSeverity.Information);
                }
                else
                {
                    ShowTransientMessage(
                        Localize(AvaloniaStringKeys.SettingsSavedAutomatically),
                        NotificationSeverity.Success);
                }
                SettingsSaved?.Invoke(this, EventArgs.Empty);
                return;
            }

            TOTP.Core.Models.AppPreferencesMapper.ApplyTo(previous, _settingsService.Current);
            SetPersistentMessage(
                Localize(AvaloniaStringKeys.SettingsSaveFailed),
                NotificationSeverity.Error);
        }
        catch (Exception)
        {
            TOTP.Core.Models.AppPreferencesMapper.ApplyTo(previous, _settingsService.Current);
            SetPersistentMessage(
                Localize(AvaloniaStringKeys.SettingsSaveFailed),
                NotificationSeverity.Error);
        }
        finally
        {
            _isBusy = false;
            _openLogFolderCommand.NotifyCanExecuteChanged();
            if (_saveRequested) QueueAutoSave();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_localization is not null)
            _localization.CultureChanged -= LocalizationCultureChanged;
        CancelAutoSaveDelay();
        SettingsNotification.Dispose();
        LogFolderNotification.Dispose();
    }

    private void LocalizationCultureChanged(object? sender, EventArgs args)
    {
        if (_disposed || _localization is null) return;
        SetField(
            ref _selectedLanguage,
            _localization.CurrentLanguage,
            nameof(SelectedLanguage));
    }

    public async Task OpenLogFolderAsync()
    {
        if (_isBusy || _applicationPaths is null || _folderLauncher is null) return;
        _isBusy = true;
        _openLogFolderCommand.NotifyCanExecuteChanged();
        try
        {
            var opened = await _folderLauncher.OpenFolderAsync(_applicationPaths.LogDirectory);
            if (opened.IsSuccess)
            {
                ShowTransientLogFolderMessage(
                    Localize(AvaloniaStringKeys.LogFolderOpened),
                    NotificationSeverity.Success);
            }
            else
            {
                SetPersistentLogFolderMessage(
                    Localize(AvaloniaStringKeys.LogFolderOpenFailed),
                    NotificationSeverity.Error);
            }
        }
        catch (Exception)
        {
            SetPersistentLogFolderMessage(
                Localize(AvaloniaStringKeys.LogFolderOpenFailedSafely),
                NotificationSeverity.Error);
        }
        finally
        {
            _isBusy = false;
            _openLogFolderCommand.NotifyCanExecuteChanged();
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void QueueAutoSave()
    {
        if (_isReloading || _disposed) return;

        _saveRequested = true;
        CancelAutoSaveDelay();
        var cts = new CancellationTokenSource();
        _autoSaveCts = cts;
        _ = SaveAfterDelayAsync(cts.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_autoSaveDelay, cancellationToken);
            await SaveAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void CancelAutoSaveDelay()
    {
        var cts = _autoSaveCts;
        _autoSaveCts = null;
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }

    private void ShowTransientMessage(string message, NotificationSeverity severity)
        => SettingsNotification.ShowTransient(message, severity);

    private void SetPersistentMessage(string message, NotificationSeverity severity)
        => SettingsNotification.ShowPersistent(message, severity);

    private void ShowTransientLogFolderMessage(string message, NotificationSeverity severity)
        => LogFolderNotification.ShowTransient(message, severity);

    private void SetPersistentLogFolderMessage(string message, NotificationSeverity severity)
        => LogFolderNotification.ShowPersistent(message, severity);

    private string Localize(string key) =>
        _localization?.GetString(key)
        ?? _fallbackLocalization.Get(key, CultureInfo.CurrentUICulture);

    private IReadOnlyList<InterfaceScaleOption> CreateInterfaceScaleOptions() =>
    [
        new(
            TOTP.Core.Models.AppSettings.DefaultInterfaceScalePercent,
            Localize(AvaloniaStringKeys.SystemInterfaceScale)),
        .. Enumerable.Range(4, 9).Select(index =>
        {
            var percent = index * 25;
            return new InterfaceScaleOption(percent, $"{percent}%");
        })
    ];

    public sealed record InterfaceScaleOption(int Percent, string Label);
}
