using TOTP.Camera.OpenCv;
using TOTP.Platform.Linux;
using TOTP.Platform.MacOS;

namespace TOTP.Tests.Platform;

public sealed class CameraAccessProbeTests
{
    [Theory]
    [InlineData(0, CameraAccessStatus.Unknown)]
    [InlineData(1, CameraAccessStatus.PermissionDenied)]
    [InlineData(2, CameraAccessStatus.PermissionDenied)]
    [InlineData(3, CameraAccessStatus.Ready)]
    public void MacOSAuthorizationStatus_MapsWithoutPrompting(
        int nativeStatus,
        CameraAccessStatus expected)
    {
        Assert.Equal(expected, MacOSCameraAccessProbe.MapAuthorizationStatus(nativeStatus));
    }

    [Fact]
    public void LinuxProbe_WhenNoV4l2DeviceExists_ReportsNoCamera()
    {
        var sut = new LinuxCameraAccessProbe(new FakeLinuxDevices([]));

        Assert.Equal(CameraAccessStatus.NoCamera, sut.Probe());
    }

    [Fact]
    public void LinuxProbe_WhenEveryV4l2DeviceIsDenied_ReportsPermissionDenied()
    {
        var sut = new LinuxCameraAccessProbe(new FakeLinuxDevices(["/dev/video0"], canAccess: false));

        Assert.Equal(CameraAccessStatus.PermissionDenied, sut.Probe());
    }

    [Fact]
    public void LinuxProbe_WhenOneV4l2DeviceIsAccessible_IsReady()
    {
        var sut = new LinuxCameraAccessProbe(new FakeLinuxDevices(["/dev/video0", "/dev/video1"], canAccess: true));

        Assert.Equal(CameraAccessStatus.Ready, sut.Probe());
    }

    [Theory]
    [InlineData(CameraAccessStatus.PermissionDenied, CameraOpenFailure.PermissionDenied)]
    [InlineData(CameraAccessStatus.NoCamera, CameraOpenFailure.NoCamera)]
    public void OpenCvFactory_WhenPreflightBlocks_DoesNotOpenNativeCamera(
        CameraAccessStatus status,
        CameraOpenFailure expected)
    {
        var sut = new OpenCvCameraSessionFactory(new FixedProbe(status));

        var result = sut.OpenDefault();

        Assert.Null(result.Session);
        Assert.Equal(expected, result.Failure);
    }

    private sealed class FixedProbe(CameraAccessStatus status) : ICameraAccessProbe
    {
        public CameraAccessStatus Probe() => status;
    }

    private sealed class FakeLinuxDevices(
        IReadOnlyList<string> paths,
        bool canAccess = false) : ILinuxCameraDeviceAccess
    {
        public bool IsPlatformSupported => true;
        public IReadOnlyList<string> EnumerateVideoDevices() => paths;
        public bool CanReadAndWrite(string path) => canAccess && path == paths[^1];
    }
}
