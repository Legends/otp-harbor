using FluentResults;
using Moq;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed partial class MainWindowViewModelTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public async Task SettingsDiagnostics_RefreshesWhenOpenedAndShowsAppliedLogLevel(int tab)
    {
        var state = new AuthorizationState();
        state.SetConfiguration(true, PreferredUnlockMethod.Password);
        state.Unlock();
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(value => value.State).Returns(state);
        var coordinator = new Mock<IAvaloniaStartupCoordinator>();
        coordinator.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AvaloniaStartupOutcome.ReadyUnlocked);
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var service = new Mock<ISupportDiagnosticsService>();
        service.Setup(value => value.Capture()).Returns(new SupportDiagnosticsSnapshot("3.2.1", "Windows", "X64", ".NET 10", true, []));
        var preferences = new Mock<ISettingsService>();
        preferences.SetupGet(value => value.Current).Returns(new AppSettings());
        preferences.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Ok());
        var level = AppLogLevel.Information;
        var logging = new Mock<ILogSwitchService>();
        logging.SetupGet(value => value.MinimumLevel).Returns(() => level);
        logging.Setup(value => value.SetLevel(It.IsAny<AppLogLevel>())).Callback((AppLogLevel value) => level = value);
        var localization = CreateLocalization();
        using var settings = new SettingsPageViewModel(preferences.Object, localization,
            autoSaveDelay: TimeSpan.FromHours(1), logSwitchService: logging.Object);
        using var diagnostics = new DiagnosticsViewModel(service.Object, localization, logSwitch: logging.Object);
        using var sut = CreateSut(coordinator.Object, authorization.Object, accountManager: manager.Object,
            diagnostics: diagnostics, settingsPage: settings);
        await sut.InitializeAsync();
        await sut.ShowSettingsAsync();
        service.Verify(value => value.Capture(), Times.Never);

        sut.SelectedSettingsTabIndex = tab;
        await WaitUntilAsync(() => diagnostics.ApplicationVersion == "3.2.1");

        service.Verify(value => value.Capture(), Times.Once);
        var changes = new List<string?>();
        diagnostics.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        settings.MinimumLogLevel = AppLogLevel.Warning;
        await settings.SaveAsync();
        Assert.Equal(localization.GetString(TOTP.Avalonia.Desktop.Localization.AvaloniaStringKeys.DiagnosticLogLevelWarning), diagnostics.LogLevel);
        Assert.Contains(nameof(diagnostics.SecondaryDetails), changes);
        await sut.CloseSettingsAsync();
        await sut.ShowSettingsAsync();
        service.Verify(value => value.Capture(), Times.Exactly(2));
    }
}
