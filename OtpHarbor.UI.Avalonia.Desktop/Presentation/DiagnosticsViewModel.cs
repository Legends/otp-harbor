using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using TOTP.Core.Services.Interfaces;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Core.Services.Models;
using TOTP.Core.Enums;
using TOTP.Avalonia.Shared.Diagnostics;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed record DiagnosticSummaryItem(SymbolIconKind Icon, string Label, string Value);

public sealed class DiagnosticsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISupportDiagnosticsService _diagnostics;
    private readonly IAvaloniaLocalizationService _localization;
    private readonly IAvaloniaDialogService? _dialogs;
    private readonly IPlatformCapabilityReport? _capabilities;
    private readonly IDiagnosticEnvironmentProvider? _environmentProvider;
    private readonly ILogSwitchService? _logSwitch;
    private readonly AsyncCommand _refreshCommand;
    private string _supportInformation = string.Empty;
    private bool _isBusy;
    private bool _disposed;
    private SupportDiagnosticsSnapshot? _snapshot;
    private DiagnosticEnvironment? _environment;

    public DiagnosticsViewModel(
        ISupportDiagnosticsService diagnostics,
        IAvaloniaLocalizationService localization,
        IAvaloniaDialogService? dialogs = null,
        IPlatformCapabilityReport? capabilities = null,
        TimeSpan? transientMessageDuration = null,
        IDiagnosticEnvironmentProvider? environmentProvider = null,
        ILogSwitchService? logSwitch = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _dialogs = dialogs;
        _capabilities = capabilities;
        _environmentProvider = environmentProvider;
        _logSwitch = logSwitch;
        Notification = new NotificationState(transientMessageDuration);
        _refreshCommand = new AsyncCommand(RefreshAsync, () => !_isBusy);
        _localization.CultureChanged += CultureChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SupportInformation
    {
        get => _supportInformation;
        private set => SetField(ref _supportInformation, value);
    }

    public NotificationState Notification { get; }
    public string Message => Notification.Text;
    public NotificationSeverity MessageSeverity => Notification.Severity;
    public bool HasMessage => Notification.HasMessage;
    public ICommand RefreshCommand => _refreshCommand;
    public string ApplicationVersion => _environment?.AppVersion ?? _snapshot?.ApplicationVersion ?? Unavailable;
    public string OperatingSystem => _environment?.OperatingSystem ?? _snapshot?.OperatingSystem ?? Unavailable;
    public string Runtime => _snapshot?.Framework ?? Unavailable;
    public string Display => _environment?.ScreenDisplay ?? _environment?.Display ?? Unavailable;
    public string Language => Localized(AvaloniaStringKeys.DiagnosticLanguageValue,
        _localization.CurrentLanguage.DisplayName, _localization.CurrentLanguage.CultureName);
    public string LogLevel => _logSwitch is null ? Unavailable : _localization.GetString(_logSwitch.MinimumLevel switch
    {
        AppLogLevel.Verbose => AvaloniaStringKeys.DiagnosticLogLevelVerbose,
        AppLogLevel.Debug => AvaloniaStringKeys.DiagnosticLogLevelDebug,
        AppLogLevel.Information => AvaloniaStringKeys.DiagnosticLogLevelInformation,
        AppLogLevel.Warning => AvaloniaStringKeys.DiagnosticLogLevelWarning,
        AppLogLevel.Error => AvaloniaStringKeys.DiagnosticLogLevelError,
        AppLogLevel.Fatal => AvaloniaStringKeys.DiagnosticLogLevelFatal,
        _ => AvaloniaStringKeys.DiagnosticValueUnavailable
    });
    private string Unavailable => _localization.GetString(AvaloniaStringKeys.DiagnosticValueUnavailable);
    public IReadOnlyList<DiagnosticSummaryItem> PrimaryDetails =>
    [
        new(SymbolIconKind.Cube, _localization.GetString(TOTP.Localization.CommonStringKeys.AboutAppVersion), ApplicationVersion),
        new(SymbolIconKind.Monitor, _localization.GetString(AvaloniaStringKeys.DiagnosticOperatingSystemLabel), OperatingSystem),
        new(SymbolIconKind.Code, _localization.GetString(AvaloniaStringKeys.DiagnosticRuntimeLabel), Runtime)
    ];
    public IReadOnlyList<DiagnosticSummaryItem> SecondaryDetails =>
    [
        new(SymbolIconKind.Monitor, _localization.GetString(AvaloniaStringKeys.DiagnosticDisplayLabel), Display),
        new(SymbolIconKind.Globe, _localization.GetString(AvaloniaStringKeys.Language), Language),
        new(SymbolIconKind.Sliders, _localization.GetString(AvaloniaStringKeys.DiagnosticLogLevelLabel), LogLevel)
    ];

    public void RefreshSummaryLabels()
    {
        if (_disposed) return;
        foreach (var property in new[] { nameof(ApplicationVersion), nameof(OperatingSystem), nameof(Runtime),
                     nameof(Display), nameof(Language), nameof(LogLevel), nameof(PrimaryDetails), nameof(SecondaryDetails) })
            OnPropertyChanged(property);
    }

    public async Task RefreshAsync()
    {
        if (_isBusy || _disposed) return;
        _isBusy = true;
        _refreshCommand.NotifyCanExecuteChanged();
        try
        {
            var snapshot = _diagnostics.Capture();
            var environment = _environmentProvider?.Capture();
            var output = new StringBuilder()
                .AppendLine($"OTP Harbor {snapshot.ApplicationVersion}")
                .AppendLine(Localized(AvaloniaStringKeys.DiagnosticPlatform, snapshot.OperatingSystem))
                .AppendLine(Localized(AvaloniaStringKeys.DiagnosticArchitecture, snapshot.ProcessArchitecture))
                .AppendLine(Localized(AvaloniaStringKeys.DiagnosticRuntime, snapshot.Framework))
                .AppendLine(Localized(
                    AvaloniaStringKeys.DiagnosticLogDirectoryConfigured,
                    _localization.GetString(
                        snapshot.LogDirectoryConfigured ? AvaloniaStringKeys.Yes : AvaloniaStringKeys.No)));
            if (snapshot.StartupRecords.Count > 0)
            {
                output.AppendLine(_localization.GetString(AvaloniaStringKeys.DiagnosticStartupStages));
                foreach (var record in snapshot.StartupRecords)
                {
                    output.AppendLine(Localized(
                        AvaloniaStringKeys.DiagnosticStageLine,
                        record.Stage,
                        record.ElapsedMilliseconds,
                        _localization.GetString(
                            record.Succeeded
                                ? AvaloniaStringKeys.DiagnosticSucceeded
                                : AvaloniaStringKeys.DiagnosticFailed)));
                }
            }

            if (_capabilities is not null)
            {
                var capabilities = await _capabilities.CaptureAsync();
                output.AppendLine(_localization.GetString(
                    AvaloniaStringKeys.DiagnosticPlatformCapabilities));
                foreach (var capability in capabilities)
                    output.AppendLine(Localized(
                        AvaloniaStringKeys.DiagnosticCapabilityLine,
                        capability.Name,
                        LocalizedCapabilityStatus(capability.Status)));
            }

            if (_disposed) return;
            _snapshot = snapshot;
            _environment = environment;
            RefreshSummaryLabels();
            SupportInformation = output.ToString().TrimEnd();
        }
        catch (Exception)
        {
            if (_disposed) return;
            _snapshot = null;
            _environment = null;
            RefreshSummaryLabels();
            SupportInformation = string.Empty;
            Notification.ShowTransient(
                _localization.GetString(AvaloniaStringKeys.DiagnosticRefreshFailed),
                NotificationSeverity.Error);
            if (_dialogs is not null)
            {
                try
                {
                    await _dialogs.ShowMessageAsync(new MessageDialogRequest(
                        _localization.GetString(AvaloniaStringKeys.DiagnosticUnavailableTitle),
                        _localization.GetString(AvaloniaStringKeys.DiagnosticUnavailableMessage),
                        NotificationSeverity.Error,
                        _localization.GetString(AvaloniaStringKeys.Close)));
                }
                catch (Exception)
                {
                    // The non-modal banner remains the safe recovery path.
                }
            }
        }
        finally
        {
            _isBusy = false;
            _refreshCommand.NotifyCanExecuteChanged();
        }

    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localization.CultureChanged -= CultureChanged;
        Notification.Dispose();
    }

    private void CultureChanged(object? sender, EventArgs e) => RefreshSummaryLabels();

    private string Localized(string key, params object[] arguments) =>
        string.Format(_localization.GetString(key), arguments);

    private string LocalizedCapabilityStatus(PlatformCapabilityStatus status) =>
        _localization.GetString(status switch
        {
            PlatformCapabilityStatus.Supported => AvaloniaStringKeys.CapabilitySupported,
            PlatformCapabilityStatus.PermanentlyUnavailable =>
                AvaloniaStringKeys.CapabilityPermanentlyUnavailable,
            PlatformCapabilityStatus.TemporarilyUnavailable =>
                AvaloniaStringKeys.CapabilityTemporarilyUnavailable,
            PlatformCapabilityStatus.Misconfigured => AvaloniaStringKeys.CapabilityMisconfigured,
            PlatformCapabilityStatus.PermissionDenied => AvaloniaStringKeys.CapabilityPermissionDenied,
            _ => AvaloniaStringKeys.CapabilityFailed
        });

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
