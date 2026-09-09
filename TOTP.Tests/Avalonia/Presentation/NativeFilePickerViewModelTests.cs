using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Avalonia.Controls;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class NativeFilePickerViewModelTests
{
    [Fact]
    public async Task ImportGoogleQrAsync_OpensImagePickerDirectlyWithoutStartingCamera()
    {
        const string payload = "otpauth-migration://offline?data=synthetic";
        var selected = new TestStorageFile("google-transfer.png", content: [1, 2, 3]);
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickQrImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(selected);
        var decoder = new Mock<IQrImageDecoder>();
        decoder.Setup(value => value.DecodeAsync(
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(QrImageDecodeResult.Decoded(payload));
        var validator = new Mock<IQrPayloadValidator>();
        validator.Setup(value => value.Validate(payload))
            .Returns(new QrPayloadValidationResult(
                true,
                "Example",
                "alice",
                QrPayloadKind.GoogleAuthenticatorMigration,
                2));
        var qrImport = new Mock<IQrAccountImportService>();
        qrImport.Setup(value => value.ImportAsync(
                payload,
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.BulkImported,
                Guid.NewGuid(),
                "Example",
                "alice",
                TotalCount: 2,
                AddedCount: 2)));
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var runner = new Mock<IQrScannerRunner>();
        using var cameraScanner = new CameraScannerViewModel(
            runner.Object,
            decoder.Object,
            picker.Object,
            validator.Object,
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IUiScheduler>(),
            NullLogger<CameraScannerViewModel>.Instance,
            qrImport.Object,
            dialogs.Object,
            Localization());
        using var sut = Create(
            picker.Object,
            Mock.Of<IExportService>(),
            Mock.Of<IAccountManager>(),
            dialogs.Object,
            transientMessageDuration: TimeSpan.FromMilliseconds(20),
            cameraScanner: cameraScanner);

        await sut.ImportGoogleQrAsync();

        picker.Verify(value => value.PickQrImageAsync(It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(value => value.RunAsync(
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<byte[]>>(),
            It.IsAny<Action>(),
            It.IsAny<Action>()), Times.Never);
        Assert.Equal(NotificationSeverity.Success, sut.MessageSeverity);
        Assert.Contains("2", sut.Message, StringComparison.Ordinal);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task ImportAsync_WhenNoFileIsSelected_ShowsOneShortInformationNotice()
    {
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickImportFileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((INativeStorageFile?)null);
        using var sut = Create(
            picker.Object,
            Mock.Of<IExportService>(),
            Mock.Of<IAccountManager>(),
            Mock.Of<IAvaloniaDialogService>(),
            transientMessageDuration: TimeSpan.FromMilliseconds(20));

        await sut.ImportAsync();

        Assert.Equal("No import file selected.", sut.Message);
        Assert.Equal(NotificationSeverity.Information, sut.MessageSeverity);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task ExportEncryptedAsync_WhenPasswordPromptIsCancelled_ShowsOneShortInformationNotice()
    {
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.PromptForPasswordAsync(
                It.IsAny<PasswordDialogRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        using var sut = Create(
            Mock.Of<IAvaloniaFilePicker>(),
            Mock.Of<IExportService>(),
            accounts.Object,
            dialogs.Object,
            transientMessageDuration: TimeSpan.FromMilliseconds(20));

        await sut.ExportEncryptedAsync();

        Assert.Equal("Export cancelled.", sut.Message);
        Assert.Equal(NotificationSeverity.Information, sut.MessageSeverity);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task ImportAsync_WhenValidatedAndConfirmed_CreatesBackupBeforeAddingAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickImportFileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestStorageFile("backup.json"));
        var export = new Mock<IExportService>();
        var imported = new Account(Guid.NewGuid(), "GitHub", "JBSWY3DPEHPK3PXP", "user");
        export.Setup(value => value.ImportFromStreamAsync(
                It.IsAny<Stream>(), "backup.json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new List<Account> { imported }));
        var accounts = new Mock<IAccountManager>(MockBehavior.Strict);
        var sequence = new MockSequence();
        accounts.InSequence(sequence).Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        accounts.InSequence(sequence).Setup(value => value.BackupOtpEntriesStorageFileAsync())
            .ReturnsAsync(Result.Ok());
        accounts.InSequence(sequence).Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .ReturnsAsync(Result.Ok());
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = Create(picker.Object, export.Object, accounts.Object, dialogs.Object);
        var changed = 0;
        sut.AccountsChanged += (_, _) => changed++;

        await sut.ImportAsync();

        Assert.Contains("1 added", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, changed);
        accounts.Verify(value => value.AddNewAsync(It.Is<Account>(account =>
            account.Secret == "JBSWY3DPEHPK3PXP")), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WhenEveryAccountExists_ShowsInformationOnlyWithoutImportConfirmation()
    {
        var existing = Enumerable.Range(1, 22)
            .Select(index => new Account(
                Guid.NewGuid(),
                "Issuer",
                "JBSWY3DPEHPK3PXP",
                $"user-{index}"))
            .ToList();
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickImportFileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestStorageFile("backup.json"));
        var export = new Mock<IExportService>();
        export.Setup(value => value.ImportFromStreamAsync(
                It.IsAny<Stream>(), "backup.json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(existing));
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(existing));
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ShowMessageAsync(
                It.IsAny<MessageDialogRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var sut = Create(picker.Object, export.Object, accounts.Object, dialogs.Object);

        await sut.ImportAsync();

        dialogs.Verify(value => value.ShowMessageAsync(
            It.Is<MessageDialogRequest>(request =>
                request.Severity == NotificationSeverity.Information
                && request.CloseText == "OK"
                && request.Message.Contains("Accounts received: 22", StringComparison.Ordinal)
                && request.Message.Contains("Existing accounts skipped: 22", StringComparison.Ordinal)
                && request.Message.Contains("nothing to import", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<CancellationToken>()), Times.Once);
        dialogs.Verify(value => value.ConfirmAsync(
            It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task ImportAsync_WhenSkipExistingContainsExistingAndNewAccounts_ExplainsBothCounts()
    {
        var existing = Enumerable.Range(1, 5)
            .Select(index => new Account(
                Guid.NewGuid(),
                "Issuer",
                "JBSWY3DPEHPK3PXP",
                $"existing-{index}"))
            .ToList();
        var newAccounts = Enumerable.Range(1, 12)
            .Select(index => new Account(
                Guid.NewGuid(),
                "Issuer",
                "JBSWY3DPEHPK3PXP",
                $"new-{index}"))
            .ToList();
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickImportFileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestStorageFile("backup.json"));
        var export = new Mock<IExportService>();
        export.Setup(value => value.ImportFromStreamAsync(
                It.IsAny<Stream>(), "backup.json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(existing.Concat(newAccounts).ToList()));
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(existing));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.AddNewAsync(It.IsAny<Account>())).ReturnsAsync(Result.Ok());
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var sut = Create(picker.Object, export.Object, accounts.Object, dialogs.Object);

        await sut.ImportAsync();

        dialogs.Verify(value => value.ConfirmAsync(
            It.Is<ConfirmationDialogRequest>(request =>
                request.Message.Contains("Existing accounts skipped: 5", StringComparison.Ordinal)
                && request.Message.Contains("New accounts to import: 12", StringComparison.Ordinal)
                && !request.Message.Contains("?", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Exactly(12));
        Assert.Contains("12 added", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5 skipped", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_WhenBackupFails_DoesNotMutateAccounts()
    {
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickImportFileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestStorageFile("backup.json"));
        var export = new Mock<IExportService>();
        export.Setup(value => value.ImportFromStreamAsync(
                It.IsAny<Stream>(), "backup.json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new List<Account>
            {
                new(Guid.NewGuid(), "GitHub", "JBSWY3DPEHPK3PXP", "user")
            }));
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync())
            .ReturnsAsync(Result.Fail("backup unavailable"));
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = Create(picker.Object, export.Object, accounts.Object, dialogs.Object);

        await sut.ImportAsync();

        Assert.Contains("stopped", sut.Message, StringComparison.OrdinalIgnoreCase);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WithReplaceStrategy_PreservesExistingIdentifier()
    {
        var id = Guid.NewGuid();
        var existing = new Account(id, "GitHub", "JBSWY3DPEHPK3PXP", "user");
        var incoming = new Account(Guid.NewGuid(), "GitHub", "KRSXG5DSNFXGOIDB", "user");
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickImportFileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestStorageFile("backup.json"));
        var export = new Mock<IExportService>();
        export.Setup(value => value.ImportFromStreamAsync(
                It.IsAny<Stream>(), "backup.json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new List<Account> { incoming }));
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.UpdateAsync(existing, It.IsAny<Account>())).ReturnsAsync(Result.Ok());
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = Create(picker.Object, export.Object, accounts.Object, dialogs.Object);
        sut.ConflictStrategy = ImportConflictStrategy.ReplaceExisting;

        await sut.ImportAsync();

        accounts.Verify(value => value.UpdateAsync(existing, It.Is<Account>(replacement =>
            replacement.ID == id && replacement.Secret == incoming.Secret)), Times.Once);
        Assert.Contains("1 replaced", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportEncryptedAsync_RequiresConfirmedPasswordAndHardensLocalFile()
    {
        var file = new TestStorageFile("backup.totp", "C:\\safe\\backup.totp");
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickEncryptedExportFileAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);
        var export = new Mock<IExportService>();
        export.Setup(value => value.ExportToEncryptedStreamAsync(
                It.IsAny<IEnumerable<Account>>(), "strong-password", It.IsAny<Stream>(),
                ExportFileFormat.Json, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [new Account(Guid.NewGuid(), "GitHub", "JBSWY3DPEHPK3PXP", "user")]));
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.PromptForPasswordAsync(
                It.Is<PasswordDialogRequest>(request => request.RequireConfirmation),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("strong-password");
        var security = new Mock<IPlatformFileSecurity>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings
        {
            OpenExportFileAfterExport = true
        });
        var folderLauncher = new Mock<IPlatformFolderLauncher>();
        folderLauncher.Setup(value => value.OpenFolderAsync(
                "C:\\safe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var sut = Create(
            picker.Object,
            export.Object,
            accounts.Object,
            dialogs.Object,
            security.Object,
            settings.Object,
            folderLauncher.Object,
            transientMessageDuration: TimeSpan.FromMilliseconds(20));

        await sut.ExportEncryptedAsync();

        Assert.Contains("successfully", sut.Message, StringComparison.OrdinalIgnoreCase);
        security.Verify(value => value.RestrictFileToCurrentUser("C:\\safe\\backup.totp"), Times.Once);
        folderLauncher.Verify(value => value.OpenFolderAsync(
            "C:\\safe", It.IsAny<CancellationToken>()), Times.Once);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task ImportAsync_WhenBoundaryThrows_DoesNotExposeExceptionText()
    {
        var picker = new Mock<IAvaloniaFilePicker>();
        picker.Setup(value => value.PickImportFileAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive local path"));
        var sut = Create(
            picker.Object,
            Mock.Of<IExportService>(),
            Mock.Of<IAccountManager>(),
            Mock.Of<IAvaloniaDialogService>());

        await sut.ImportAsync();

        Assert.DoesNotContain("sensitive", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static NativeFilePickerViewModel Create(
        IAvaloniaFilePicker picker,
        IExportService export,
        IAccountManager accounts,
        IAvaloniaDialogService dialogs,
        IPlatformFileSecurity? security = null,
        ISettingsService? settings = null,
        IPlatformFolderLauncher? folderLauncher = null,
        TimeSpan? transientMessageDuration = null,
        CameraScannerViewModel? cameraScanner = null)
    {
        var passwordValidation = new Mock<IPasswordValidationService>();
        passwordValidation.SetupGet(value => value.MinimumLength).Returns(8);
        passwordValidation.Setup(value => value.IsValidNew(It.IsAny<string>())).Returns(true);
        if (settings is null)
        {
            var settingsMock = new Mock<ISettingsService>();
            settingsMock.SetupGet(value => value.Current).Returns(new AppSettings
            {
                OpenExportFileAfterExport = false
            });
            settings = settingsMock.Object;
        }
        return new NativeFilePickerViewModel(
            picker,
            export,
            accounts,
            new AccountImportService(accounts),
            dialogs,
            passwordValidation.Object,
            security ?? Mock.Of<IPlatformFileSecurity>(),
            settings,
            folderLauncher ?? Mock.Of<IPlatformFolderLauncher>(),
            Localization(),
            transientMessageDuration,
            cameraScanner);
    }

    private static IAvaloniaLocalizationService Localization()
    {
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture("en");
        return localization;
    }

    private sealed class TestStorageFile(
        string name,
        string? localPath = null,
        byte[]? content = null) : INativeStorageFile
    {
        private readonly byte[] _content = content ?? [];
        public string Name { get; } = name;
        public string? LocalPath { get; } = localPath;

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(_content, writable: false));

        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
