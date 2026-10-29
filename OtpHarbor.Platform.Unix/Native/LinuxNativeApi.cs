using System.Runtime.InteropServices;

namespace TOTP.Platform.Unix.Native;

internal sealed class LinuxNativeApi : IUnixNativeApi
{
    private const int NonBlockingOpen = 0x000800;
    private const int NoFollow = 0x020000;
    private const int CloseOnExec = 0x080000;
    private const int SecureOpenFlags = NonBlockingOpen | NoFollow | CloseOnExec;
    private const uint FileTypeMask = 0xF000;
    private const uint PermissionMask = 0x0FFF;

    public uint EffectiveUserId => GetEffectiveUserId();
    public int SymbolicLinkLoopError => 40;

    public int OpenNoFollow(string path) => Open(path, SecureOpenFlags);

    public bool TryGetStatus(int descriptor, out UnixFileStatus status)
    {
        var succeeded = GetFileStatus(descriptor, out var nativeStatus) == 0;
        status = succeeded ? Map(nativeStatus) : default;
        return succeeded;
    }

    public bool TryGetStatusNoFollow(string path, out UnixFileStatus status)
    {
        var succeeded = GetLinkStatus(path, out var nativeStatus) == 0;
        status = succeeded ? Map(nativeStatus) : default;
        return succeeded;
    }

    private static UnixFileStatus Map(LinuxFileStatus status) => new(
        UnixFileStatusMapper.MapKind(status.Mode & FileTypeMask),
        status.OwnerUserId,
        (UnixFileMode)(status.Mode & PermissionMask),
        status.DeviceId,
        status.Inode);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int GetFileStatus(int descriptor, out LinuxFileStatus status);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int GetLinkStatus(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out LinuxFileStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxFileStatus
    {
        public ulong DeviceId;
        public ulong Inode;
        public ulong HardLinkCount;
        public uint Mode;
        public uint OwnerUserId;
        public uint GroupId;
        public int Padding;
        public ulong DeviceType;
        public long Size;
        public long BlockSize;
        public long BlockCount;
        public NativeTimespec AccessTime;
        public NativeTimespec ModificationTime;
        public NativeTimespec StatusChangeTime;
        public long Reserved0;
        public long Reserved1;
        public long Reserved2;
    }
}
