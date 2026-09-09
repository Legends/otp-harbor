using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Models;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using FluentResults;
using TOTP.Core.Security.Models;
using TOTP.Core.Security;
using TOTP.Core.Enums;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;
using Avalonia.Controls;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task ClosingSettingsAfterImport_AwaitsAccountListRefresh()
    {
        var imported = new Account(Guid.NewGuid(), "Imported issuer", "JBSWY3DPEHPK3PXP", "Imported account");
        var accountReads = 0;
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(
                ++accountReads < 3 ? [] : [imported]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.AddNewAsync(It.IsAny<Account>())).ReturnsAsync(Result.Ok());

        var nativePicker = new Mock<IAvaloniaFilePicker>();
        nativePicker.Setup(value => value.PickImportFileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestStorageFile("backup.json"));
        var export = new Mock<IExportService>();
        export.Setup(value => value.ImportFromStreamAsync(
                It.IsAny<Stream>(), "backup.json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new List<Account> { imported }));
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var validation = new Mock<IPasswordValidationService>();
        validation.SetupGet(value => value.MinimumLength).Returns(8);
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        var filePicker = new NativeFilePickerViewModel(
            nativePicker.Object,
            export.Object,
            accounts.Object,
            new AccountImportService(accounts.Object),
            dialogs.Object,
            validation.Object,
            Mock.Of<IPlatformFileSecurity>(),
            settings.Object,
            Mock.Of<IPlatformFolderLauncher>(),
            CreateLocalization());
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        using var sut = CreateSut(
            coordinator.Object,
            Mock.Of<IAuthorizationService>(),
            accountManager: accounts.Object,
            nativeFilePicker: filePicker);

        await sut.InitializeAsync();
        await sut.ShowSettingsAsync();
        await filePicker.ImportAsync();

        Assert.Empty(sut.AccountList.Accounts);

        await sut.CloseSettingsAsync();

        Assert.Equal(imported.ID, Assert.Single(sut.AccountList.Accounts).Id);
        Assert.False(sut.IsSettingsVisible);
    }

    [Theory]
    [InlineData(AvaloniaStartupOutcome.ReadyForPasswordSetup, false, "StartupCreateMasterPassword")]
    [InlineData(AvaloniaStartupOutcome.ReadyForUnlock, false, "StartupEnterMasterPassword")]
    [InlineData(AvaloniaStartupOutcome.ReadyForPasswordFallback, false, "QuickUnlockFallback")]
    [InlineData(AvaloniaStartupOutcome.ReadyUnlocked, false, "VaultUnlocked")]
    [InlineData(AvaloniaStartupOutcome.PreferencesUnavailable, true, "StartupPreferencesUnavailable")]
    [InlineData(AvaloniaStartupOutcome.UnexpectedFailure, true, "StartupFailedSafely")]
    public async Task InitializeAsync_ProjectsSafeRecoverableState(
        AvaloniaStartupOutcome outcome,
        bool canRetry,
        string expectedText)
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        using var sut = CreateSut(coordinator.Object);

        await sut.InitializeAsync();

        Assert.False(sut.IsBusy);
        Assert.Equal(canRetry, sut.CanRetry);
        Assert.Contains(expectedText, sut.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            outcome is AvaloniaStartupOutcome.ReadyForUnlock or AvaloniaStartupOutcome.ReadyForPasswordFallback,
            sut.IsPasswordUnlockVisible);
        Assert.Equal(outcome == AvaloniaStartupOutcome.ReadyForPasswordSetup, sut.IsPasswordSetupVisible);
        Assert.Equal(outcome == AvaloniaStartupOutcome.ReadyUnlocked, sut.IsShellVisible);
        Assert.Equal(
            outcome switch
            {
                AvaloniaStartupOutcome.PreferencesUnavailable => NotificationSeverity.Warning,
                AvaloniaStartupOutcome.UnexpectedFailure => NotificationSeverity.Error,
                AvaloniaStartupOutcome.ReadyForPasswordFallback => NotificationSeverity.Warning,
                AvaloniaStartupOutcome.ReadyUnlocked => NotificationSeverity.Success,
                _ => NotificationSeverity.Information
            },
            sut.StatusSeverity);
    }

    [Fact]
    public async Task InitializeAsync_WhenSignedUpdateIsAvailable_NotifiesAuthorizedUserWithoutDownloading()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var offer = new PortableUpdateOffer(
            new Version(2, 1, 0),
            new Uri("https://example.invalid/otp-harbor.zip"),
            Convert.ToBase64String(new byte[64]),
            "Synthetic release notes");
        var updates = new Mock<IPortableUpdateService>();
        updates.Setup(value => value.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new PortableUpdateCheckResult(
                PortableUpdateCheckStatus.UpdateAvailable,
                offer)));
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture("de");
        using var updateCheck = new UpdateCheckViewModel(
            updates.Object,
            Mock.Of<IUpdateInstallerLauncher>(),
            localization);
        using var sut = CreateSut(
            coordinator.Object,
            Mock.Of<IAuthorizationService>(),
            localization: localization,
            updateCheck: updateCheck);

        await sut.InitializeAsync();

        updates.Verify(value => value.CheckAsync(It.IsAny<CancellationToken>()), Times.Once);
        updates.Verify(value => value.DownloadAsync(
            It.IsAny<PortableUpdateOffer>(),
            It.IsAny<IProgress<PortableUpdateDownloadProgress>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(
            "Das signierte Update 2.1.0 ist verfügbar. Öffnen Sie Einstellungen → Über, um es zu prüfen.",
            sut.StatusText);
        Assert.Equal(NotificationSeverity.Success, sut.StatusSeverity);
    }

    [Fact]
    public async Task InitializeAsync_WhenCoordinatorContractThrows_RemainsRecoverable()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive synthetic detail"));
        using var sut = CreateSut(coordinator.Object);

        await sut.InitializeAsync();

        Assert.True(sut.CanRetry);
        Assert.Equal(NotificationSeverity.Error, sut.StatusSeverity);
        Assert.DoesNotContain("sensitive", sut.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_WhenGermanIsActive_UsesCompleteLocalizedFailureMessage()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.UnexpectedFailure);
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture("de");
        using var sut = CreateSut(
            coordinator.Object,
            Mock.Of<IAuthorizationService>(),
            localization: localization);

        await sut.InitializeAsync();

        Assert.Equal(
            "OTP Harbor konnte nicht sicher gestartet werden. Ihre verschlüsselten Daten wurden nicht geändert.",
            sut.StatusText);
    }

    [Fact]
    public async Task LockAsync_WhenPasswordPreferred_ReturnsToPasswordGate()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var authorization = new Mock<IAuthorizationService>();
        var state = CreateAuthorizationState(PreferredUnlockMethod.Password);
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        using var sut = CreateSut(coordinator.Object, authorization.Object);
        await sut.InitializeAsync();

        await sut.LockAsync();

        authorization.Verify(value => value.Lock(), Times.Once);
        Assert.True(sut.IsPasswordUnlockVisible);
        Assert.False(sut.IsQuickUnlockVisible);
        Assert.False(sut.IsAccountListVisible);
        Assert.Contains("locked", sut.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NotificationSeverity.Information, sut.StatusSeverity);
    }

    [Fact]
    public async Task LockAsync_WhenQuickUnlockPreferred_ReturnsToQuickUnlockGate()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var authorization = new Mock<IAuthorizationService>();
        var state = CreateAuthorizationState(PreferredUnlockMethod.PlatformQuickUnlock);
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        using var sut = CreateSut(coordinator.Object, authorization.Object);
        await sut.InitializeAsync();

        await sut.LockAsync();

        Assert.True(sut.IsQuickUnlockVisible);
        Assert.False(sut.IsPasswordUnlockVisible);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, state.PreferredUnlockMethod);
    }

    [Fact]
    public async Task IdlePolicy_WhenTimeoutReached_ReturnsAuthorizedShellToLockGate()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var authorization = new Mock<IAuthorizationService>();
        var state = CreateAuthorizationState(PreferredUnlockMethod.Password);
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings
        {
            IdleTimeout = TimeSpan.FromMinutes(10)
        });
        var time = new ManualTimeProvider();
        var idlePolicy = new IdleMonitoringBackgroundService(
            authorization.Object,
            settings.Object,
            NullLogger<IdleMonitoringBackgroundService>.Instance,
            time);
        using var sut = CreateSut(
            coordinator.Object,
            authorization.Object,
            idleLockPolicy: idlePolicy);
        await sut.InitializeAsync();

        idlePolicy.EvaluateIdlePolicy();
        time.Advance(TimeSpan.FromMinutes(10));
        idlePolicy.EvaluateIdlePolicy();

        Assert.False(sut.IsShellVisible);
        Assert.False(sut.IsAccountListVisible);
        Assert.True(sut.IsPasswordUnlockVisible);
        authorization.Verify(value => value.Lock(), Times.Once);
    }

    [Fact]
    public async Task TryQuickUnlockAsync_WhenAuthorized_ReentersShell()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var authorization = new Mock<IAuthorizationService>();
        var state = CreateAuthorizationState(PreferredUnlockMethod.PlatformQuickUnlock);
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        authorization.Setup(value => value.TryUnlockWithHelloAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                state.Unlock();
                return Task.FromResult(AuthorizationResult.Success);
            });
        using var sut = CreateSut(coordinator.Object, authorization.Object);
        await sut.InitializeAsync();
        await sut.LockAsync();

        await sut.TryQuickUnlockAsync();

        Assert.True(sut.IsShellVisible);
        Assert.True(sut.IsAccountListVisible);
        Assert.False(sut.IsQuickUnlockVisible);
        Assert.False(sut.IsPasswordUnlockVisible);
    }

    [Fact]
    public async Task TryQuickUnlockAsync_WhenCancelled_RemainsAtQuickUnlockGate()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var authorization = new Mock<IAuthorizationService>();
        var state = CreateAuthorizationState(PreferredUnlockMethod.PlatformQuickUnlock);
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        authorization.Setup(value => value.TryUnlockWithHelloAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.Cancelled);
        using var sut = CreateSut(coordinator.Object, authorization.Object);
        await sut.InitializeAsync();
        await sut.LockAsync();

        await sut.TryQuickUnlockAsync();

        Assert.True(sut.IsQuickUnlockVisible);
        Assert.False(sut.IsPasswordUnlockVisible);
        Assert.Contains(AvaloniaStringKeys.QuickUnlockCancelled, sut.QuickUnlockMessage);
    }

    [Fact]
    public async Task TryQuickUnlockAsync_WhenPasswordIsRequired_ShowsRecoveryGate()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var authorization = new Mock<IAuthorizationService>();
        var state = CreateAuthorizationState(PreferredUnlockMethod.PlatformQuickUnlock);
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        authorization.Setup(value => value.TryUnlockWithHelloAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.PasswordRequired);
        using var sut = CreateSut(coordinator.Object, authorization.Object);
        await sut.InitializeAsync();
        await sut.LockAsync();

        await sut.TryQuickUnlockAsync();

        Assert.False(sut.IsQuickUnlockVisible);
        Assert.True(sut.IsPasswordUnlockVisible);
        Assert.False(sut.IsShellVisible);
    }

    [Fact]
    public async Task UsePasswordFallbackAsync_DoesNotChangeQuickUnlockPreference()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var authorization = new Mock<IAuthorizationService>();
        var state = CreateAuthorizationState(PreferredUnlockMethod.PlatformQuickUnlock);
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        using var sut = CreateSut(coordinator.Object, authorization.Object);
        await sut.InitializeAsync();
        await sut.LockAsync();

        await sut.UsePasswordFallbackAsync();

        Assert.False(sut.IsQuickUnlockVisible);
        Assert.True(sut.IsPasswordUnlockVisible);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, state.PreferredUnlockMethod);
    }

    [Fact]
    public void PrepareForShutdown_LocksOnceAndHidesAuthorizedSurfaces()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        var authorization = new Mock<IAuthorizationService>();
        using var accounts = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            Mock.Of<IAccountTotpService>(),
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            CreateLocalization());
        using var sut = new MainWindowViewModel(
            coordinator.Object,
            authorization.Object,
            new PasswordUnlockViewModel(authorization.Object, CreateLocalization()),
            CreatePasswordSetup(authorization.Object),
            accounts,
            CreateSettingsPage(),
            CreateAuthorizationSettings(authorization.Object),
            CreateFilePicker(),
            CreateCameraScanner(),
            CreateUpdateCheck(),
            CreateDiagnostics(),
            CreateLocalization(),
            Mock.Of<IAvaloniaCameraScannerDialogService>());

        sut.PrepareForShutdown();
        sut.PrepareForShutdown();

        authorization.Verify(value => value.Lock(), Times.Once);
        Assert.False(sut.IsPasswordUnlockVisible);
        Assert.False(sut.IsAccountListVisible);
        Assert.False(sut.IsSettingsVisible);
        Assert.Contains("closing", sut.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthorizedNavigation_PreservesActivePageBehindModalSettingsWindow()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyForUnlock);
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(value => value.TryUnlockWithPasswordAsync("test-password"))
            .ReturnsAsync(AuthorizationResult.Success);
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var password = new PasswordUnlockViewModel(authorization.Object, CreateLocalization());
        using var accounts = new AccountListViewModel(
            manager.Object,
            Mock.Of<IAccountTotpService>(),
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            CreateLocalization());
        using var sut = new MainWindowViewModel(
            coordinator.Object,
            authorization.Object,
            password,
            CreatePasswordSetup(authorization.Object),
            accounts,
            CreateSettingsPage(),
            CreateAuthorizationSettings(authorization.Object),
            CreateFilePicker(),
            CreateCameraScanner(),
            CreateUpdateCheck(),
            CreateDiagnostics(),
            CreateLocalization(),
            Mock.Of<IAvaloniaCameraScannerDialogService>());
        await sut.InitializeAsync();
        password.Password = "test-password";

        await password.UnlockAsync();

        Assert.True(sut.IsShellVisible);
        Assert.True(sut.IsAccountListVisible);
        Assert.False(sut.IsToolsVisible);
        Assert.False(sut.IsSettingsVisible);

        await sut.ShowToolsAsync();
        Assert.False(sut.IsAccountListVisible);
        Assert.True(sut.IsToolsVisible);
        Assert.False(sut.IsSettingsVisible);

        await sut.ShowSettingsAsync();
        Assert.False(sut.IsAccountListVisible);
        Assert.True(sut.IsToolsVisible);
        Assert.True(sut.IsSettingsVisible);
        Assert.True(sut.CloseSettingsCommand.CanExecute(null));
        Assert.False(sut.LockCommand.CanExecute(null));
        Assert.False(sut.ToggleSearchCommand.CanExecute(null));
        Assert.False(sut.BeginAddAccountCommand.CanExecute(null));

        await sut.CloseSettingsAsync();
        Assert.False(sut.IsAccountListVisible);
        Assert.True(sut.IsToolsVisible);
        Assert.False(sut.IsSettingsVisible);
        Assert.True(sut.LockCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("security-authorization")]
    [InlineData("security-and-miscellaneous-settings")]
    [InlineData("import-export")]
    [InlineData("about-log-folder")]
    [InlineData("about-updates")]
    [InlineData("about-diagnostics")]
    public void SettingsNotifications_FromEveryTabUseTheWindowOverlay(string sourceName)
    {
        using var sut = CreateSut(Mock.Of<IAvaloniaStartupCoordinator>());
        var source = sourceName switch
        {
            "security-authorization" => sut.AuthorizationSettings.Notification,
            "security-and-miscellaneous-settings" => sut.SettingsPage.SettingsNotification,
            "import-export" => sut.NativeFilePicker.Notification,
            "about-log-folder" => sut.SettingsPage.LogFolderNotification,
            "about-updates" => sut.UpdateCheck.Notification,
            "about-diagnostics" => sut.Diagnostics.Notification,
            _ => throw new ArgumentOutOfRangeException(nameof(sourceName))
        };

        source.ShowPersistent("Synthetic settings notice", NotificationSeverity.Error);

        Assert.Equal("Synthetic settings notice", sut.SettingsNotification.Text);
        Assert.Equal(NotificationSeverity.Error, sut.SettingsNotification.Severity);
    }

    [Fact]
    public async Task SettingsWindowOverlay_AlwaysDismissesRecoverableErrors()
    {
        using var sut = CreateSut(
            Mock.Of<IAvaloniaStartupCoordinator>(),
            Mock.Of<IAuthorizationService>(),
            settingsNotificationDuration: TimeSpan.FromMilliseconds(20));

        sut.Diagnostics.Notification.ShowPersistent(
            "Synthetic recoverable error",
            NotificationSeverity.Error);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(sut.SettingsNotification.Text);
        Assert.False(sut.SettingsNotification.HasMessage);
    }

    [Fact]
    public async Task ToolbarSearch_TogglesClearsAndReturnsToAccounts()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        using var sut = CreateSut(coordinator.Object);
        await sut.InitializeAsync();

        await sut.ToggleSearchAsync();
        Assert.True(sut.IsSearchVisible);
        sut.AccountList.SearchText = "github";

        await sut.ToggleSearchAsync();
        Assert.False(sut.IsSearchVisible);
        Assert.Empty(sut.AccountList.SearchText);

        await sut.ShowToolsAsync();
        await sut.ToggleSearchAsync();
        Assert.True(sut.IsAccountListVisible);
        Assert.False(sut.IsToolsVisible);
        Assert.True(sut.IsSearchVisible);
    }

    [Fact]
    public async Task ScanQrAsync_WhenUnlocked_OpensScannerDialogDirectly()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var scannerDialogs = new Mock<IAvaloniaCameraScannerDialogService>();
        using var sut = CreateSut(
            coordinator.Object,
            Mock.Of<IAuthorizationService>(),
            scannerDialogs.Object);
        await sut.InitializeAsync();

        await sut.ScanQrAsync();

        scannerDialogs.Verify(value => value.ShowAsync(
            sut.CameraScanner,
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(sut.IsAccountListVisible);
        Assert.False(sut.IsToolsVisible);
    }

    [Fact]
    public async Task HandleWindowMinimizedAsync_WhenPolicyEnabled_LocksAuthorizedShell()
    {
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var authorization = new Mock<IAuthorizationService>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings
        {
            LockOnMinimize = true
        });
        using var sut = new MainWindowViewModel(
            coordinator.Object,
            authorization.Object,
            new PasswordUnlockViewModel(authorization.Object, CreateLocalization()),
            CreatePasswordSetup(authorization.Object),
            new AccountListViewModel(
                Mock.Of<IAccountManager>(),
                Mock.Of<IAccountTotpService>(),
                Mock.Of<IAsyncClipboardService>(),
                Mock.Of<IAccountQrCodeService>(),
                Mock.Of<IAvaloniaQrImageFactory>(),
                Mock.Of<IAvaloniaDialogService>(),
                CreateLocalization()),
            CreateSettingsPage(),
            CreateAuthorizationSettings(authorization.Object),
            CreateFilePicker(),
            CreateCameraScanner(),
            CreateUpdateCheck(),
            CreateDiagnostics(),
            CreateLocalization(),
            Mock.Of<IAvaloniaCameraScannerDialogService>(),
            settings.Object);
        await sut.InitializeAsync();

        await sut.HandleWindowMinimizedAsync();

        authorization.Verify(value => value.Lock(), Times.Once);
        Assert.True(sut.IsPasswordUnlockVisible);
        Assert.False(sut.IsShellVisible);
    }

    private static MainWindowViewModel CreateSut(IAvaloniaStartupCoordinator coordinator) =>
        CreateSut(coordinator, Mock.Of<IAuthorizationService>());

    private static MainWindowViewModel CreateSut(
        IAvaloniaStartupCoordinator coordinator,
        IAuthorizationService authorization,
        IAvaloniaCameraScannerDialogService? scannerDialogs = null,
        IAccountManager? accountManager = null,
        NativeFilePickerViewModel? nativeFilePicker = null,
        IAvaloniaLocalizationService? localization = null,
        IdleMonitoringBackgroundService? idleLockPolicy = null,
        UpdateCheckViewModel? updateCheck = null,
        TimeSpan? settingsNotificationDuration = null) =>
        new(
            coordinator,
            authorization,
            new PasswordUnlockViewModel(authorization, CreateLocalization()),
            CreatePasswordSetup(authorization),
            new AccountListViewModel(
                accountManager ?? Mock.Of<IAccountManager>(),
                Mock.Of<IAccountTotpService>(),
                Mock.Of<IAsyncClipboardService>(),
                Mock.Of<IAccountQrCodeService>(),
                Mock.Of<IAvaloniaQrImageFactory>(),
                Mock.Of<IAvaloniaDialogService>(),
                CreateLocalization()),
            CreateSettingsPage(),
            CreateAuthorizationSettings(authorization),
            nativeFilePicker ?? CreateFilePicker(),
            CreateCameraScanner(),
            updateCheck ?? CreateUpdateCheck(),
            CreateDiagnostics(),
            localization ?? CreateLocalization(),
            scannerDialogs ?? Mock.Of<IAvaloniaCameraScannerDialogService>(),
            idleLockPolicy: idleLockPolicy,
            settingsNotificationDuration: settingsNotificationDuration);

    private sealed class TestStorageFile(string name) : INativeStorageFile
    {
        public string Name { get; } = name;
        public string? LocalPath => null;
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());
        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    private static AuthorizationState CreateAuthorizationState(PreferredUnlockMethod preference)
    {
        var state = new AuthorizationState();
        state.SetConfiguration(true, preference);
        state.Unlock();
        return state;
    }

    private static SettingsPageViewModel CreateSettingsPage()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        return new SettingsPageViewModel(settings.Object);
    }

    private static AuthorizationSettingsViewModel CreateAuthorizationSettings(
        IAuthorizationService authorization)
    {
        var validation = new Mock<IPasswordValidationService>();
        validation.SetupGet(value => value.MinimumLength).Returns(8);
        return new AuthorizationSettingsViewModel(
            authorization,
            Mock.Of<IAvaloniaDialogService>(),
            CreateLocalization(),
            validation.Object);
    }

    private static NativeFilePickerViewModel CreateFilePicker()
    {
        var validation = new Mock<IPasswordValidationService>();
        validation.SetupGet(value => value.MinimumLength).Returns(8);
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        return new NativeFilePickerViewModel(
            Mock.Of<IAvaloniaFilePicker>(),
            Mock.Of<IExportService>(),
            Mock.Of<IAccountManager>(),
            Mock.Of<IAccountImportService>(),
            Mock.Of<IAvaloniaDialogService>(),
            validation.Object,
            Mock.Of<IPlatformFileSecurity>(),
            settings.Object,
            Mock.Of<IPlatformFolderLauncher>(),
            CreateLocalization());
    }

    private static CameraScannerViewModel CreateCameraScanner() =>
        new(
            Mock.Of<IQrScannerRunner>(),
            Mock.Of<IQrImageDecoder>(),
            Mock.Of<IAvaloniaFilePicker>(),
            Mock.Of<IQrPayloadValidator>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IUiScheduler>(),
            NullLogger<CameraScannerViewModel>.Instance,
            Mock.Of<IQrAccountImportService>(),
            Mock.Of<IAvaloniaDialogService>(),
            CreateLocalization());

    private static UpdateCheckViewModel CreateUpdateCheck()
    {
        var updates = new Mock<IPortableUpdateService>();
        updates.Setup(value => value.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new PortableUpdateCheckResult(
                PortableUpdateCheckStatus.Disabled)));
        return new UpdateCheckViewModel(
            updates.Object,
            Mock.Of<IUpdateInstallerLauncher>(),
            CreateLocalization());
    }

    private static DiagnosticsViewModel CreateDiagnostics() =>
        new(Mock.Of<ISupportDiagnosticsService>(), CreateLocalization());

    private static PasswordSetupViewModel CreatePasswordSetup(IAuthorizationService authorization)
    {
        var validation = new Mock<IPasswordValidationService>();
        validation.SetupGet(value => value.MinimumLength).Returns(8);
        return new PasswordSetupViewModel(authorization, validation.Object, CreateLocalization());
    }

    private static IAvaloniaLocalizationService CreateLocalization()
    {
        var localization = new Mock<IAvaloniaLocalizationService>();
        localization.Setup(value => value.GetString(It.IsAny<string>()))
            .Returns((string key) => key);
        return localization.Object;
    }
}
