using System.Runtime.InteropServices;
using TOTP.Camera.OpenCv;

namespace TOTP.Platform.Linux;

public interface ILinuxCameraDeviceAccess
{
    bool IsPlatformSupported { get; }
    IReadOnlyList<string> EnumerateVideoDevices();
    bool CanReadAndWrite(string path);
}

public sealed class LinuxCameraAccessProbe(ILinuxCameraDeviceAccess devices) : ICameraAccessProbe
{
    public CameraAccessStatus Probe()
    {
        if (!devices.IsPlatformSupported) return CameraAccessStatus.Unknown;
        var paths = devices.EnumerateVideoDevices();
        if (paths.Count == 0) return CameraAccessStatus.NoCamera;
        return paths.Any(devices.CanReadAndWrite)
            ? CameraAccessStatus.Ready
            : CameraAccessStatus.PermissionDenied;
    }
}

public sealed partial class LinuxCameraDeviceAccess : ILinuxCameraDeviceAccess
{
    private const int ReadAccess = 4;
    private const int WriteAccess = 2;
    public bool IsPlatformSupported => OperatingSystem.IsLinux();

    public IReadOnlyList<string> EnumerateVideoDevices() => OperatingSystem.IsLinux()
        ? Directory.EnumerateFiles("/dev", "video*", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray()
        : [];

    public bool CanReadAndWrite(string path) =>
        OperatingSystem.IsLinux() && NativeMethods.access(path, ReadAccess | WriteAccess) == 0;

    private static partial class NativeMethods
    {
        [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
        public static partial int access(string path, int mode);
    }
}
