using Moq;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Camera.OpenCv;
using TOTP.Core.Platform;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using Avalonia.Controls;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Tests.Avalonia.Platform;

public sealed class AvaloniaPlatformCapabilityReportTests
{
    [Fact]
    public async Task CaptureAsync_ExposesClosedCapabilityStatesWithoutPathsOrSecrets()
    {
        var quickUnlock = new Mock<IPlatformQuickUnlock>();
        quickUnlock.Setup(value => value.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlatformQuickUnlockAvailability.NotConfigured);
        var secretStore = new Mock<IPlatformSecretStore>();
        secretStore.Setup(value => value.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlatformSecretStoreAvailability.DisabledByPolicy);
        var sessions = new Mock<IPlatformSessionEventSource>();
        sessions.SetupGet(value => value.IsSupported).Returns(true);
        var clipboard = new Mock<IAsyncPlatformClipboard>();
        clipboard.SetupGet(value => value.Capabilities).Returns(ClipboardCapabilities.WriteText);
        var installer = new Mock<IUpdateInstallerLauncher>();
        installer.SetupGet(value => value.IsSupported).Returns(false);
        var sut = new AvaloniaPlatformCapabilityReport(
            quickUnlock.Object,
            [secretStore.Object],
            sessions.Object,
            clipboard.Object,
            [new FixedCameraProbe(CameraAccessStatus.PermissionDenied)],
            installer.Object,
            CreateLocalization("en"));

        var result = await sut.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Contains(result, value => value is { Name: "Platform quick unlock", Status: PlatformCapabilityStatus.Misconfigured });
        Assert.Contains(result, value => value is { Name: "Device secret store", Status: PlatformCapabilityStatus.PermissionDenied });
        Assert.Contains(result, value => value is { Name: "Session lock detection", Status: PlatformCapabilityStatus.Supported });
        Assert.Contains(result, value => value is { Name: "Conditional clipboard clear", Status: PlatformCapabilityStatus.PermanentlyUnavailable });
        Assert.Contains(result, value => value is { Name: "Camera", Status: PlatformCapabilityStatus.PermissionDenied });
        Assert.Contains(result, value => value is { Name: "Update installation", Status: PlatformCapabilityStatus.PermanentlyUnavailable });
    }

    private static IAvaloniaLocalizationService CreateLocalization(string culture)
    {
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture(culture);
        return localization;
    }

    private sealed class FixedCameraProbe(CameraAccessStatus status) : ICameraAccessProbe
    {
        public CameraAccessStatus Probe() => status;
    }
}
