using System.Globalization;
using Avalonia.Media;
using FluentResults;
using Moq;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Core.Icons;
using TOTP.Core.Models;
using TOTP.Core.Enums;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Core.Validation;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobileShellViewModelTests
{
    private const string ValidSecret = "JBSWY3DPEHPK3PXP";

    [Fact]
    public async Task InitializeAsync_WhenNoEnvelopeExists_ShowsPasswordSetup()
    {
        var context = CreateContext(isConfigured: false);

        await context.Sut.InitializeAsync();

        Assert.True(context.Sut.IsSetupVisible);
        Assert.False(context.Sut.IsUnlockVisible);
        Assert.Empty(context.Sut.NotificationText);
    }

    [Fact]
    public async Task InitializeAsync_WhenAppLockIsDisabled_UnlocksThroughDeviceWrapper()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user@example.test");
        var context = CreateContext(
            isConfigured: true,
            accounts: [account],
            appLockEnabled: false,
            preferredUnlockMethod: TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);

        await context.Sut.InitializeAsync();

        Assert.True(context.Sut.IsAccountsVisible);
        Assert.False(context.Sut.IsManualLockVisible);
        Assert.Single(context.Sut.Accounts);
        context.Authorization.Verify(value => value.TryUnlockOnStartupAsync(), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WhenLoadedPreferenceDisablesAppLock_RefreshesToggleBindings()
    {
        var context = CreateContext(isConfigured: true);
        context.Settings.Setup(value => value.LoadAsync())
            .Callback(() =>
            {
                context.SettingsValue.AppLockEnabled = false;
                context.SettingsValue.PreferredUnlockMethod =
                    TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock;
            })
            .ReturnsAsync(Result.Ok<IAppSettings>(context.SettingsValue));
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        var changedProperties = new List<string?>();
        context.Sut.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        await context.Sut.InitializeAsync();
        await context.Sut.ShowSettingsAsync();

        Assert.False(context.Sut.IsAppLockEnabled);
        Assert.Equal("Activate app lock", context.Sut.AppLockActionText);
        Assert.True(context.Sut.ToggleAppLockCommand.CanExecute(null));
        Assert.Contains(nameof(MobileShellViewModel.IsAppLockEnabled), changedProperties);
        Assert.Contains(nameof(MobileShellViewModel.IsAppLockDisabled), changedProperties);
        Assert.Contains(nameof(MobileShellViewModel.AppLockActionText), changedProperties);
    }

    [Fact]
    public async Task OnEnteredBackground_WhenAppLockIsDisabled_PreservesAuthorization()
    {
        var context = CreateContext(isConfigured: true, appLockEnabled: false);
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();

        context.Sut.OnEnteredBackground(lockImmediately: true);
        context.Sut.OnReturnedToForeground();

        context.Authorization.Verify(value => value.Lock(), Times.Never);
        Assert.True(context.Sut.IsAccountsVisible);
    }

    [Fact]
    public async Task EnableAppLockAsync_FromDisabledSettings_ImmediatelyLocksAndClearsAccounts()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user@example.test");
        var context = CreateContext(
            isConfigured: true,
            accounts: [account],
            appLockEnabled: false,
            preferredUnlockMethod: TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.SetAppLockEnabledAsync(
                true,
                "recovery-password"))
            .Callback(() =>
            {
                context.SettingsValue.AppLockEnabled = true;
                context.State.SetConfiguration(
                    true,
                    TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
            })
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        await context.Sut.ShowSettingsAsync();

        Assert.Equal("Activate app lock", context.Sut.AppLockActionText);
        await context.Sut.ToggleAppLockAsync();
        Assert.True(context.Sut.IsBiometricEnrollmentVisible);
        context.Sut.BiometricRecoveryPassword = "recovery-password";
        await context.Sut.EnableBiometricAsync();

        Assert.True(context.Sut.IsAppLockEnabled);
        Assert.True(context.Sut.IsUnlockVisible);
        Assert.False(context.Sut.IsSettingsVisible);
        Assert.Empty(context.Sut.Accounts);
        Assert.False(context.Sut.EnableAppLockCommand.CanExecute(null));
        context.Authorization.Verify(value => value.Lock(), Times.Once);
    }

    [Fact]
    public async Task EnableAppLockAsync_WhenServiceReportsFailureAfterFailClosedState_StillLocks()
    {
        var context = CreateContext(isConfigured: true, appLockEnabled: false);
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.SetAppLockEnabledAsync(true, string.Empty))
            .Callback(() => context.SettingsValue.AppLockEnabled = true)
            .ReturnsAsync(AuthorizationResult.Failed);
        await context.Sut.InitializeAsync();
        await context.Sut.ShowSettingsAsync();

        await context.Sut.EnableAppLockAsync();

        Assert.True(context.Sut.IsAppLockEnabled);
        Assert.True(context.Sut.IsUnlockVisible);
        Assert.Empty(context.Sut.NotificationText);
        context.Authorization.Verify(value => value.Lock(), Times.Once);
    }

    [Fact]
    public async Task Settings_CategoryNavigationOpensDetailsAndReturnsToOverview()
    {
        var context = CreateContext(isConfigured: true, appLockEnabled: false);
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        await context.Sut.ShowSettingsAsync();

        Assert.True(context.Sut.IsSettingsCategoryListVisible);
        context.Sut.ShowBrandIconSettingsCommand.Execute(null);

        Assert.True(context.Sut.IsSettingsCategoryDetailVisible);
        Assert.True(context.Sut.IsBrandIconSettingsVisible);
        Assert.False(context.Sut.IsSettingsCategoryListVisible);

        await context.Sut.NavigateBackAsync();

        Assert.True(context.Sut.IsSettingsCategoryListVisible);
        Assert.False(context.Sut.IsSettingsCategoryDetailVisible);

        context.Sut.ShowFaqSettingsCommand.Execute(null);

        Assert.True(context.Sut.IsFaqSettingsVisible);
        Assert.False(string.IsNullOrWhiteSpace(context.Sut.FaqImportIconPacksAnswerText));

        context.Sut.ShowSettingsCategoriesCommand.Execute(null);
        context.Sut.ShowImportExportSettingsCommand.Execute(null);
        context.Sut.ShowImportFormatsFaqCommand.Execute(null);

        Assert.True(context.Sut.IsFaqSettingsVisible);
        Assert.Contains("otpauth://", context.Sut.FaqImportFormatsOtpAuthExampleText, StringComparison.Ordinal);

        context.Sut.ShowSettingsCategoriesCommand.Execute(null);
        context.Sut.ShowMiscSettingsCommand.Execute(null);

        Assert.True(context.Sut.IsMiscSettingsVisible);
        Assert.Equal(context.SettingsValue.MinimumLogLevel, context.Sut.MinimumLogLevel);
    }

    [Fact]
    public async Task BackNavigation_FromSettingsDetailReturnsToOverviewThenAccounts()
    {
        var context = CreateContext(isConfigured: true, appLockEnabled: false);
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        await context.Sut.ShowSettingsAsync();
        context.Sut.ShowAppearanceSettingsCommand.Execute(null);

        Assert.True(context.Sut.CanHandleSystemBack);
        Assert.True(await context.Sut.TryHandleBackNavigationAsync());
        Assert.True(context.Sut.IsSettingsCategoryListVisible);
        Assert.True(context.Sut.CanHandleSystemBack);

        Assert.True(await context.Sut.TryHandleBackNavigationAsync());
        Assert.False(context.Sut.IsSettingsVisible);
        Assert.True(context.Sut.IsAccountListVisible);
        Assert.False(context.Sut.CanHandleSystemBack);
    }

    [Fact]
    public async Task Settings_MinimumLogLevelPersistsAndRollsBackOnFailure()
    {
        var context = CreateContext(isConfigured: true, appLockEnabled: false);
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        await context.Sut.ShowSettingsAsync();

        await context.Sut.SelectMinimumLogLevelAsync(AppLogLevel.Warning);

        Assert.Equal(AppLogLevel.Warning, context.SettingsValue.MinimumLogLevel);
        context.Settings.Verify(value => value.SaveAsync(), Times.Once);

        context.Settings.Setup(value => value.SaveAsync())
            .ReturnsAsync(Result.Fail("simulated settings failure"));
        await context.Sut.SelectMinimumLogLevelAsync(AppLogLevel.Error);

        Assert.Equal(AppLogLevel.Warning, context.SettingsValue.MinimumLogLevel);
        Assert.Equal(AppLogLevel.Warning, context.Sut.MinimumLogLevel);
        Assert.NotEmpty(context.Sut.NotificationText);
    }

    [Fact]
    public async Task Settings_LanguagePickerProvidesSupportedLanguagesAndCurrentSelection()
    {
        var context = CreateContext(isConfigured: true, appLockEnabled: false, cultureName: "de");
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        await context.Sut.ShowSettingsAsync();

        Assert.Equal(["en", "de", "fr", "es"],
            context.Sut.Languages.Select(option => option.CultureName));
        Assert.All(context.Sut.Languages, option => Assert.False(
            string.IsNullOrWhiteSpace(option.DisplayName)));
        Assert.Equal("de", context.Sut.SelectedLanguage.CultureName);
    }

    [Fact]
    public async Task Security_ChangeMasterPasswordRotatesPasswordAndClearsInputs()
    {
        var context = CreateContext(isConfigured: true, appLockEnabled: false);
        context.Authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.ChangePasswordAsync(
                "current-password",
                "new-synthetic-password"))
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        await context.Sut.ShowSettingsAsync();
        context.Sut.ShowSecuritySettingsCommand.Execute(null);
        await WaitUntilAsync(() => context.Sut.IsSecuritySettingsVisible);
        context.Sut.CurrentMasterPassword = "current-password";
        context.Sut.NewMasterPassword = "new-synthetic-password";
        context.Sut.NewMasterPasswordConfirmation = "new-synthetic-password";

        await context.Sut.ChangeMasterPasswordAsync();

        context.Authorization.Verify(value => value.ChangePasswordAsync(
            "current-password",
            "new-synthetic-password"), Times.Once);
        Assert.Empty(context.Sut.CurrentMasterPassword);
        Assert.Empty(context.Sut.NewMasterPassword);
        Assert.Empty(context.Sut.NewMasterPasswordConfirmation);
        Assert.Equal("Master password changed.", context.Sut.NotificationText);
    }

    [Fact]
    public async Task DisableAppLockCommands_FromEnabledSettings_ConfirmWithRecoveryPassword()
    {
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.SetAppLockEnabledAsync(false, "recovery-password"))
            .Callback(() => context.SettingsValue.AppLockEnabled = false)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        Assert.True(context.Sut.IsAppLockEnabled);
        Assert.Equal("Deactivate app lock", context.Sut.AppLockActionText);
        Assert.True(context.Sut.ToggleAppLockCommand.CanExecute(null));
        Assert.True(context.Sut.BeginDisableAppLockCommand.CanExecute(null));

        context.Sut.ToggleAppLockCommand.Execute(null);
        await WaitUntilAsync(() => context.Sut.IsDisableAppLockConfirmationVisible);

        Assert.False(context.Sut.ConfirmDisableAppLockCommand.CanExecute(null));
        context.Sut.AppLockRecoveryPassword = "recovery-password";
        Assert.True(context.Sut.ConfirmDisableAppLockCommand.CanExecute(null));

        context.Sut.ConfirmDisableAppLockCommand.Execute(null);
        await WaitUntilAsync(() => !context.Sut.IsAppLockEnabled);

        Assert.False(context.Sut.IsDisableAppLockConfirmationVisible);
        Assert.True(context.Sut.IsAppLockDisabled);
        Assert.Equal("Activate app lock", context.Sut.AppLockActionText);
        Assert.True(context.Sut.EnableAppLockCommand.CanExecute(null));
        Assert.Empty(context.Sut.AppLockRecoveryPassword);
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(
            false,
            "recovery-password"), Times.Once);
    }

    [Fact]
    public async Task ImportGoogleQrAsync_FromSettings_ImportsWithoutAdditionalConfirmation()
    {
        const string payload = "otpauth-migration://offline?data=synthetic";
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.QrScanner.Setup(value => value.ScanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MobileQrScanResult.Successful(payload));
        context.QrPayloadValidator.Setup(value => value.Validate(payload))
            .Returns(new QrPayloadValidationResult(
                true,
                string.Empty,
                string.Empty,
                QrPayloadKind.GoogleAuthenticatorMigration,
                2));
        context.QrImport.Setup(value => value.ImportAsync(
                payload,
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.BulkImported,
                Guid.Empty,
                string.Empty,
                string.Empty,
                TotalCount: 2,
                AddedCount: 2)));
        await ConfigureAndBeginAddAsync(context);
        await context.Sut.CancelEditAsync();
        await context.Sut.ShowSettingsAsync();

        await context.Sut.ImportGoogleQrAsync();

        Assert.False(context.Sut.IsImportConfirmationVisible);
        context.QrImport.Verify(value => value.ImportAsync(
            payload,
            It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfigureAsync_WhenSuccessful_OpensEmptyEncryptedVault()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync("synthetic password", "synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.SetupPassword = "synthetic password";
        context.Sut.SetupConfirmation = "synthetic password";

        await context.Sut.ConfigureAsync();

        Assert.True(context.Sut.IsAccountsVisible);
        Assert.True(context.Sut.HasNoAccounts);
        Assert.Empty(context.Sut.SetupPassword);
        Assert.Empty(context.Sut.SetupConfirmation);
        context.Authorization.Verify(
            value => value.ConfigureHelloAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfigureAsync_ErrorNotification_ClearsAfterTwoSeconds()
    {
        var context = CreateContext(isConfigured: false);
        await context.Sut.InitializeAsync();

        await context.Sut.ConfigureAsync();

        Assert.Equal(
            context.Strings.Get(MobileStringKeys.PasswordRequired),
            context.Sut.NotificationText);
        await Task.Delay(
            TimeSpan.FromMilliseconds(1100),
            global::Xunit.TestContext.Current.CancellationToken);
        Assert.NotEmpty(context.Sut.NotificationText);
        await Task.Delay(
            TimeSpan.FromMilliseconds(1000),
            global::Xunit.TestContext.Current.CancellationToken);
        Assert.Empty(context.Sut.NotificationText);
    }

    [Fact]
    public async Task ConfigureAsync_WithAvailableBiometrics_ImmediatelyEnrollsQuickUnlock()
    {
        var context = CreateContext(isConfigured: false, biometricAvailable: true);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync("synthetic password", "synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.ConfigureHelloAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.SetupPassword = "synthetic password";
        context.Sut.SetupConfirmation = "synthetic password";

        await context.Sut.ConfigureAsync();

        Assert.True(context.Sut.IsAccountsVisible);
        Assert.True(context.Sut.IsBiometricEnabled);
        Assert.Empty(context.Sut.SetupPassword);
        Assert.Empty(context.Sut.SetupConfirmation);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.BiometricEnabled),
            context.Sut.NotificationText);
        context.Authorization.Verify(
            value => value.ConfigureHelloAsync("synthetic password"),
            Times.Once);
    }

    [Fact]
    public async Task ConfigureAsync_WhenBiometricsBecomeAvailableAfterSetup_ImmediatelyEnrollsQuickUnlock()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .SetupSequence(value => value.IsHelloAvailableAsync())
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync("synthetic password", "synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.ConfigureHelloAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        Assert.False(context.Sut.IsBiometricAvailable);
        context.Sut.SetupPassword = "synthetic password";
        context.Sut.SetupConfirmation = "synthetic password";

        await context.Sut.ConfigureAsync();

        Assert.True(context.Sut.IsAccountsVisible);
        Assert.True(context.Sut.IsBiometricAvailable);
        Assert.True(context.Sut.IsBiometricEnabled);
        context.Authorization.Verify(value => value.IsHelloAvailableAsync(), Times.Exactly(2));
        context.Authorization.Verify(
            value => value.ConfigureHelloAsync("synthetic password"),
            Times.Once);
    }

    [Fact]
    public async Task ShowSettingsAsync_RefreshesBiometricAvailabilityAfterSetup()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .SetupSequence(value => value.IsHelloAvailableAsync())
            .ReturnsAsync(false)
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync("synthetic password", "synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.SetupPassword = "synthetic password";
        context.Sut.SetupConfirmation = "synthetic password";
        await context.Sut.ConfigureAsync();

        await context.Sut.ShowSettingsAsync();

        Assert.True(context.Sut.IsSettingsVisible);
        Assert.True(context.Sut.IsBiometricAvailable);
        Assert.True(context.Sut.BeginBiometricEnrollmentCommand.CanExecute(null));
        context.Authorization.Verify(value => value.IsHelloAvailableAsync(), Times.Exactly(3));
    }

    [Fact]
    public async Task ConfigureAsync_WhenInitialBiometricPromptIsCancelled_OpensVaultWithoutEnrolling()
    {
        var context = CreateContext(isConfigured: false, biometricAvailable: true);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync("synthetic password", "synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.ConfigureHelloAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Cancelled);
        await context.Sut.InitializeAsync();
        context.Sut.SetupPassword = "synthetic password";
        context.Sut.SetupConfirmation = "synthetic password";

        await context.Sut.ConfigureAsync();

        Assert.True(context.Sut.IsAccountsVisible);
        Assert.False(context.Sut.IsBiometricEnabled);
        Assert.Empty(context.Sut.NotificationText);
        Assert.Empty(context.Sut.SetupPassword);
        Assert.Empty(context.Sut.SetupConfirmation);
    }

    [Fact]
    public async Task ConfigureAsync_WhenInitialBiometricEnrollmentFails_KeepsConfiguredVaultUsable()
    {
        var context = CreateContext(isConfigured: false, biometricAvailable: true);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync("synthetic password", "synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.ConfigureHelloAsync("synthetic password"))
            .ThrowsAsync(new InvalidOperationException("synthetic platform failure"));
        await context.Sut.InitializeAsync();
        context.Sut.SetupPassword = "synthetic password";
        context.Sut.SetupConfirmation = "synthetic password";

        await context.Sut.ConfigureAsync();

        Assert.True(context.Sut.IsAccountsVisible);
        Assert.False(context.Sut.IsBiometricEnabled);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.BiometricEnableFailed),
            context.Sut.NotificationText);
        Assert.Empty(context.Sut.SetupPassword);
        Assert.Empty(context.Sut.SetupConfirmation);
    }

    [Fact]
    public async Task UnlockAsync_ProjectsCurrentCodeForEveryVisibleAccount()
    {
        var first = new Account(Guid.NewGuid(), "First", ValidSecret, "one");
        var second = new Account(Guid.NewGuid(), "Second", ValidSecret, "two");
        var context = CreateContext(isConfigured: true, [first, second]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateManyAsync(It.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 2 && ids.Contains(first.ID) && ids.Contains(second.ID))))
            .ReturnsAsync(Result.Ok(new AccountTotpGenerationBatch(
                new Dictionary<Guid, TotpGenerationResult>
                {
                    [first.ID] = new("123456", 20, 30),
                    [second.ID] = new("654321", 20, 30)
                },
                new HashSet<Guid>())));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";

        await context.Sut.UnlockAsync();

        Assert.Collection(
            context.Sut.Accounts,
            account => Assert.Equal("123 456", account.DisplayCode),
            account => Assert.Equal("654 321", account.DisplayCode));
        context.AccountTotp.Verify(value => value.GenerateManyAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 2 && ids.Contains(first.ID) && ids.Contains(second.ID))), Times.Once);
        context.AccountTotp.Verify(
            value => value.GenerateAsync(It.IsAny<Guid>()),
            Times.Never);
    }

    [Fact]
    public async Task OnEnteredBackground_WhenDeviceIsLocked_LocksAndClearsAccountProjection()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user@example.test");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        context.Sut.OnEnteredBackground(lockImmediately: true);

        context.Authorization.Verify(value => value.Lock(), Times.Once);
        Assert.True(context.Sut.IsUnlockVisible);
        Assert.Empty(context.Sut.Accounts);
        Assert.Null(context.Sut.SelectedAccount);
    }

    [Fact]
    public async Task OnReturnedToForeground_WithinGracePeriod_RemainsUnlocked()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user@example.test");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        var projectedAccount = Assert.Single(context.Sut.Accounts);
        Assert.Equal("123456", projectedAccount.Code);
        Assert.Equal("123 456", projectedAccount.DisplayCode);
        context.Sut.OnEnteredBackground(lockImmediately: false);
        Assert.Empty(projectedAccount.Code);
        context.Time.Advance(TimeSpan.FromSeconds(29));
        context.Sut.OnReturnedToForeground();

        context.Authorization.Verify(value => value.Lock(), Times.Never);
        Assert.True(context.Sut.IsAccountsVisible);
        Assert.Single(context.Sut.Accounts);
    }

    [Fact]
    public async Task OnReturnedToForeground_WhenGracePeriodExpired_LocksAndClearsAccountProjection()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user@example.test");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        context.Sut.OnEnteredBackground(lockImmediately: false);
        context.Time.Advance(TimeSpan.FromSeconds(30));
        context.Sut.OnReturnedToForeground();

        context.Authorization.Verify(value => value.Lock(), Times.Once);
        Assert.True(context.Sut.IsUnlockVisible);
        Assert.Empty(context.Sut.Accounts);
        Assert.Null(context.Sut.SelectedAccount);
    }

    [Fact]
    public async Task OnReturnedToForeground_WhenGracePeriodExpiresWithBiometrics_AutomaticallyUnlocks()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user@example.test");
        var context = CreateContext(
            isConfigured: true,
            accounts: [account],
            biometricAvailable: true,
            preferredUnlockMethod: TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.TryUnlockWithHelloAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        context.Sut.OnEnteredBackground(lockImmediately: false);
        context.Time.Advance(TimeSpan.FromSeconds(30));
        context.Sut.OnReturnedToForeground();

        context.Authorization.Verify(value => value.Lock(), Times.Once);
        context.Authorization.Verify(value => value.TryUnlockWithHelloAsync(), Times.Once);
        Assert.True(context.Sut.IsAccountsVisible);
        Assert.Single(context.Sut.Accounts);
    }

    [Fact]
    public async Task UnlockAsync_ProjectsNoSecretIntoMobileAccountRows()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user@example.test", 60);
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync(It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 60)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";

        await context.Sut.UnlockAsync();

        var projected = Assert.Single(context.Sut.Accounts);
        Assert.Equal(account.ID, projected.Id);
        Assert.Equal(60, projected.ConfiguredPeriodSeconds);
        Assert.True(projected.HasCustomPeriod);
        Assert.Equal("60 s", projected.CustomPeriodLabel);
        Assert.DoesNotContain(
            typeof(MobileAccountItem).GetProperties(),
            property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InitializeAsync_WithConfiguredBiometrics_OffersBiometricUnlockAndPasswordFallback()
    {
        var context = CreateContext(
            isConfigured: true,
            biometricAvailable: true,
            preferredUnlockMethod: TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);

        await context.Sut.InitializeAsync();

        Assert.True(context.Sut.IsBiometricUnlockVisible);
        Assert.True(context.Sut.IsFingerprintUnlockVisible);
        Assert.False(context.Sut.IsDeviceCredentialUnlockVisible);
        Assert.True(context.Sut.IsUnlockVisible);
        Assert.False(context.Sut.UnlockCommand.CanExecute(null));
        Assert.True(context.Sut.BiometricUnlockCommand.CanExecute(null));
    }

    [Fact]
    public async Task InitializeAsync_WithDeviceCredential_KeepsTextUnlockAction()
    {
        var context = CreateContext(
            isConfigured: true,
            deviceCredentialAvailable: true,
            preferredUnlockMethod:
                TOTP.Core.Enums.PreferredUnlockMethod.PlatformDeviceCredential);

        await context.Sut.InitializeAsync();

        Assert.True(context.Sut.IsBiometricUnlockVisible);
        Assert.False(context.Sut.IsFingerprintUnlockVisible);
        Assert.True(context.Sut.IsDeviceCredentialUnlockVisible);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.UnlockWithDevicePin),
            context.Sut.UnlockWithDevicePinText);
        Assert.False(string.IsNullOrWhiteSpace(context.Sut.UnlockWithDevicePinText));
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.DeviceCredentialUnlockButton),
            context.Sut.DeviceCredentialUnlockButtonText);
        Assert.Contains('\n', context.Sut.DeviceCredentialUnlockButtonText);
    }

    [Fact]
    public async Task OnReturnedToForeground_WithConfiguredBiometrics_AutomaticallyPromptsAndUnlocks()
    {
        var context = CreateContext(
            isConfigured: true,
            biometricAvailable: true,
            preferredUnlockMethod: TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization
            .Setup(value => value.TryUnlockWithHelloAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);

        context.Sut.OnReturnedToForeground();
        await context.Sut.InitializeAsync();

        Assert.True(context.Sut.IsAccountsVisible);
        context.Authorization.Verify(value => value.TryUnlockWithHelloAsync(), Times.Once);
    }

    [Fact]
    public async Task OnReturnedToForeground_WhenAutomaticBiometricPromptIsCancelled_KeepsPasswordFallback()
    {
        var context = CreateContext(
            isConfigured: true,
            biometricAvailable: true,
            preferredUnlockMethod: TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization
            .Setup(value => value.TryUnlockWithHelloAsync())
            .ReturnsAsync(AuthorizationResult.Cancelled);

        context.Sut.OnReturnedToForeground();
        await context.Sut.InitializeAsync();

        Assert.True(context.Sut.IsUnlockVisible);
        Assert.True(context.Sut.IsBiometricUnlockVisible);
        Assert.Empty(context.Sut.NotificationText);
        context.Authorization.Verify(value => value.TryUnlockWithHelloAsync(), Times.Once);
    }

    [Fact]
    public async Task BiometricUnlockAsync_WhenSuccessful_OpensEncryptedAccounts()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user@example.test");
        var context = CreateContext(
            isConfigured: true,
            accounts: [account],
            biometricAvailable: true,
            preferredUnlockMethod: TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization
            .Setup(value => value.TryUnlockWithHelloAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();

        await context.Sut.BiometricUnlockAsync();

        Assert.True(context.Sut.IsAccountsVisible);
        Assert.Single(context.Sut.Accounts);
        context.Authorization.Verify(value => value.TryUnlockWithHelloAsync(), Times.Once);
    }

    [Fact]
    public async Task BiometricUnlockAsync_WhileAccountsAreLoading_DoesNotShowFirstAccountOnboarding()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user@example.test");
        var context = CreateContext(
            isConfigured: true,
            accounts: [account],
            biometricAvailable: true,
            preferredUnlockMethod: TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization
            .Setup(value => value.TryUnlockWithHelloAsync())
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        var accountLoad = new TaskCompletionSource<Result<IReadOnlyList<Account>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.AccountManager
            .Setup(value => value.GetAllOtpEntriesSortedAsync())
            .Returns(accountLoad.Task);

        var unlock = context.Sut.BiometricUnlockAsync();
        await WaitUntilAsync(() => context.Sut.IsAccountsVisible);

        Assert.True(context.Sut.IsBusy);
        Assert.False(context.Sut.HasNoAccounts);

        accountLoad.SetResult(Result.Ok<IReadOnlyList<Account>>([account]));
        await unlock;
        Assert.Single(context.Sut.Accounts);
        Assert.False(context.Sut.HasNoAccounts);
    }

    [Fact]
    public async Task EnableBiometricAsync_RequiresRecoveryPasswordAndClearsIt()
    {
        var context = CreateContext(isConfigured: true, biometricAvailable: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.ConfigureHelloAsync("recovery-password"))
            .Callback(() =>
            {
                context.SettingsValue.PreferredUnlockMethod =
                    TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock;
                context.State.SetConfiguration(
                    true,
                    TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
            })
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();
        await context.Sut.BeginBiometricEnrollmentAsync();
        context.Sut.BiometricRecoveryPassword = "recovery-password";

        await context.Sut.EnableBiometricAsync();

        Assert.True(context.Sut.IsBiometricEnabled);
        Assert.False(context.Sut.IsBiometricEnrollmentVisible);
        Assert.Empty(context.Sut.BiometricRecoveryPassword);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.UnlockMethodChanged),
            context.Sut.NotificationText);
    }

    [Fact]
    public async Task ShowSettingsAsync_MovesBiometricEnrollmentOutOfAccountList()
    {
        var context = CreateContext(isConfigured: true, biometricAvailable: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        Assert.True(context.Sut.IsAccountListVisible);
        Assert.False(context.Sut.IsBiometricSetupAvailable);

        await context.Sut.ShowSettingsAsync();

        Assert.True(context.Sut.IsSettingsVisible);
        Assert.False(context.Sut.IsAccountListVisible);
        Assert.True(context.Sut.IsBiometricSetupAvailable);
        Assert.True(context.Sut.ShowAccountsCommand.CanExecute(null));
        Assert.False(context.Sut.ShowSettingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeviceCredentialSelection_RequiresMasterPasswordAndUpdatesPrimaryMethod()
    {
        var context = CreateContext(
            isConfigured: true,
            deviceCredentialAvailable: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.ConfigureUnlockMethodAsync(
                TOTP.Core.Enums.PreferredUnlockMethod.PlatformDeviceCredential,
                "recovery-password"))
            .Callback(() =>
            {
                context.SettingsValue.PreferredUnlockMethod =
                    TOTP.Core.Enums.PreferredUnlockMethod.PlatformDeviceCredential;
                context.State.SetConfiguration(
                    true,
                    TOTP.Core.Enums.PreferredUnlockMethod.PlatformDeviceCredential);
            })
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        Assert.True(context.Sut.IsPasswordUnlockSelected);
        await context.Sut.BeginDeviceCredentialEnrollmentAsync();
        context.Sut.BiometricRecoveryPassword = "recovery-password";
        await context.Sut.EnableBiometricAsync();

        Assert.True(context.Sut.IsDeviceCredentialUnlockSelected);
        Assert.True(context.Sut.IsDeviceCredentialEnabled);
        Assert.False(context.Sut.IsBiometricUnlockSelected);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.UnlockMethodChanged),
            context.Sut.NotificationText);
    }

    [Fact]
    public async Task UnlockMethodProjection_UsesAuthoritativeAuthorizationState()
    {
        var context = CreateContext(
            isConfigured: true,
            biometricAvailable: false,
            deviceCredentialAvailable: false,
            preferredUnlockMethod:
                TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.State.SetConfiguration(
            true,
            TOTP.Core.Enums.PreferredUnlockMethod.PlatformDeviceCredential);

        await context.Sut.InitializeAsync();

        Assert.True(context.Sut.IsDeviceCredentialUnlockSelected);
        Assert.False(context.Sut.IsBiometricUnlockSelected);
        Assert.True(context.Sut.IsDeviceCredentialUnlockOptionEnabled);
        Assert.False(context.Sut.IsBiometricUnlockOptionEnabled);
    }

    [Fact]
    public async Task PasswordSelection_RemainsAvailableAsPlatformUnlockFallback()
    {
        var context = CreateContext(
            isConfigured: true,
            biometricAvailable: true,
            preferredUnlockMethod:
                TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.ConfigureUnlockMethodAsync(
                TOTP.Core.Enums.PreferredUnlockMethod.Password,
                "recovery-password"))
            .Callback(() =>
            {
                context.SettingsValue.PreferredUnlockMethod =
                    TOTP.Core.Enums.PreferredUnlockMethod.Password;
                context.State.SetConfiguration(
                    true,
                    TOTP.Core.Enums.PreferredUnlockMethod.Password);
            })
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        await context.Sut.SelectPasswordUnlockAsync();
        Assert.True(context.Sut.IsBiometricEnrollmentVisible);
        Assert.True(context.Sut.IsBiometricUnlockSelected);
        context.Sut.BiometricRecoveryPassword = "recovery-password";
        await context.Sut.EnableBiometricAsync();

        Assert.True(context.Sut.IsPasswordUnlockSelected);
        Assert.False(context.Sut.IsBiometricEnabled);
        Assert.False(context.Sut.IsDeviceCredentialEnabled);
    }

    [Fact]
    public async Task LeavingSettings_DiscardsUnconfirmedUnlockMethodSelection()
    {
        var context = CreateContext(
            isConfigured: true,
            biometricAvailable: true,
            deviceCredentialAvailable: true,
            preferredUnlockMethod:
                TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();
        await context.Sut.BeginDeviceCredentialEnrollmentAsync();
        context.Sut.BiometricRecoveryPassword = "unconfirmed-password";
        var changedProperties = new List<string?>();
        context.Sut.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        await context.Sut.ShowAccountsAsync();
        await context.Sut.ShowSettingsAsync();

        Assert.True(context.Sut.IsBiometricUnlockSelected);
        Assert.False(context.Sut.IsDeviceCredentialUnlockSelected);
        Assert.False(context.Sut.IsBiometricEnrollmentVisible);
        Assert.Empty(context.Sut.BiometricRecoveryPassword);
        Assert.Contains(
            nameof(MobileShellViewModel.IsBiometricUnlockSelected),
            changedProperties);
        Assert.Contains(
            nameof(MobileShellViewModel.IsDeviceCredentialUnlockSelected),
            changedProperties);
        context.Authorization.Verify(
            value => value.ConfigureUnlockMethodAsync(
                It.IsAny<TOTP.Core.Enums.PreferredUnlockMethod>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ScreenCaptureProtection_IsRequiredOnlyWhileAccountListIsVisible()
    {
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();

        Assert.False(context.Sut.IsScreenCaptureProtectionRequired);
        var policyChanges = new List<bool>();
        context.Sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(
                    MobileShellViewModel.IsScreenCaptureProtectionRequired))
            {
                policyChanges.Add(context.Sut.IsScreenCaptureProtectionRequired);
            }
        };

        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        Assert.True(context.Sut.IsAccountListVisible);
        Assert.True(context.Sut.IsScreenCaptureProtectionRequired);

        await context.Sut.ShowSettingsAsync();

        Assert.True(context.Sut.IsSettingsVisible);
        Assert.False(context.Sut.IsScreenCaptureProtectionRequired);

        await context.Sut.ShowAccountsAsync();

        Assert.True(context.Sut.IsScreenCaptureProtectionRequired);

        await context.Sut.BeginAddAsync();

        Assert.True(context.Sut.IsEditorVisible);
        Assert.False(context.Sut.IsScreenCaptureProtectionRequired);

        await context.Sut.CancelEditAsync();

        Assert.True(context.Sut.IsScreenCaptureProtectionRequired);

        await context.Sut.LockAsync();

        Assert.True(context.Sut.IsUnlockVisible);
        Assert.False(context.Sut.IsScreenCaptureProtectionRequired);
        Assert.Equal([true, false, true, false, true, false], policyChanges);
    }

    [Fact]
    public async Task SelectLanguageAsync_ChangesTheVisibleLanguageAndPersistsThePreference()
    {
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        await context.Sut.SelectLanguageAsync("de");

        Assert.Equal("de", context.SettingsValue.CultureName);
        Assert.Equal("Einstellungen", context.Sut.SettingsText);
        Assert.True(context.Sut.IsGermanLanguageSelected);
        Assert.False(context.Sut.IsEnglishLanguageSelected);
        context.Settings.Verify(value => value.SaveAsync(), Times.Once);
    }

    [Theory]
    [InlineData("fr", "Paramètres", true, false)]
    [InlineData("es", "Configuración", false, true)]
    public async Task SelectLanguageAsync_SupportsAdditionalLanguages(
        string cultureName,
        string expectedSettings,
        bool expectedFrenchSelection,
        bool expectedSpanishSelection)
    {
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        var changedProperties = new List<string>();
        context.Sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                changedProperties.Add(args.PropertyName);
        };

        await context.Sut.SelectLanguageAsync(cultureName);

        Assert.Equal(cultureName, context.SettingsValue.CultureName);
        Assert.Equal(expectedSettings, context.Sut.SettingsText);
        Assert.Equal(expectedFrenchSelection, context.Sut.IsFrenchLanguageSelected);
        Assert.Equal(expectedSpanishSelection, context.Sut.IsSpanishLanguageSelected);
        Assert.False(context.Sut.IsEnglishLanguageSelected);
        Assert.False(context.Sut.IsGermanLanguageSelected);
        Assert.Contains(nameof(MobileShellViewModel.IsEnglishLanguageSelected), changedProperties);
        Assert.Contains(nameof(MobileShellViewModel.IsGermanLanguageSelected), changedProperties);
        Assert.Contains(nameof(MobileShellViewModel.IsFrenchLanguageSelected), changedProperties);
        Assert.Contains(nameof(MobileShellViewModel.IsSpanishLanguageSelected), changedProperties);
        context.Settings.Verify(value => value.SaveAsync(), Times.Once);
    }

    [Fact]
    public async Task SelectLanguageAsync_WhenSavingFails_KeepsTheCurrentLanguage()
    {
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Settings.Setup(value => value.SaveAsync())
            .ReturnsAsync(Result.Fail("synthetic failure"));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        await context.Sut.SelectLanguageAsync("de");

        Assert.Equal("en", context.SettingsValue.CultureName);
        Assert.Equal("Settings", context.Sut.SettingsText);
        Assert.Equal(
            "The language preference could not be saved.",
            context.Sut.NotificationText);
    }

    [Fact]
    public async Task ShowAccountsAsync_ClearsUnsubmittedSettingsPasswords()
    {
        var context = CreateContext(isConfigured: true, biometricAvailable: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();
        await context.Sut.BeginBiometricEnrollmentAsync();
        context.Sut.BiometricRecoveryPassword = "recovery-password";
        context.Sut.BackupPassword = "backup-password";
        context.Sut.BackupPasswordConfirmation = "backup-password";
        context.Sut.ImportPassword = "backup-password";

        await context.Sut.ShowAccountsAsync();

        Assert.True(context.Sut.IsAccountListVisible);
        Assert.Empty(context.Sut.BiometricRecoveryPassword);
        Assert.Empty(context.Sut.BackupPassword);
        Assert.Empty(context.Sut.BackupPasswordConfirmation);
        Assert.Empty(context.Sut.ImportPassword);
    }

    [Fact]
    public async Task SearchText_FiltersByIssuerAndAccountNameWithoutChangingEmptyVaultState()
    {
        var work = new Account(Guid.NewGuid(), "Microsoft", ValidSecret, "work@example.test");
        var privateAccount = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "private");
        var context = CreateContext(isConfigured: true, [work, privateAccount]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        context.Sut.SearchText = "PRIVATE";

        var match = Assert.Single(context.Sut.Accounts);
        Assert.Equal(privateAccount.ID, match.Id);
        Assert.False(context.Sut.HasNoAccounts);
        Assert.False(context.Sut.HasNoSearchResults);
        Assert.Equal("Showing 1 of 2 accounts", context.Sut.SearchResultSummary);

        context.Sut.SearchText = "does-not-exist";

        Assert.Empty(context.Sut.Accounts);
        Assert.False(context.Sut.HasNoAccounts);
        Assert.True(context.Sut.HasNoSearchResults);
        Assert.Equal("Showing 0 of 2 accounts", context.Sut.SearchResultSummary);

        Assert.True(context.Sut.HasSearchText);
        Assert.True(context.Sut.ClearSearchCommand.CanExecute(null));
        await context.Sut.ClearSearchAsync();

        Assert.False(context.Sut.HasSearchText);
        Assert.False(context.Sut.ClearSearchCommand.CanExecute(null));
        Assert.Equal(2, context.Sut.Accounts.Count);
    }

    [Fact]
    public async Task Groups_FilterAccountsAndSearchReturnsToAllAccounts()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        var first = new Account(
            Guid.NewGuid(),
            "GitHub",
            ValidSecret,
            "alice",
            group: group);
        var second = new Account(
            Guid.NewGuid(),
            "GitLab",
            ValidSecret,
            "bob",
            group: group);
        var ungrouped = new Account(Guid.NewGuid(), "Microsoft", ValidSecret, "private");
        var context = CreateContext(isConfigured: true, [first, second, ungrouped]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        var work = Assert.Single(context.Sut.Groups);
        Assert.Equal("Work", work.Name);
        Assert.Equal(2, work.AccountCount);
        Assert.True(context.Sut.HasGroups);
        Assert.True(context.Sut.HasAccountNavigationCards);
        Assert.Equal(3, context.Sut.Accounts.Count);

        work.SelectCommand.Execute(null);

        Assert.True(context.Sut.HasSelectedGroup);
        Assert.Equal(2, context.Sut.Accounts.Count);
        Assert.All(context.Sut.Accounts, account => Assert.Equal(group.Id, account.Group?.Id));

        context.Sut.SearchText = "private";

        Assert.False(context.Sut.HasSelectedGroup);
        Assert.Equal(ungrouped.ID, Assert.Single(context.Sut.Accounts).Id);

        await context.Sut.ClearSearchAsync();
        Assert.Equal(3, context.Sut.Accounts.Count);
    }

    [Fact]
    public async Task LargeVault_NavigatesGroupsFavoritesAndSearchWithoutLosingAccounts()
    {
        var groups = new[]
        {
            new AccountGroup(Guid.NewGuid(), "Group Alpha", "#4C956C"),
            new AccountGroup(Guid.NewGuid(), "Group Beta", "#4F6BED"),
            new AccountGroup(Guid.NewGuid(), "Group Gamma", "#9C6ADE"),
            new AccountGroup(Guid.NewGuid(), "Group Delta", "#D97706")
        };
        var accounts = Enumerable.Range(0, 600)
            .Select(index => new Account(
                Guid.NewGuid(),
                index >= 427 ? $"Needle Service {index:D3}" : $"Issuer {index:D3}",
                ValidSecret,
                $"user-{index:D3}@example.test",
                isFavorite: index % 7 == 0,
                group: index % 5 < groups.Length ? groups[index % 5] : null))
            .ToArray();
        var context = CreateContext(isConfigured: true, accounts);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";

        await context.Sut.UnlockAsync();

        Assert.Equal(accounts.Length, context.Sut.Accounts.Count);
        Assert.Equal(groups.Length, context.Sut.Groups.Count);
        Assert.All(
            context.Sut.Groups,
            item => Assert.Equal(
                accounts.Count(account => account.Group?.Id == item.Id),
                item.AccountCount));

        var beta = Assert.Single(context.Sut.Groups, item => item.Id == groups[1].Id);
        beta.SelectCommand.Execute(null);

        Assert.True(context.Sut.HasSelectedGroup);
        Assert.Equal(beta.AccountCount, context.Sut.Accounts.Count);
        Assert.All(context.Sut.Accounts, account => Assert.Equal(beta.Id, account.Group?.Id));

        context.Sut.SearchText = "needle";

        Assert.False(context.Sut.HasSelectedGroup);
        Assert.Equal(173, context.Sut.Accounts.Count);
        Assert.All(
            context.Sut.Accounts,
            account => Assert.Contains("Needle", account.Issuer, StringComparison.Ordinal));
        Assert.Equal(accounts[427].ID, context.Sut.Accounts[0].Id);
        Assert.Equal(accounts[427].ID, context.Sut.AccountRevealRequest?.AccountId);
        Assert.False(context.Sut.AccountRevealRequest?.Highlight);
        Assert.True(context.Sut.AccountRevealRequest?.AlignToTop);

        await context.Sut.ClearSearchAsync();
        await context.Sut.ToggleFavoritesFilterAsync();

        var favoriteCount = accounts.Count(account => account.IsFavorite);
        Assert.Equal(favoriteCount, context.Sut.Accounts.Count);
        Assert.All(context.Sut.Accounts, account => Assert.True(account.IsFavorite));

        context.Sut.SearchText = groups[3].Name;

        var favoriteDeltaCount = accounts.Count(account =>
            account.IsFavorite && account.Group?.Id == groups[3].Id);
        Assert.Equal(favoriteDeltaCount, context.Sut.Accounts.Count);
        Assert.All(context.Sut.Accounts, account => Assert.Equal(groups[3].Id, account.Group?.Id));

        await context.Sut.ClearSearchAsync();
        await context.Sut.ToggleFavoritesFilterAsync();

        Assert.False(context.Sut.HasActiveAccountFilter);
        Assert.Equal(accounts.Length, context.Sut.Accounts.Count);
        context.Sut.Dispose();
    }

    [Fact]
    public async Task GroupEditor_CreatesAndEditsPersistedGroup()
    {
        var account = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice");
        var secondAccount = new Account(Guid.NewGuid(), "Microsoft", ValidSecret, "bob");
        IReadOnlyList<Account> storedAccounts = [account, secondAccount];
        var context = CreateContext(isConfigured: true, storedAccounts);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(storedAccounts));
        context.AccountManager
            .Setup(value => value.SaveGroupAsync(
                It.IsAny<AccountGroup>(),
                It.IsAny<IReadOnlyCollection<Guid>>()))
            .Callback<AccountGroup, IReadOnlyCollection<Guid>>((group, accountIds) =>
                storedAccounts = storedAccounts
                    .Select(value => accountIds.Contains(value.ID)
                        ? value.WithGroup(group)
                        : value)
                    .ToArray())
            .ReturnsAsync(Result.Ok());
        context.AccountTotp
            .Setup(value => value.GenerateAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        await context.Sut.BeginAddGroupAsync();

        Assert.True(context.Sut.IsGroupEditorVisible);
        Assert.True(context.Sut.IsCreatingGroup);
        Assert.Equal(6, context.Sut.GroupColorOptions.Count);
        Assert.Equal(2, context.Sut.GroupEditorAccounts.Count);
        context.Sut.GroupEditorSearchText = "micro";
        Assert.Equal(secondAccount.ID, Assert.Single(context.Sut.GroupEditorAccounts).AccountId);
        Assert.True(context.Sut.HasGroupEditorSearchText);
        Assert.True(context.Sut.ClearGroupEditorSearchCommand.CanExecute(null));
        await context.Sut.ClearGroupEditorSearchAsync();
        Assert.False(context.Sut.HasGroupEditorSearchText);
        Assert.False(context.Sut.ClearGroupEditorSearchCommand.CanExecute(null));
        var selection = Assert.Single(
            context.Sut.GroupEditorAccounts,
            value => value.AccountId == account.ID);

        await context.Sut.SaveGroupAsync();
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.GroupNameRequired),
            context.Sut.GroupEditorMessage);
        context.Sut.GroupEditorName = "Work";
        await context.Sut.SaveGroupAsync();
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.GroupAccountRequired),
            context.Sut.GroupEditorMessage);
        context.AccountManager.Verify(value => value.SaveGroupAsync(
            It.IsAny<AccountGroup>(),
            It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);

        selection.IsSelected = true;
        context.Sut.SelectedGroupColor = context.Sut.GroupColorOptions[2];

        await context.Sut.SaveGroupAsync();

        var created = Assert.Single(context.Sut.Groups);
        Assert.Equal("Work", created.Name);
        Assert.Equal("#4F6BED", created.Group.Color);
        Assert.False(context.Sut.IsGroupEditorVisible);
        Assert.True(context.Sut.IsNativeAccountGroupsVisible);
        Assert.Equal(created.Id, context.Sut.GroupRevealRequest?.GroupId);
        context.AccountManager.Verify(value => value.SaveGroupAsync(
            It.Is<AccountGroup>(group => group.Name == "Work" && group.Color == "#4F6BED"),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { account.ID }))));

        await context.Sut.BeginEditGroupAsync(created.Id);
        Assert.True(context.Sut.IsEditingGroup);
        Assert.False(context.Sut.IsNativeAccountGroupsVisible);
        Assert.Equal("Work", context.Sut.GroupEditorName);
        Assert.True(Assert.Single(
            context.Sut.GroupEditorAccounts,
            value => value.AccountId == account.ID).IsSelected);
        context.Sut.GroupEditorName = "Projects";
        context.Sut.SelectedGroupColor = context.Sut.GroupColorOptions[0];

        await context.Sut.SaveGroupAsync();

        var edited = Assert.Single(context.Sut.Groups);
        Assert.Equal(created.Id, edited.Id);
        Assert.Equal("Projects", edited.Name);
        Assert.Equal("#4C956C", edited.Group.Color);
        Assert.All(context.Sut.Accounts, item => Assert.Equal(edited.Group, item.Group));
        context.AccountManager.Verify(
            value => value.GetAllOtpEntriesSortedAsync(),
            Times.Once);
    }

    [Fact]
    public async Task GroupEditor_LargeAccountListPublishesOneVirtualizedCollectionReset()
    {
        var accounts = Enumerable.Range(1, 600)
            .Select(index => new Account(
                Guid.NewGuid(),
                $"Issuer {index}",
                ValidSecret,
                $"account-{index}"))
            .ToArray();
        var context = CreateContext(isConfigured: true, accounts);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(accounts));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        var collectionEvents = 0;
        context.Sut.GroupEditorAccounts.CollectionChanged += (_, _) => collectionEvents++;

        await context.Sut.BeginAddGroupAsync();

        Assert.Equal(600, context.Sut.GroupEditorAccounts.Count);
        Assert.Equal(1, collectionEvents);
        context.Sut.Dispose();
    }

