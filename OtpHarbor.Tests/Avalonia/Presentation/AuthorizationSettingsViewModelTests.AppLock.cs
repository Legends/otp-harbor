using Avalonia.Controls;
using Moq;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed partial class AuthorizationSettingsViewModelTests
{
    [Theory]
    [InlineData(true, PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(false, PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(true, PreferredUnlockMethod.PlatformDeviceCredential)]
    [InlineData(false, PreferredUnlockMethod.PlatformDeviceCredential)]
    public async Task AppLock_UsesCurrentOrSavedPlatformMethodWithoutPassword(bool enabled, PreferredUnlockMethod method)
    {
        var context = AppLockContext(enabled, method);
        using var sut = context.Sut;
        var changes = 0;
        sut.AppLockChanged += (_, _) => changes++;
        if (enabled)
            context.Authorization.Setup(value => value.SetAppLockEnabledWithPlatformAsync(false, method, It.IsAny<CancellationToken>()))
                .Callback(() => context.Settings.AppLockEnabled = false).ReturnsAsync(AuthorizationResult.Success);
        else
            context.Authorization.Setup(value => value.ReactivateAppLockWithPlatformAsync(method, It.IsAny<CancellationToken>()))
                .Callback(() => context.Settings.AppLockEnabled = true).ReturnsAsync(AuthorizationResult.Success);
        await sut.RefreshAsync();

        await sut.ToggleAppLockAsync();

        Assert.Equal(!enabled, sut.IsAppLockEnabled);
        Assert.Equal(method, context.Settings.PreferredUnlockMethod);
        Assert.Equal(1, changes);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(false, method, It.IsAny<CancellationToken>()), enabled ? Times.Once() : Times.Never());
        context.Authorization.Verify(value => value.ReactivateAppLockWithPlatformAsync(method, It.IsAny<CancellationToken>()), enabled ? Times.Never() : Times.Once());
        Assert.False(sut.RequiresLockAfterAppLockChange);
        context.Dialogs.Verify(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(false, "de")]
    [InlineData(true, "fr")]
    [InlineData(false, "es")]
    public async Task AppLock_PasswordChangeUsesCompleteActiveLocalePrompt(bool enabled, string locale)
    {
        var context = AppLockContext(enabled, PreferredUnlockMethod.Password, locale);
        using var sut = context.Sut;
        context.Dialogs.Setup(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()))
            .Returns((PasswordDialogRequest request, CancellationToken _) =>
            {
                Assert.Equal(context.Localization.GetString(enabled ? AvaloniaStringKeys.DisableAppLock : AvaloniaStringKeys.EnableAppLock), request.Title);
                Assert.Equal(context.Localization.GetString(enabled ? AvaloniaStringKeys.DisableAppLockWarning : AvaloniaStringKeys.EnableAppLockPasswordPrompt), request.Message);
                return Task.FromResult<string?>("synthetic recovery");
            });
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic recovery", It.IsAny<CancellationToken>())).ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.SetAppLockEnabledAsync(!enabled, "synthetic recovery", It.IsAny<CancellationToken>()))
            .Callback(() => context.Settings.AppLockEnabled = !enabled).ReturnsAsync(AuthorizationResult.Success);
        await sut.RefreshAsync();

        await sut.ToggleAppLockAsync();

        Assert.Equal(!enabled, sut.IsAppLockEnabled);
        Assert.Equal(context.Localization.GetString(enabled ? AvaloniaStringKeys.AppLockDisabled : AvaloniaStringKeys.AppLockEnabled), sut.Message);
        context.Authorization.Verify(value => value.TryUnlockWithPasswordAsync("synthetic recovery", It.IsAny<CancellationToken>()), enabled ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData(AuthorizationResult.Cancelled)]
    [InlineData(AuthorizationResult.Failed)]
    [InlineData(AuthorizationResult.InvalidCredentials)]
    [InlineData(AuthorizationResult.TooManyAttempts)]
    public async Task AppLock_PlatformRejectionNeverFallsBackOrChangesPolicy(AuthorizationResult result)
    {
        var context = AppLockContext(true, PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.SetAppLockEnabledWithPlatformAsync(false,
            PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        await sut.RefreshAsync();
        await sut.ToggleAppLockAsync();
        Assert.True(sut.IsAppLockEnabled);
        context.Dialogs.Verify(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AuthorizationResult.NotAvailable)]
    [InlineData(AuthorizationResult.PasswordRequired)]
    [InlineData(AuthorizationResult.DisabledByPolicy)]
    public async Task AppLock_UnavailableCurrentMethodOffersRecoveryPassword(AuthorizationResult result)
    {
        var context = AppLockContext(true, PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.SetAppLockEnabledWithPlatformAsync(false,
            PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        context.Dialogs.Setup(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("synthetic");
        context.Authorization.Setup(value => value.SetAppLockEnabledAsync(false, "synthetic", It.IsAny<CancellationToken>()))
            .Callback(() => context.Settings.AppLockEnabled = false).ReturnsAsync(AuthorizationResult.Success);
        await sut.RefreshAsync();
        await sut.ToggleAppLockAsync();
        Assert.False(sut.IsAppLockEnabled);
    }

    [Fact]
    public async Task AppLock_InvalidRecoveryCannotRemoveUnattendedAccess()
    {
        var context = AppLockContext(false, PreferredUnlockMethod.Password);
        using var sut = context.Sut;
        context.Dialogs.Setup(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("wrong");
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("wrong", It.IsAny<CancellationToken>())).ReturnsAsync(AuthorizationResult.InvalidCredentials);
        await sut.RefreshAsync();
        await sut.ToggleAppLockAsync();
        Assert.False(sut.IsAppLockEnabled);
        context.Authorization.Verify(value => value.SetAppLockEnabledAsync(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(context.Localization.GetString(AvaloniaStringKeys.PasswordVerificationFailed), sut.Message);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("lock")]
    [InlineData("dispose")]
    public async Task AppLock_CancellationAndRepeatedTapsCannotApplyLateSuccess(string source)
    {
        var context = AppLockContext(true, PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        var pending = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken operation = default;
        context.Authorization.Setup(value => value.SetAppLockEnabledWithPlatformAsync(false,
                PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .Callback((bool _, PreferredUnlockMethod _, CancellationToken token) => operation = token).Returns(pending.Task);
        await sut.RefreshAsync();
        var change = sut.ToggleAppLockAsync();
        Assert.False(sut.ToggleAppLockCommand.CanExecute(null));
        await sut.ToggleAppLockAsync();
        if (source == "lock") context.State.Lock();
        else if (source == "dispose") sut.Dispose();
        else sut.ClearSensitiveInputs();
        Assert.True(operation.IsCancellationRequested);
        pending.SetResult(AuthorizationResult.Success);
        await change;
        Assert.True(sut.IsAppLockEnabled);
        Assert.Empty(sut.Message);
        context.Authorization.Verify(value => value.SetAppLockEnabledWithPlatformAsync(false,
            PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AppLock_UnavailableStoreCannotDisableProtection()
    {
        var context = AppLockContext(true, PreferredUnlockMethod.Password);
        using var sut = context.Sut;
        context.Platform.Setup(value => value.GetAvailabilityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PlatformQuickUnlockAvailability.NotSupported);
        await sut.RefreshAsync();
        Assert.False(sut.ToggleAppLockCommand.CanExecute(null));
        await sut.ToggleAppLockAsync();
        Assert.True(sut.IsAppLockEnabled);
        Assert.Equal(context.Localization.GetString(AvaloniaStringKeys.AppLockUnavailable), sut.AppLockDescription);
        context.Dialogs.Verify(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AppLock_FailedClosedActivationStillNotifiesShellToLock()
    {
        var context = AppLockContext(false, PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        var changes = 0;
        sut.AppLockChanged += (_, _) => changes++;
        context.Authorization.Setup(value => value.ReactivateAppLockWithPlatformAsync(
                PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .Callback(() => context.Settings.AppLockEnabled = true).ReturnsAsync(AuthorizationResult.Failed);
        await sut.RefreshAsync();
        await sut.ToggleAppLockAsync();
        Assert.True(sut.IsAppLockEnabled);
        Assert.Equal(1, changes);
        Assert.True(sut.RequiresLockAfterAppLockChange);
        Assert.Equal(context.Localization.GetString(AvaloniaStringKeys.AppLockChangeFailed), sut.Message);
    }

    private static AppLockTestContext AppLockContext(bool enabled, PreferredUnlockMethod method, string locale = "en")
    {
        var settings = new AppSettings { AppLockEnabled = enabled, PreferredUnlockMethod = method };
        var preferences = new Mock<ISettingsService>();
        preferences.SetupGet(value => value.Current).Returns(settings);
        var state = ConfiguredState(enabled ? method : PreferredUnlockMethod.Password);
        var auth = Authorization(state);
        var dialogs = new Mock<IAvaloniaDialogService>();
        var platform = new Mock<IPlatformUnattendedUnlock>();
        platform.Setup(value => value.GetAvailabilityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        var localization = new AvaloniaLocalizationService(new ResourceDictionary(), new AvaloniaStringCatalog());
        localization.ApplyCulture(locale);
        var sut = new AuthorizationSettingsViewModel(auth.Object, dialogs.Object, localization,
            Mock.Of<IPasswordValidationService>(), settingsService: preferences.Object, unattendedUnlock: platform.Object);
        return new(sut, auth, dialogs, platform, settings, state, localization);
    }

    private sealed record AppLockTestContext(AuthorizationSettingsViewModel Sut, Mock<IAuthorizationService> Authorization,
        Mock<IAvaloniaDialogService> Dialogs, Mock<IPlatformUnattendedUnlock> Platform, AppSettings Settings,
        AuthorizationState State, AvaloniaLocalizationService Localization);
}
