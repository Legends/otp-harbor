namespace TOTP.Platform.Unix;

public enum UnixFileKind
{
    RegularFile,
    Directory,
    SymbolicLink,
    Other
}

public readonly record struct UnixFileStatus(
    UnixFileKind Kind,
    uint OwnerUserId,
    UnixFileMode Permissions,
    ulong DeviceId,
    ulong Inode);
