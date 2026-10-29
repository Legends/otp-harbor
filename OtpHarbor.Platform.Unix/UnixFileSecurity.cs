using TOTP.Core.Services.Interfaces;
using TOTP.Platform.Unix.Native;

namespace TOTP.Platform.Unix;

public sealed class UnixFileSecurity : IPlatformFileSecurity
{
    private const UnixFileMode DirectoryPermissions =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FilePermissions =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly IUnixFileSystem _fileSystem;

    public UnixFileSecurity()
        : this(new NativeUnixFileSystem())
    {
    }

    public UnixFileSecurity(IUnixFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public void RestrictDirectoryToCurrentUser(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        Restrict(directoryPath, UnixFileKind.Directory, DirectoryPermissions);
    }

    public void RestrictFileToCurrentUser(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Restrict(filePath, UnixFileKind.RegularFile, FilePermissions);
    }

    private void Restrict(string path, UnixFileKind expectedKind, UnixFileMode requiredPermissions)
    {
        if (!_fileSystem.IsSupported)
            throw new PlatformNotSupportedException("Unix file security is supported only on Linux and macOS.");

        var effectiveUserId = _fileSystem.EffectiveUserId;
        using var handle = _fileSystem.OpenNoFollow(path, expectedKind);
        ValidateStatus(handle.GetStatus(), expectedKind, effectiveUserId, path);

        handle.SetPermissions(requiredPermissions);

        var hardenedStatus = handle.GetStatus();
        ValidateStatus(hardenedStatus, expectedKind, effectiveUserId, path);
        ValidatePermissions(hardenedStatus, requiredPermissions, path);

        var pathStatus = _fileSystem.GetStatusNoFollow(path);
        ValidateStatus(pathStatus, expectedKind, effectiveUserId, path);
        if (pathStatus.DeviceId != hardenedStatus.DeviceId || pathStatus.Inode != hardenedStatus.Inode)
            throw new UnauthorizedAccessException($"Sensitive path changed during permission hardening: {path}");

        ValidatePermissions(pathStatus, requiredPermissions, path);
    }

    private static void ValidateStatus(
        UnixFileStatus status,
        UnixFileKind expectedKind,
        uint effectiveUserId,
        string path)
    {
        if (status.Kind != expectedKind)
            throw new UnauthorizedAccessException($"Sensitive path has an unexpected filesystem type: {path}");

        if (status.OwnerUserId != effectiveUserId)
            throw new UnauthorizedAccessException($"Sensitive path is not owned by the effective user: {path}");
    }

    private static void ValidatePermissions(
        UnixFileStatus status,
        UnixFileMode requiredPermissions,
        string path)
    {
        if (status.Permissions != requiredPermissions)
            throw new IOException($"Unix permissions could not be verified for sensitive path: {path}");
    }
}
