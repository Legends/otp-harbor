using System.Security.AccessControl;
using System.Security.Principal;
using TOTP.Platform.Windows;

namespace TOTP.Tests.Platform.Windows;

public sealed class WindowsFileSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"totp-acl-tests-{Guid.NewGuid():N}");

    [Fact]
    public void RestrictDirectoryToCurrentUser_AppliesProtectedCurrentUserAcl()
    {
        Directory.CreateDirectory(_root);
        var sut = new WindowsFileSecurity();

        sut.RestrictDirectoryToCurrentUser(_root);

        var security = new DirectoryInfo(_root).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        AssertCurrentUserHasFullControl(security);
    }

    [Fact]
    public void RestrictFileToCurrentUser_AppliesProtectedCurrentUserAcl()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "vault.totp");
        File.WriteAllBytes(path, [1, 2, 3]);
        var sut = new WindowsFileSecurity();

        sut.RestrictFileToCurrentUser(path);

        var security = new FileInfo(path).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        AssertCurrentUserHasFullControl(security);
    }

    [Fact]
    public void RestrictMissingPaths_FailsClosed()
    {
        var sut = new WindowsFileSecurity();

        Assert.Throws<DirectoryNotFoundException>(() => sut.RestrictDirectoryToCurrentUser(_root));
        Assert.Throws<FileNotFoundException>(() => sut.RestrictFileToCurrentUser(Path.Combine(_root, "missing.totp")));
    }

    private static void AssertCurrentUserHasFullControl(FileSystemSecurity security)
    {
        var currentUser = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(currentUser);

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>();

        Assert.Contains(rules, rule =>
            currentUser!.Equals(rule.IdentityReference)
            && rule.AccessControlType == AccessControlType.Allow
            && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
