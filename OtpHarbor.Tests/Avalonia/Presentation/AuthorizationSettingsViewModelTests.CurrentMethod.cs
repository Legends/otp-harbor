using Avalonia.Controls;
using Moq;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed partial class AuthorizationSettingsViewModelTests
{
    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, AuthorizationResult.Cancelled)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, AuthorizationResult.Cancelled)]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, AuthorizationResult.Failed)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, AuthorizationResult.TooManyAttempts)]
    public async Task UsePassword_CurrentPlatformRejectionPreservesPreferenceWithoutPasswordPrompt(
        PreferredUnlockMethod previous, AuthorizationResult result)
    {
        var state = ConfiguredState(previous);
        var authorization = Authorization(state);
        authorization.Setup(value => value.ConfigureUnlockMethodWithPlatformAsync(
                PreferredUnlockMethod.Password, previous, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        var dialogs = new Mock<IAvaloniaDialogService>();
        using var sut = CreateSut(authorization.Object, dialogs.Object);
        await sut.RefreshAsync();

        await sut.UsePasswordAsync();

        Assert.True(sut.IsQuickUnlockEnabled);
        Assert.Equal(previous, state.PreferredUnlockMethod);
        Assert.Equal(result == AuthorizationResult.Cancelled ? AvaloniaStringKeys.PasswordPreferenceUnchanged
            : AvaloniaStringKeys.PasswordPreferenceFailed, sut.Message);
        dialogs.Verify(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        authorization.Verify(value => value.SetGateAsync(It.IsAny<AuthorizationGateKind>()), Times.Never);
    }

    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential)]
    public async Task UsePassword_CurrentPlatformSuccessUpdatesAuthoritativeSelectionWithoutPassword(PreferredUnlockMethod previous)
    {
        var state = ConfiguredState(previous);
        var authorization = Authorization(state);
        authorization.Setup(value => value.ConfigureUnlockMethodWithPlatformAsync(
                PreferredUnlockMethod.Password, previous, It.IsAny<CancellationToken>()))
            .Callback(() => state.SetConfiguration(true, PreferredUnlockMethod.Password)).ReturnsAsync(AuthorizationResult.Success);
        var dialogs = new Mock<IAvaloniaDialogService>();
        using var sut = CreateSut(authorization.Object, dialogs.Object);
        await sut.RefreshAsync();

        await sut.UsePasswordAsync();

        Assert.False(sut.IsQuickUnlockEnabled);
        Assert.Equal(AvaloniaStringKeys.PasswordPreferred, sut.Message);
        dialogs.Verify(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        authorization.Verify(value => value.ConfigureUnlockMethodWithPlatformAsync(
            PreferredUnlockMethod.Password, previous, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("en", AuthorizationResult.PasswordRequired)]
    [InlineData("de", AuthorizationResult.NotAvailable)]
    [InlineData("fr", AuthorizationResult.DisabledByPolicy)]
    [InlineData("es", AuthorizationResult.PasswordRequired)]
    public async Task UsePassword_UnavailablePlatformUsesLocalizedVerifiedRecovery(string locale, AuthorizationResult result)
    {
        var state = ConfiguredState(PreferredUnlockMethod.PlatformQuickUnlock);
        var authorization = Authorization(state);
        authorization.Setup(value => value.ConfigureUnlockMethodWithPlatformAsync(
                PreferredUnlockMethod.Password, PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic recovery")).ReturnsAsync(AuthorizationResult.Success);
        authorization.Setup(value => value.ConfigureUnlockMethodAsync(PreferredUnlockMethod.Password, "synthetic recovery"))
            .Callback(() => state.SetConfiguration(true, PreferredUnlockMethod.Password)).ReturnsAsync(AuthorizationResult.Success);
        var localization = new AvaloniaLocalizationService(new ResourceDictionary(), new AvaloniaStringCatalog());
        localization.ApplyCulture(locale);
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (PasswordDialogRequest request, CancellationToken token) =>
            {
                Assert.Equal(localization.GetString(AvaloniaStringKeys.PasswordPreferencePrompt), request.Message);
                Assert.Equal(localization.GetString(AvaloniaStringKeys.UsePasswordAtStartup), request.Title);
                Assert.Null(await request.ValidateAsync!("synthetic recovery", token));
                Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, state.PreferredUnlockMethod);
                return "synthetic recovery";
            });
        var validation = new Mock<IPasswordValidationService>();
        using var sut = new AuthorizationSettingsViewModel(authorization.Object, dialogs.Object, localization, validation.Object);
        await sut.RefreshAsync();

        await sut.UsePasswordAsync();

        Assert.False(sut.IsQuickUnlockEnabled);
        Assert.Equal(localization.GetString(AvaloniaStringKeys.PasswordPreferred), sut.Message);
        authorization.Verify(value => value.ConfigureUnlockMethodAsync(PreferredUnlockMethod.Password, "synthetic recovery"), Times.Once);
        authorization.Verify(value => value.SetGateAsync(It.IsAny<AuthorizationGateKind>()), Times.Never);
    }

    [Theory]
    [InlineData(AuthorizationResult.Cancelled)]
    [InlineData(AuthorizationResult.InvalidCredentials)]
    public async Task UsePassword_CancelledOrRejectedRecoveryDoesNotChangePreference(AuthorizationResult result)
    {
        var state = ConfiguredState(PreferredUnlockMethod.PlatformQuickUnlock);
        var authorization = Authorization(state);
        authorization.Setup(value => value.ConfigureUnlockMethodAsync(PreferredUnlockMethod.Password, "synthetic recovery"))
            .ReturnsAsync(AuthorizationResult.InvalidCredentials);
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result == AuthorizationResult.Cancelled ? null : "synthetic recovery");
        using var sut = CreateSut(authorization.Object, dialogs.Object);
        await sut.RefreshAsync();

        await sut.UsePasswordAsync();

        Assert.True(sut.IsQuickUnlockEnabled);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, state.PreferredUnlockMethod);
        authorization.Verify(value => value.SetGateAsync(It.IsAny<AuthorizationGateKind>()), Times.Never);
    }

    [Fact]
    public async Task EnableQuickUnlock_ExistingWrapperCannotBypassCurrentPassword()
    {
        var state = ConfiguredState(PreferredUnlockMethod.Password);
        var authorization = Authorization(state);
        authorization.Setup(value => value.IsHelloAvailableAsync()).ReturnsAsync(true);
        authorization.Setup(value => value.SetGateAsync(AuthorizationGateKind.Hello)).ReturnsAsync(AuthorizationResult.Success);
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        using var sut = CreateSut(authorization.Object, dialogs.Object);
        await sut.RefreshAsync();

        await sut.EnableQuickUnlockAsync();

        Assert.False(sut.IsQuickUnlockEnabled);
        dialogs.Verify(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        authorization.Verify(value => value.SetGateAsync(It.IsAny<AuthorizationGateKind>()), Times.Never);
        authorization.Verify(value => value.ConfigureHelloAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("lock", false)]
    [InlineData("close", false)]
    [InlineData("dispose", false)]
    [InlineData("lock", true)]
    [InlineData("close", true)]
    [InlineData("dispose", true)]
    public async Task UsePassword_SessionClosureCancelsPlatformPromptAndIgnoresLateCompletion(string close, bool throws)
    {
        var state = ConfiguredState(PreferredUnlockMethod.PlatformQuickUnlock);
        var authorization = Authorization(state);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        authorization.Setup(value => value.ConfigureUnlockMethodWithPlatformAsync(
                PreferredUnlockMethod.Password, PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .Returns((PreferredUnlockMethod _, PreferredUnlockMethod _, CancellationToken token) => { entered.SetResult(token); return complete.Task; });
        var dialogs = new Mock<IAvaloniaDialogService>();
        using var sut = CreateSut(authorization.Object, dialogs.Object);
        await sut.RefreshAsync();
        var changing = sut.UsePasswordAsync();
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        await sut.UsePasswordAsync();
        Assert.False(sut.UsePasswordCommand.CanExecute(null));
        if (close == "lock") state.Lock();
        else if (close == "close") sut.ClearSensitiveInputs();
        else sut.Dispose();
        Assert.True(token.IsCancellationRequested);
        if (throws) complete.SetException(new InvalidOperationException("Synthetic provider shutdown failure"));
        else complete.SetResult(AuthorizationResult.Success);
        await changing;

        Assert.True(sut.IsQuickUnlockEnabled);
        Assert.Empty(sut.Message);
        authorization.Verify(value => value.ConfigureUnlockMethodWithPlatformAsync(
            PreferredUnlockMethod.Password, PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()), Times.Once);
        dialogs.Verify(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task EnableQuickUnlock_ClosingDuringPasswordDialogPreventsLateEnrollment(string close)
    {
        var state = ConfiguredState(PreferredUnlockMethod.Password);
        var authorization = Authorization(state);
        authorization.Setup(value => value.IsHelloAvailableAsync()).ReturnsAsync(true);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()))
            .Returns((PasswordDialogRequest _, CancellationToken token) => { entered.SetResult(token); return complete.Task; });
        using var sut = CreateSut(authorization.Object, dialogs.Object);
        await sut.RefreshAsync();
        var changing = sut.EnableQuickUnlockAsync();
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        await sut.EnableQuickUnlockAsync();
        if (close == "lock") state.Lock();
        else if (close == "close") sut.ClearSensitiveInputs();
        else sut.Dispose();
        Assert.True(token.IsCancellationRequested);
        complete.SetResult("synthetic recovery");
        await changing;

        Assert.Empty(sut.Message);
        Assert.False(sut.IsQuickUnlockEnabled);
        authorization.Verify(value => value.ConfigureHelloAsync(It.IsAny<string>()), Times.Never);
        dialogs.Verify(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
