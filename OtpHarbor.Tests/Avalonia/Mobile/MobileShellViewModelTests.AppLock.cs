using Moq;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed partial class MobileShellViewModelTests
{
    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential)]
    public async Task AppLockToggle_DeactivateAndReactivateAutomaticallyUseSavedPlatformMethod(PreferredUnlockMethod method)
    {
        var context = await CreateUnlockMethodChangeContextAsync(method);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.Lock()).Callback(context.State.Lock);
        Task<AuthorizationResult> Apply(bool enabled)
            {
                Assert.True(sut.IsBusy);
                Assert.False(sut.HasAuthenticationMethodChoice);
                Assert.False(sut.IsDisableAppLockConfirmationVisible);
                Assert.False(sut.IsBiometricEnrollmentVisible);
                Assert.Equal(method, sut.SelectedAuthenticationMethod?.Method);
                context.SettingsValue.AppLockEnabled = enabled;
                // Unattended unlock projects Password while retaining the saved preference.
                context.State.SetConfiguration(true, enabled ? method : PreferredUnlockMethod.Password);
                return Task.FromResult(AuthorizationResult.Success);
            }
        ArrangeToggleAuthorization(context, true, method, _ => Apply(false));
        ArrangeToggleAuthorization(context, false, method, _ => Apply(true));

        await sut.ToggleAppLockAsync();
        Assert.False(sut.IsAppLockEnabled);
        Assert.True(context.State.IsUnlocked);
        Assert.Equal(method, context.SettingsValue.PreferredUnlockMethod);
        Assert.Equal(PreferredUnlockMethod.Password, context.State.PreferredUnlockMethod);

        await sut.ToggleAppLockAsync();

        Assert.True(sut.IsAppLockEnabled);
        Assert.False(sut.IsUnlockVisible);
        Assert.True(sut.IsSettingsVisible);
        Assert.True(context.State.IsUnlocked);
        Assert.False(sut.IsAppLockReactivationPending);
        Assert.Equal(method, context.SettingsValue.PreferredUnlockMethod);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(false, method, It.IsAny<CancellationToken>()), Times.Once);
        context.Authorization.Verify(value => value.ReactivateAppLockWithPlatformAsync(method, It.IsAny<CancellationToken>()), Times.Once);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(true, method, It.IsAny<CancellationToken>()), Times.Never);
        context.Authorization.Verify(value => value.Lock(), Times.Never);
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(true, PreferredUnlockMethod.PlatformQuickUnlock, AuthorizationResult.Cancelled, false)]
    [InlineData(true, PreferredUnlockMethod.PlatformDeviceCredential, AuthorizationResult.Failed, false)]
    [InlineData(true, PreferredUnlockMethod.PlatformQuickUnlock, AuthorizationResult.PasswordRequired, true)]
    [InlineData(true, PreferredUnlockMethod.PlatformDeviceCredential, AuthorizationResult.NotAvailable, true)]
    [InlineData(false, PreferredUnlockMethod.PlatformQuickUnlock, AuthorizationResult.Cancelled, false)]
    [InlineData(false, PreferredUnlockMethod.PlatformDeviceCredential, AuthorizationResult.Failed, false)]
    [InlineData(false, PreferredUnlockMethod.PlatformQuickUnlock, AuthorizationResult.PasswordRequired, true)]
    [InlineData(false, PreferredUnlockMethod.PlatformDeviceCredential, AuthorizationResult.NotAvailable, true)]
    public async Task AppLockToggle_UnsuccessfulPromptPreservesPolicyAndOnlyOffersPasswordRecovery(
        bool enabled, PreferredUnlockMethod method, AuthorizationResult result, bool recovery)
    {
        var context = await CreateAppLockToggleContextAsync(enabled, method);
        using var sut = context.Sut;
        ArrangeToggleAuthorization(context, enabled, method, _ => Task.FromResult(result));

        await sut.ToggleAppLockAsync();

        Assert.Equal(enabled, sut.IsAppLockEnabled);
        Assert.Equal(method, context.SettingsValue.PreferredUnlockMethod);
        Assert.True(context.State.IsUnlocked);
        Assert.False(sut.HasAuthenticationMethodChoice);
        Assert.Equal(recovery && enabled, sut.IsDisableAppLockConfirmationVisible);
        Assert.Equal(recovery && !enabled, sut.IsBiometricEnrollmentVisible);
        Assert.Empty(sut.AppLockRecoveryPassword);
        Assert.Empty(sut.BiometricRecoveryPassword);
        if (recovery)
        {
            Assert.True(sut.IsPasswordAuthorizationSelected);
            if (enabled)
                Assert.Equal(context.Strings.Get(MobileStringKeys.AppLockPlatformRecoveryRequired), sut.NotificationText);
        }
        if (result == AuthorizationResult.Cancelled) Assert.Empty(sut.NotificationText);
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(false, "en")]
    [InlineData(true, "de")]
    [InlineData(false, "de")]
    [InlineData(true, "fr")]
    [InlineData(false, "fr")]
    [InlineData(true, "es")]
    [InlineData(false, "es")]
    public async Task AppLockToggle_CurrentPasswordUsesLocalizedPasswordEntryWithoutChooser(bool enabled, string locale)
    {
        var context = await CreateAppLockToggleContextAsync(enabled, PreferredUnlockMethod.Password, locale);
        using var sut = context.Sut;

        await sut.ToggleAppLockAsync();

        Assert.Equal(enabled, sut.IsAppLockEnabled);
        Assert.False(sut.HasAuthenticationMethodChoice);
        Assert.True(sut.IsPasswordAuthorizationSelected);
        Assert.Equal(enabled, sut.IsDisableAppLockConfirmationVisible);
        Assert.Equal(!enabled, sut.IsBiometricEnrollmentVisible);
        Assert.False(enabled ? sut.ConfirmDisableAppLockCommand.CanExecute(null) : sut.EnableBiometricCommand.CanExecute(null));
        Assert.Equal(context.Strings.Get(enabled ? MobileStringKeys.DisableAppLockWarning : MobileStringKeys.EnableAppLockPasswordPrompt),
            enabled ? sut.DisableAppLockWarningText : sut.UnlockMethodPasswordPromptText);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(It.IsAny<bool>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AppLockToggle_UnavailableSavedMethodUsesPasswordWithoutChoosingAnotherAvailablePlatform(bool enabled)
    {
        var context = CreateContext(isConfigured: true, appLockEnabled: enabled, biometricAvailable: false,
            deviceCredentialAvailable: true, preferredUnlockMethod: PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        context.State.Unlock();
        await sut.InitializeAsync();
        context.State.Unlock();
        await sut.ShowSettingsAsync();

        await sut.ToggleAppLockAsync();

        Assert.True(sut.IsPasswordAuthorizationSelected);
        Assert.False(sut.HasAuthenticationMethodChoice);
        Assert.Equal(enabled, sut.IsAppLockEnabled);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(It.IsAny<bool>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AppLockToggle_PlatformRecoveryRequiresPasswordBeforeChangingPolicy(bool enabled)
    {
        var method = PreferredUnlockMethod.PlatformQuickUnlock;
        var context = await CreateAppLockToggleContextAsync(enabled, method);
        using var sut = context.Sut;
        ArrangeToggleAuthorization(context, enabled, method, _ => Task.FromResult(AuthorizationResult.PasswordRequired));
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic recovery"))
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.SetAppLockEnabledAsync(!enabled, "synthetic recovery"))
            .Callback(() => context.SettingsValue.AppLockEnabled = !enabled).ReturnsAsync(AuthorizationResult.Success);
        await sut.ToggleAppLockAsync();
        Assert.Equal(enabled, sut.IsAppLockEnabled);
        Assert.False(sut.HasAuthenticationMethodChoice);

        if (enabled)
        {
            await sut.ConfirmDisableAppLockAsync();
            Assert.True(sut.IsAppLockEnabled);
            sut.AppLockRecoveryPassword = "synthetic recovery";
            await sut.ConfirmDisableAppLockAsync();
        }
        else
        {
            await sut.EnableBiometricAsync();
            Assert.False(sut.IsAppLockEnabled);
            sut.BiometricRecoveryPassword = "synthetic recovery";
            await sut.EnableBiometricAsync();
        }

        Assert.Equal(!enabled, sut.IsAppLockEnabled);
        Assert.Equal(method, context.SettingsValue.PreferredUnlockMethod);
        Assert.Empty(sut.AppLockRecoveryPassword);
        Assert.Empty(sut.BiometricRecoveryPassword);
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(!enabled, "synthetic recovery"), Times.Once);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task AppLockToggle_SessionClosingDuringAvailabilityCheckDoesNotOpenPrompt(bool enabled, bool dispose)
    {
        var context = await CreateAppLockToggleContextAsync(enabled, PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.Lock()).Callback(context.State.Lock);
        var availability = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Authorization.Setup(value => value.IsHelloAvailableAsync()).Returns(availability.Task);
        var toggling = sut.ToggleAppLockAsync();
        Assert.True(sut.IsBusy);

        if (dispose) sut.Dispose();
        else await sut.LockAsync();
        availability.SetResult(true);
        await toggling;

        Assert.False(sut.IsDisableAppLockConfirmationVisible);
        Assert.False(sut.IsBiometricEnrollmentVisible);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(It.IsAny<bool>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task AppLockToggle_PendingPromptRejectsDuplicateTapsAndCancelsOnSessionClose(bool enabled, bool dispose)
    {
        var method = PreferredUnlockMethod.PlatformQuickUnlock;
        var context = await CreateAppLockToggleContextAsync(enabled, method);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.Lock()).Callback(context.State.Lock);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        ArrangeToggleAuthorization(context, enabled, method, token => { entered.SetResult(token); return complete.Task; });
        var toggling = sut.ToggleAppLockAsync();
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);

        await sut.ToggleAppLockAsync();
        Assert.False(sut.ToggleAppLockCommand.CanExecute(null));
        if (dispose) sut.Dispose();
        else await sut.LockAsync();
        Assert.True(token.IsCancellationRequested);
        complete.SetResult(AuthorizationResult.Success);
        await toggling;

        Assert.False(context.State.IsUnlocked);
        Assert.Equal(enabled, sut.IsAppLockEnabled);
        Assert.False(sut.IsDisableAppLockConfirmationVisible);
        Assert.False(sut.IsBiometricEnrollmentVisible);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(false, method, It.IsAny<CancellationToken>()), enabled ? Times.Once() : Times.Never());
        context.Authorization.Verify(value => value.ReactivateAppLockWithPlatformAsync(method, It.IsAny<CancellationToken>()), enabled ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential)]
    public async Task AppLockReactivation_RenewsBackgroundGraceAfterFreshVerificationAndStillLocksOnFutureBackground(
        PreferredUnlockMethod method)
    {
        var context = await CreateAppLockToggleContextAsync(false, method);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.Lock()).Callback(context.State.Lock);
        context.Authorization.Setup(value => value.TryUnlockWithHelloAsync()).ReturnsAsync(AuthorizationResult.Cancelled);
        ArrangeToggleAuthorization(context, false, method, _ =>
        {
            sut.OnEnteredBackground(lockImmediately: false);
            context.Time.Advance(TimeSpan.FromSeconds(45));
            context.SettingsValue.AppLockEnabled = true;
            context.State.SetConfiguration(true, method);
            return Task.FromResult(AuthorizationResult.Success);
        });

        await sut.ToggleAppLockAsync();
        sut.OnReturnedToForeground();

        Assert.True(context.State.IsUnlocked);
        Assert.True(sut.IsSettingsVisible);
        context.Authorization.Verify(value => value.Lock(), Times.Never);

        sut.OnEnteredBackground(lockImmediately: false);
        context.Time.Advance(TimeSpan.FromSeconds(31));
        sut.OnReturnedToForeground();

        Assert.False(context.State.IsUnlocked);
        Assert.True(sut.IsUnlockVisible);
        context.Authorization.Verify(value => value.Lock(), Times.Once);
    }

    private static void ArrangeToggleAuthorization(TestContext context, bool currentlyEnabled, PreferredUnlockMethod method,
        Func<CancellationToken, Task<AuthorizationResult>> action)
    {
        if (currentlyEnabled)
            context.Authorization.Setup(value => value.SetAppLockEnabledWithPlatformAsync(false, method, It.IsAny<CancellationToken>()))
                .Returns((bool _, PreferredUnlockMethod _, CancellationToken token) => action(token));
        else
            context.Authorization.Setup(value => value.ReactivateAppLockWithPlatformAsync(method, It.IsAny<CancellationToken>()))
                .Returns((PreferredUnlockMethod _, CancellationToken token) => action(token));
    }

    private static async Task<TestContext> CreateAppLockToggleContextAsync(bool enabled, PreferredUnlockMethod method, string locale = "en")
    {
        if (enabled) return await CreateUnlockMethodChangeContextAsync(method, locale);
        var context = CreateContext(isConfigured: true, appLockEnabled: false, biometricAvailable: true,
            deviceCredentialAvailable: true, preferredUnlockMethod: method, cultureName: locale);
        await context.Sut.InitializeAsync();
        context.State.SetConfiguration(true, PreferredUnlockMethod.Password);
        await context.Sut.ShowSettingsAsync();
        return context;
    }
}
