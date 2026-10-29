namespace TOTP.Core.Services.Interfaces;

/// <summary>
/// Applies platform-specific access restrictions to application files and directories.
/// Implementations must throw when the requested protection cannot be applied.
/// </summary>
public interface IPlatformFileSecurity
{
    void RestrictDirectoryToCurrentUser(string directoryPath);
    void RestrictFileToCurrentUser(string filePath);
}
