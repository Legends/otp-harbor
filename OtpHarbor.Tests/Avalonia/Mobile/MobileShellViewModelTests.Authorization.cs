using FluentResults;
using Moq;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed partial class MobileShellViewModelTests
{
    [Fact]
    public async Task FirstPasswordSetup_OffersAuthenticationBeforeLoadingAccounts()
    {
        var context = CreateContext(isConfigured: false, biometricAvailable: true);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ConfigurePasswordAsync("synthetic setup", "synthetic setup"))
            .Callback(() => { context.State.SetConfiguration(true, PreferredUnlockMethod.Password); context.State.Unlock(); })
            .ReturnsAsync(AuthorizationResult.Success);
        var accounts = new TaskCompletionSource<Result<IReadOnlyList<Account>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.AccountManager.Setup(value => value.GetAllOtpEntriesSortedAsync()).Returns(accounts.Task);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();
        Assert.False(sut.IsInitialQuickUnlockOfferVisible);
        sut.SetupPassword = "synthetic setup";
        sut.SetupConfirmation = "synthetic setup";

        await sut.ConfigureAsync().WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(sut.IsInitialQuickUnlockOfferVisible);
        Assert.True(sut.SkipInitialQuickUnlockCommand.CanExecute(null));
        Assert.False(sut.IsNativeAccountListVisible);
        Assert.False(sut.IsNativeAccountGroupsVisible);
        context.AccountManager.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Never);
        sut.SkipInitialQuickUnlockCommand.Execute(null);
        Assert.False(sut.IsInitialQuickUnlockOfferVisible);
        accounts.SetResult(Result.Ok<IReadOnlyList<Account>>([]));
        await WaitUntilAsync(() => !sut.IsBusy);
        Assert.True(sut.HasNoAccounts);
        context.AccountManager.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstPasswordSetup_LockOrDisposalDuringCapabilityCheckNeverShowsLateOffer(bool dispose)
    {
        var context = CreateContext(isConfigured: false);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ConfigurePasswordAsync("synthetic setup", "synthetic setup"))
            .Callback(() => { context.State.SetConfiguration(true, PreferredUnlockMethod.Password); context.State.Unlock(); })
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.Lock()).Callback(context.State.Lock);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var availability = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Authorization.Setup(value => value.IsHelloAvailableAsync()).Returns(() =>
        {
            entered.SetResult();
            return availability.Task;
        });
        sut.SetupPassword = "synthetic setup";
        sut.SetupConfirmation = "synthetic setup";
        var configuring = sut.ConfigureAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);

        if (dispose) sut.Dispose();
        else sut.OnEnteredBackground(lockImmediately: true);
        availability.SetResult(true);
        await configuring;

        Assert.False(sut.IsInitialQuickUnlockOfferVisible);
        Assert.False(context.State.IsUnlocked);
        context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(
            It.IsAny<PreferredUnlockMethod?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthorizeEditorSecret_PendingPlatformPromptIsCancelledWhenViewCloses(bool dispose)
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "synthetic user");
        var context = CreateContext(isConfigured: true, [account], biometricAvailable: true,
            preferredUnlockMethod: PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic session"))
            .Callback(context.State.Unlock).ReturnsAsync(AuthorizationResult.Success);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Authorization.Setup(value => value.AuthorizeSensitiveOperationAsync(
                PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .Returns((PreferredUnlockMethod _, CancellationToken token) =>
            {
                entered.SetResult(token);
                return completed.Task;
            });
        await sut.InitializeAsync();
        sut.UnlockPassword = "synthetic session";
        await sut.UnlockAsync();
        sut.SelectedAccount = Assert.Single(sut.Accounts);
        await sut.BeginEditAsync();

        var authorization = sut.AuthorizeEditorSecretAsync();
        var promptToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);
        if (dispose) sut.Dispose();
        else sut.OnEnteredBackground(lockImmediately: true);
        var cancelled = promptToken.IsCancellationRequested;
        completed.SetResult(AuthorizationResult.Success);
        await authorization.WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(cancelled);
        Assert.False(sut.IsEditingExistingAccount);
        Assert.False(sut.IsEditorSecretRevealed);
        Assert.Empty(sut.EditorSecret);
        Assert.Empty(sut.EditorSecretMessage);
    }

    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential)]
    public async Task DisableAppLock_CurrentPlatformMethodDoesNotRequireMasterPassword(PreferredUnlockMethod method)
    {
        var context = CreateContext(isConfigured: true, biometricAvailable: true, deviceCredentialAvailable: true);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic session"))
            .Callback(context.State.Unlock).ReturnsAsync(AuthorizationResult.Success);
        await sut.InitializeAsync();
        sut.UnlockPassword = "synthetic session";
        await sut.UnlockAsync();
        context.State.SetConfiguration(true, method);
        context.SettingsValue.PreferredUnlockMethod = method;
        await sut.ShowSettingsAsync();
        context.Authorization.Setup(value => value.SetAppLockEnabledWithPlatformAsync(false, method, It.IsAny<CancellationToken>()))
            .Callback(() => context.SettingsValue.AppLockEnabled = false).ReturnsAsync(AuthorizationResult.Success);

        await sut.BeginDisableAppLockAsync();

        Assert.Equal(method, sut.SelectedAuthenticationMethod?.Method);
        Assert.False(sut.IsPasswordAuthorizationSelected);
        Assert.Empty(sut.AppLockRecoveryPassword);
        Assert.False(sut.HasAuthenticationMethodChoice);
        Assert.False(sut.IsAppLockEnabled);
        Assert.False(sut.IsDisableAppLockConfirmationVisible);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(false, method, It.IsAny<CancellationToken>()), Times.Once);
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(false, It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(AuthorizationResult.Cancelled)]
    [InlineData(AuthorizationResult.NotAvailable)]
    [InlineData(AuthorizationResult.Failed)]
    public async Task DisableAppLock_UnsuccessfulPlatformAuthenticationKeepsProtectionEnabled(AuthorizationResult outcome)
    {
        var context = CreateContext(isConfigured: true, biometricAvailable: true);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic session"))
            .Callback(context.State.Unlock).ReturnsAsync(AuthorizationResult.Success);
        await sut.InitializeAsync();
        sut.UnlockPassword = "synthetic session";
        await sut.UnlockAsync();
        context.State.SetConfiguration(true, PreferredUnlockMethod.PlatformQuickUnlock);
        context.SettingsValue.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        await sut.ShowSettingsAsync();
        context.Authorization.Setup(value => value.SetAppLockEnabledWithPlatformAsync(false,
                PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>())).ReturnsAsync(outcome);
        await sut.BeginDisableAppLockAsync();

        Assert.True(sut.IsAppLockEnabled);
        Assert.Equal(outcome == AuthorizationResult.NotAvailable, sut.IsDisableAppLockConfirmationVisible);
        Assert.False(sut.HasAuthenticationMethodChoice);
        Assert.Empty(sut.AppLockRecoveryPassword);
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(false, It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DisabledAppLock_ProtectedActionOffersOnlyAvailableAuthenticationMethods(bool biometric, bool credential)
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "synthetic user");
        var context = CreateContext(isConfigured: true, [account], biometricAvailable: biometric,
            deviceCredentialAvailable: credential, appLockEnabled: false);
        using var sut = context.Sut;
        await sut.InitializeAsync();
        sut.SelectedAccount = Assert.Single(sut.Accounts);
        await sut.BeginEditAsync();

        await sut.AuthorizeEditorSecretAsync();

        Assert.True(sut.IsEditorSecretPasswordPromptVisible);
        var expected = new List<PreferredUnlockMethod> { PreferredUnlockMethod.Password };
        if (biometric) expected.Add(PreferredUnlockMethod.PlatformQuickUnlock);
        if (credential) expected.Add(PreferredUnlockMethod.PlatformDeviceCredential);
        Assert.Equal(expected, sut.AuthenticationMethods.Select(value => value.Method));
        Assert.Equal(biometric || credential, sut.HasAuthenticationMethodChoice);
        Assert.False(sut.IsEditorSecretRevealAuthorized);
        if (biometric || credential)
        {
            var method = biometric ? PreferredUnlockMethod.PlatformQuickUnlock : PreferredUnlockMethod.PlatformDeviceCredential;
            sut.SelectedAuthenticationMethod = sut.AuthenticationMethods.Single(value => value.Method == method);
            context.Authorization.Setup(value => value.AuthorizeSensitiveOperationAsync(method, It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthorizationResult.Success);
            Assert.True(sut.ConfirmEditorSecretAuthorizationCommand.CanExecute(null));
            await sut.ConfirmEditorSecretAuthorizationAsync();
            Assert.True(sut.IsEditorSecretRevealAuthorized);
            Assert.False(sut.IsAppLockEnabled);
            context.Authorization.Verify(value => value.TryUnlockWithHelloAsync(), Times.Never);
        }
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(It.IsAny<bool>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FirstPasswordSetup_OffersAvailableQuickUnlockOnceAndAllowsSkipping(bool biometric, bool credential)
    {
        var context = CreateContext(isConfigured: false, biometricAvailable: biometric, deviceCredentialAvailable: credential);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ConfigurePasswordAsync("synthetic setup", "synthetic setup"))
            .Callback(() => { context.State.SetConfiguration(true, PreferredUnlockMethod.Password); context.State.Unlock(); })
            .ReturnsAsync(AuthorizationResult.Success);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();
        sut.SetupPassword = "synthetic setup";
        sut.SetupConfirmation = "synthetic setup";

        await sut.ConfigureAsync();

        Assert.Equal(biometric || credential, sut.IsInitialQuickUnlockOfferVisible);
        Assert.Equal(biometric, sut.ChooseInitialBiometricCommand.CanExecute(null));
        Assert.Equal(credential, sut.ChooseInitialDeviceCredentialCommand.CanExecute(null));
        Assert.Empty(sut.SetupPassword);
        Assert.Empty(sut.SetupConfirmation);
        context.Authorization.Verify(value => value.ConfigureHelloAsync(It.IsAny<string>()), Times.Never);
        if (sut.IsInitialQuickUnlockOfferVisible)
        {
            Assert.True(sut.CanHandleSystemBack);
            Assert.True(await sut.TryHandleBackNavigationAsync());
        }
        Assert.False(sut.IsInitialQuickUnlockOfferVisible);
        Assert.True(sut.IsAccountListVisible);
        await sut.ShowSettingsAsync();
        Assert.False(sut.IsInitialQuickUnlockOfferVisible);
        context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FirstPasswordSetup_CanChooseDeviceCredentialWithoutBiometrics()
    {
        var context = CreateContext(isConfigured: false, deviceCredentialAvailable: true);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ConfigurePasswordAsync("synthetic setup", "synthetic setup"))
            .Callback(() => { context.State.SetConfiguration(true, PreferredUnlockMethod.Password); context.State.Unlock(); })
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.CompleteInitialQuickUnlockSetupAsync(
                PreferredUnlockMethod.PlatformDeviceCredential, It.IsAny<CancellationToken>()))
            .Callback(() => context.State.SetConfiguration(true, PreferredUnlockMethod.PlatformDeviceCredential))
            .ReturnsAsync(AuthorizationResult.Success);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();
        sut.SetupPassword = "synthetic setup";
        sut.SetupConfirmation = "synthetic setup";
        await sut.ConfigureAsync();

        sut.ChooseInitialDeviceCredentialCommand.Execute(null);
        await WaitUntilAsync(() => !sut.IsBusy);

        Assert.False(sut.IsInitialQuickUnlockOfferVisible);
        Assert.True(sut.IsDeviceCredentialEnabled);
        Assert.Equal(context.Strings.Get(MobileStringKeys.QuickUnlockEnabled), sut.NotificationText);
        context.Authorization.Verify(value => value.ConfigureHelloAsync(It.IsAny<string>()), Times.Never);
    }
}
