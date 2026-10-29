using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TOTP.Platform.Unix.Native;

internal sealed class NativeUnixFileSystem : IUnixFileSystem
{
    private const int MissingPathError = 2;
    private const int AccessDeniedError = 13;

    private readonly IUnixNativeApi? _nativeApi = CreateNativeApi();

    public bool IsSupported => _nativeApi is not null;

    public uint EffectiveUserId => GetNativeApi().EffectiveUserId;

    public IUnixFileHandle OpenNoFollow(string path, UnixFileKind expectedKind)
    {
        var nativeApi = GetNativeApi();
        var descriptor = nativeApi.OpenNoFollow(path);
        if (descriptor < 0)
            throw CreateOpenException(path, expectedKind, Marshal.GetLastPInvokeError(), nativeApi);

        return new NativeUnixFileHandle(
            new SafeFileHandle((nint)descriptor, ownsHandle: true),
            path,
            nativeApi);
    }

    public UnixFileStatus GetStatusNoFollow(string path)
    {
        var nativeApi = GetNativeApi();
        if (!nativeApi.TryGetStatusNoFollow(path, out var status))
            throw CreateIOException("verify Unix path", path, Marshal.GetLastPInvokeError());

        return status;
    }

    private IUnixNativeApi GetNativeApi() =>
        _nativeApi ?? throw new PlatformNotSupportedException(
            "Unix file security supports Linux x64 and macOS x64/ARM64.");

    private static IUnixNativeApi? CreateNativeApi()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;

        if (OperatingSystem.IsLinux() && architecture == Architecture.X64)
            return new LinuxNativeApi();

        if (OperatingSystem.IsMacOS() && architecture is Architecture.X64 or Architecture.Arm64)
            return new MacOSNativeApi();

        return null;
    }

    private static Exception CreateOpenException(
        string path,
        UnixFileKind expectedKind,
        int error,
        IUnixNativeApi nativeApi) => error switch
        {
            MissingPathError when expectedKind == UnixFileKind.Directory =>
                new DirectoryNotFoundException($"Sensitive directory was not found: {path}"),
            MissingPathError => new FileNotFoundException("Sensitive file was not found.", path),
            var value when value == nativeApi.SymbolicLinkLoopError =>
                new UnauthorizedAccessException($"Sensitive paths cannot be symbolic links: {path}"),
            AccessDeniedError =>
                new UnauthorizedAccessException($"Access to the sensitive path was denied: {path}"),
            _ => CreateIOException("open Unix path without following links", path, error)
        };

    private static IOException CreateIOException(string operation, string path, int error) =>
        new($"Failed to {operation} '{path}': {new Win32Exception(error).Message} (errno {error}).");

    private sealed class NativeUnixFileHandle(
        SafeFileHandle handle,
        string path,
        IUnixNativeApi nativeApi) : IUnixFileHandle
    {
        private readonly SafeFileHandle _handle = handle;

        public UnixFileStatus GetStatus()
        {
            var descriptor = _handle.DangerousGetHandle().ToInt32();
            if (!nativeApi.TryGetStatus(descriptor, out var status))
                throw CreateIOException("inspect opened Unix path", path, Marshal.GetLastPInvokeError());

            return status;
        }

        public void SetPermissions(UnixFileMode permissions) =>
            SetUnixFileMode(_handle, permissions);

        private static void SetUnixFileMode(SafeFileHandle handle, UnixFileMode permissions)
        {
            if (OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Unix permissions are unavailable on Windows.");

            File.SetUnixFileMode(handle, permissions);
        }

        public void Dispose() => _handle.Dispose();
    }
}
