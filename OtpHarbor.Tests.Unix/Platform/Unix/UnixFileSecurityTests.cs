using System.Runtime.Versioning;
using TOTP.Platform.Unix;

namespace TOTP.Tests.Unix.Platform.Unix;

public sealed class UnixFileSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"totp-unix-mode-tests-{Guid.NewGuid():N}");

    [Fact]
    public void RestrictPaths_OnUnsupportedPlatform_FailsClosed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "This guard test applies to non-Unix platforms.");
        var sut = new UnixFileSecurity();

        Assert.Throws<PlatformNotSupportedException>(() => sut.RestrictDirectoryToCurrentUser(_root));
        Assert.Throws<PlatformNotSupportedException>(() => sut.RestrictFileToCurrentUser(Path.Combine(_root, "vault.totp")));
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public void RestrictDirectoryToCurrentUser_AppliesAndVerifiesMode0700()
    {
        RequireUnix();
        Directory.CreateDirectory(_root);
        File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        var sut = new UnixFileSecurity();

        sut.RestrictDirectoryToCurrentUser(_root);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_root));
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public void RestrictFileToCurrentUser_AppliesAndVerifiesMode0600()
    {
        RequireUnix();
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "vault.totp");
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        var sut = new UnixFileSecurity();

        sut.RestrictFileToCurrentUser(path);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(path));
    }

    [Fact]
    public void RestrictFileToCurrentUser_WhenPathIsSymbolicLink_FailsClosed()
    {
        RequireUnix();
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "target.totp");
        var link = Path.Combine(_root, "link.totp");
        File.WriteAllBytes(target, [1]);
        File.CreateSymbolicLink(link, target);
        var sut = new UnixFileSecurity();

        Assert.Throws<UnauthorizedAccessException>(() => sut.RestrictFileToCurrentUser(link));
    }

    [Fact]
    public void RestrictFileToCurrentUser_WhenPathIsNotRegularFile_FailsClosed()
    {
        RequireUnix();
        Directory.CreateDirectory(_root);
        var sut = new UnixFileSecurity();

        Assert.Throws<UnauthorizedAccessException>(() => sut.RestrictFileToCurrentUser(_root));
    }

    [Fact]
    public void RestrictMissingPaths_FailsClosed()
    {
        RequireUnix();
        var sut = new UnixFileSecurity();

        Assert.Throws<DirectoryNotFoundException>(() => sut.RestrictDirectoryToCurrentUser(_root));
        Assert.Throws<FileNotFoundException>(() => sut.RestrictFileToCurrentUser(Path.Combine(_root, "missing.totp")));
    }

    private static void RequireUnix() =>
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix permission behavior runs on Linux and macOS.");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
