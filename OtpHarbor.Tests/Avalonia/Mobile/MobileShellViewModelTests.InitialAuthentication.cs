using Moq;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed partial class MobileShellViewModelTests
{
    [Fact]
    public async Task NewInstallation_AllowsScreenshotsByDefault()
    {
        var context = CreateContext(isConfigured: false);
        using var sut = context.Sut;
        Assert.True(context.SettingsValue.AllowScreenshots);
        await sut.InitializeAsync();
        Assert.True(sut.AllowScreenshots);
        Assert.False(sut.IsScreenCaptureProtectionRequired);
    }

    [Theory]
    [InlineData("en", "Deactivate")]
    [InlineData("de", "Deaktivieren")]
    [InlineData("fr", "Désactiver")]
    [InlineData("es", "Desactivar")]
    public void AppLockConfirmation_UsesShortLocalizedActionWithoutChangingMainAction(string culture, string confirmation)
    {
        var context = CreateContext(isConfigured: true, cultureName: culture);
        using var sut = context.Sut;
        Assert.Equal(confirmation, sut.DeactivateAppLockConfirmationText);
        Assert.Equal(context.Strings.Get(MobileStringKeys.DisableAppLock), sut.AppLockActionText);
        Assert.NotEqual(sut.DeactivateAppLockConfirmationText, sut.AppLockActionText);
    }

    [Theory]
    [InlineData(AuthorizationResult.Success)]
    [InlineData(AuthorizationResult.Failed)]
    [InlineData(AuthorizationResult.Cancelled)]
    public async Task FirstSetup_NoAppLock_UsesInitialGrantAndKeepsPasswordProtectionOnFailure(AuthorizationResult outcome)
    {
        var context = CreateContext(isConfigured: false);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ConfigurePasswordAsync("synthetic setup", "synthetic setup"))
            .Callback(() => { context.State.SetConfiguration(true, PreferredUnlockMethod.Password); context.State.Unlock(); })
            .ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.CompleteInitialAppLockSetupAsync(
                InitialAppLockChoice.AppLockDisabled, It.IsAny<CancellationToken>()))
            .Callback(() => { if (outcome == AuthorizationResult.Success) context.SettingsValue.AppLockEnabled = false; })
            .ReturnsAsync(outcome);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();
        sut.SetupPassword = "synthetic setup";
        sut.SetupConfirmation = "synthetic setup";
        await sut.ConfigureAsync();

        Assert.True(sut.IsInitialQuickUnlockOfferVisible);
        Assert.True(sut.ChooseInitialMasterPasswordCommand.CanExecute(null));
        Assert.True(sut.ChooseInitialNoAppLockCommand.CanExecute(null));
        sut.ChooseInitialNoAppLockCommand.Execute(null);
        await WaitUntilAsync(() => !sut.IsBusy);

        Assert.False(sut.IsInitialQuickUnlockOfferVisible);
        Assert.True(sut.IsAccountListVisible);
        Assert.Equal(outcome != AuthorizationResult.Success, sut.IsAppLockEnabled);
        Assert.True(sut.IsPasswordUnlockSelected);
        Assert.Empty(sut.SetupPassword);
        Assert.Empty(sut.SetupConfirmation);
        context.Authorization.Verify(value => value.CompleteInitialAppLockSetupAsync(
            InitialAppLockChoice.AppLockDisabled, It.IsAny<CancellationToken>()), Times.Once);
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
        context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(
            It.IsAny<PreferredUnlockMethod?>(), It.IsAny<CancellationToken>()), Times.Never);
        if (outcome == AuthorizationResult.Failed)
            Assert.Equal(context.Strings.Get(MobileStringKeys.AppLockChangeFailed), sut.NotificationText);
        else
            Assert.Empty(sut.NotificationText);
    }
}
