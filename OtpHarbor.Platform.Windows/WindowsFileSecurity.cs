using System.Security.AccessControl;
using System.Security.Principal;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Platform.Windows;

public sealed class WindowsFileSecurity : IPlatformFileSecurity
{
    public void RestrictDirectoryToCurrentUser(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        RejectReparsePoint(directoryPath);

        var userSid = GetCurrentUserSid();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(userSid);
        security.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(directoryPath).SetAccessControl(security);
    }

    public void RestrictFileToCurrentUser(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File to harden was not found.", filePath);
        }

        RejectReparsePoint(filePath);

        var userSid = GetCurrentUserSid();
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(userSid);
        security.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));

        new FileInfo(filePath).SetAccessControl(security);
    }

    private static SecurityIdentifier GetCurrentUserSid() =>
        WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException("Sensitive application paths cannot be reparse points.");
        }
    }
}