#if DEBUG
    [Fact]
    public async Task DebugSyntheticAccounts_UseSingleBatchCommitAndDeleteOnlyMarkedAccounts()
    {
        var normal = new Account(Guid.NewGuid(), "Personal", ValidSecret, "user@example.test");
        IReadOnlyList<Account> storedAccounts = [normal];
        var context = CreateContext(isConfigured: true, storedAccounts);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(() => context.Authorization.Object.State.Unlock())
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(storedAccounts));
        context.AccountManager
            .Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(accounts => storedAccounts = accounts.ToArray())
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        Assert.True(await context.Sut.AddDebugSyntheticAccountsAsync(600));

        Assert.Equal(601, storedAccounts.Count);
        Assert.Equal(600, storedAccounts.Count(account =>
            account.AccountName?.StartsWith("otp-harbor-debug-load-test:", StringComparison.Ordinal) == true));
        Assert.True(await context.Sut.DeleteDebugSyntheticAccountsAsync());
        Assert.Equal(normal, Assert.Single(storedAccounts));
        context.AccountManager.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Exactly(2));
        context.Sut.Dispose();
    }

    [Fact]
    public async Task DebugAccountMethods_ImportThroughProductionPipelineAndDeleteAllInOneCommit()
    {
        IReadOnlyList<Account> storedAccounts = [];
        var importedAccount = new Account(Guid.NewGuid(), "Google", ValidSecret, "load-test");
        var context = CreateContext(isConfigured: true, storedAccounts);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(() => context.Authorization.Object.State.Unlock())
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(storedAccounts));
        context.AccountManager
            .Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(accounts => storedAccounts = accounts.ToArray())
            .ReturnsAsync(Result.Ok());
        context.ExportService.Setup(value => value.ImportFromStreamAsync(
                It.IsAny<Stream>(),
                "fixture.json",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new List<Account> { importedAccount }));
        context.AccountImport.Setup(value => value.ImportAsync(
                It.IsAny<IReadOnlyList<Account>>(),
                ImportConflictStrategy.SkipExisting,
                It.IsAny<Func<AccountImportPreview, CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => storedAccounts = [importedAccount])
            .ReturnsAsync(Result.Ok(new AccountImportOutcome(
                AccountImportStatus.Completed,
                Added: 1)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await using var json = new MemoryStream([1, 2, 3]);

        Assert.True(await context.Sut.ImportAccountsAsync(
            json,
            "fixture.json",
            global::Xunit.TestContext.Current.CancellationToken));
        Assert.Single(storedAccounts);
        Assert.True(await context.Sut.DeleteAllAccountsAsync());
        Assert.Empty(storedAccounts);

        context.AccountManager.Verify(
            value => value.CommitImportAsync(
                It.Is<IReadOnlyCollection<Account>>(accounts => accounts.Count == 0)),
            Times.Once);
        context.Sut.Dispose();
    }
#endif

    [Fact]
    public async Task GroupEditor_DeletesGroupAfterConfirmationWithoutDeletingAccounts()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        var account = new Account(
            Guid.NewGuid(),
            "GitHub",
            ValidSecret,
            "alice",
            group: group);
        IReadOnlyList<Account> storedAccounts = [account];
        var context = CreateContext(isConfigured: true, storedAccounts);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(storedAccounts));
        context.AccountManager
            .Setup(value => value.DeleteGroupAsync(group.Id))
            .Callback(() => storedAccounts = [account.WithGroup(null)])
            .ReturnsAsync(Result.Ok());
        context.AccountTotp
            .Setup(value => value.GenerateAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        await context.Sut.BeginEditGroupAsync(group.Id);
        await context.Sut.BeginDeleteGroupAsync();

        Assert.True(context.Sut.IsDeleteGroupConfirmationVisible);
        Assert.Contains("Work", context.Sut.GroupDeletePrompt);

        await context.Sut.ConfirmDeleteGroupAsync();

        Assert.False(context.Sut.IsGroupEditorVisible);
        Assert.False(context.Sut.HasGroups);
        Assert.Null(Assert.Single(context.Sut.Accounts).Group);
        context.AccountManager.Verify(value => value.DeleteGroupAsync(group.Id), Times.Once);
        context.AccountManager.Verify(
            value => value.DeleteAsync(It.IsAny<Account>()),
            Times.Never);
    }

    [Fact]
    public async Task FavoritesFilter_ShowsFavoritesAndCanBeNarrowedBySearch()
    {
        var github = new Account(
            Guid.NewGuid(),
            "GitHub",
            ValidSecret,
            "alice",
            isFavorite: true);
        var microsoft = new Account(Guid.NewGuid(), "Microsoft", ValidSecret, "bob");
        var gitlab = new Account(
            Guid.NewGuid(),
            "GitLab",
            ValidSecret,
            "carol",
            isFavorite: true);
        var context = CreateContext(isConfigured: true, [github, microsoft, gitlab]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        Assert.True(context.Sut.HasFavoriteAccounts);
        Assert.Equal(2, context.Sut.FavoriteCount);
        Assert.True(context.Sut.ToggleFavoritesFilterCommand.CanExecute(null));
        await context.Sut.ToggleFavoritesFilterAsync();

        Assert.True(context.Sut.IsFavoritesFilterSelected);
        Assert.Equal(2, context.Sut.Accounts.Count);
        Assert.All(context.Sut.Accounts, account => Assert.True(account.IsFavorite));

        context.Sut.SearchText = "hub";

        Assert.Equal(github.ID, Assert.Single(context.Sut.Accounts).Id);
        Assert.Equal("Showing 1 of 3 accounts", context.Sut.SearchResultSummary);
        await context.Sut.ClearSearchAsync();
        Assert.Equal(2, context.Sut.Accounts.Count);

        await context.Sut.ToggleFavoritesFilterAsync();

        Assert.False(context.Sut.IsFavoritesFilterSelected);
        Assert.Equal(3, context.Sut.Accounts.Count);
    }

    [Fact]
    public async Task ToggleAccountFavoriteAsync_PersistsAndUpdatesVisibleRow()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.UpdateAsync(
                account,
                It.Is<Account>(updated => updated.IsFavorite)))
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        var row = Assert.Single(context.Sut.Accounts);
        var changedProperties = new List<string?>();
        context.Sut.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        Assert.Equal("Add to favorites", row.FavoriteActionText);

        await context.Sut.ToggleAccountFavoriteAsync(row);

        Assert.True(row.IsFavorite);
        Assert.Equal("Remove from favorites", row.FavoriteActionText);
        Assert.Equal(1, context.Sut.FavoriteCount);
        Assert.True(context.Sut.HasFavoriteAccounts);
        Assert.DoesNotContain(nameof(context.Sut.IsBusy), changedProperties);
        Assert.False(context.Sut.IsBusy);
        context.AccountManager.VerifyAll();
    }

    [Fact]
    public async Task ToggleAccountFavoriteAsync_RemovingLastFilteredFavorite_RevealsAllAccounts()
    {
        var favorite = new Account(
            Guid.NewGuid(),
            "Favorite",
            ValidSecret,
            "user",
            isFavorite: true);
        var other = new Account(Guid.NewGuid(), "Other", ValidSecret, "other");
        var context = CreateContext(isConfigured: true, [favorite, other]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.UpdateAsync(
                favorite,
                It.Is<Account>(updated => !updated.IsFavorite)))
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ToggleFavoritesFilterAsync();
        var row = Assert.Single(context.Sut.Accounts);

        await context.Sut.ToggleAccountFavoriteAsync(row);

        Assert.False(context.Sut.IsFavoritesFilterSelected);
        Assert.False(context.Sut.HasFavoriteAccounts);
        Assert.Equal(2, context.Sut.Accounts.Count);
        Assert.Contains(context.Sut.Accounts, account => account.Id == favorite.ID && !account.IsFavorite);
    }

    [Fact]
    public async Task ToggleAccountFavoriteAsync_WhenUpdateFails_PreservesRowAndReportsLocalizedError()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account], cultureName: "de");
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.UpdateAsync(account, It.IsAny<Account>()))
            .ReturnsAsync(Result.Fail("synthetic failure"));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        var row = Assert.Single(context.Sut.Accounts);

        await context.Sut.ToggleAccountFavoriteAsync(row);

        Assert.False(row.IsFavorite);
        Assert.Equal("Zu Favoriten hinzufügen", row.FavoriteActionText);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.FavoriteUpdateFailed),
            context.Sut.NotificationText);
        Assert.Equal(NotificationSeverity.Error, context.Sut.NotificationSeverity);
    }

    [Fact]
    public async Task CopyAccountCodeAsync_CopiesCodeDisplayedInAccountRow()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        context.AccountTotp.Invocations.Clear();
        var accountRow = Assert.Single(context.Sut.Accounts);

        await context.Sut.CopyAccountCodeAsync(accountRow);

        Assert.Equal(account.ID, context.Sut.SelectedAccount?.Id);
        context.AccountTotp.Verify(value => value.GenerateAsync(account.ID), Times.Never);
        context.Clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            "123456",
            TimeSpan.FromSeconds(AppSettings.DefaultClearClipboardSeconds)), Times.Once);
        Assert.Empty(context.Sut.NotificationText);
        Assert.Equal(context.Strings.Get(MobileStringKeys.CodeCopied), accountRow.CopyConfirmation);

        await Task.Delay(
            TimeSpan.FromMilliseconds(1100),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.Empty(context.Sut.NotificationText);

        await Task.Delay(
            TimeSpan.FromMilliseconds(1000),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.Empty(context.Sut.NotificationText);
        Assert.Equal(NotificationSeverity.Information, context.Sut.NotificationSeverity);
        Assert.Empty(accountRow.CopyConfirmation);
    }

    [Fact]
    public async Task CopyAccountCodeAsync_LegacyDisabledSettingStillUsesNormalizedLifetime()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.SettingsValue.ClearClipboardEnabled = false;
        context.SettingsValue.ClearClipboardSeconds = 12;
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        var accountRow = Assert.Single(context.Sut.Accounts);

        await context.Sut.CopyAccountCodeAsync(accountRow);

        context.Clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            "123456",
            TimeSpan.FromSeconds(10)), Times.Once);
        context.Clipboard.Verify(value => value.CopyAsync(
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AccountCountdown_BatchesBindingUpdatesWhileListIsScrolling()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        var row = Assert.Single(context.Sut.Accounts);
        var changedProperties = new List<string?>();
        row.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        context.Sut.SetAccountListScrolling(true);
        await Task.Delay(
            TimeSpan.FromMilliseconds(1100),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.DoesNotContain(nameof(MobileAccountItem.RemainingSeconds), changedProperties);
        Assert.DoesNotContain(nameof(MobileAccountItem.IsExpiring), changedProperties);

        context.Sut.SetAccountListScrolling(false);

        Assert.DoesNotContain(nameof(MobileAccountItem.RemainingSeconds), changedProperties);
        Assert.DoesNotContain(nameof(MobileAccountItem.IsExpiring), changedProperties);

        await Task.Delay(
            TimeSpan.FromMilliseconds(1100),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.Contains(nameof(MobileAccountItem.RemainingSeconds), changedProperties);
        Assert.Contains(nameof(MobileAccountItem.IsExpiring), changedProperties);
        context.Sut.Dispose();
    }

    [Fact]
    public async Task AccountCodeRefresh_LargeListsPrecomputeAllRowsInOneStorageRead()
    {
        var accounts = Enumerable.Range(1, 500)
            .Select(index => new Account(
                Guid.NewGuid(),
                $"Issuer {index}",
                ValidSecret,
                $"account-{index}"))
            .ToArray();
        var context = CreateContext(isConfigured: true, accounts);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";

        await context.Sut.UnlockAsync();
        await WaitUntilAsync(() => context.Sut.Accounts.All(account => account.Code.Length > 0));

        Assert.Equal(500, context.Sut.Accounts.Count);
        context.AccountTotp.Verify(
            value => value.GenerateManyAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 500)),
            Times.Once);
        context.AccountTotp.Verify(
            value => value.GenerateAsync(It.IsAny<Guid>()),
            Times.Never);
        context.Sut.Dispose();
    }

    [Fact]
    public async Task AccountCodeRefresh_LargeListsNotifyOnlyRealizedRows()
    {
        var accounts = Enumerable.Range(1, 100)
            .Select(index => new Account(
                Guid.NewGuid(),
                $"Issuer {index}",
                ValidSecret,
                $"account-{index}"))
            .ToArray();
        var context = CreateContext(isConfigured: true, accounts);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        var generated = new TaskCompletionSource<Result<AccountTotpGenerationBatch>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.AccountTotp
            .Setup(value => value.GenerateManyAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .Returns(generated.Task);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        var realized = context.Sut.Accounts.Take(10).ToArray();
        var realizedChanges = new List<string?>();
        var offscreenChanges = new List<string?>();
        realized[0].PropertyChanged += (_, args) => realizedChanges.Add(args.PropertyName);
        context.Sut.Accounts[50].PropertyChanged += (_, args) =>
            offscreenChanges.Add(args.PropertyName);
        context.Sut.SetRealizedAccounts(realized);

        generated.SetResult(Result.Ok(new AccountTotpGenerationBatch(
            accounts.ToDictionary(
                account => account.ID,
                _ => new TotpGenerationResult("123456", 20, 30)),
            new HashSet<Guid>())));
        await WaitUntilAsync(() => context.Sut.Accounts.All(account => account.Code.Length > 0));

        Assert.Contains(nameof(MobileAccountItem.DisplayCode), realizedChanges);
        Assert.Contains(nameof(MobileAccountItem.RemainingSeconds), realizedChanges);
        Assert.DoesNotContain(nameof(MobileAccountItem.DisplayCode), offscreenChanges);
        Assert.DoesNotContain(nameof(MobileAccountItem.RemainingSeconds), offscreenChanges);
        Assert.Equal("123 456", context.Sut.Accounts[50].DisplayCode);
        context.Sut.Dispose();
    }

    [Fact]
    public async Task AccountCodeRefresh_RegeneratesOnlyRowsWhoseCodesExpire()
    {
        var first = new Account(Guid.NewGuid(), "First", ValidSecret, "one");
        var second = new Account(Guid.NewGuid(), "Second", ValidSecret, "two");
        var context = CreateContext(isConfigured: true, [first, second]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        var refreshCount = 0;
        context.AccountTotp
            .Setup(value => value.GenerateManyAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .Returns((IReadOnlyCollection<Guid> accountIds) =>
            {
                var refresh = Interlocked.Increment(ref refreshCount);
                var codes = accountIds.ToDictionary(
                    accountId => accountId,
                    accountId => new TotpGenerationResult(
                        accountId == first.ID ? "111111" : "222222",
                        refresh == 1 && accountId == first.ID ? 1 : 20,
                        30));
                return Task.FromResult(Result.Ok(new AccountTotpGenerationBatch(
                    codes,
                    new HashSet<Guid>())));
            });
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";

        await context.Sut.UnlockAsync();
        await WaitUntilAsync(() => Volatile.Read(ref refreshCount) >= 2);

        context.AccountTotp.Verify(
            value => value.GenerateManyAsync(It.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(first.ID))),
            Times.Once);
        context.AccountTotp.Verify(
            value => value.GenerateManyAsync(It.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(second.ID))),
            Times.Never);
        context.Sut.Dispose();
    }

    [Fact]
    public async Task SwipeAccountActions_SelectAccountAndOpenEditOrDeleteConfirmation()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        var item = Assert.Single(context.Sut.Accounts);
        Assert.True(context.Sut.IsNativeAccountListVisible);

        await context.Sut.BeginEditForAccountAsync(item);

        Assert.True(context.Sut.IsEditorVisible);
        Assert.False(context.Sut.IsNativeAccountListVisible);
        Assert.Equal(account.Issuer, context.Sut.EditorIssuer);
        await context.Sut.CancelEditAsync();
        Assert.True(context.Sut.IsNativeAccountListVisible);

        await context.Sut.BeginDeleteForAccountAsync(item);

        Assert.True(context.Sut.IsDeleteConfirmationVisible);
        Assert.False(context.Sut.IsNativeAccountListVisible);
        Assert.Equal(account.ID, context.Sut.SelectedAccount?.Id);
        await context.Sut.CancelDeleteAsync();
        Assert.True(context.Sut.IsNativeAccountListVisible);
    }

    [Fact]
    public async Task DeleteConfirmation_KeepsOriginalAccountWhenSelectionChangeIsAttempted()
    {
        var first = new Account(Guid.NewGuid(), "First", ValidSecret, "one");
        var second = new Account(Guid.NewGuid(), "Second", ValidSecret, "two");
        var context = CreateContext(isConfigured: true, [first, second]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        context.AccountManager
            .Setup(value => value.DeleteAsync(It.Is<Account>(account => account.ID == first.ID)))
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        var firstItem = context.Sut.Accounts.Single(account => account.Id == first.ID);
        var secondItem = context.Sut.Accounts.Single(account => account.Id == second.ID);

        await context.Sut.BeginDeleteForAccountAsync(firstItem);
        context.Sut.SelectedAccount = secondItem;

        Assert.Equal(first.ID, context.Sut.SelectedAccount?.Id);
        Assert.Contains(firstItem.DisplayName, context.Sut.DeletePrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(secondItem.DisplayName, context.Sut.DeletePrompt, StringComparison.Ordinal);

        await context.Sut.ConfirmDeleteAsync();

        context.AccountManager.Verify(
            value => value.DeleteAsync(It.Is<Account>(account => account.ID == first.ID)),
            Times.Once);
        context.AccountManager.Verify(
            value => value.DeleteAsync(It.Is<Account>(account => account.ID == second.ID)),
            Times.Never);
    }

    [Fact]
    public async Task ScanQrAsync_WhenScannerIsUnavailable_OffersManualFallback()
    {
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.QrScanner.Setup(value => value.ScanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MobileQrScanResult.Unavailable);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        await context.Sut.ScanQrAsync();

        Assert.Equal(
            context.Strings.Get(MobileStringKeys.QrScannerUnavailable),
            context.Sut.NotificationText);
        context.QrImport.Verify(value => value.ImportAsync(
            It.IsAny<string>(),
            It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScanQrAsync_WhenImportSucceeds_ShowsLocalizedResult()
    {
        const string payload =
            "otpauth://totp/Example:user?secret=JBSWY3DPEHPK3PXP&issuer=Example";
        var importedId = Guid.NewGuid();
        var context = CreateContext(
            isConfigured: true,
            [new Account(importedId, "Example", ValidSecret, "user")]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.QrScanner.Setup(value => value.ScanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MobileQrScanResult.Successful(payload));
        context.QrImport.Setup(value => value.ImportAsync(
                payload,
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.Added,
                importedId,
                "Example",
                "user")));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        context.Sut.SearchText = "temporarily hidden";

        await context.Sut.ScanQrAsync();

        Assert.Equal(
            context.Strings.Get(MobileStringKeys.QrAccountAdded),
            context.Sut.NotificationText);
        Assert.Equal(importedId, context.Sut.AccountRevealRequest?.AccountId);
        Assert.Empty(context.Sut.SearchText);
    }

    [Fact]
    public async Task ScanQrAsync_WhenExactAccountIsSkipped_RevealsExistingAccount()
    {
        const string payload =
            "otpauth://totp/Example:user?secret=JBSWY3DPEHPK3PXP&issuer=Example";
        var existingId = Guid.NewGuid();
        var context = CreateContext(
            isConfigured: true,
            [new Account(existingId, "Example", ValidSecret, "user")]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.QrScanner.Setup(value => value.ScanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MobileQrScanResult.Successful(payload));
        context.QrImport.Setup(value => value.ImportAsync(
                payload,
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.DuplicateUnchanged,
                existingId,
                "Example",
                "user")));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        context.Sut.SearchText = "temporarily hidden";

        await context.Sut.ScanQrAsync();

        Assert.Equal(existingId, context.Sut.AccountRevealRequest?.AccountId);
        Assert.Empty(context.Sut.SearchText);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.QrAccountDuplicate),
            context.Sut.NotificationText);
    }

    [Fact]
    public async Task ScanQrAsync_WhenBulkMigrationIsScanned_ImportsWithoutAdditionalConfirmation()
    {
        const string payload = "otpauth-migration://offline?data=synthetic";
        var importedId = Guid.NewGuid();
        var context = CreateContext(isConfigured: true, cultureName: "de");
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.QrScanner.Setup(value => value.ScanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MobileQrScanResult.Successful(payload));
        context.QrPayloadValidator.Setup(value => value.Validate(payload))
            .Returns(new QrPayloadValidationResult(
                true,
                "Example",
                "user",
                QrPayloadKind.GoogleAuthenticatorMigration,
                3));
        context.QrImport.Setup(value => value.ImportAsync(
                payload,
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.BulkImported,
                importedId,
                "Example",
                "user",
                TotalCount: 3,
                AddedCount: 2,
                DuplicateCount: 1,
                BatchIndex: 0,
                BatchSize: 2)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        await context.Sut.ScanQrAsync();

        Assert.False(context.Sut.IsImportConfirmationVisible);
        Assert.Equal(
            string.Format(
                context.Strings.Get(MobileStringKeys.QrBulkImportedMore),
                1,
                2,
                2,
                1,
                0),
            context.Sut.NotificationText);
        Assert.DoesNotContain("Scan the next", context.Sut.NotificationText, StringComparison.Ordinal);
        context.QrImport.Verify(value => value.ImportAsync(
            payload,
            It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScanQrAsync_WhenAccountConflicts_UsesLocalizedExplicitDecision()
    {
        const string payload =
            "otpauth://totp/Example:user?secret=JBSWY3DPEHPK3PXP&issuer=Example";
        var importedId = Guid.NewGuid();
        var context = CreateContext(isConfigured: true, cultureName: "de");
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.QrScanner.Setup(value => value.ScanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MobileQrScanResult.Successful(payload));
        var conflictRequested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.QrImport.Setup(value => value.ImportAsync(
                payload,
                It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                string _,
                Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>> resolve,
                CancellationToken cancellationToken) =>
            {
                conflictRequested.TrySetResult();
                var decision = await resolve(
                    new QrAccountConflict("Example", "user"),
                    cancellationToken);
                Assert.Equal(QrAccountConflictDecision.UpdateExisting, decision);
                return Result.Ok(new QrAccountImportOutcome(
                    QrAccountImportStatus.Updated,
                    importedId,
                    "Example",
                    "user"));
            });
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        var scanTask = context.Sut.ScanQrAsync();
        await conflictRequested.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(context.Sut.IsQrConflictVisible);
        Assert.Equal(
            string.Format(
                context.Strings.Get(MobileStringKeys.QrConflictPrompt),
                "Example: user"),
            context.Sut.QrConflictPrompt);
        Assert.DoesNotContain("Choose how", context.Sut.QrConflictPrompt, StringComparison.Ordinal);

        await context.Sut.ResolveQrConflictAsync(QrAccountConflictDecision.UpdateExisting);
        await scanTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(context.Sut.IsQrConflictVisible);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.QrAccountUpdated),
            context.Sut.NotificationText);
    }

    [Fact]
    public async Task LockAsync_AfterSettings_ReopensSettingsOnlyAfterSuccessfulUnlock()
    {
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        await context.Sut.LockAsync();

        Assert.True(context.Sut.IsUnlockVisible);
        Assert.False(context.Sut.IsSettingsVisible);

        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        Assert.True(context.Sut.IsSettingsVisible);
        Assert.False(context.Sut.IsAccountListVisible);
    }

    [Fact]
    public async Task LockAsync_WhileEditingExistingAccount_ReloadsPersistedEditorById()
    {
        var account = new Account(Guid.NewGuid(), "Persisted issuer", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        context.Sut.SelectedAccount = Assert.Single(context.Sut.Accounts);
        await context.Sut.BeginEditAsync();
        context.Sut.EditorIssuer = "Unsaved draft";

        await context.Sut.LockAsync();

        Assert.False(context.Sut.IsEditorVisible);
        Assert.Empty(context.Sut.EditorIssuer);

        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        Assert.True(context.Sut.IsEditorVisible);
        Assert.Equal(account.ID, context.Sut.SelectedAccount?.Id);
        Assert.Equal("Persisted issuer", context.Sut.EditorIssuer);
        Assert.Empty(context.Sut.EditorSecret);
    }

    [Fact]
    public async Task LockAsync_WhileAddingAccount_DoesNotRetainUnsavedDraft()
    {
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.BeginAddAsync();
        context.Sut.EditorIssuer = "Unsaved issuer";
        context.Sut.EditorSecret = ValidSecret;

        await context.Sut.LockAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        Assert.False(context.Sut.IsEditorVisible);
        Assert.Empty(context.Sut.EditorIssuer);
        Assert.Empty(context.Sut.EditorSecret);
    }

    [Fact]
    public async Task LockAsync_WhileEditingExistingGroup_ReloadsPersistedGroupById()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Persisted group", "#4F6BED");
        var account = new Account(
            Guid.NewGuid(),
            "Example",
            ValidSecret,
            "user",
            group: group);
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.BeginEditGroupAsync(group.Id);
        context.Sut.GroupEditorName = "Unsaved group name";

        await context.Sut.LockAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        Assert.True(context.Sut.IsGroupEditorVisible);
        Assert.True(context.Sut.IsEditingGroup);
        Assert.Equal("Persisted group", context.Sut.GroupEditorName);
    }

    [Fact]
    public async Task LockAsync_DuringQrScan_CancelsScanAndClearsUnlockedState()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        var scanStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.QrScanner
            .Setup(value => value.ScanAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                scanStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return MobileQrScanResult.Cancelled;
            });
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        var scanTask = context.Sut.ScanQrAsync();
        await scanStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(context.Sut.IsBusy);
        Assert.True(context.Sut.LockCommand.CanExecute(null));

        await context.Sut.LockAsync();
        await scanTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            global::Xunit.TestContext.Current.CancellationToken);

        context.Authorization.Verify(value => value.Lock(), Times.Once);
        Assert.True(context.Sut.IsUnlockVisible);
        Assert.False(context.Sut.IsBusy);
        Assert.Empty(context.Sut.Accounts);
        Assert.Null(context.Sut.SelectedAccount);
    }

    [Fact]
    public async Task ShowQrAsync_ClearsSensitivePngAndDisposesImageWhenBackgrounded()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountTotp
            .Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        var sensitivePng = SensitiveBuffer.CopyFrom([1, 2, 3]);
        context.AccountQrCode.Setup(value => value.GenerateAsync(account.ID))
            .ReturnsAsync(Result.Ok(sensitivePng));
        var imageLifetime = new Mock<IDisposable>();
        context.QrImageFactory.Setup(value => value.Create(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new MobileQrImageHandle(Mock.Of<IImage>(), imageLifetime.Object));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        await context.Sut.ShowQrAsync();

        Assert.True(context.Sut.HasQrImage);
        Assert.Throws<ObjectDisposedException>(() => _ = sensitivePng.Memory);

        context.Sut.OnEnteredBackground(lockImmediately: false);

        Assert.False(context.Sut.HasQrImage);
        imageLifetime.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ExportBackupAsync_UsesOnlyEncryptedExportAndClearsPasswordInputs()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Documents.Setup(value => value.CreateEncryptedBackupAsync(
                It.Is<string>(name => name.EndsWith(".totp", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileWritableDocument(
                new MemoryStream(),
                _ => Task.CompletedTask,
                "otp-harbor-20260920.totp"));
        context.ExportService.Setup(value => value.ExportToEncryptedStreamAsync(
                It.IsAny<IEnumerable<Account>>(),
                "backup-password",
                It.IsAny<Stream>(),
                ExportFileFormat.Json,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();
        context.Sut.BackupPassword = "backup-password";
        context.Sut.BackupPasswordConfirmation = "backup-password";

        await context.Sut.ExportBackupAsync();

        Assert.Empty(context.Sut.BackupPassword);
        Assert.Empty(context.Sut.BackupPasswordConfirmation);
        Assert.Equal(
            string.Format(
                context.Strings.Get(MobileStringKeys.BackupExported),
                "otp-harbor-20260920.totp"),
            context.Sut.NotificationText);
        context.ExportService.Verify(value => value.ExportToStreamAsync(
            It.IsAny<IEnumerable<Account>>(),
            It.IsAny<Stream>(),
            It.IsAny<ExportFileFormat>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExportBackupAsync_WhenEncryptionFails_DiscardsIncompleteDocument()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        var stream = new MemoryStream();
        var discarded = false;
        var streamWasClosedBeforeDiscard = false;
        context.Documents.Setup(value => value.CreateEncryptedBackupAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileWritableDocument(
                stream,
                _ =>
                {
                    discarded = true;
                    streamWasClosedBeforeDiscard = !stream.CanWrite;
                    return Task.CompletedTask;
                }));
        context.ExportService.Setup(value => value.ExportToEncryptedStreamAsync(
                It.IsAny<IEnumerable<Account>>(),
                "backup-password",
                It.IsAny<Stream>(),
                ExportFileFormat.Json,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("synthetic encryption failure"));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();
        context.Sut.BackupPassword = "backup-password";
        context.Sut.BackupPasswordConfirmation = "backup-password";

        await context.Sut.ExportBackupAsync();

        Assert.True(discarded);
        Assert.True(streamWasClosedBeforeDiscard);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.BackupExportFailed),
            context.Sut.NotificationText);
    }

    [Fact]
    public async Task ImportBackupAsync_WhenEveryAccountIsIdentical_ShowsNoImportAcknowledgement()
    {
        var importedAccount = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, cultureName: "de");
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Documents.Setup(value => value.OpenEncryptedBackupAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileReadableDocument(new MemoryStream([1, 2, 3])));
        context.ExportService.Setup(value => value.ImportFromEncryptedStreamAsync(
                "backup-password",
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new List<Account> { importedAccount }));
        context.AccountImport.Setup(value => value.ImportWithConflictResolutionAsync(
                It.IsAny<IReadOnlyList<Account>>(),
                It.IsAny<Func<AccountImportPreview, CancellationToken, Task<AccountImportResolution?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                IReadOnlyList<Account> _,
                Func<AccountImportPreview, CancellationToken, Task<AccountImportResolution?>> resolve,
                CancellationToken cancellationToken) =>
            {
                var resolution = await resolve(
                    new AccountImportPreview(1, 1, ImportConflictStrategy.SkipExisting)
                    {
                        UnchangedCount = 1
                    },
                    cancellationToken);
                return Result.Ok(resolution is not null
                    ? new AccountImportOutcome(AccountImportStatus.Completed, Added: 0, Skipped: 1)
                    : new AccountImportOutcome(AccountImportStatus.Cancelled));
            });
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();
        context.Sut.ImportPassword = "backup-password";

        var importTask = context.Sut.ImportBackupAsync();
        for (var attempt = 0;
             attempt < 20 && !context.Sut.IsImportConfirmationVisible;
             attempt++)
        {
            await Task.Yield();
        }

        Assert.True(context.Sut.IsImportConfirmationVisible);
        Assert.True(context.Sut.IsImportConfirmationAcknowledgementOnly);
        Assert.False(context.Sut.IsImportConfirmationCancelVisible);
        Assert.Equal(context.Strings.Get(MobileStringKeys.NoImportTitle),
            context.Sut.ImportConfirmationTitle);
        Assert.Equal(context.Strings.Get(MobileStringKeys.NoImportIdentical),
            context.Sut.ImportConfirmationText);
        Assert.Equal(context.Strings.Get(MobileStringKeys.Ok), context.Sut.ConfirmImportText);

        await context.Sut.ResolveImportConfirmationAsync(true);
        await importTask;

        Assert.False(context.Sut.IsImportConfirmationVisible);
        Assert.Empty(context.Sut.ImportPassword);
        Assert.Empty(context.Sut.NotificationText);
    }

    [Fact]
    public async Task ImportAccountFileAsync_UsesPortableParserAndTransactionalImportWorkflow()
    {
        var importedAccount = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice");
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Documents.Setup(value => value.OpenAccountImportAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileReadableDocument(
                new MemoryStream([1, 2, 3]),
                "authenticator-export.txt"));
        context.ExportService.Setup(value => value.ImportFromStreamAsync(
                It.IsAny<Stream>(),
                "authenticator-export.txt",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new List<Account> { importedAccount }));
        context.AccountImport.Setup(value => value.ImportWithConflictResolutionAsync(
                It.Is<IReadOnlyList<Account>>(accounts =>
                    accounts.Count == 1 && ReferenceEquals(accounts[0], importedAccount)),
                It.IsAny<Func<AccountImportPreview, CancellationToken, Task<AccountImportResolution?>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new AccountImportOutcome(
                AccountImportStatus.Completed,
                Added: 1,
                Replaced: 0,
                Skipped: 0)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        await context.Sut.ImportAccountFileAsync();

        context.ExportService.VerifyAll();
        context.AccountImport.VerifyAll();
        Assert.Equal(
            string.Format(context.Strings.Get(MobileStringKeys.AccountFileImported), 1, 0, 0),
            context.Sut.NotificationText);
        Assert.Equal(NotificationSeverity.Success, context.Sut.NotificationSeverity);
    }

    [Fact]
    public async Task ImportAccountFileAsync_WhenParsingTakesLong_ShowsLocalizedProgressAfterDelay()
    {
        var context = CreateContext(isConfigured: true, cultureName: "de");
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Documents.Setup(value => value.OpenAccountImportAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileReadableDocument(
                new MemoryStream([1, 2, 3]),
                "large-import.json"));
        var parsing = new TaskCompletionSource<Result<List<Account>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.ExportService.Setup(value => value.ImportFromStreamAsync(
                It.IsAny<Stream>(),
                "large-import.json",
                null,
                It.IsAny<CancellationToken>()))
            .Returns(parsing.Task);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        var importTask = context.Sut.ImportAccountFileAsync();
        await Task.Delay(
            TimeSpan.FromMilliseconds(1650),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(context.Sut.IsImportProgressVisible);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.ImportingAccounts),
            context.Sut.ImportProgressText);

        parsing.SetResult(Result.Fail("synthetic parse failure"));
        await importTask;

        Assert.False(context.Sut.IsImportProgressVisible);
        Assert.Empty(context.Sut.ImportProgressText);
    }

    [Fact]
    public async Task ImportBrandIconsAsync_WhenImportTakesLong_ShowsIconPackProgressAfterDelay()
    {
        var brandIcons = new Mock<IBrandIconPackService>();
        brandIcons.SetupGet(value => value.Status)
            .Returns(new BrandIconPackStatus(false, null, 0));
        var context = CreateContext(
            isConfigured: true,
            cultureName: "en",
            brandIconPackService: brandIcons.Object);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Documents.Setup(value => value.OpenBrandIconPackAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileReadableDocument(
                new MemoryStream([1, 2, 3]),
                "simple-icons.zip"));
        var importing = new TaskCompletionSource<Result<BrandIconPackImportResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        brandIcons.Setup(value => value.ImportAsync(
                It.IsAny<Stream>(),
                "simple-icons.zip",
                It.IsAny<CancellationToken>()))
            .Returns(importing.Task);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        var importTask = context.Sut.ImportBrandIconsAsync();
        await Task.Delay(
            TimeSpan.FromMilliseconds(1650),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(context.Sut.IsImportProgressVisible);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.ImportingBrandIcons),
            context.Sut.ImportProgressText);

        importing.SetResult(Result.Fail("synthetic icon import failure"));
        await importTask;

        Assert.False(context.Sut.IsImportProgressVisible);
        Assert.Empty(context.Sut.ImportProgressText);
    }

    [Fact]
    public void BrandIconPackStatusText_IncludesInstalledProviderAndVersion()
    {
        var brandIcons = new Mock<IBrandIconPackService>();
        brandIcons.SetupGet(value => value.Status).Returns(new BrandIconPackStatus(
            true,
            "264",
            3460,
            BrandIconPackFormat.Aegis,
            "Aegis Simple Icons"));
        var context = CreateContext(
            isConfigured: true,
            cultureName: "en",
            brandIconPackService: brandIcons.Object);

        Assert.Equal(
            "3460 local icons installed (Aegis Simple Icons v264).",
            context.Sut.BrandIconPackStatusText);
        context.Sut.Dispose();
    }

    [Fact]
    public void BrandIconPackStatusText_DescribesCombinedProviders()
    {
        var installations = new[]
        {
            new BrandIconPackInstallation(
                "aegis", "Aegis Icons", "2026.10", 400,
                BrandIconPackFormat.Aegis, 100),
            new BrandIconPackInstallation(
                "simple-icons", "Simple Icons", "17.0.0", 3_000,
                BrandIconPackFormat.SimpleIcons, 90)
        };
        var brandIcons = new Mock<IBrandIconPackService>();
        brandIcons.SetupGet(value => value.Status).Returns(new BrandIconPackStatus(
            true, null, 3_250, InstalledPacks: installations));
        var context = CreateContext(
            isConfigured: true,
            cultureName: "en",
            brandIconPackService: brandIcons.Object);

        Assert.Equal(
            "3250 local icons available from 2 installed packs (Aegis Icons, Simple Icons).",
            context.Sut.BrandIconPackStatusText);
        context.Sut.Dispose();
    }

    [Fact]
    public async Task SelectThemeAsync_PersistsAndUpdatesVisibleSelection()
    {
        var context = CreateContext(isConfigured: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();

        await context.Sut.SelectThemeAsync(AppThemePreference.Light);

        Assert.True(context.Sut.IsLightThemeSelected);
        Assert.False(context.Sut.IsDarkThemeSelected);
        context.Appearance.Verify(value => value.SetThemePreferenceAsync(
            AppThemePreference.Light,
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ImportBackupAsync_WithChangedAccount_OffersPerAccountRadioResolution()
    {
        var importedAccount = new Account(Guid.NewGuid(), "Microsoft", ValidSecret, "backup-name");
        var context = CreateContext(isConfigured: true, cultureName: "en");
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Documents.Setup(value => value.OpenEncryptedBackupAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileReadableDocument(new MemoryStream([1, 2, 3])));
        context.ExportService.Setup(value => value.ImportFromEncryptedStreamAsync(
                "backup-password",
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new List<Account> { importedAccount }));
        context.AccountImport.Setup(value => value.ImportWithConflictResolutionAsync(
                It.IsAny<IReadOnlyList<Account>>(),
                It.IsAny<Func<AccountImportPreview, CancellationToken, Task<AccountImportResolution?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                IReadOnlyList<Account> _,
                Func<AccountImportPreview, CancellationToken, Task<AccountImportResolution?>> resolve,
                CancellationToken cancellationToken) =>
            {
                var resolution = await resolve(
                    new AccountImportPreview(4, 3, ImportConflictStrategy.SkipExisting)
                    {
                        NewCount = 1,
                        UnchangedCount = 2,
                        ChangedConflicts =
                        [
                            new AccountImportConflict(
                                1,
                                "Microsoft",
                                "current-name",
                                "Microsoft",
                                "backup-name",
                                IssuerChanged: false,
                                AccountNameChanged: true,
                                SecretChanged: false,
                                PeriodChanged: false)
                        ]
                    },
                    cancellationToken);
                Assert.NotNull(resolution);
                var decision = Assert.Single(resolution.Conflicts);
                Assert.Equal(AccountImportConflictAction.Replace, decision.Action);
                return Result.Ok(new AccountImportOutcome(
                    AccountImportStatus.Completed,
                    Added: 1,
                    Replaced: 1,
                    Skipped: 2));
            });
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();
        context.Sut.ImportPassword = "backup-password";

        var importTask = context.Sut.ImportBackupAsync();
        await WaitUntilAsync(() => context.Sut.IsBackupConflictResolutionVisible);

        var conflict = Assert.Single(context.Sut.BackupImportConflicts);
        Assert.True(conflict.IsSkipSelected);
        Assert.False(conflict.IsReplaceSelected);
        Assert.True(context.Sut.IsAllBackupConflictsSkipped);
        Assert.False(context.Sut.ConfirmBackupConflictResolutionCommand.CanExecute(null));
        Assert.Contains("1 new, 2 unchanged, 1 need review", context.Sut.BackupConflictResolutionText);
        Assert.Equal("Changed: account name", conflict.ChangedFieldsText);

        await context.Sut.SelectAllBackupConflictsAsync(AccountImportConflictAction.Replace);
        Assert.True(conflict.IsReplaceSelected);
        Assert.True(context.Sut.IsAllBackupConflictsReplaced);
        Assert.True(context.Sut.ConfirmBackupConflictResolutionCommand.CanExecute(null));
        await context.Sut.ConfirmBackupConflictResolutionAsync();
        await importTask;

        Assert.False(context.Sut.IsBackupConflictResolutionVisible);
        Assert.Empty(context.Sut.BackupImportConflicts);
        Assert.Equal(
            string.Format(context.Strings.Get(MobileStringKeys.BackupImported), 1, 1, 2),
            context.Sut.NotificationText);
    }

    [Fact]
    public async Task BackNavigation_WithDirtyAccountEditorPromptsBeforeDiscarding()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        await ConfigureAndBeginAddAsync(context);

        Assert.False(context.Sut.HasUnsavedAccountChanges);
        context.Sut.EditorIssuer = "Example";
        Assert.True(context.Sut.HasUnsavedAccountChanges);
        Assert.True(context.Sut.CanHandleSystemBack);

        Assert.True(await context.Sut.TryHandleBackNavigationAsync());
        Assert.True(context.Sut.IsAccountEditorExitConfirmationVisible);
        Assert.True(context.Sut.IsEditorVisible);

        await context.Sut.CancelAccountNavigationAsync();
        Assert.False(context.Sut.IsAccountEditorExitConfirmationVisible);
        Assert.True(context.Sut.IsEditorVisible);

        await context.Sut.TryHandleBackNavigationAsync();
        await context.Sut.DiscardAccountChangesAsync();

        Assert.False(context.Sut.IsAccountEditorExitConfirmationVisible);
        Assert.False(context.Sut.IsEditorVisible);
        Assert.False(context.Sut.CanHandleSystemBack);
        context.AccountManager.Verify(
            value => value.AddNewAsync(It.IsAny<Account>()),
            Times.Never);
    }

    [Fact]
    public async Task BackNavigation_SaveChoicePersistsDirtyAccountAndClosesEditor()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .ReturnsAsync(Result.Ok());
        await ConfigureAndBeginAddAsync(context);
        context.Sut.EditorIssuer = "Example";
        context.Sut.EditorAccountName = "alice@example.test";
        context.Sut.EditorSecret = ValidSecret;

        await context.Sut.TryHandleBackNavigationAsync();
        await context.Sut.SaveAccountAndNavigateBackAsync();

        Assert.False(context.Sut.IsAccountEditorExitConfirmationVisible);
        Assert.False(context.Sut.IsEditorVisible);
        context.AccountManager.Verify(
            value => value.AddNewAsync(It.Is<Account>(account =>
                account.Issuer == "Example"
                && account.AccountName == "alice@example.test")),
            Times.Once);
    }

    [Fact]
    public async Task SaveAccountAsync_WithInvalidSecret_DoesNotPersistAccount()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        await ConfigureAndBeginAddAsync(context);
        context.Sut.EditorIssuer = "Example";
        context.Sut.EditorSecret = "not-valid-*";

        await context.Sut.SaveAccountAsync();

        context.AccountManager.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.SecretInvalid),
            context.Sut.EditorSecretMessage);
        Assert.Empty(context.Sut.NotificationText);
        Assert.Empty(context.Sut.EditorSecret);
    }

    [Fact]
    public async Task SaveAccountAsync_WithValidInput_PersistsOnlyNormalizedAccount()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        Account? persisted = null;
        context.AccountManager
            .Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .Callback<Account>(account => persisted = account)
            .ReturnsAsync(Result.Ok());
        await ConfigureAndBeginAddAsync(context);
        context.Sut.EditorIssuer = "  Example  ";
        context.Sut.EditorAccountName = " user@example.test ";
        context.Sut.EditorSecret = "JBSW Y3DP EHPK 3PXP";
        context.Sut.EditorPeriodSeconds = 600;

        await context.Sut.SaveAccountAsync();

        Assert.NotNull(persisted);
        Assert.Equal("Example", persisted.Issuer);
        Assert.Equal("user@example.test", persisted.AccountName);
        Assert.Equal(ValidSecret, persisted.Secret);
        Assert.Equal(600, persisted.PeriodSeconds);
        Assert.Empty(context.Sut.EditorSecret);
        Assert.False(context.Sut.IsEditorVisible);
    }

    [Fact]
    public async Task EditAccount_CanImportAccountSpecificCustomSvg()
    {
        var account = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice");
        var custom = new BrandDefinition(
            "custom_0123456789abcdef01234567",
            "personal-mark",
            "#334155",
            "custom_0123456789abcdef01234567.svg",
            "personal-mark.svg");
        var brandIcons = new Mock<IBrandIconPackService>();
        brandIcons.Setup(value => value.ImportCustomIconAsync(
                account.ID,
                It.IsAny<Stream>(),
                "personal-mark.svg",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(custom));
        var context = CreateContext(
            isConfigured: true,
            accounts: [account],
            brandIconPackService: brandIcons.Object);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Documents.Setup(value => value.OpenCustomSvgIconAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileReadableDocument(
                new MemoryStream([1, 2, 3]),
                "personal-mark.svg"));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        context.Sut.SelectedAccount = Assert.Single(context.Sut.Accounts);
        await context.Sut.BeginEditAsync();

        await context.Sut.ImportCustomIconAsync();

        Assert.Equal(
            context.Strings.Get(MobileStringKeys.CustomIconImported),
            context.Sut.NotificationText);
        Assert.Equal("personal-mark.svg", context.Sut.SelectedEditorBrandIconFileName);
        Assert.True(context.Sut.HasSelectedEditorCustomIconFileName);
        Assert.Equal(
            "Custom image: personal-mark.svg",
            context.Sut.SelectedEditorCustomIconFileName);
        brandIcons.Verify(value => value.ImportCustomIconAsync(
            account.ID,
            It.IsAny<Stream>(),
            "personal-mark.svg",
            It.IsAny<CancellationToken>()), Times.Once);
        context.Sut.Dispose();
    }

    [Fact]
    public async Task EditAccount_BrandIconPickerLoadsAndPersistsExplicitSelection()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "alice");
        var original = new BrandDefinition("example", "Example", "#334155", "example.svg");
        var selected = new BrandDefinition("github", "GitHub", "#181717", "github.svg");
        var brandIcons = new Mock<IBrandIconPackService>();
        brandIcons.SetupGet(value => value.AvailableBrands).Returns([original, selected]);
        brandIcons.Setup(value => value.GetAccountBrandId(account.ID)).Returns(original.Id);
        brandIcons.Setup(value => value.SetAccountBrandIdAsync(
                account.ID,
                selected.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var context = CreateContext(
            isConfigured: true,
            accounts: [account],
            brandIconPackService: brandIcons.Object);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager.Setup(value => value.UpdateAsync(account, It.IsAny<Account>()))
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        context.Sut.SelectedAccount = Assert.Single(context.Sut.Accounts);
        await context.Sut.BeginEditAsync();

        Assert.Equal(original.Id, context.Sut.SelectedEditorBrandIconOption?.Id);
        Assert.InRange(context.Sut.EditorBrandIconPickerWidth, 180d, 260d);
        context.Sut.SelectedEditorBrandIconOption = context.Sut.EditorBrandIconOptions
            .Single(option => option.Id == selected.Id);

        await context.Sut.SaveAccountAsync();

        brandIcons.Verify(value => value.SetAccountBrandIdAsync(
            account.ID,
            selected.Id,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EditAccount_WhenCustomSvgHasNoVectorPath_ExplainsValidationFailure()
    {
        var account = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice");
        var brandIcons = new Mock<IBrandIconPackService>();
        brandIcons.Setup(value => value.ImportCustomIconAsync(
                account.ID,
                It.IsAny<Stream>(),
                "empty-shape.svg",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<BrandDefinition>(
                new CustomIconImportError(CustomIconImportFailureReason.MissingVectorPath)));
        var context = CreateContext(
            isConfigured: true,
            accounts: [account],
            brandIconPackService: brandIcons.Object);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Documents.Setup(value => value.OpenCustomSvgIconAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileReadableDocument(
                new MemoryStream([1, 2, 3]),
                "empty-shape.svg"));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        context.Sut.SelectedAccount = Assert.Single(context.Sut.Accounts);
        await context.Sut.BeginEditAsync();

        await context.Sut.ImportCustomIconAsync();

        Assert.Equal(
            context.Strings.Get(MobileStringKeys.CustomIconImportMissingPath),
            context.Sut.NotificationText);
        context.Sut.Dispose();
    }

    [Fact]
    public async Task SaveAccountAsync_PersistsFavoriteSelection()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        Account? persisted = null;
        context.AccountManager
            .Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .Callback<Account>(account => persisted = account)
            .ReturnsAsync(Result.Ok());
        await ConfigureAndBeginAddAsync(context);
        context.Sut.EditorIssuer = "Example";
        context.Sut.EditorSecret = ValidSecret;
        context.Sut.EditorIsFavorite = true;

        await context.Sut.SaveAccountAsync();

        Assert.NotNull(persisted);
        Assert.True(persisted.IsFavorite);
        Assert.False(context.Sut.EditorIsFavorite);
    }

    [Fact]
    public async Task SaveAccountAsync_LeavesFavoritesFilterToRevealNewNonFavorite()
    {
        var stored = new List<Account>
        {
            new(
                Guid.NewGuid(),
                "Favorite",
                ValidSecret,
                "alice",
                isFavorite: true)
        };
        var context = CreateContext(isConfigured: true, stored);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .Callback<Account>(stored.Add)
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await context.Sut.ToggleFavoritesFilterAsync();
        Assert.True(context.Sut.IsFavoritesFilterSelected);
        await context.Sut.BeginAddAsync();
        context.Sut.EditorIssuer = "New account";
        context.Sut.EditorSecret = ValidSecret;

        await context.Sut.SaveAccountAsync();

        Assert.False(context.Sut.IsFavoritesFilterSelected);
        Assert.Equal("New account", context.Sut.SelectedAccount?.Issuer);
        Assert.Equal(2, context.Sut.Accounts.Count);
    }

    [Fact]
    public async Task BeginEditAndSave_CanRemoveFavorite()
    {
        var original = new Account(
            Guid.NewGuid(),
            "Example",
            ValidSecret,
            "user",
            isFavorite: true);
        var context = CreateContext(isConfigured: true, [original]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        Account? updated = null;
        context.AccountManager
            .Setup(value => value.UpdateAsync(original, It.IsAny<Account>()))
            .Callback<Account, Account>((_, account) => updated = account)
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        context.Sut.SelectedAccount = Assert.Single(context.Sut.Accounts);

        await context.Sut.BeginEditAsync();
        Assert.True(context.Sut.EditorIsFavorite);
        context.Sut.EditorIsFavorite = false;
        await context.Sut.SaveAccountAsync();

        Assert.NotNull(updated);
        Assert.False(updated.IsFavorite);
    }

    [Fact]
    public async Task SaveAccountAsync_ChangingIssuerUpdatesOnlyEditedItemMetadata()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        var before = new Account(Guid.NewGuid(), "Alpha", ValidSecret, "before");
        var edited = new Account(
            Guid.NewGuid(),
            "Beta",
            ValidSecret,
            "edited",
            group: group);
        var after = new Account(Guid.NewGuid(), "Gamma", ValidSecret, "after");
        var stored = new List<Account> { before, edited, after };
        var context = CreateContext(isConfigured: true, stored);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.UpdateAsync(edited, It.IsAny<Account>()))
            .Callback<Account, Account>((_, updated) => stored[1] = updated)
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await WaitUntilAsync(() => context.Sut.Accounts.All(account => account.Code.Length > 0));
        var beforeItem = context.Sut.Accounts.Single(account => account.Id == before.ID);
        var editedItem = context.Sut.Accounts.Single(account => account.Id == edited.ID);
        var afterItem = context.Sut.Accounts.Single(account => account.Id == after.ID);
        var beforeCode = beforeItem.Code;
        var afterCode = afterItem.Code;
        var collectionChanges = 0;
        var beforeChanges = new List<string?>();
        var afterChanges = new List<string?>();
        context.Sut.Accounts.CollectionChanged += (_, _) => collectionChanges++;
        beforeItem.PropertyChanged += (_, args) => beforeChanges.Add(args.PropertyName);
        afterItem.PropertyChanged += (_, args) => afterChanges.Add(args.PropertyName);
        context.Sut.SelectedAccount = editedItem;
        context.AccountTotp.Invocations.Clear();

        await context.Sut.BeginEditAsync();
        context.Sut.EditorIssuer = "Beta Updated";
        await context.Sut.SaveAccountAsync();

        Assert.Same(beforeItem, context.Sut.Accounts.Single(account => account.Id == before.ID));
        Assert.Same(editedItem, context.Sut.Accounts.Single(account => account.Id == edited.ID));
        Assert.Same(afterItem, context.Sut.Accounts.Single(account => account.Id == after.ID));
        Assert.Equal("Beta Updated", editedItem.Issuer);
        Assert.Equal(group, stored[1].Group);
        Assert.Equal(0, collectionChanges);
        Assert.DoesNotContain(nameof(MobileAccountItem.Code), beforeChanges);
        Assert.DoesNotContain(nameof(MobileAccountItem.RemainingSeconds), beforeChanges);
        Assert.DoesNotContain(nameof(MobileAccountItem.Code), afterChanges);
        Assert.DoesNotContain(nameof(MobileAccountItem.RemainingSeconds), afterChanges);
        Assert.Equal(beforeCode, beforeItem.Code);
        Assert.Equal(afterCode, afterItem.Code);
        Assert.True(beforeItem.RemainingSeconds > 0);
        Assert.True(afterItem.RemainingSeconds > 0);
        context.AccountTotp.Verify(value => value.GenerateManyAsync(
            It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);
    }

    [Fact]
    public async Task SaveAccountAsync_ChangingIssuerPreservesEqualIssuerNeighborOrder()
    {
        var edited = new Account(Guid.NewGuid(), "112rvhh", ValidSecret, "edited");
        var stored = new List<Account> { edited };
        stored.AddRange(Enumerable.Range(0, 600).Select(index => new Account(
            Guid.NewGuid(),
            "1Password",
            ValidSecret,
            $"loadtest-{index:0000}")));
        var context = CreateContext(isConfigured: true, stored);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.UpdateAsync(edited, It.IsAny<Account>()))
            .Callback<Account, Account>((_, updated) => stored[0] = updated)
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        var orderBeforeSave = context.Sut.Accounts.Select(account => account.Id).ToArray();
        var collectionChanges = 0;
        context.Sut.Accounts.CollectionChanged += (_, _) => collectionChanges++;
        context.Sut.SelectedAccount = context.Sut.Accounts.Single(account => account.Id == edited.ID);

        await context.Sut.BeginEditAsync();
        context.Sut.EditorIssuer = "112rvhhgv";
        await context.Sut.SaveAccountAsync();

        Assert.Equal(orderBeforeSave, context.Sut.Accounts.Select(account => account.Id));
        Assert.Equal(0, collectionChanges);
        Assert.Equal(edited.ID, context.Sut.AccountRevealRequest?.AccountId);
    }

    [Fact]
    public async Task SaveAccountAsync_ChangingPeriodRefreshesOnlyEditedAccountWithoutListReset()
    {
        var edited = new Account(Guid.NewGuid(), "Alpha", ValidSecret, "edited");
        var neighbor = new Account(Guid.NewGuid(), "Beta", ValidSecret, "neighbor");
        var stored = new List<Account> { edited, neighbor };
        var context = CreateContext(isConfigured: true, stored);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.AccountManager
            .Setup(value => value.UpdateAsync(edited, It.IsAny<Account>()))
            .Callback<Account, Account>((_, updated) => stored[0] = updated)
            .ReturnsAsync(Result.Ok());
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        await WaitUntilAsync(() => context.Sut.Accounts.All(account => account.Code.Length > 0));
        var editedItem = context.Sut.Accounts.Single(account => account.Id == edited.ID);
        var neighborItem = context.Sut.Accounts.Single(account => account.Id == neighbor.ID);
        var collectionResetCount = 0;
        var neighborChanges = new List<string?>();
        context.Sut.Accounts.CollectionChanged += (_, _) => collectionResetCount++;
        neighborItem.PropertyChanged += (_, args) => neighborChanges.Add(args.PropertyName);
        context.Sut.SelectedAccount = editedItem;
        context.AccountTotp.Invocations.Clear();

        await context.Sut.BeginEditAsync();
        context.Sut.EditorPeriodSeconds = 60;
        await context.Sut.SaveAccountAsync();
        await WaitUntilAsync(() => editedItem.PeriodSeconds == 60);

        Assert.Same(editedItem, context.Sut.Accounts.Single(account => account.Id == edited.ID));
        Assert.Same(neighborItem, context.Sut.Accounts.Single(account => account.Id == neighbor.ID));
        Assert.Equal(0, collectionResetCount);
        Assert.DoesNotContain(nameof(MobileAccountItem.Code), neighborChanges);
        Assert.DoesNotContain(nameof(MobileAccountItem.DisplayCode), neighborChanges);
        Assert.DoesNotContain(nameof(MobileAccountItem.PeriodSeconds), neighborChanges);
        context.AccountTotp.Verify(value => value.GenerateManyAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Single() == edited.ID)), Times.Once);
        context.AccountTotp.Verify(value => value.GenerateManyAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Any(id => id != edited.ID))), Times.Never);
    }

    [Fact]
    public async Task SaveAccountAsync_WithUnsupportedPeriod_DoesNotPersistAccount()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        await ConfigureAndBeginAddAsync(context);
        context.Sut.EditorIssuer = "Example";
        context.Sut.EditorSecret = ValidSecret;
        context.Sut.EditorPeriodSeconds = 3601;

        await context.Sut.SaveAccountAsync();

        context.AccountManager.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.TotpPeriodInvalid),
            context.Sut.EditorPeriodMessage);
        Assert.True(context.Sut.IsAdvancedOptionsExpanded);
        Assert.Empty(context.Sut.NotificationText);
        Assert.Empty(context.Sut.EditorSecret);
    }

    [Fact]
    public async Task SaveAccountAsync_WithEmptyPeriod_ShowsFieldErrorWithoutPersisting()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        await ConfigureAndBeginAddAsync(context);
        context.Sut.EditorIssuer = "Example";
        context.Sut.EditorSecret = ValidSecret;
        context.Sut.EditorPeriodSeconds = null;

        await context.Sut.SaveAccountAsync();

        context.AccountManager.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.TotpPeriodInvalid),
            context.Sut.EditorPeriodMessage);
        Assert.Empty(context.Sut.EditorIssuerMessage);
        Assert.Empty(context.Sut.EditorSecretMessage);
    }

    [Fact]
    public async Task AccountEditor_FieldErrorsResetForTheNextAddForm()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        await ConfigureAndBeginAddAsync(context);

        await context.Sut.SaveAccountAsync();
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.IssuerRequired),
            context.Sut.EditorIssuerMessage);

        context.Sut.EditorIssuer = "Example";
        await context.Sut.SaveAccountAsync();
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.SecretRequired),
            context.Sut.EditorSecretMessage);

        await context.Sut.CancelEditAsync();
        await context.Sut.BeginAddAsync();

        Assert.Equal(30, context.Sut.EditorPeriodSeconds);
        Assert.Empty(context.Sut.EditorIssuerMessage);
        Assert.Empty(context.Sut.EditorSecretMessage);
        Assert.Empty(context.Sut.EditorPeriodMessage);
    }

    [Fact]
    public async Task EditAccount_EmptyPeriodErrorIsResetWhenEditorIsReopened()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "user");
        var context = CreateContext(isConfigured: true, [account]);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();
        context.Sut.SelectedAccount = Assert.Single(context.Sut.Accounts);
        await context.Sut.BeginEditAsync();
        context.Sut.EditorPeriodSeconds = null;

        await context.Sut.SaveAccountAsync();

        context.AccountManager.Verify(value => value.UpdateAsync(
            It.IsAny<Account>(),
            It.IsAny<Account>()), Times.Never);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.TotpPeriodInvalid),
            context.Sut.EditorPeriodMessage);

        await context.Sut.CancelEditAsync();
        await context.Sut.BeginEditAsync();

        Assert.Equal(30, context.Sut.EditorPeriodSeconds);
        Assert.Empty(context.Sut.EditorPeriodMessage);
    }

    [Fact]
    public async Task AdvancedOptions_AfterCancellingEditor_StartsCollapsedForNextAction()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        await ConfigureAndBeginAddAsync(context);
        Assert.False(context.Sut.IsAdvancedOptionsExpanded);
        context.Sut.IsAdvancedOptionsExpanded = true;

        await context.Sut.CancelEditAsync();
        await context.Sut.BeginAddAsync();

        Assert.False(context.Sut.IsAdvancedOptionsExpanded);
    }

    private static async Task ConfigureAndBeginAddAsync(TestContext context)
    {
        await context.Sut.InitializeAsync();
        context.Sut.SetupPassword = "synthetic password";
        context.Sut.SetupConfirmation = "synthetic password";
        await context.Sut.ConfigureAsync();
        await context.Sut.BeginAddAsync();
    }

    [Fact]
    public async Task ClearEditorPeriodCommand_ClearsCurrentPeriodAndDisablesItself()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        await ConfigureAndBeginAddAsync(context);

        Assert.Equal(30, context.Sut.EditorPeriodSeconds);
        Assert.True(context.Sut.HasEditorPeriodSeconds);
        Assert.True(context.Sut.ClearEditorPeriodCommand.CanExecute(null));

        context.Sut.ClearEditorPeriodCommand.Execute(null);

        Assert.Null(context.Sut.EditorPeriodSeconds);
        Assert.False(context.Sut.HasEditorPeriodSeconds);
        Assert.False(context.Sut.ClearEditorPeriodCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveAccountAsync_WithGoogleAuthenticatorCompatibleShortSecret_PersistsAccount()
    {
        var context = CreateContext(isConfigured: false);
        context.Authorization
            .Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success);
        Account? persisted = null;
        context.AccountManager
            .Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .Callback<Account>(account => persisted = account)
            .ReturnsAsync(Result.Ok());
        await ConfigureAndBeginAddAsync(context);
        context.Sut.EditorIssuer = "Example";
        context.Sut.EditorSecret = "ORSXG5A";

        await context.Sut.SaveAccountAsync();

        Assert.NotNull(persisted);
        Assert.Equal("ORSXG5A", persisted.Secret);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }

    private static TestContext CreateContext(
        bool isConfigured,
        IReadOnlyList<Account>? accounts = null,
        bool biometricAvailable = false,
        bool deviceCredentialAvailable = false,
        TOTP.Core.Enums.PreferredUnlockMethod preferredUnlockMethod =
            TOTP.Core.Enums.PreferredUnlockMethod.Password,
        string cultureName = "en",
        bool appLockEnabled = true,
        IBrandIconPackService? brandIconPackService = null)
    {
        accounts ??= [];
        var state = new AuthorizationState();
        state.SetConfiguration(isConfigured, preferredUnlockMethod);
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.InitializeAsync()).Returns(Task.CompletedTask);
        authorization.Setup(value => value.IsHelloAvailableAsync())
            .ReturnsAsync(biometricAvailable);
        authorization.Setup(value => value.IsUnlockMethodAvailableAsync(
                TOTP.Core.Enums.PreferredUnlockMethod.PlatformDeviceCredential))
            .ReturnsAsync(deviceCredentialAvailable);

        var passwordValidation = new Mock<IPasswordValidationService>();
        passwordValidation.SetupGet(value => value.MinimumLength).Returns(8);

        var accountManager = new Mock<IAccountManager>();
        accountManager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(accounts));

        var accountTotp = new Mock<IAccountTotpService>();
        accountTotp
            .Setup(value => value.GenerateManyAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .Returns((IReadOnlyCollection<Guid> accountIds) =>
            {
                var periods = accounts.ToDictionary(
                    account => account.ID,
                    account => account.PeriodSeconds);
                var codes = accountIds
                    .Distinct()
                    .ToDictionary(
                        accountId => accountId,
                        accountId => new TotpGenerationResult(
                            "123456",
                            20,
                            periods.GetValueOrDefault(accountId, TotpPeriodPolicy.DefaultSeconds)));
                return Task.FromResult(Result.Ok(new AccountTotpGenerationBatch(
                    codes,
                    new HashSet<Guid>())));
            });
        var clipboard = new Mock<IAsyncClipboardService>();
        clipboard.SetupGet(value => value.Capabilities)
            .Returns(ClipboardCapabilities.WriteText | ClipboardCapabilities.ConditionalClear);
        clipboard.Setup(value => value.CopyAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());
        clipboard.Setup(value => value.CopyAndScheduleClearAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(Result.Ok());
        var qrScanner = new Mock<IMobileQrScanner>();
        var qrPayloadValidator = new Mock<IQrPayloadValidator>();
        qrPayloadValidator.Setup(value => value.Validate(It.IsAny<string>()))
            .Returns(new QrPayloadValidationResult(true, string.Empty, string.Empty));
        var qrImport = new Mock<IQrAccountImportService>();
        var accountQrCode = new Mock<IAccountQrCodeService>();
        var qrImageFactory = new Mock<IMobileQrImageFactory>();
        var documents = new Mock<IMobileDocumentService>();
        var exportService = new Mock<IExportService>();
        var accountImport = new Mock<IAccountImportService>();

        var settingsValue = new AppSettings
        {
            CultureName = cultureName,
            AppLockEnabled = appLockEnabled,
            PreferredUnlockMethod = preferredUnlockMethod
        };
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(settingsValue);
        settings.Setup(value => value.LoadAsync())
            .ReturnsAsync(Result.Ok<IAppSettings>(settingsValue));
        settings.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Ok());

        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.AuthorizationEnvelopeFilePath)
            .Returns(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.bin"));

        var strings = new MobileStringCatalog(CultureInfo.GetCultureInfo(cultureName));
        var time = new ManualTimeProvider();
        var themePreference = AppThemePreference.Dark;
        var appearance = new Mock<IAppearanceSettingsService>();
        appearance.SetupGet(value => value.ThemePreference)
            .Returns(() => themePreference);
        appearance.Setup(value => value.SetThemePreferenceAsync(
                It.IsAny<AppThemePreference>(),
                CancellationToken.None))
            .Callback<AppThemePreference, CancellationToken>((value, _) =>
                themePreference = value)
            .ReturnsAsync(Result.Ok());
        var sut = new MobileShellViewModel(
            authorization.Object,
            passwordValidation.Object,
            accountManager.Object,
            accountTotp.Object,
            clipboard.Object,
            qrScanner.Object,
            qrPayloadValidator.Object,
            qrImport.Object,
            accountQrCode.Object,
            qrImageFactory.Object,
            documents.Object,
            exportService.Object,
            accountImport.Object,
            settings.Object,
            paths.Object,
            strings,
            time,
            brandIconPackService: brandIconPackService,
            appearanceSettingsService: appearance.Object);
        return new TestContext(
            sut,
            state,
            authorization,
            accountManager,
            accountTotp,
            clipboard,
            qrScanner,
            qrPayloadValidator,
            qrImport,
            accountQrCode,
            qrImageFactory,
            documents,
            exportService,
            accountImport,
            settings,
            settingsValue,
            strings,
            time,
            appearance);
    }

    private sealed record TestContext(
        MobileShellViewModel Sut,
        AuthorizationState State,
        Mock<IAuthorizationService> Authorization,
        Mock<IAccountManager> AccountManager,
        Mock<IAccountTotpService> AccountTotp,
        Mock<IAsyncClipboardService> Clipboard,
        Mock<IMobileQrScanner> QrScanner,
        Mock<IQrPayloadValidator> QrPayloadValidator,
        Mock<IQrAccountImportService> QrImport,
        Mock<IAccountQrCodeService> AccountQrCode,
        Mock<IMobileQrImageFactory> QrImageFactory,
        Mock<IMobileDocumentService> Documents,
        Mock<IExportService> ExportService,
        Mock<IAccountImportService> AccountImport,
        Mock<ISettingsService> Settings,
        AppSettings SettingsValue,
        MobileStringCatalog Strings,
        ManualTimeProvider Time,
        Mock<IAppearanceSettingsService> Appearance);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
