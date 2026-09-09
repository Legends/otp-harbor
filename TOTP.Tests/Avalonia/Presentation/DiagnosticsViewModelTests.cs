using Moq;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using Avalonia.Controls;
using TOTP.Avalonia.Desktop.Localization;

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
        Assert.Equal(NotificationSeverity.Success, sut.MessageSeverity);
        await Task.Delay(100, TestContext.Current.CancellationToken);
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
        var sut = new DiagnosticsViewModel(
            service.Object,
            CreateLocalization("en"),
            dialogs.Object,
            transientMessageDuration: TimeSpan.FromMilliseconds(20));

        await sut.RefreshAsync();

        Assert.Empty(sut.SupportInformation);
        Assert.DoesNotContain("person", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NotificationSeverity.Error, sut.MessageSeverity);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(sut.Message));
        dialogs.Verify(value => value.ShowMessageAsync(
            It.Is<MessageDialogRequest>(request => request.Severity == NotificationSeverity.Error),
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
        Assert.Equal(
            "Supportinformationen aktualisiert. Sie enthalten keine Kontodaten oder Dateisystempfade.",
            sut.Message);
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
