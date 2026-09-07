using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Input;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class CameraScannerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IQrScannerRunner _runner;
    private readonly IQrImageDecoder _imageDecoder;
    private readonly IAvaloniaFilePicker _filePicker;
    private readonly IQrPayloadValidator _payloadValidator;
    private readonly IAvaloniaQrImageFactory _imageFactory;
    private readonly IUiScheduler _uiScheduler;
    private readonly ILogger<CameraScannerViewModel> _logger;
    private readonly IQrAccountImportService _importService;
    private readonly IAvaloniaDialogService _dialogs;
    private readonly IAvaloniaLocalizationService _localization;
    private readonly TimeSpan _reconnectDelay;
    private readonly AsyncCommand _startCommand;
    private readonly AsyncCommand _openImageCommand;
    private readonly AsyncCommand _cancelCommand;
    private CancellationTokenSource? _captureLifetime;
    private AvaloniaQrImageHandle? _preview;
    private string _message;
    private string _statusMessage = string.Empty;
    private bool _isScanning;
    private bool _isOpeningImage;
    private NotificationSeverity _lastImageNotificationSeverity = NotificationSeverity.Information;
    private bool _disposed;
    private long _generation;

    public CameraScannerViewModel(
        IQrScannerRunner runner,
        IQrImageDecoder imageDecoder,
        IAvaloniaFilePicker filePicker,
        IQrPayloadValidator payloadValidator,
        IAvaloniaQrImageFactory imageFactory,
        IUiScheduler uiScheduler,
        ILogger<CameraScannerViewModel> logger,
        IQrAccountImportService importService,
        IAvaloniaDialogService dialogs,
        IAvaloniaLocalizationService localization,
        TimeSpan? reconnectDelay = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _imageDecoder = imageDecoder ?? throw new ArgumentNullException(nameof(imageDecoder));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _payloadValidator = payloadValidator ?? throw new ArgumentNullException(nameof(payloadValidator));
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        _uiScheduler = uiScheduler ?? throw new ArgumentNullException(nameof(uiScheduler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _message = _localization.GetString(AvaloniaStringKeys.CameraReadyToStart);
        _reconnectDelay = reconnectDelay ?? TimeSpan.FromMilliseconds(750);
        if (_reconnectDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconnectDelay));
        _startCommand = new AsyncCommand(
            StartAsync,
            () => !_disposed && !IsScanning && !IsOpeningImage);
        _openImageCommand = new AsyncCommand(
            OpenImageAsync,
            () => !_disposed && !IsOpeningImage);
        _cancelCommand = new AsyncCommand(CancelAsync, () => !_disposed);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AccountImportedEventArgs>? AccountImported;
    public event EventHandler? CloseRequested;

    public ICommand StartCommand => _startCommand;

    public ICommand OpenImageCommand => _openImageCommand;

    public ICommand CancelCommand => _cancelCommand;

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!SetField(ref _isScanning, value)) return;
            OnPropertyChanged(nameof(IsWaitingForPreview));
            _startCommand.NotifyCanExecuteChanged();
            _cancelCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsOpeningImage
    {
        get => _isOpeningImage;
        private set
        {
            if (!SetField(ref _isOpeningImage, value)) return;
            _startCommand.NotifyCanExecuteChanged();
            _openImageCommand.NotifyCanExecuteChanged();
            _cancelCommand.NotifyCanExecuteChanged();
        }
    }

    public NotificationSeverity LastImageNotificationSeverity
    {
        get => _lastImageNotificationSeverity;
        private set => SetField(ref _lastImageNotificationSeverity, value);
    }

    public IImage? PreviewImage => _preview?.Image;

    public bool HasPreview => PreviewImage is not null;

    public bool IsWaitingForPreview => IsScanning && !HasPreview;

    public async Task StartAsync()
    {
        if (_disposed || IsScanning || IsOpeningImage) return;

        ClearPreview();
        var generation = Interlocked.Increment(ref _generation);
        _captureLifetime?.Dispose();
        _captureLifetime = new CancellationTokenSource();
        var token = _captureLifetime.Token;
        IsScanning = true;
        Message = string.Empty;
        StatusMessage = _localization.GetString(AvaloniaStringKeys.CameraSearching);

        try
        {
            var lastReportedFailure = QrScannerFailureKind.None;
            var isReconnecting = false;
            while (!token.IsCancellationRequested)
            {
                if (!isReconnecting)
                    StatusMessage = _localization.GetString(AvaloniaStringKeys.CameraSearching);
                var result = await _runner.RunAsync(
                    token,
                    bytes => QueuePreview(bytes, generation),
                    () => QueueUi(
                        generation,
                        () => SetCameraAvailableStatus(
                            AvaloniaStringKeys.CameraInitializing)),
                    () => QueueUi(
                        generation,
                        () => SetCameraAvailableStatus(
                            AvaloniaStringKeys.CameraActive)));

                if (generation != Volatile.Read(ref _generation) || _disposed) return;

                if (result.IsDecoded)
                {
                    var validation = _payloadValidator.Validate(result.DecodedText!);
                    if (!validation.IsValid)
                    {
                        Message = _localization.GetString(AvaloniaStringKeys.QrInvalid);
                    }
                    else
                    {
                        await ImportDecodedAsync(result.DecodedText!, validation, token);
                    }

                    return;
                }

                if (result.Failure != lastReportedFailure)
                {
                    _logger.LogWarning(
                        "Avalonia camera is temporarily unavailable. failure={Failure}",
                        result.Failure);
                    lastReportedFailure = result.Failure;
                }
                Message = FailureMessage(result.Failure);
                ClearPreview();
                if (!ShouldReconnect(result.Failure)) return;

                isReconnecting = true;
                StatusMessage = string.Empty;
                await Task.Delay(_reconnectDelay, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!_disposed && generation == Volatile.Read(ref _generation))
            {
                StatusMessage = string.Empty;
                Message = _localization.GetString(AvaloniaStringKeys.CameraScanCancelled);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Avalonia camera scan failed at the platform boundary with exception type {ExceptionType}.",
                exception.GetType().FullName);
            if (!_disposed && generation == Volatile.Read(ref _generation))
            {
                StatusMessage = string.Empty;
                Message = _localization.GetString(AvaloniaStringKeys.CameraScanFailedSafely);
            }
        }
        finally
        {
            if (!_disposed && generation == Volatile.Read(ref _generation))
                IsScanning = false;
        }
    }

    public async Task OpenImageAsync()
    {
        if (_disposed || IsOpeningImage) return;

        var generation = Interlocked.Increment(ref _generation);
        _captureLifetime?.Cancel();
        _captureLifetime?.Dispose();
        _captureLifetime = new CancellationTokenSource();
        var token = _captureLifetime.Token;
        IsScanning = false;
        IsOpeningImage = true;
        StatusMessage = string.Empty;
        Message = string.Empty;
        LastImageNotificationSeverity = NotificationSeverity.Information;
        ClearPreview();

        try
        {
            await using var selected = await _filePicker.PickQrImageAsync(token);
            if (selected is null)
            {
                Message = _localization.GetString(AvaloniaStringKeys.QrImageSelectionCancelled);
                return;
            }

            StatusMessage = _localization.GetString(AvaloniaStringKeys.QrImageReading);
            await using var stream = await selected.OpenReadAsync(token);
            var decoded = await _imageDecoder.DecodeAsync(stream, token);
            if (_disposed || generation != Volatile.Read(ref _generation)) return;

            if (!decoded.IsDecoded)
            {
                Message = ImageDecodeFailureMessage(decoded.Status);
                LastImageNotificationSeverity = NotificationSeverity.Error;
                StatusMessage = string.Empty;
                return;
            }

            var validation = _payloadValidator.Validate(decoded.Payload!);
            if (!validation.IsValid)
            {
                Message = _localization.GetString(AvaloniaStringKeys.QrInvalid);
                LastImageNotificationSeverity = NotificationSeverity.Error;
                StatusMessage = string.Empty;
                return;
            }

            LastImageNotificationSeverity = await ImportDecodedAsync(decoded.Payload!, validation, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!_disposed && generation == Volatile.Read(ref _generation))
            {
                StatusMessage = string.Empty;
                Message = _localization.GetString(AvaloniaStringKeys.QrImageSelectionCancelled);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "QR image import failed at the platform boundary with exception type {ExceptionType}.",
                exception.GetType().FullName);
            if (!_disposed && generation == Volatile.Read(ref _generation))
            {
                StatusMessage = string.Empty;
                Message = _localization.GetString(AvaloniaStringKeys.QrImageReadFailedSafely);
                LastImageNotificationSeverity = NotificationSeverity.Error;
            }
        }
        finally
        {
            if (!_disposed && generation == Volatile.Read(ref _generation))
                IsOpeningImage = false;
        }
    }

    public Task CancelAsync()
    {
        _captureLifetime?.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Dismiss()
    {
        Clear();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _captureLifetime?.Cancel();
        _captureLifetime?.Dispose();
        _captureLifetime = null;
        IsScanning = false;
        IsOpeningImage = false;
        StatusMessage = string.Empty;
        Message = _localization.GetString(AvaloniaStringKeys.CameraReadyToStart);
        ClearPreview();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
        _startCommand.NotifyCanExecuteChanged();
        _openImageCommand.NotifyCanExecuteChanged();
        _cancelCommand.NotifyCanExecuteChanged();
    }

    private void QueuePreview(byte[] encodedFrame, long generation)
    {
        if (_disposed || generation != Volatile.Read(ref _generation))
        {
            CryptographicOperations.ZeroMemory(encodedFrame);
            return;
        }

        try
        {
            _uiScheduler.Post(() =>
            {
                try
                {
                    if (_disposed || generation != Volatile.Read(ref _generation)) return;
                    var replacement = _imageFactory.Create(encodedFrame);
                    var previous = _preview;
                    _preview = replacement;
                    OnPropertyChanged(nameof(PreviewImage));
                    OnPropertyChanged(nameof(HasPreview));
                    OnPropertyChanged(nameof(IsWaitingForPreview));
                    previous?.Dispose();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(encodedFrame);
                }
            });
        }
        catch
        {
            CryptographicOperations.ZeroMemory(encodedFrame);
            throw;
        }
    }

    private void QueueUi(long generation, Action action)
    {
        _uiScheduler.Post(() =>
        {
            if (!_disposed && generation == Volatile.Read(ref _generation))
                action();
        });
    }

    private void SetCameraAvailableStatus(string statusKey)
    {
        Message = string.Empty;
        StatusMessage = _localization.GetString(statusKey);
    }

    private void ClearPreview()
    {
        var preview = _preview;
        _preview = null;
        OnPropertyChanged(nameof(PreviewImage));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(IsWaitingForPreview));
        preview?.Dispose();
    }

    private async Task<NotificationSeverity> ImportDecodedAsync(
        string payload,
        QrPayloadValidationResult validation,
        CancellationToken cancellationToken)
    {
        if (validation.Kind == QrPayloadKind.GoogleAuthenticatorMigration)
        {
            var confirmed = await _dialogs.ConfirmAsync(new ConfirmationDialogRequest(
                _localization.GetString(AvaloniaStringKeys.QrMigrationConfirmationTitle),
                string.Format(
                    _localization.GetString(AvaloniaStringKeys.QrMigrationConfirmationMessage),
                    validation.AccountCount),
                NotificationSeverity.Information,
                _localization.GetString(AvaloniaStringKeys.ImportAccounts),
                _localization.GetString(AvaloniaStringKeys.Cancel)),
                cancellationToken);
            if (!confirmed)
            {
                Message = _localization.GetString(AvaloniaStringKeys.QrImportCancelled);
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return NotificationSeverity.Information;
            }
        }

        var imported = await _importService.ImportAsync(payload, ResolveConflictAsync, cancellationToken);
        if (imported.IsFailed)
        {
            Message = _localization.GetString(AvaloniaStringKeys.QrImportFailed);
            return NotificationSeverity.Error;
        }

        Message = imported.Value.Status == QrAccountImportStatus.BulkImported
            ? FormatBulkImportMessage(imported.Value)
            : _localization.GetString(imported.Value.Status switch
            {
                QrAccountImportStatus.Added => AvaloniaStringKeys.QrAccountAdded,
                QrAccountImportStatus.Updated => AvaloniaStringKeys.QrAccountUpdated,
                QrAccountImportStatus.KeptBoth => AvaloniaStringKeys.QrAccountKeptBoth,
                QrAccountImportStatus.DuplicateUnchanged => AvaloniaStringKeys.QrAccountDuplicate,
                _ => AvaloniaStringKeys.QrImportCancelled
            });
        if (imported.Value.Status is QrAccountImportStatus.Added
            or QrAccountImportStatus.Updated
            or QrAccountImportStatus.KeptBoth
            or QrAccountImportStatus.BulkImported)
        {
            AccountImported?.Invoke(this, new AccountImportedEventArgs(
                imported.Value.AccountId,
                imported.Value.Status,
                Message));
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
        return imported.Value.Status switch
        {
            QrAccountImportStatus.Added
                or QrAccountImportStatus.Updated
                or QrAccountImportStatus.KeptBoth
                or QrAccountImportStatus.BulkImported => NotificationSeverity.Success,
            _ => NotificationSeverity.Information
        };

        async Task<QrAccountConflictDecision> ResolveConflictAsync(
            QrAccountConflict conflict,
            CancellationToken token)
        {
            var choice = await _dialogs.ChooseAsync(new ChoiceDialogRequest(
                _localization.GetString(AvaloniaStringKeys.QrConflictTitle),
                string.Format(
                    _localization.GetString(AvaloniaStringKeys.QrConflictMessage),
                    conflict.Issuer,
                    conflict.AccountName),
                NotificationSeverity.Warning,
                _localization.GetString(AvaloniaStringKeys.UpdateExisting),
                _localization.GetString(AvaloniaStringKeys.KeepBoth),
                _localization.GetString(AvaloniaStringKeys.Cancel)),
                token);
            return choice switch
            {
                ChoiceDialogResult.Primary => QrAccountConflictDecision.UpdateExisting,
                ChoiceDialogResult.Secondary => QrAccountConflictDecision.KeepBoth,
                _ => QrAccountConflictDecision.Cancel
            };
        }
    }

    private string FormatBulkImportMessage(QrAccountImportOutcome outcome)
    {
        if (outcome.HasMoreBatches)
        {
            return string.Format(
                _localization.GetString(AvaloniaStringKeys.QrBulkImportedMore),
                outcome.BatchIndex + 1,
                outcome.BatchSize,
                outcome.ImportedCount,
                outcome.DuplicateCount,
                outcome.FailedCount);
        }

        return string.Format(
            _localization.GetString(AvaloniaStringKeys.QrBulkImported),
            outcome.ImportedCount,
            outcome.DuplicateCount,
            outcome.FailedCount);
    }

    private string FailureMessage(QrScannerFailureKind failure)
    {
        if (ShouldReconnect(failure))
        {
            return $"{_localization.GetString(AvaloniaStringKeys.CameraNotFound)} "
                + _localization.GetString(AvaloniaStringKeys.CameraReconnectHint);
        }

        var key = failure switch
        {
            QrScannerFailureKind.NativeRuntimeUnavailable => AvaloniaStringKeys.CameraRuntimeUnavailable,
            _ => AvaloniaStringKeys.CameraStartFailed
        };
        return _localization.GetString(key);
    }

    private string ImageDecodeFailureMessage(QrImageDecodeStatus status) =>
        _localization.GetString(status switch
        {
            QrImageDecodeStatus.NoQrCode => AvaloniaStringKeys.QrImageNoCode,
            QrImageDecodeStatus.InvalidImage => AvaloniaStringKeys.QrImageInvalid,
            QrImageDecodeStatus.TooLarge => AvaloniaStringKeys.QrImageTooLarge,
            _ => AvaloniaStringKeys.QrImageReadFailedSafely
        });

    private static bool ShouldReconnect(QrScannerFailureKind failure) => failure is
        QrScannerFailureKind.NoCamera
        or QrScannerFailureKind.PermissionDenied
        or QrScannerFailureKind.DeviceLost
        or QrScannerFailureKind.Stalled;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
