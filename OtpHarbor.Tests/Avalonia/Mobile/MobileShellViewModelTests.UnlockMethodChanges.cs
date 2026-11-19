using Moq;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed partial class MobileShellViewModelTests
{
    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, PreferredUnlockMethod.PlatformDeviceCredential)]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, PreferredUnlockMethod.Password)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, PreferredUnlockMethod.Password)]
    public async Task UnlockMethodSelection_ImmediatelyUsesPreviousPlatformMethodWithoutChooser(
        PreferredUnlockMethod previous, PreferredUnlockMethod requested)
    {
        var context = await CreateUnlockMethodChangeContextAsync(previous);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ConfigureUnlockMethodWithPlatformAsync(requested, previous, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.True(sut.IsBusy);
                Assert.False(sut.HasAuthenticationMethodChoice);
                Assert.False(sut.IsBiometricEnrollmentVisible);
                Assert.Equal(previous, context.State.PreferredUnlockMethod);
                Assert.Equal(previous, sut.SelectedAuthenticationMethod?.Method);
                Assert.Empty(sut.BiometricRecoveryPassword);
                context.SettingsValue.PreferredUnlockMethod = requested;
                context.State.SetConfiguration(true, requested);
            }).ReturnsAsync(AuthorizationResult.Success);

        await SelectUnlockMethodAsync(sut, requested);

        Assert.Equal(requested, context.State.PreferredUnlockMethod);
        Assert.False(sut.IsBiometricEnrollmentVisible);
        Assert.False(sut.HasAuthenticationMethodChoice);
        Assert.Equal(context.Strings.Get(MobileStringKeys.UnlockMethodChanged), sut.NotificationText);
        context.Authorization.Verify(value => value.ConfigureUnlockMethodWithPlatformAsync(requested, previous, It.IsAny<CancellationToken>()), Times.Once);
        context.Authorization.Verify(value => value.ConfigureHelloAsync(It.IsAny<string>()), Times.Never);
        context.Authorization.Verify(value => value.ConfigureUnlockMethodAsync(It.IsAny<PreferredUnlockMethod>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(AuthorizationResult.Cancelled, false)]
    [InlineData(AuthorizationResult.Failed, false)]
    [InlineData(AuthorizationResult.NotAvailable, true)]
    [InlineData(AuthorizationResult.PasswordRequired, true)]
    public async Task UnlockMethodSelection_FailedCurrentAuthenticationKeepsPreviousMethod(
        AuthorizationResult result, bool passwordFallback)
    {
        var context = await CreateUnlockMethodChangeContextAsync(PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ConfigureUnlockMethodWithPlatformAsync(
                PreferredUnlockMethod.PlatformDeviceCredential, PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        await sut.BeginDeviceCredentialEnrollmentAsync();

        Assert.True(sut.IsBiometricUnlockSelected);
        Assert.False(sut.IsDeviceCredentialUnlockSelected);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, context.SettingsValue.PreferredUnlockMethod);
        Assert.False(sut.HasAuthenticationMethodChoice);
        Assert.Equal(passwordFallback, sut.IsBiometricEnrollmentVisible);
        Assert.Empty(sut.BiometricRecoveryPassword);
        Assert.False(sut.IsBusy);
        if (passwordFallback)
        {
            Assert.True(sut.IsPasswordAuthorizationSelected);
            Assert.False(sut.EnableBiometricCommand.CanExecute(null));
        }
        if (result == AuthorizationResult.Cancelled) Assert.Empty(sut.NotificationText);
    }

    [Fact]
    public async Task UnlockMethodSelection_PasswordFallbackStillVerifiesRecoveryPasswordBeforeApplying()
    {
        var context = await CreateUnlockMethodChangeContextAsync(PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ConfigureUnlockMethodWithPlatformAsync(
                PreferredUnlockMethod.PlatformDeviceCredential, PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.PasswordRequired);
        context.Authorization.Setup(value => value.ConfigureUnlockMethodAsync(
                PreferredUnlockMethod.PlatformDeviceCredential, "synthetic recovery"))
            .Callback(() => context.State.SetConfiguration(true, PreferredUnlockMethod.PlatformDeviceCredential))
            .ReturnsAsync(AuthorizationResult.Success);
        await sut.BeginDeviceCredentialEnrollmentAsync();
        Assert.True(sut.IsPasswordAuthorizationSelected);
        Assert.False(sut.HasAuthenticationMethodChoice);
        sut.BiometricRecoveryPassword = "synthetic recovery";

        await sut.EnableBiometricAsync();

        context.Authorization.Verify(value => value.ConfigureUnlockMethodAsync(
            PreferredUnlockMethod.PlatformDeviceCredential, "synthetic recovery"), Times.Once);
        Assert.True(sut.IsDeviceCredentialUnlockSelected);
        Assert.Empty(sut.BiometricRecoveryPassword);
    }

    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential)]
    public async Task UnlockMethodSelection_UnavailableCurrentMethodOffersPasswordRecoveryWithoutChooser(PreferredUnlockMethod previous)
    {
        var context = await CreateUnlockMethodChangeContextAsync(previous);
        using var sut = context.Sut;
        if (previous == PreferredUnlockMethod.PlatformQuickUnlock)
            context.Authorization.Setup(value => value.IsHelloAvailableAsync()).ReturnsAsync(false);
        else
            context.Authorization.Setup(value => value.IsUnlockMethodAvailableAsync(previous)).ReturnsAsync(false);

        await sut.SelectPasswordUnlockAsync();

        Assert.Equal(previous, context.State.PreferredUnlockMethod);
        Assert.True(sut.IsBiometricEnrollmentVisible);
        Assert.True(sut.IsPasswordAuthorizationSelected);
        Assert.False(sut.HasAuthenticationMethodChoice);
        context.Authorization.Verify(value => value.ConfigureUnlockMethodWithPlatformAsync(
            It.IsAny<PreferredUnlockMethod>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnlockMethodSelection_LockCancelsPendingPromptAndPreventsLateUiSuccess()
    {
        var context = await CreateUnlockMethodChangeContextAsync(PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.Lock()).Callback(context.State.Lock);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Authorization.Setup(value => value.ConfigureUnlockMethodWithPlatformAsync(
                PreferredUnlockMethod.PlatformDeviceCredential, PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .Returns((PreferredUnlockMethod _, PreferredUnlockMethod _, CancellationToken token) =>
            {
                entered.SetResult(token);
                return complete.Task;
            });
        var selecting = sut.BeginDeviceCredentialEnrollmentAsync();
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);

        await sut.LockAsync();
        Assert.True(token.IsCancellationRequested);
        complete.SetResult(AuthorizationResult.Success);
        await selecting;

        Assert.False(context.State.IsUnlocked);
        Assert.True(sut.IsUnlockVisible);
        Assert.False(sut.IsBiometricEnrollmentVisible);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, context.SettingsValue.PreferredUnlockMethod);
        Assert.NotEqual(context.Strings.Get(MobileStringKeys.UnlockMethodChanged), sut.NotificationText);
    }

    [Fact]
    public async Task UnlockMethodSelection_SecondTapDoesNotStartAnotherPromptOrChangePendingTarget()
    {
        var context = await CreateUnlockMethodChangeContextAsync(PreferredUnlockMethod.PlatformQuickUnlock);
        using var sut = context.Sut;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Authorization.Setup(value => value.ConfigureUnlockMethodWithPlatformAsync(
                PreferredUnlockMethod.PlatformDeviceCredential, PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .Returns(() => { entered.SetResult(); return complete.Task; });
        var selecting = sut.BeginDeviceCredentialEnrollmentAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);

        await sut.SelectPasswordUnlockAsync();
        complete.SetResult(AuthorizationResult.Cancelled);
        await selecting;

        context.Authorization.Verify(value => value.ConfigureUnlockMethodWithPlatformAsync(
            It.IsAny<PreferredUnlockMethod>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(sut.IsBiometricUnlockSelected);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public async Task UnlockMethodSelection_PasswordEntryUsesActiveLocaleAndNoAuthenticationChooser(string locale)
    {
        var context = await CreateUnlockMethodChangeContextAsync(PreferredUnlockMethod.Password, locale);
        using var sut = context.Sut;

        await sut.BeginDeviceCredentialEnrollmentAsync();

        Assert.True(sut.IsBiometricEnrollmentVisible);
        Assert.True(sut.IsPasswordAuthorizationSelected);
        Assert.False(sut.HasAuthenticationMethodChoice);
        Assert.False(sut.EnableBiometricCommand.CanExecute(null));
        Assert.Equal(context.Strings.Get(MobileStringKeys.UnlockMethodPasswordPrompt), sut.UnlockMethodPasswordPromptText);
        context.Authorization.Verify(value => value.ConfigureUnlockMethodWithPlatformAsync(
            It.IsAny<PreferredUnlockMethod>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<TestContext> CreateUnlockMethodChangeContextAsync(PreferredUnlockMethod previous, string locale = "en")
    {
        var context = CreateContext(isConfigured: true, biometricAvailable: true, deviceCredentialAvailable: true,
            preferredUnlockMethod: previous, cultureName: locale);
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic session"))
            .Callback(context.State.Unlock).ReturnsAsync(AuthorizationResult.Success);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic session";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();
        return context;
    }

    private static Task SelectUnlockMethodAsync(MobileShellViewModel sut, PreferredUnlockMethod method) => method switch
    {
        PreferredUnlockMethod.PlatformQuickUnlock => sut.BeginBiometricEnrollmentAsync(),
        PreferredUnlockMethod.PlatformDeviceCredential => sut.BeginDeviceCredentialEnrollmentAsync(),
        _ => sut.SelectPasswordUnlockAsync()
    };
}
