using TOTP.Camera.OpenCv;
using TOTP.Core.Security.Models;
using TOTP.Platform.Linux;
using TOTP.Platform.Linux.Security;
using TOTP.Platform.MacOS;
using TOTP.Platform.MacOS.Security;

namespace TOTP.Tests.Unix.Platform;

public sealed class TargetAdapterSmokeTests
{
    [Fact]
    public void LinuxNativeCapabilityProbes_ReturnClosedStates()
    {
        if (!OperatingSystem.IsLinux()) return;

        var camera = new LinuxCameraAccessProbe(new LinuxCameraDeviceAccess()).Probe();
        var secretService = new LinuxSecretServiceRuntime();

        Assert.Contains(camera, Enum.GetValues<CameraAccessStatus>());
        Assert.True(secretService.SecretToolPath is null || Path.IsPathFullyQualified(secretService.SecretToolPath));
        if (secretService.HasSessionBus && secretService.SecretToolPath is not null)
            Assert.True(secretService.IsPlatformSupported);
    }

    [Fact]
    public void MacOSNativeCapabilityProbes_LoadFrameworksAndReturnClosedStates()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var keychain = new MacOSKeychainNative().GetAvailability();
        var camera = new MacOSCameraAccessProbe().Probe();
        var session = new MacOSSessionStateReader();
        var locked = session.IsScreenLocked();

        Assert.Contains(keychain, Enum.GetValues<PlatformSecretStoreAvailability>());
        Assert.NotEqual(PlatformSecretStoreAvailability.Unknown, keychain);
        Assert.Contains(camera, Enum.GetValues<CameraAccessStatus>());
        Assert.True(session.IsSupported);
        _ = locked;
    }
}
