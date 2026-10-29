namespace TOTP.Platform.Unix.Native;

internal interface IUnixNativeApi
{
    uint EffectiveUserId { get; }
    int SymbolicLinkLoopError { get; }
    int OpenNoFollow(string path);
    bool TryGetStatus(int descriptor, out UnixFileStatus status);
    bool TryGetStatusNoFollow(string path, out UnixFileStatus status);
}
