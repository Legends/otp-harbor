using Avalonia.Controls;
using FluentResults;
using Moq;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed partial class MainWindowViewModelTests
{
    [Theory]
    [InlineData(false, false, false, null)]
    [InlineData(true, false, true, PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(false, true, true, PreferredUnlockMethod.PlatformDeviceCredential)]
    [InlineData(true, false, false, null)]
    [InlineData(false, true, false, null)]
    public async Task FirstPasswordSetup_OffersOnlyAvailableMethodsAfterPasswordCreation(
        bool quickUnlock, bool credential, bool accept, PreferredUnlockMethod? expected)
    {
        var context = CreateInitialSetup(quickUnlock, credential);
        using var sut = context.Sut;
        context.Dialogs.Setup(value => value.ConfirmAsync(It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.True(context.State.IsConfigured);
                Assert.True(context.State.IsUnlocked);
                Assert.Empty(sut.PasswordSetup.Password);
                Assert.Empty(sut.PasswordSetup.ConfirmPassword);
                Assert.False(sut.IsSettingsVisible);
            }).ReturnsAsync(accept);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();
        context.Dialogs.VerifyNoOtherCalls();

        await CreateFirstPasswordAsync(sut);

        Assert.True(sut.IsShellVisible);
        context.Dialogs.Verify(value => value.ConfirmAsync(It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()),
            quickUnlock || credential ? Times.Once() : Times.Never());
        context.Dialogs.Verify(value => value.ChooseAsync(It.IsAny<ChoiceDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(expected, It.IsAny<CancellationToken>()), Times.Once);
        context.Authorization.Verify(value => value.ConfigureHelloAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(ChoiceDialogResult.Primary, PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(ChoiceDialogResult.Secondary, PreferredUnlockMethod.PlatformDeviceCredential)]
    [InlineData(ChoiceDialogResult.Cancel, null)]
    public async Task FirstPasswordSetup_BothAvailableMethodsAndSkipRemainExplicitChoices(
        ChoiceDialogResult choice, PreferredUnlockMethod? expected)
    {
        var context = CreateInitialSetup(true, true);
        using var sut = context.Sut;
        context.Dialogs.Setup(value => value.ChooseAsync(It.IsAny<ChoiceDialogRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(choice);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();

        await CreateFirstPasswordAsync(sut);

        context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(expected, It.IsAny<CancellationToken>()), Times.Once);
        context.Dialogs.Verify(value => value.ConfirmAsync(It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(sut.IsShellVisible);
        Assert.False(sut.IsSettingsVisible);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public async Task FirstPasswordSetup_DeclinedOrFailedEnrollmentKeepsPasswordVaultAndUsesActiveLocale(string culture)
    {
        var localization = new AvaloniaLocalizationService(new ResourceDictionary(), new AvaloniaStringCatalog());
        localization.ApplyCulture(culture);
        var context = CreateInitialSetup(true, false, localization);
        using var sut = context.Sut;
        ConfirmationDialogRequest? offered = null;
        context.Dialogs.Setup(value => value.ConfirmAsync(It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ConfirmationDialogRequest, CancellationToken>((request, _) => offered = request).ReturnsAsync(true);
        context.Authorization.Setup(value => value.CompleteInitialQuickUnlockSetupAsync(
                PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.Failed);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();

        await CreateFirstPasswordAsync(sut);

        Assert.NotNull(offered);
        Assert.Equal(localization.GetString(AvaloniaStringKeys.InitialQuickUnlockTitle), offered.Title);
        Assert.Equal(localization.GetString(AvaloniaStringKeys.InitialQuickUnlockDescription), offered.Message);
        Assert.Equal(localization.GetString(AvaloniaStringKeys.EnableQuickUnlock), offered.ConfirmText);
        Assert.Equal(localization.GetString(AvaloniaStringKeys.NotNow), offered.CancelText);
        Assert.Equal(localization.GetString(AvaloniaStringKeys.QuickUnlockEnrollmentFailed), sut.StatusText);
        Assert.True(sut.IsShellVisible);
        Assert.True(context.State.IsUnlocked);
        Assert.Equal(PreferredUnlockMethod.Password, context.State.PreferredUnlockMethod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstPasswordSetup_LockCancelsPendingOfferOrEnrollmentAndIgnoresLateSuccess(bool enrolling)
    {
        var context = CreateInitialSetup(true, false);
        using var sut = context.Sut;
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completePrompt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeEnrollment = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Dialogs.Setup(value => value.ConfirmAsync(It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ConfirmationDialogRequest _, CancellationToken token) =>
            {
                if (enrolling) return Task.FromResult(true);
                entered.SetResult(token);
                return completePrompt.Task;
            });
        context.Authorization.Setup(value => value.CompleteInitialQuickUnlockSetupAsync(
                PreferredUnlockMethod.PlatformQuickUnlock, It.IsAny<CancellationToken>()))
            .Returns((PreferredUnlockMethod? _, CancellationToken token) =>
            {
                entered.SetResult(token);
                return completeEnrollment.Task;
            });
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();
        var configuring = CreateFirstPasswordAsync(sut);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);

        await sut.LockAsync();
        Assert.True(token.IsCancellationRequested);
        completePrompt.TrySetResult(true);
        completeEnrollment.TrySetResult(AuthorizationResult.Success);
        await configuring.WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(context.State.IsUnlocked);
        Assert.False(sut.IsShellVisible);
        if (!enrolling)
            context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(
                It.IsAny<PreferredUnlockMethod?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FirstPasswordSetup_OneUnavailableProviderDoesNotHideAnotherAvailableMethod()
    {
        var context = CreateInitialSetup(false, true);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.IsHelloAvailableAsync())
            .ThrowsAsync(new InvalidOperationException("Synthetic unavailable provider"));
        context.Dialogs.Setup(value => value.ConfirmAsync(It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ConfirmationDialogRequest, CancellationToken>((request, _) =>
                Assert.Equal(AvaloniaStringKeys.DeviceScreenLock, request.ConfirmText)).ReturnsAsync(true);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();

        await CreateFirstPasswordAsync(sut);

        context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(
            PreferredUnlockMethod.PlatformDeviceCredential, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FirstPasswordSetup_FailedPasswordCreationNeverOffersQuickUnlock()
    {
        var context = CreateInitialSetup(true, true);
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Failed);
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();

        await CreateFirstPasswordAsync(sut);

        context.Dialogs.VerifyNoOtherCalls();
        context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(
            It.IsAny<PreferredUnlockMethod?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(sut.IsShellVisible);
    }

    [Fact]
    public async Task ExistingVaultStartupAndLaterUnlock_DoNotRepeatFirstSetupOffer()
    {
        var context = CreateInitialSetup(true, false);
        using var sut = context.Sut;
        await sut.InitializeAsync();
        await sut.FinishOnboardingAsync();
        await CreateFirstPasswordAsync(sut);
        await sut.LockAsync();
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic recovery"))
            .Callback(context.State.Unlock).ReturnsAsync(AuthorizationResult.Success);
        sut.PasswordUnlock.Password = "synthetic recovery";

        await sut.PasswordUnlock.UnlockAsync();
        await WaitUntilAsync(() => sut.IsShellVisible);
        context.Coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        await sut.InitializeAsync();

        context.Dialogs.Verify(value => value.ConfirmAsync(It.IsAny<ConfirmationDialogRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task CreateFirstPasswordAsync(MainWindowViewModel sut)
    {
        sut.PasswordSetup.Password = "synthetic recovery";
        sut.PasswordSetup.ConfirmPassword = "synthetic recovery";
        await sut.PasswordSetup.ConfigureAsync();
    }

    private static (MainWindowViewModel Sut, Mock<IAuthorizationService> Authorization,
        Mock<IAvaloniaDialogService> Dialogs, AuthorizationState State, Mock<IAvaloniaStartupCoordinator> Coordinator)
        CreateInitialSetup(bool quickUnlock, bool credential, IAvaloniaLocalizationService? localization = null)
    {
        var state = new AuthorizationState();
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { state.SetConfiguration(true, PreferredUnlockMethod.Password); state.Unlock(); })
            .ReturnsAsync(AuthorizationResult.Success);
        authorization.Setup(value => value.IsHelloAvailableAsync()).ReturnsAsync(quickUnlock);
        authorization.Setup(value => value.IsUnlockMethodAvailableAsync(PreferredUnlockMethod.PlatformDeviceCredential)).ReturnsAsync(credential);
        authorization.Setup(value => value.CompleteInitialQuickUnlockSetupAsync(It.IsAny<PreferredUnlockMethod?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.Success);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AvaloniaStartupOutcome.ReadyForPasswordSetup);
        var dialogs = new Mock<IAvaloniaDialogService>();
        return (CreateSut(coordinator.Object, authorization.Object, accountManager: accounts.Object,
            dialogs: dialogs.Object, localization: localization), authorization, dialogs, state, coordinator);
    }
}
