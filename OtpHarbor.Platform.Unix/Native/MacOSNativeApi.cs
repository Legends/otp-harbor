using System.Runtime.InteropServices;

namespace TOTP.Platform.Unix.Native;

internal sealed class MacOSNativeApi : IUnixNativeApi
{
    private const int NonBlockingOpen = 0x000004;
    private const int NoFollow = 0x000100;
    private const int CloseOnExec = 0x1000000;
    private const int SecureOpenFlags = NonBlockingOpen | NoFollow | CloseOnExec;
    private const uint FileTypeMask = 0xF000;
    private const uint PermissionMask = 0x0FFF;

    public uint EffectiveUserId => GetEffectiveUserId();
    public int SymbolicLinkLoopError => 62;

    public int OpenNoFollow(string path) => Open(path, SecureOpenFlags);

    public bool TryGetStatus(int descriptor, out UnixFileStatus status)
    {
        var result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? GetFileStatusArm64(descriptor, out var nativeStatus)
            : GetFileStatusInode64(descriptor, out nativeStatus);
        var succeeded = result == 0;
        status = succeeded ? Map(nativeStatus) : default;
        return succeeded;
    }

    public bool TryGetStatusNoFollow(string path, out UnixFileStatus status)
    {
        var result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? GetLinkStatusArm64(path, out var nativeStatus)
            : GetLinkStatusInode64(path, out nativeStatus);
        var succeeded = result == 0;
        status = succeeded ? Map(nativeStatus) : default;
        return succeeded;
    }

    private static UnixFileStatus Map(MacOSFileStatus status) => new(
        UnixFileStatusMapper.MapKind(status.Mode & FileTypeMask),
        status.OwnerUserId,
        (UnixFileMode)(status.Mode & PermissionMask),
        unchecked((uint)status.DeviceId),
        status.Inode);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int GetFileStatusArm64(int descriptor, out MacOSFileStatus status);

    [DllImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)]
    private static extern int GetFileStatusInode64(int descriptor, out MacOSFileStatus status);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int GetLinkStatusArm64(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out MacOSFileStatus status);

    [DllImport("libc", EntryPoint = "lstat$INODE64", SetLastError = true)]
    private static extern int GetLinkStatusInode64(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out MacOSFileStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct MacOSFileStatus
    {
        public int DeviceId;
        public ushort Mode;
        public ushort HardLinkCount;
        public ulong Inode;
        public uint OwnerUserId;
        public uint GroupId;
        public int DeviceType;
        public int Padding;
        public NativeTimespec AccessTime;
        public NativeTimespec ModificationTime;
        public NativeTimespec StatusChangeTime;
        public NativeTimespec BirthTime;
        public long Size;
        public long BlockCount;
        public int BlockSize;
        public uint Flags;
        public uint Generation;
        public int Spare;
        public long Reserved0;
        public long Reserved1;
    }
}
