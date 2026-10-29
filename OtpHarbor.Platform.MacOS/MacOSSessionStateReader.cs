using System.Runtime.InteropServices;

namespace TOTP.Platform.MacOS;

public interface IMacOSSessionStateReader
{
    bool IsSupported { get; }
    bool? IsScreenLocked();
}

public sealed partial class MacOSSessionStateReader : IMacOSSessionStateReader
{
    private const uint Utf8Encoding = 0x08000100;
    public bool IsSupported => OperatingSystem.IsMacOS();

    public bool? IsScreenLocked()
    {
        if (!IsSupported) return null;
        var session = NativeMethods.CGSessionCopyCurrentDictionary();
        if (session == IntPtr.Zero) return null;
        try
        {
            var key = NativeMethods.CFStringCreateWithCString(
                IntPtr.Zero,
                "CGSSessionScreenIsLocked",
                Utf8Encoding);
            if (key == IntPtr.Zero) return null;
            try
            {
                var value = NativeMethods.CFDictionaryGetValue(session, key);
                return value == IntPtr.Zero ? null : NativeMethods.CFBooleanGetValue(value);
            }
            finally
            {
                NativeMethods.CFRelease(key);
            }
        }
        finally
        {
            NativeMethods.CFRelease(session);
        }
    }

    private static partial class NativeMethods
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [LibraryImport(CoreGraphics)]
        public static partial IntPtr CGSessionCopyCurrentDictionary();

        [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

        [LibraryImport(CoreFoundation)]
        public static partial IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

        [LibraryImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool CFBooleanGetValue(IntPtr value);

        [LibraryImport(CoreFoundation)]
        public static partial void CFRelease(IntPtr value);
    }
}
