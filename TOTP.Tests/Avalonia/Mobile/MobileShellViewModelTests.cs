using System.Globalization;
using Avalonia.Media;
using FluentResults;
using Moq;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

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
    public async Task ImportGoogleQrAsync_FromSettings_UsesBoundedMigrationWorkflow()
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

        var import = context.Sut.ImportGoogleQrAsync();
        Assert.True(context.Sut.IsImportConfirmationVisible);
        await context.Sut.ResolveImportConfirmationAsync(true);
        await import;

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
            .Setup(value => value.GenerateAsync(first.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 20, 30)));
        context.AccountTotp
            .Setup(value => value.GenerateAsync(second.ID))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("654321", 20, 30)));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";

        await context.Sut.UnlockAsync();

        Assert.Collection(
            context.Sut.Accounts,
            account => Assert.Equal("123 456", account.DisplayCode),
            account => Assert.Equal("654 321", account.DisplayCode));
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
        Assert.True(context.Sut.IsUnlockVisible);
        Assert.False(context.Sut.UnlockCommand.CanExecute(null));
        Assert.True(context.Sut.BiometricUnlockCommand.CanExecute(null));
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
    public async Task EnableBiometricAsync_RequiresRecoveryPasswordAndClearsIt()
    {
        var context = CreateContext(isConfigured: true, biometricAvailable: true);
        context.Authorization
            .Setup(value => value.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization
            .Setup(value => value.ConfigureHelloAsync("recovery-password"))
            .Callback(() => context.State.SetConfiguration(
                true,
                TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock))
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
            context.Strings.Get(MobileStringKeys.BiometricEnabled),
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

        context.Sut.SearchText = "does-not-exist";

        Assert.Empty(context.Sut.Accounts);
        Assert.False(context.Sut.HasNoAccounts);
        Assert.True(context.Sut.HasNoSearchResults);
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

        await context.Sut.CopyAccountCodeAsync(Assert.Single(context.Sut.Accounts));

        Assert.Equal(account.ID, context.Sut.SelectedAccount?.Id);
        context.AccountTotp.Verify(value => value.GenerateAsync(account.ID), Times.Never);
        context.Clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            "123456",
            TimeSpan.FromSeconds(AppSettings.DefaultClearClipboardSeconds)), Times.Once);
        Assert.Equal(
            string.Format(
                context.Strings.Get(MobileStringKeys.CodeCopiedWithClear),
                AppSettings.DefaultClearClipboardSeconds),
            context.Sut.NotificationText);

        await Task.Delay(
            TimeSpan.FromMilliseconds(1100),
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.Empty(context.Sut.NotificationText);
        Assert.Equal(NotificationSeverity.Information, context.Sut.NotificationSeverity);
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

        await context.Sut.BeginEditForAccountAsync(item);

        Assert.True(context.Sut.IsEditorVisible);
        Assert.Equal(account.Issuer, context.Sut.EditorIssuer);
        await context.Sut.CancelEditAsync();

        await context.Sut.BeginDeleteForAccountAsync(item);

        Assert.True(context.Sut.IsDeleteConfirmationVisible);
        Assert.Equal(account.ID, context.Sut.SelectedAccount?.Id);
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
        var context = CreateContext(isConfigured: true);
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

        await context.Sut.ScanQrAsync();

        Assert.Equal(
            context.Strings.Get(MobileStringKeys.QrAccountAdded),
            context.Sut.NotificationText);
    }

    [Fact]
    public async Task ScanQrAsync_WhenBulkMigrationIsImported_ShowsLocalizedBatchProgress()
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

        var scanTask = context.Sut.ScanQrAsync();
        for (var attempt = 0;
             attempt < 20 && !context.Sut.IsImportConfirmationVisible;
             attempt++)
        {
            await Task.Delay(10, global::Xunit.TestContext.Current.CancellationToken);
        }

        Assert.True(context.Sut.IsImportConfirmationVisible);
        Assert.Equal(
            string.Format(
                context.Strings.Get(MobileStringKeys.QrMigrationConfirmation),
                3),
            context.Sut.ImportConfirmationText);
        await context.Sut.ResolveImportConfirmationAsync(true);
        await scanTask;

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
    }

    [Fact]
    public async Task ScanQrAsync_WhenBulkMigrationIsDeclined_DoesNotImportAccounts()
    {
        const string payload = "otpauth-migration://offline?data=synthetic";
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
                2));
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic password";
        await context.Sut.UnlockAsync();

        var scanTask = context.Sut.ScanQrAsync();
        for (var attempt = 0;
             attempt < 20 && !context.Sut.IsImportConfirmationVisible;
             attempt++)
        {
            await Task.Delay(10, global::Xunit.TestContext.Current.CancellationToken);
        }

        Assert.True(context.Sut.IsImportConfirmationVisible);
        await context.Sut.ResolveImportConfirmationAsync(false);
        await scanTask;

        Assert.Equal(
            context.Strings.Get(MobileStringKeys.QrImportCancelled),
            context.Sut.NotificationText);
        context.QrImport.Verify(value => value.ImportAsync(
            It.IsAny<string>(),
            It.IsAny<Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
                _ => Task.CompletedTask));
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
            context.Strings.Get(MobileStringKeys.BackupExported),
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
    public async Task ImportBackupAsync_RequiresExplicitConfirmationAndSkipsExistingAccounts()
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
        context.AccountImport.Setup(value => value.ImportAsync(
                It.IsAny<IReadOnlyList<Account>>(),
                ImportConflictStrategy.SkipExisting,
                It.IsAny<Func<AccountImportPreview, CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                IReadOnlyList<Account> _,
                ImportConflictStrategy _,
                Func<AccountImportPreview, CancellationToken, Task<bool>> confirm,
                CancellationToken cancellationToken) =>
            {
                var accepted = await confirm(
                    new AccountImportPreview(1, 1, ImportConflictStrategy.SkipExisting),
                    cancellationToken);
                return Result.Ok(accepted
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
        Assert.Equal(
            string.Format(context.Strings.Get(MobileStringKeys.ImportConfirmation), 1, 1),
            context.Sut.ImportConfirmationText);

        await context.Sut.ResolveImportConfirmationAsync(true);
        await importTask;

        Assert.False(context.Sut.IsImportConfirmationVisible);
        Assert.Empty(context.Sut.ImportPassword);
        Assert.Equal(
            string.Format(context.Strings.Get(MobileStringKeys.BackupImported), 0, 1),
            context.Sut.NotificationText);
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
            context.Sut.NotificationText);
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
        context.Sut.EditorPeriodSeconds = 60;

        await context.Sut.SaveAccountAsync();

        Assert.NotNull(persisted);
        Assert.Equal("Example", persisted.Issuer);
        Assert.Equal("user@example.test", persisted.AccountName);
        Assert.Equal(ValidSecret, persisted.Secret);
        Assert.Equal(60, persisted.PeriodSeconds);
        Assert.Empty(context.Sut.EditorSecret);
        Assert.False(context.Sut.IsEditorVisible);
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
        context.Sut.EditorPeriodSeconds = 301;

        await context.Sut.SaveAccountAsync();

        context.AccountManager.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        Assert.Equal(
            context.Strings.Get(MobileStringKeys.TotpPeriodInvalid),
            context.Sut.NotificationText);
        Assert.Empty(context.Sut.EditorSecret);
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

    private static TestContext CreateContext(
        bool isConfigured,
        IReadOnlyList<Account>? accounts = null,
        bool biometricAvailable = false,
        TOTP.Core.Enums.PreferredUnlockMethod preferredUnlockMethod =
            TOTP.Core.Enums.PreferredUnlockMethod.Password,
        string cultureName = "en",
        bool appLockEnabled = true)
    {
        accounts ??= [];
        var state = new AuthorizationState();
        state.SetConfiguration(isConfigured, preferredUnlockMethod);
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.InitializeAsync()).Returns(Task.CompletedTask);
        authorization.Setup(value => value.IsHelloAvailableAsync())
            .ReturnsAsync(biometricAvailable);

        var passwordValidation = new Mock<IPasswordValidationService>();
        passwordValidation.SetupGet(value => value.MinimumLength).Returns(8);

        var accountManager = new Mock<IAccountManager>();
        accountManager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(accounts));

        var accountTotp = new Mock<IAccountTotpService>();
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
            time);
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
            time);
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
        ManualTimeProvider Time);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
