using System.Runtime.InteropServices;
using TOTP.Camera.OpenCv;

namespace TOTP.Platform.MacOS;

public sealed partial class MacOSCameraAccessProbe : ICameraAccessProbe
{
    public CameraAccessStatus Probe()
    {
        if (!OperatingSystem.IsMacOS()) return CameraAccessStatus.Unknown;
        try
        {
            _ = Symbols.AVFoundationHandle;
            var deviceClass = NativeMethods.objc_getClass("AVCaptureDevice");
            if (deviceClass == IntPtr.Zero) return CameraAccessStatus.Unknown;
            var status = NativeMethods.objc_msgSend_nint_IntPtr(
                deviceClass,
                Symbols.AuthorizationStatusForMediaType,
                Symbols.VideoMediaType);
            return MapAuthorizationStatus(status);
        }
        catch
        {
            return CameraAccessStatus.Unknown;
        }
    }

    public static CameraAccessStatus MapAuthorizationStatus(nint status) => status switch
    {
        1 or 2 => CameraAccessStatus.PermissionDenied,
        3 => CameraAccessStatus.Ready,
        _ => CameraAccessStatus.Unknown
    };

    private static class Symbols
    {
        private static readonly Lazy<IntPtr> AVFoundationLibrary = new(() => NativeLibrary.Load(
            "/System/Library/Frameworks/AVFoundation.framework/AVFoundation"));

        public static IntPtr AuthorizationStatusForMediaType { get; } =
            NativeMethods.sel_registerName("authorizationStatusForMediaType:");
        public static IntPtr AVFoundationHandle => AVFoundationLibrary.Value;
        public static IntPtr VideoMediaType => Marshal.ReadIntPtr(NativeLibrary.GetExport(
            AVFoundationLibrary.Value,
            "AVMediaTypeVideo"));
    }

    private static partial class NativeMethods
    {
        private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

        [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr objc_getClass(string name);

        [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr sel_registerName(string name);

        [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        public static partial nint objc_msgSend_nint_IntPtr(
            IntPtr receiver,
            IntPtr selector,
            IntPtr value);
    }
}
