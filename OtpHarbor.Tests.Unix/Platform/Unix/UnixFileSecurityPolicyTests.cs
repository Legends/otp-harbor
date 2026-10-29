using TOTP.Platform.Unix;

namespace TOTP.Tests.Unix.Platform.Unix;

public sealed class UnixFileSecurityPolicyTests
{
    private const uint EffectiveUserId = 1000;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Fact]
    public void RestrictFileToCurrentUser_AppliesAndVerifiesRequiredPolicy()
    {
        var fileSystem = CreateFileSystem(
            Status(UnixFileKind.RegularFile, UnixFileMode.OtherRead),
            Status(UnixFileKind.RegularFile, FileMode),
            Status(UnixFileKind.RegularFile, FileMode));
        var sut = new UnixFileSecurity(fileSystem);

        sut.RestrictFileToCurrentUser("vault.totp");

        Assert.Equal(FileMode, fileSystem.Handle.AppliedPermissions);
        Assert.True(fileSystem.Handle.IsDisposed);
    }

    [Fact]
    public void RestrictFileToCurrentUser_WhenOwnerDiffers_FailsBeforeChmod()
    {
        var fileSystem = CreateFileSystem(
            Status(UnixFileKind.RegularFile, FileMode) with { OwnerUserId = EffectiveUserId + 1 });
        var sut = new UnixFileSecurity(fileSystem);

        Assert.Throws<UnauthorizedAccessException>(() => sut.RestrictFileToCurrentUser("vault.totp"));
        Assert.Null(fileSystem.Handle.AppliedPermissions);
    }

    [Fact]
    public void RestrictFileToCurrentUser_WhenEntryIsNotRegularFile_FailsBeforeChmod()
    {
        var fileSystem = CreateFileSystem(Status(UnixFileKind.SymbolicLink, FileMode));
        var sut = new UnixFileSecurity(fileSystem);

        Assert.Throws<UnauthorizedAccessException>(() => sut.RestrictFileToCurrentUser("vault.totp"));
        Assert.Null(fileSystem.Handle.AppliedPermissions);
    }

    [Fact]
    public void RestrictFileToCurrentUser_WhenAppliedModeCannotBeVerified_FailsClosed()
    {
        var fileSystem = CreateFileSystem(
            Status(UnixFileKind.RegularFile, UnixFileMode.OtherRead),
            Status(UnixFileKind.RegularFile, FileMode | UnixFileMode.GroupRead));
        var sut = new UnixFileSecurity(fileSystem);

        Assert.Throws<IOException>(() => sut.RestrictFileToCurrentUser("vault.totp"));
    }

    [Fact]
    public void RestrictFileToCurrentUser_WhenPathIdentityChanges_FailsClosed()
    {
        var fileSystem = CreateFileSystem(
            Status(UnixFileKind.RegularFile, UnixFileMode.OtherRead),
            Status(UnixFileKind.RegularFile, FileMode),
            Status(UnixFileKind.RegularFile, FileMode) with { Inode = 99 });
        var sut = new UnixFileSecurity(fileSystem);

        Assert.Throws<UnauthorizedAccessException>(() => sut.RestrictFileToCurrentUser("vault.totp"));
    }

    [Fact]
    public void RestrictFileToCurrentUser_WhenPlatformIsUnsupported_FailsBeforeOpen()
    {
        var fileSystem = CreateFileSystem(Status(UnixFileKind.RegularFile, FileMode));
        fileSystem.IsSupported = false;
        var sut = new UnixFileSecurity(fileSystem);

        Assert.Throws<PlatformNotSupportedException>(() => sut.RestrictFileToCurrentUser("vault.totp"));
        Assert.False(fileSystem.WasOpened);
    }

    private static FakeUnixFileSystem CreateFileSystem(
        UnixFileStatus initialStatus,
        UnixFileStatus? hardenedStatus = null,
        UnixFileStatus? pathStatus = null) =>
        new(
            new FakeUnixFileHandle(initialStatus, hardenedStatus ?? initialStatus),
            pathStatus ?? hardenedStatus ?? initialStatus);

    private static UnixFileStatus Status(UnixFileKind kind, UnixFileMode mode) =>
        new(kind, EffectiveUserId, mode, DeviceId: 10, Inode: 20);

    private sealed class FakeUnixFileSystem(
        FakeUnixFileHandle handle,
        UnixFileStatus pathStatus) : IUnixFileSystem
    {
        public bool IsSupported { get; set; } = true;
        public uint EffectiveUserId => UnixFileSecurityPolicyTests.EffectiveUserId;
        public FakeUnixFileHandle Handle { get; } = handle;
        public bool WasOpened { get; private set; }

        public IUnixFileHandle OpenNoFollow(string path, UnixFileKind expectedKind)
        {
            WasOpened = true;
            return Handle;
        }

        public UnixFileStatus GetStatusNoFollow(string path) => pathStatus;
    }

    private sealed class FakeUnixFileHandle(params UnixFileStatus[] statuses) : IUnixFileHandle
    {
        private readonly Queue<UnixFileStatus> _statuses = new(statuses);

        public UnixFileMode? AppliedPermissions { get; private set; }
        public bool IsDisposed { get; private set; }

        public UnixFileStatus GetStatus() => _statuses.Dequeue();
        public void SetPermissions(UnixFileMode permissions) => AppliedPermissions = permissions;
        public void Dispose() => IsDisposed = true;
    }
}
