using Moq;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using Avalonia.Controls;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Shared.Diagnostics;
using TOTP.Core.Enums;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public async Task RefreshAsync_FormatsOnlyAllowlistedSupportInformation()
    {
        var service = new Mock<ISupportDiagnosticsService>();
        service.Setup(value => value.Capture()).Returns(new SupportDiagnosticsSnapshot(
            "1.2.3",
            "Linux",
            "X64",
            ".NET 10.0",
            true,
            [new StartupDiagnosticRecord(StartupDiagnosticStage.Preferences, 12, true)]));
        var sut = new DiagnosticsViewModel(
            service.Object,
            CreateLocalization("en"),
            transientMessageDuration: TimeSpan.FromMilliseconds(20));

        await sut.RefreshAsync();

        Assert.Contains("OTP Harbor 1.2.3", sut.SupportInformation, StringComparison.Ordinal);
        Assert.Contains("Platform: Linux", sut.SupportInformation, StringComparison.Ordinal);
        Assert.Contains("Preferences: 12 ms", sut.SupportInformation, StringComparison.Ordinal);
        Assert.DoesNotContain("\\Users\\", sut.SupportInformation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", sut.SupportInformation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task RefreshAsync_WhenBoundaryThrows_DoesNotExposeExceptionDetails()
    {
        var service = new Mock<ISupportDiagnosticsService>();
        service.Setup(value => value.Capture())
            .Throws(new InvalidOperationException("C:\\Users\\person\\secret-file"));
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ShowMessageAsync(
                It.IsAny<MessageDialogRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var localization = CreateLocalization("en");
        using var sut = new DiagnosticsViewModel(
            service.Object,
            localization,
            dialogs.Object,
            transientMessageDuration: TimeSpan.FromMilliseconds(20));
        var shown = new List<NotificationShownEventArgs>();
        sut.Notification.Shown += (_, args) => shown.Add(args);
        var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Notification.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NotificationState.HasMessage) && !sut.Notification.HasMessage)
                cleared.TrySetResult();
        };

        await sut.RefreshAsync();

        Assert.Empty(sut.SupportInformation);
        var notification = Assert.Single(shown);
        Assert.Equal(localization.GetString(AvaloniaStringKeys.DiagnosticRefreshFailed), notification.Text);
        Assert.DoesNotContain("person", notification.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-file", notification.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NotificationSeverity.Error, notification.Severity);
        await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Empty(sut.Message);
        dialogs.Verify(value => value.ShowMessageAsync(
            It.Is<MessageDialogRequest>(request => request.Severity == NotificationSeverity.Error
                && request.Title == localization.GetString(AvaloniaStringKeys.DiagnosticUnavailableTitle)
                && request.Message == localization.GetString(AvaloniaStringKeys.DiagnosticUnavailableMessage)
                && request.CloseText == localization.GetString(AvaloniaStringKeys.Close)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_WhenGermanIsActive_UsesLocalizedCompleteOutput()
    {
        var service = new Mock<ISupportDiagnosticsService>();
        service.Setup(value => value.Capture()).Returns(new SupportDiagnosticsSnapshot(
            "1.2.3",
            "Windows",
            "X64",
            ".NET 10.0",
            false,
            []));
        var sut = new DiagnosticsViewModel(service.Object, CreateLocalization("de"));

        await sut.RefreshAsync();

        Assert.Contains("Plattform: Windows", sut.SupportInformation, StringComparison.Ordinal);
        Assert.Contains("Protokollordner konfiguriert: nein", sut.SupportInformation, StringComparison.Ordinal);
        Assert.Empty(sut.Message);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public async Task Summary_UsesLiveEnvironmentActiveLanguageAndLocalizedLogLevel(string culture)
    {
        var localization = CreateLocalization(culture);
        var service = new Mock<ISupportDiagnosticsService>();
        service.Setup(value => value.Capture()).Returns(new SupportDiagnosticsSnapshot("3.2.1", "Windows", "X64", ".NET 10.0", true, []));
        var environment = new Mock<IDiagnosticEnvironmentProvider>();
        environment.Setup(value => value.Capture()).Returns(DiagnosticEnvironmentTests.Sample() with
        {
            AppVersion = "3.2.1", OperatingSystem = "Windows 11 (23H2)", ScreenDisplay = "1920 × 1080 (150%)"
        });
        var level = AppLogLevel.Information;
        var logging = new Mock<ILogSwitchService>();
        logging.SetupGet(value => value.MinimumLevel).Returns(() => level);
        using var sut = new DiagnosticsViewModel(service.Object, localization, environmentProvider: environment.Object, logSwitch: logging.Object);

        await sut.RefreshAsync();

        Assert.Equal("3.2.1", sut.ApplicationVersion);
        Assert.Equal("Windows 11 (23H2)", sut.OperatingSystem);
        Assert.Equal(".NET 10.0", sut.Runtime);
        Assert.Equal("1920 × 1080 (150%)", sut.Display);
        Assert.Equal(string.Format(localization.GetString(AvaloniaStringKeys.DiagnosticLanguageValue),
            localization.CurrentLanguage.DisplayName, culture), sut.Language);
        Assert.Equal(new[] { sut.ApplicationVersion, sut.OperatingSystem, sut.Runtime }, sut.PrimaryDetails.Select(row => row.Value));
        Assert.Equal(new[] { sut.Display, sut.Language, sut.LogLevel }, sut.SecondaryDetails.Select(row => row.Value));
        foreach (var candidate in Enum.GetValues<AppLogLevel>())
        {
            level = candidate;
            sut.RefreshSummaryLabels();
            Assert.Equal(localization.GetString(candidate switch
            {
                AppLogLevel.Verbose => AvaloniaStringKeys.DiagnosticLogLevelVerbose,
                AppLogLevel.Debug => AvaloniaStringKeys.DiagnosticLogLevelDebug,
                AppLogLevel.Information => AvaloniaStringKeys.DiagnosticLogLevelInformation,
                AppLogLevel.Warning => AvaloniaStringKeys.DiagnosticLogLevelWarning,
                AppLogLevel.Error => AvaloniaStringKeys.DiagnosticLogLevelError,
                _ => AvaloniaStringKeys.DiagnosticLogLevelFatal
            }), sut.SecondaryDetails[2].Value);
        }
        environment.Verify(value => value.Capture(), Times.Once);
    }

    [Fact]
    public async Task Summary_PrefersInstalledApplicationVersionOverSemanticSupportVersion()
    {
        var source = new Mock<ISupportDiagnosticsService>();
        source.Setup(value => value.Capture()).Returns(new SupportDiagnosticsSnapshot("3.0.3", "Windows", "X64", ".NET 10", true, []));
        var environment = new Mock<IDiagnosticEnvironmentProvider>();
        environment.Setup(value => value.Capture()).Returns(DiagnosticEnvironmentTests.Sample() with { AppVersion = "3.0.3.65535" });
        using var model = new DiagnosticsViewModel(source.Object, CreateLocalization("en"), environmentProvider: environment.Object);
        await model.RefreshAsync();
        Assert.Equal("3.0.3.65535", model.ApplicationVersion);
        Assert.Equal(model.ApplicationVersion, model.PrimaryDetails[0].Value);
    }

    [Fact]
    public async Task Summary_CultureChangesRefreshRowsAndFailedCaptureClearsStaleValues()
    {
        var localization = CreateLocalization("en");
        var service = new Mock<ISupportDiagnosticsService>();
        service.Setup(value => value.Capture()).Returns(new SupportDiagnosticsSnapshot("3.2.1", "Windows", "X64", ".NET 10", true, []));
        using var sut = new DiagnosticsViewModel(service.Object, localization);
        await sut.RefreshAsync();
        var changed = new List<string?>();
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        localization.ApplyCulture("de");

        Assert.Contains(nameof(sut.PrimaryDetails), changed);
        Assert.Contains(nameof(sut.SecondaryDetails), changed);
        Assert.Equal(localization.GetString(AvaloniaStringKeys.DiagnosticOperatingSystemLabel), sut.PrimaryDetails[1].Label);
        Assert.Equal(localization.GetString(AvaloniaStringKeys.DiagnosticValueUnavailable), sut.Display);
        service.Setup(value => value.Capture()).Throws(new IOException("sensitive-path"));
        await sut.RefreshAsync();
        Assert.Empty(sut.SupportInformation);
        Assert.All(sut.PrimaryDetails, row => Assert.Equal(localization.GetString(AvaloniaStringKeys.DiagnosticValueUnavailable), row.Value));
        Assert.DoesNotContain("sensitive-path", sut.Message, StringComparison.Ordinal);
    }

    private static IAvaloniaLocalizationService CreateLocalization(string culture)
    {
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture(culture);
        return localization;
    }
}
