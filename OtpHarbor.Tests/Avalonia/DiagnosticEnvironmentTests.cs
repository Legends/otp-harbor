using System.Globalization;
using Moq;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Shared.Diagnostics;

namespace TOTP.Tests.Avalonia;

public sealed class DiagnosticEnvironmentTests
{
    public static DiagnosticEnvironment Sample(string? device = null) => new(
        new DateTimeOffset(2026, 11, 6, 12, 30, 0, TimeSpan.Zero),
        "3.0.1", "3.0.1+synthetic-build", "Release", "direct", "Synthetic OS 16",
        "Arm64 / Arm64", ".NET 10; Avalonia 12", "420 × 860; 2.75×", true, "en", device, true);

    [Theory]
    [InlineData("en", "System information", "Device:")]
    [InlineData("de", "Systeminformationen", "Gerät:")]
    [InlineData("fr", "Informations système", "Appareil :")]
    [InlineData("es", "Información del sistema", "Dispositivo:")]
    public void EmailSummary_UsesActiveLocaleAndOnlyIncludesDeviceForMobile(
        string language, string heading, string deviceLabel)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var desktop = new AvaloniaStringCatalog();
        var mobile = new MobileStringCatalog(culture);
        foreach (var isMobile in new[] { false, true })
        {
            string Get(string key) => isMobile ? mobile.Get(key) : desktop.Get(key, culture);
            var provider = new Mock<IDiagnosticEnvironmentProvider>();
            provider.Setup(value => value.Capture()).Returns(Sample(isMobile ? "Synthetic Phone" : null));
            var body = DiagnosticEmailBody.Format(Get("DiagnosticLogEmailBody"),
                Get("DiagnosticSystemSummary"), Get("DiagnosticDeviceSummary"),
                Get("ThemeDark"), Get("ThemeLight"), "Verbose", provider.Object, language,
                Get("DiagnosticPortrait"), Get("DiagnosticLandscape"));

            Assert.StartsWith(Get("DiagnosticLogEmailBody"), body);
            Assert.Contains(heading, body);
            Assert.Contains("2026-11-06 12:30:00 UTC", body);
            Assert.Contains("3.0.1+synthetic-build", body);
            Assert.Contains("420 × 860; 2.75×", body);
            Assert.Contains(Get("DiagnosticPortrait"), body);
            Assert.Contains(Get("ThemeDark"), body);
            Assert.Contains("Verbose", body);
            Assert.DoesNotContain("{", body);
            Assert.Equal(isMobile, body.Contains(deviceLabel, StringComparison.Ordinal));
            Assert.Equal(isMobile, body.Contains("Synthetic Phone", StringComparison.Ordinal));
            if (language != "en") Assert.DoesNotContain("System information", body);
            provider.Verify(value => value.Capture(), Times.Once);
        }
    }

    [Fact]
    public void Capture_IsOnDemandAndOptionalPlatformFailuresDoNotPreventSummary()
    {
        var reads = 0;
        var provider = new DiagnosticEnvironmentProvider(typeof(DiagnosticEnvironmentTests).Assembly,
            "unexpected-channel", () => { reads++; throw new InvalidOperationException(); },
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException());
        Assert.Equal(0, reads);

        var snapshot = provider.Capture();

        Assert.Equal(1, reads);
        Assert.Null(snapshot.Device);
        Assert.Equal("—", snapshot.Display);
        Assert.Equal("—", snapshot.Distribution);
        Assert.NotEmpty(snapshot.AppVersion);
        Assert.NotEmpty(snapshot.OperatingSystem);
        Assert.NotEmpty(snapshot.Runtime);
    }

    [Fact]
    public void Capture_UsesMobileVersionAndStripsControlCharactersFromDeviceMetadata()
    {
        var provider = new DiagnosticEnvironmentProvider(typeof(DiagnosticEnvironmentTests).Assembly,
            "direct", () => null,
            () => new("Synthetic\r\nMaker", "Phone\tModel", "Android 16 (API 36)"),
            () => "3.0.1");

        var snapshot = provider.Capture();

        Assert.Equal("3.0.1", snapshot.AppVersion);
        Assert.Equal("Android 16 (API 36)", snapshot.OperatingSystem);
        Assert.Equal("SyntheticMaker PhoneModel", snapshot.Device);
    }

    [Fact]
    public void EmailSummary_WhenCollectorFails_PreservesSupportDraft()
    {
        var provider = new Mock<IDiagnosticEnvironmentProvider>();
        provider.Setup(value => value.Capture()).Throws<InvalidOperationException>();
        Assert.Equal("Localized introduction", DiagnosticEmailBody.Format(
            "Localized introduction", "unused", "unused", "dark", "light", "Verbose", provider.Object));
    }
}
