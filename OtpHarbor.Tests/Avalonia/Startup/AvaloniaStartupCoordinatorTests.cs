using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Startup;

public sealed class AvaloniaStartupCoordinatorTests
{
    [Fact]
    public async Task InitializeAsync_WhenAuthorizationIsConfigured_IsReadyForUnlock()
    {
        var state = new AuthorizationState();
        state.SetConfiguration(true, TOTP.Core.Enums.PreferredUnlockMethod.Password);
        var (sut, settings, authorization) = CreateSut(state);

        var outcome = await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AvaloniaStartupOutcome.ReadyForUnlock, outcome);
        settings.Verify(service => service.LoadAsync(), Times.Once);
        authorization.Verify(service => service.InitializeAsync(), Times.Once);
        authorization.Verify(service => service.TryUnlockOnStartupAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_WhenPreferredQuickUnlockSucceeds_IsReadyUnlocked()
    {
        var state = new AuthorizationState();
        state.SetConfiguration(true, TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        var (sut, _, authorization) = CreateSut(state);
        authorization.Setup(service => service.TryUnlockOnStartupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                state.Unlock();
                return AuthorizationResult.Success;
            });

        var outcome = await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AvaloniaStartupOutcome.ReadyUnlocked, outcome);
    }

    [Theory]
    [InlineData(AuthorizationResult.PasswordRequired)]
    [InlineData(AuthorizationResult.Cancelled)]
    [InlineData(AuthorizationResult.TooManyAttempts)]
    [InlineData(AuthorizationResult.DisabledByPolicy)]
    [InlineData(AuthorizationResult.Failed)]
    public async Task InitializeAsync_WhenQuickUnlockDoesNotSucceed_RequiresPasswordRecovery(
        AuthorizationResult quickUnlockResult)
    {
        var state = new AuthorizationState();
        state.SetConfiguration(true, TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        var (sut, _, authorization) = CreateSut(state);
        authorization.Setup(service => service.TryUnlockOnStartupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(quickUnlockResult);

        var outcome = await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AvaloniaStartupOutcome.ReadyForPasswordFallback, outcome);
        Assert.False(state.IsUnlocked);
    }

    [Fact]
    public async Task InitializeAsync_WhenQuickUnlockReportsSuccessWithoutUnlocking_FailsClosedToPassword()
    {
        var state = new AuthorizationState();
        state.SetConfiguration(true, TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock);
        var (sut, _, authorization) = CreateSut(state);
        authorization.Setup(service => service.TryUnlockOnStartupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.Success);

        var outcome = await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AvaloniaStartupOutcome.ReadyForPasswordFallback, outcome);
    }

    [Fact]
    public async Task InitializeAsync_WhenAuthorizationIsNotConfigured_IsReadyForPasswordSetup()
    {
        var (sut, _, _) = CreateSut(new AuthorizationState());

        var outcome = await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AvaloniaStartupOutcome.ReadyForPasswordSetup, outcome);
    }

    [Fact]
    public async Task InitializeAsync_WhenPreferencesFail_DoesNotInitializeAuthorization()
    {
        var (sut, settings, authorization) = CreateSut(new AuthorizationState());
        settings.Setup(service => service.LoadAsync())
            .ReturnsAsync(Result.Fail<IAppSettings>("synthetic preference failure"));

        var outcome = await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AvaloniaStartupOutcome.PreferencesUnavailable, outcome);
        authorization.Verify(service => service.InitializeAsync(), Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_AppliesPersistedCultureBeforeAuthorizationProjection()
    {
        var localization = new Mock<IAvaloniaLocalizationService>();
        var (sut, settings, _) = CreateSut(new AuthorizationState(), localization);
        var preferences = new Mock<IAppSettings>();
        preferences.SetupGet(value => value.CultureName).Returns("de");
        settings.Setup(value => value.LoadAsync()).ReturnsAsync(Result.Ok(preferences.Object));

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        localization.Verify(value => value.ApplyCulture("de"), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WhenBoundaryThrows_ReturnsSanitizedFailure()
    {
        var (sut, settings, _) = CreateSut(new AuthorizationState());
        settings.Setup(service => service.LoadAsync())
            .ThrowsAsync(new InvalidOperationException("sensitive synthetic detail"));

        var outcome = await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AvaloniaStartupOutcome.UnexpectedFailure, outcome);
    }

    [Fact]
    public async Task InitializeAsync_RecordsAllowlistedStartupStagesWithoutPayloadData()
    {
        var state = new AuthorizationState();
        state.SetConfiguration(true, TOTP.Core.Enums.PreferredUnlockMethod.Password);
        var diagnostics = new Mock<IStartupDiagnostics>();
        var settings = new Mock<ISettingsService>();
        settings.Setup(value => value.LoadAsync()).ReturnsAsync(Result.Ok(Mock.Of<IAppSettings>()));
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(value => value.State).Returns(state);
        var sut = new AvaloniaStartupCoordinator(
            settings.Object,
            authorization.Object,
            Mock.Of<IAvaloniaLocalizationService>(),
            Mock.Of<ILogger<AvaloniaStartupCoordinator>>(),
            diagnostics.Object);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        diagnostics.Verify(value => value.Record(
            StartupDiagnosticStage.Preferences, It.IsAny<TimeSpan>(), true), Times.Once);
        diagnostics.Verify(value => value.Record(
            StartupDiagnosticStage.Authorization, It.IsAny<TimeSpan>(), true), Times.Once);
        diagnostics.Verify(value => value.Record(
            StartupDiagnosticStage.Completed, It.IsAny<TimeSpan>(), true), Times.Once);
    }

    private static (
        AvaloniaStartupCoordinator Sut,
        Mock<ISettingsService> Settings,
        Mock<IAuthorizationService> Authorization) CreateSut(
        AuthorizationState state,
        Mock<IAvaloniaLocalizationService>? localization = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.LoadAsync())
            .ReturnsAsync(Result.Ok(Mock.Of<IAppSettings>()));

        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(service => service.State).Returns(state);

        return (
            new AvaloniaStartupCoordinator(
                settings.Object,
                authorization.Object,
                (localization ?? new Mock<IAvaloniaLocalizationService>()).Object,
                Mock.Of<ILogger<AvaloniaStartupCoordinator>>()),
            settings,
            authorization);
    }
}
