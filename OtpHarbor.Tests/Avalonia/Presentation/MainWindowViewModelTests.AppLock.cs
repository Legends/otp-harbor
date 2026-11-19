using Moq;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed partial class MainWindowViewModelTests
{
    [Theory]
    [InlineData(AuthorizationResult.Success)]
    [InlineData(AuthorizationResult.Failed)]
    public async Task AppLock_ReactivationKeepsVerifiedSessionOpenAndFailureStillLocks(AuthorizationResult result)
    {
        var context = CreateAppLockShell();
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic", It.IsAny<CancellationToken>())).ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.SetAppLockEnabledAsync(true, "synthetic", It.IsAny<CancellationToken>()))
            .Callback(() => context.Settings.AppLockEnabled = true).ReturnsAsync(result);
        await sut.InitializeAsync();
        await sut.ShowSettingsAsync();
        Assert.False(sut.IsManualLockVisible);

        await sut.AuthorizationSettings.ToggleAppLockAsync();

        if (result == AuthorizationResult.Success)
        {
            context.Authorization.Verify(value => value.Lock(), Times.Never);
            Assert.True(sut.IsSettingsVisible);
            Assert.True(sut.IsShellVisible);
            Assert.False(sut.IsPasswordUnlockVisible);
            Assert.True(sut.IsManualLockVisible);
            await sut.CloseSettingsAsync();
            Assert.True(sut.LockCommand.CanExecute(null));
        }
        else
        {
            context.Authorization.Verify(value => value.Lock(), Times.Once);
            Assert.False(sut.IsSettingsVisible);
            Assert.False(sut.IsShellVisible);
            Assert.True(sut.IsPasswordUnlockVisible);
            Assert.False(sut.IsManualLockVisible);
        }
    }

    [Fact]
    public async Task AppLock_DisabledHidesManualLockAndSkipsMinimizeLock()
    {
        var context = CreateAppLockShell();
        using var sut = context.Sut;
        await sut.InitializeAsync();
        Assert.False(sut.IsManualLockVisible);
        Assert.False(sut.LockCommand.CanExecute(null));
        await sut.HandleWindowMinimizedAsync();
        context.Authorization.Verify(value => value.Lock(), Times.Never);
        Assert.True(sut.IsShellVisible);
    }

    private static (MainWindowViewModel Sut, Mock<IAuthorizationService> Authorization, AppSettings Settings) CreateAppLockShell()
    {
        var settings = new AppSettings { AppLockEnabled = false, LockOnMinimize = true };
        var preferences = new Mock<ISettingsService>();
        preferences.SetupGet(value => value.Current).Returns(settings);
        var state = CreateAuthorizationState(PreferredUnlockMethod.Password);
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("synthetic");
        var authorizationSettings = new AuthorizationSettingsViewModel(authorization.Object, dialogs.Object,
            CreateLocalization(), Mock.Of<IPasswordValidationService>(), settingsService: preferences.Object);
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        return (CreateSut(coordinator.Object, authorization.Object, authorizationSettings: authorizationSettings,
            settingsService: preferences.Object), authorization, settings);
    }
}
