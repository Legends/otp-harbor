namespace TOTP.Platform.Unix;

public interface IUnixFileSystem
{
    bool IsSupported { get; }
    uint EffectiveUserId { get; }
    IUnixFileHandle OpenNoFollow(string path, UnixFileKind expectedKind);
    UnixFileStatus GetStatusNoFollow(string path);
}

public interface IUnixFileHandle : IDisposable
{
    UnixFileStatus GetStatus();
    void SetPermissions(UnixFileMode permissions);
}
