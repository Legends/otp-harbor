using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Common;

internal sealed class NoOpPlatformFileSecurity : IPlatformFileSecurity
{
    public static NoOpPlatformFileSecurity Instance { get; } = new();

    public void RestrictDirectoryToCurrentUser(string directoryPath)
    {
    }

    public void RestrictFileToCurrentUser(string filePath)
    {
    }
}
