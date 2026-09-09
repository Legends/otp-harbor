using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using TOTP.Core.Services.Interfaces;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    private readonly ISupportDiagnosticsService _diagnostics;
    private readonly IAvaloniaLocalizationService _localization;
    private readonly IAvaloniaDialogService? _dialogs;
    private readonly IPlatformCapabilityReport? _capabilities;
    private readonly AsyncCommand _refreshCommand;
    private string _supportInformation = string.Empty;
    private bool _isBusy;

    public DiagnosticsViewModel(
        ISupportDiagnosticsService diagnostics,
        IAvaloniaLocalizationService localization,
        IAvaloniaDialogService? dialogs = null,
        IPlatformCapabilityReport? capabilities = null,
        TimeSpan? transientMessageDuration = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _dialogs = dialogs;
        _capabilities = capabilities;
        Notification = new NotificationState(transientMessageDuration);
        _refreshCommand = new AsyncCommand(RefreshAsync, () => !_isBusy);
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

    public async Task RefreshAsync()
    {
        if (_isBusy) return;
        _isBusy = true;
        _refreshCommand.NotifyCanExecuteChanged();
        try
        {
            var snapshot = _diagnostics.Capture();
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

            SupportInformation = output.ToString().TrimEnd();
            Notification.ShowForSeverity(
                _localization.GetString(AvaloniaStringKeys.DiagnosticRefreshSuccess),
                NotificationSeverity.Success);
        }
        catch (Exception)
        {
            SupportInformation = string.Empty;
            Notification.ShowPersistent(
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
