using System.Runtime.InteropServices;

namespace TOTP.Platform.Unix.Native;

internal static class UnixFileStatusMapper
{
    private const uint RegularFile = 0x8000;
    private const uint Directory = 0x4000;
    private const uint SymbolicLink = 0xA000;

    public static UnixFileKind MapKind(uint fileType) => fileType switch
    {
        RegularFile => UnixFileKind.RegularFile,
        Directory => UnixFileKind.Directory,
        SymbolicLink => UnixFileKind.SymbolicLink,
        _ => UnixFileKind.Other
    };
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeTimespec
{
    private readonly long _seconds;
    private readonly long _nanoseconds;
}
