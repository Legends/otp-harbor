using System.Collections.Concurrent;
using Avalonia.Media;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class CameraScannerViewModelTests
{
    [Fact]
    public async Task StartAsync_WhenValidQrIsDecoded_ShowsOnlySafeAccountMetadataAndClearsFrame()
    {
        var encodedFrame = new byte[] { 1, 2, 3 };
        var runner = new Mock<IQrScannerRunner>();
        runner.Setup(value => value.RunAsync(
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<byte[]>>(),
                It.IsAny<Action>(),
                It.IsAny<Action>()))
            .Returns<CancellationToken, Action<byte[]>, Action, Action>(
                (_, onPreview, onCameraOpened, onFirstFrame) =>
                {
                    onCameraOpened();
                    onPreview(encodedFrame);
                    onFirstFrame();
                    return Task.FromResult(QrScannerRunResult.Decoded(
                        "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP"));
                });
        var validator = new Mock<IQrPayloadValidator>();
        validator.Setup(value => value.Validate(It.IsAny<string>()))
            .Returns(new QrPayloadValidationResult(true, "Example", "alice"));
        var import = new Mock<IQrAccountImportService>();
        var importedAccountId = Guid.NewGuid();
        import.Setup(value => value.ImportAsync(
                It.IsAny<string>(),
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.Added,
                importedAccountId,
                "Example",
                "alice")));
        var lifetime = new TrackingDisposable();
        var imageFactory = new Mock<IAvaloniaQrImageFactory>();
        imageFactory.Setup(value => value.Create(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new AvaloniaQrImageHandle(Mock.Of<IImage>(), lifetime));
        using var sut = CreateSut(runner.Object, validator.Object, imageFactory.Object, import.Object);
        var closeRequested = false;
        AccountImportedEventArgs? importedEvent = null;
        sut.CloseRequested += (_, _) => closeRequested = true;
        sut.AccountImported += (_, args) => importedEvent = args;

        await sut.StartAsync();

        Assert.False(sut.IsScanning);
        Assert.True(sut.HasPreview);
        Assert.Equal(AvaloniaStringKeys.QrAccountAdded, sut.Message);
        Assert.DoesNotContain("JBSWY", sut.Message, StringComparison.Ordinal);
        Assert.All(encodedFrame, value => Assert.Equal(0, value));
        Assert.True(closeRequested);
        Assert.Equal(importedAccountId, importedEvent!.AccountId);
        Assert.Equal(AvaloniaStringKeys.QrAccountAdded, importedEvent.Message);
        validator.Verify(value => value.Validate(It.Is<string>(payload => payload.Contains("secret="))), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenBulkMigrationIsImported_ShowsLocalizedBatchProgress()
    {
        const string payload = "otpauth-migration://offline?data=synthetic";
        var runner = new Mock<IQrScannerRunner>();
        runner.Setup(value => value.RunAsync(
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<byte[]>>(),
                It.IsAny<Action>(),
                It.IsAny<Action>()))
            .ReturnsAsync(QrScannerRunResult.Decoded(payload));
        var validator = new Mock<IQrPayloadValidator>();
        validator.Setup(value => value.Validate(payload))
            .Returns(new QrPayloadValidationResult(
                true,
                "Example",
                "alice",
                QrPayloadKind.GoogleAuthenticatorMigration,
                4));
        var importedId = Guid.NewGuid();
        var import = new Mock<IQrAccountImportService>();
        import.Setup(value => value.ImportAsync(
                payload,
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.BulkImported,
                importedId,
                "Example",
                "alice",
                TotalCount: 4,
                AddedCount: 2,
                UpdatedCount: 1,
                DuplicateCount: 1,
                BatchIndex: 0,
                BatchSize: 2)));
        var localization = new Mock<IAvaloniaLocalizationService>();
        localization.Setup(value => value.GetString(It.IsAny<string>()))
            .Returns((string key) => key == AvaloniaStringKeys.QrBulkImportedMore
                ? "QR {0}/{1}: {2} imported, {3} unchanged, {4} failed. Continue."
                : key == AvaloniaStringKeys.QrMigrationConfirmationMessage
                    ? "Accounts found: {0}. Import them?"
                : key);
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var sut = CreateSut(
            runner.Object,
            validator.Object,
            importService: import.Object,
            localization: localization.Object,
            dialogs: dialogs.Object);
        AccountImportedEventArgs? importedEvent = null;
        sut.AccountImported += (_, args) => importedEvent = args;

        await sut.StartAsync();

        Assert.Equal("QR 1/2: 3 imported, 1 unchanged, 0 failed. Continue.", sut.Message);
        Assert.Equal(importedId, importedEvent!.AccountId);
        Assert.DoesNotContain("synthetic", sut.Message, StringComparison.Ordinal);
        dialogs.Verify(value => value.ConfirmAsync(
            It.Is<ConfirmationDialogRequest>(request =>
                request.Message == "Accounts found: 4. Import them?"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenBulkMigrationIsDeclined_DoesNotImportAccounts()
    {
        const string payload = "otpauth-migration://offline?data=synthetic";
        var runner = new Mock<IQrScannerRunner>();
        runner.Setup(value => value.RunAsync(
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<byte[]>>(),
                It.IsAny<Action>(),
                It.IsAny<Action>()))
            .ReturnsAsync(QrScannerRunResult.Decoded(payload));
        var validator = new Mock<IQrPayloadValidator>();
        validator.Setup(value => value.Validate(payload))
            .Returns(new QrPayloadValidationResult(
                true,
                "Example",
                "alice",
                QrPayloadKind.GoogleAuthenticatorMigration,
                2));
        var import = new Mock<IQrAccountImportService>();
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        using var sut = CreateSut(
            runner.Object,
            validator.Object,
            importService: import.Object,
            dialogs: dialogs.Object);
        var closeRequested = false;
        sut.CloseRequested += (_, _) => closeRequested = true;

        await sut.StartAsync();

        Assert.Equal(AvaloniaStringKeys.QrImportCancelled, sut.Message);
        Assert.True(closeRequested);
        import.Verify(value => value.ImportAsync(
            It.IsAny<string>(),
            It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP", QrPayloadKind.StandardAccount, 1)]
    [InlineData("otpauth-migration://offline?data=synthetic", QrPayloadKind.GoogleAuthenticatorMigration, 3)]
    public async Task OpenImageAsync_WhenQrImageIsValid_UsesTheSharedImportWorkflow(
        string payload,
        QrPayloadKind kind,
        int accountCount)
    {
        var selected = new Mock<INativeStorageFile>();
        selected.Setup(value => value.OpenReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickQrImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(selected.Object);
        var decoder = new Mock<IQrImageDecoder>();
        decoder.Setup(value => value.DecodeAsync(
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(QrImageDecodeResult.Decoded(payload));
        var validator = new Mock<IQrPayloadValidator>();
        validator.Setup(value => value.Validate(payload))
            .Returns(new QrPayloadValidationResult(
                true,
                "Example",
                "alice",
                kind,
                accountCount));
        var import = new Mock<IQrAccountImportService>();
        import.Setup(value => value.ImportAsync(
                payload,
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new QrAccountImportOutcome(
                kind == QrPayloadKind.GoogleAuthenticatorMigration
                    ? QrAccountImportStatus.BulkImported
                    : QrAccountImportStatus.Added,
                Guid.NewGuid(),
                "Example",
                "alice",
                TotalCount: accountCount,
                AddedCount: accountCount)));
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var sut = CreateSut(
            Mock.Of<IQrScannerRunner>(),
            validator.Object,
            importService: import.Object,
            dialogs: dialogs.Object,
            imageDecoder: decoder.Object,
            filePicker: picker.Object);

        await sut.OpenImageAsync();

        Assert.Equal(NotificationSeverity.Success, sut.LastImageNotificationSeverity);
        import.Verify(value => value.ImportAsync(
            payload,
            It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        dialogs.Verify(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(),
                It.IsAny<CancellationToken>()),
            kind == QrPayloadKind.GoogleAuthenticatorMigration ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task OpenImageAsync_WhenImageContainsNoQrCode_DoesNotValidateOrImport()
    {
        var selected = new Mock<INativeStorageFile>();
        selected.Setup(value => value.OpenReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickQrImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(selected.Object);
        var decoder = new Mock<IQrImageDecoder>();
        decoder.Setup(value => value.DecodeAsync(
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(QrImageDecodeResult.Rejected(QrImageDecodeStatus.NoQrCode));
        var validator = new Mock<IQrPayloadValidator>();
        var import = new Mock<IQrAccountImportService>();
        using var sut = CreateSut(
            Mock.Of<IQrScannerRunner>(),
            validator.Object,
            importService: import.Object,
            imageDecoder: decoder.Object,
            filePicker: picker.Object);

        await sut.OpenImageAsync();

        Assert.Contains("No readable QR code", sut.Message, StringComparison.Ordinal);
        Assert.Equal(NotificationSeverity.Error, sut.LastImageNotificationSeverity);
        validator.Verify(value => value.Validate(It.IsAny<string>()), Times.Never);
        import.Verify(value => value.ImportAsync(
            It.IsAny<string>(),
            It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_WhenScannerIsIdle_RequestsDialogClose()
    {
        using var sut = CreateSut(Mock.Of<IQrScannerRunner>());
        var closeRequested = false;
        sut.CloseRequested += (_, _) => closeRequested = true;

        await sut.CancelAsync();

        Assert.True(closeRequested);
        Assert.True(sut.CancelCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(QrScannerFailureKind.NativeRuntimeUnavailable, "runtime")]
    [InlineData(QrScannerFailureKind.Unexpected, "safely")]
    public async Task StartAsync_WhenRunnerReturnsFailure_ProjectsTypedSafeMessage(
        QrScannerFailureKind failure,
        string expected)
    {
        var runner = new Mock<IQrScannerRunner>();
        runner.Setup(value => value.RunAsync(
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<byte[]>>(),
                It.IsAny<Action>(),
                It.IsAny<Action>()))
            .ReturnsAsync(QrScannerRunResult.Failed(failure));
        using var sut = CreateSut(runner.Object);

        await sut.StartAsync();

        Assert.Contains(expected, sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(sut.IsScanning);
        runner.Verify(value => value.RunAsync(
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<byte[]>>(),
            It.IsAny<Action>(),
            It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenDisabledCameraBecomesAvailable_ReconnectsWithoutReopeningDialog()
    {
        var attempts = 0;
        var secondAttemptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSecondAttempt = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new Mock<IQrScannerRunner>();
        runner.Setup(value => value.RunAsync(
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<byte[]>>(),
                It.IsAny<Action>(),
                It.IsAny<Action>()))
            .Returns<CancellationToken, Action<byte[]>, Action, Action>(
                async (token, _, onCameraOpened, onFirstFrame) =>
                {
                    if (Interlocked.Increment(ref attempts) == 1)
                        return QrScannerRunResult.Failed(QrScannerFailureKind.NoCamera);

                    onCameraOpened();
                    secondAttemptStarted.SetResult();
                    await allowSecondAttempt.Task.WaitAsync(token);
                    onFirstFrame();
                    return QrScannerRunResult.Decoded(
                        "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP");
                });
        var validator = new Mock<IQrPayloadValidator>();
        validator.Setup(value => value.Validate(It.IsAny<string>()))
            .Returns(new QrPayloadValidationResult(true, "Example", "alice"));
        var import = new Mock<IQrAccountImportService>();
        import.Setup(value => value.ImportAsync(
                It.IsAny<string>(),
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.DuplicateUnchanged,
                Guid.NewGuid(),
                "Example",
                "alice")));
        using var sut = CreateSut(
            runner.Object,
            validator.Object,
            importService: import.Object,
            reconnectDelay: TimeSpan.FromMilliseconds(50));
        var closeRequested = false;
        sut.CloseRequested += (_, _) => closeRequested = true;

        var scan = sut.StartAsync();

        Assert.Contains("disabled", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reconnect automatically", sut.Message, StringComparison.OrdinalIgnoreCase);

        await secondAttemptStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.True(sut.IsScanning);
        Assert.Contains("Camera found", sut.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Initializing", sut.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Message);

        allowSecondAttempt.SetResult();
        await scan;

        Assert.False(sut.IsScanning);
        Assert.True(closeRequested);
        import.Verify(value => value.ImportAsync(
            It.IsAny<string>(),
            It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhileCameraRemainsUnavailable_DoesNotAlternateStatusText()
    {
        var attempts = 0;
        var thirdAttemptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new Mock<IQrScannerRunner>();
        runner.Setup(value => value.RunAsync(
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<byte[]>>(),
                It.IsAny<Action>(),
                It.IsAny<Action>()))
            .Returns<CancellationToken, Action<byte[]>, Action, Action>(
                async (token, _, _, _) =>
                {
                    var attempt = Interlocked.Increment(ref attempts);
                    if (attempt == 1)
                        return QrScannerRunResult.Failed(QrScannerFailureKind.NoCamera);
                    if (attempt == 2)
                        return QrScannerRunResult.Failed(QrScannerFailureKind.Stalled);

                    thirdAttemptStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return QrScannerRunResult.Failed(QrScannerFailureKind.NoCamera);
                });
        using var sut = CreateSut(
            runner.Object,
            reconnectDelay: TimeSpan.FromMilliseconds(1));
        var statuses = new ConcurrentQueue<string>();
        sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CameraScannerViewModel.StatusMessage))
                statuses.Enqueue(sut.StatusMessage);
        };

        var scan = sut.StartAsync();
        await thirdAttemptStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        var observed = statuses.ToArray();
        Assert.Single(
            observed,
            message => message.Contains("Checking", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("disabled", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Duration: < 10 sec.", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stopped", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.StatusMessage);

        sut.Clear();
        await scan;
    }

    [Fact]
    public async Task StartAsync_WhenGermanIsSelected_LocalizesTheCompleteFailureMessage()
    {
        var runner = new Mock<IQrScannerRunner>();
        runner.Setup(value => value.RunAsync(
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<byte[]>>(),
                It.IsAny<Action>(),
                It.IsAny<Action>()))
            .ReturnsAsync(QrScannerRunResult.Failed(QrScannerFailureKind.NativeRuntimeUnavailable));
        var localization = new Mock<IAvaloniaLocalizationService>();
        localization.Setup(value => value.GetString(It.IsAny<string>()))
            .Returns((string key) => key switch
            {
                AvaloniaStringKeys.CameraReadyToStart => "Der Kamerazugriff ist bereit.",
                AvaloniaStringKeys.CameraSearching => "Es wird nach einer Kamera gesucht…",
                AvaloniaStringKeys.CameraRuntimeUnavailable =>
                    "Die Kameralaufzeit ist für dieses Paket nicht verfügbar.",
                _ => key
            });
        using var sut = CreateSut(runner.Object, localization: localization.Object);

        await sut.StartAsync();

        Assert.Equal(
            "Die Kameralaufzeit ist für dieses Paket nicht verfügbar.",
            sut.Message);
        Assert.DoesNotContain("camera", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clear_WhileScanning_CancelsRunnerAndClearsPreview()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new Mock<IQrScannerRunner>();
        runner.Setup(value => value.RunAsync(
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<byte[]>>(),
                It.IsAny<Action>(),
                It.IsAny<Action>()))
            .Returns<CancellationToken, Action<byte[]>, Action, Action>(async (token, _, _, _) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return QrScannerRunResult.Failed(QrScannerFailureKind.Unexpected);
            });
        using var sut = CreateSut(runner.Object);

        var scan = sut.StartAsync();
        await started.Task;
        sut.Clear();
        await scan;

        Assert.False(sut.IsScanning);
        Assert.False(sut.HasPreview);
        Assert.Contains("starts only", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CameraScannerViewModel CreateSut(
        IQrScannerRunner runner,
        IQrPayloadValidator? validator = null,
        IAvaloniaQrImageFactory? imageFactory = null,
        IQrAccountImportService? importService = null,
        TimeSpan? reconnectDelay = null,
        IAvaloniaLocalizationService? localization = null,
        IAvaloniaDialogService? dialogs = null,
        IQrImageDecoder? imageDecoder = null,
        IAvaloniaFilePicker? filePicker = null) =>
        new(
            runner,
            imageDecoder ?? Mock.Of<IQrImageDecoder>(),
            filePicker ?? Mock.Of<IAvaloniaFilePicker>(),
            validator ?? Mock.Of<IQrPayloadValidator>(),
            imageFactory ?? Mock.Of<IAvaloniaQrImageFactory>(),
            new ImmediateUiScheduler(),
            NullLogger<CameraScannerViewModel>.Instance,
            importService ?? Mock.Of<IQrAccountImportService>(),
            dialogs ?? Mock.Of<IAvaloniaDialogService>(),
            localization ?? Localization(),
            reconnectDelay);

    private static IAvaloniaLocalizationService Localization()
    {
        var localization = new Mock<IAvaloniaLocalizationService>();
        localization.Setup(value => value.GetString(It.IsAny<string>()))
            .Returns((string key) => key switch
            {
                AvaloniaStringKeys.CameraReconnectHint =>
                    "If your camera is disabled, turn it on. The scanner will reconnect automatically. (Duration: < 10 sec.)",
                AvaloniaStringKeys.CameraReadyToStart =>
                    "Camera access starts only when you choose Scan QR.",
                AvaloniaStringKeys.CameraSearching => "Checking for an available camera…",
                AvaloniaStringKeys.CameraInitializing =>
                    "Camera found. Initializing the video stream…",
                AvaloniaStringKeys.CameraActive =>
                    "Camera active. Point it at a TOTP QR code.",
                AvaloniaStringKeys.CameraScanCancelled => "QR scan cancelled.",
                AvaloniaStringKeys.CameraScanFailedSafely =>
                    "The camera scanner failed safely. No account data was changed.",
                AvaloniaStringKeys.CameraRuntimeUnavailable =>
                    "The camera runtime is unavailable for this package.",
                AvaloniaStringKeys.CameraNotFound => "No available camera was found.",
                AvaloniaStringKeys.CameraStartFailed =>
                    "The camera scanner could not start safely.",
                AvaloniaStringKeys.QrImageNoCode =>
                    "No readable QR code was found in the selected image.",
                _ => key
            });
        return localization.Object;
    }

    private sealed class ImmediateUiScheduler : IUiScheduler
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public async Task InvokeAsync(Func<Task> action) => await action();
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
