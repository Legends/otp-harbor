using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Common;

internal sealed class DelegatingPlatformFileSecurity : IPlatformFileSecurity
{
    public Action<string>? RestrictDirectory { get; init; }
    public Action<string>? RestrictFile { get; init; }

    public void RestrictDirectoryToCurrentUser(string directoryPath) =>
        RestrictDirectory?.Invoke(directoryPath);

    public void RestrictFileToCurrentUser(string filePath) =>
        RestrictFile?.Invoke(filePath);
}
