using Microsoft.Extensions.Configuration;
using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Storage;

public static class VaultFileNameMigration
{
    private const int RotatingBackupCount = 5;

    public static void MigrateIfUsingDefaultPath(
        IConfiguration configuration,
        IPlatformApplicationPaths applicationPaths,
        IPlatformFileSecurity fileSecurity)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(applicationPaths);
        ArgumentNullException.ThrowIfNull(fileSecurity);

        if (!string.IsNullOrWhiteSpace(
                configuration[StringsConstants.TokensStorageFilePathConfigKey]))
        {
            return;
        }

        var currentPath = Path.GetFullPath(applicationPaths.VaultFilePath);
        if (File.Exists(currentPath)) return;
        if (Directory.Exists(currentPath))
        {
            throw new InvalidDataException(
                "The default vault path is occupied by a directory.");
        }

        var directory = Path.GetDirectoryName(currentPath)
            ?? throw new DirectoryNotFoundException("The default vault directory is unavailable.");
        var legacyPath = Path.Combine(directory, StringsConstants.LegacyTokensStorageFileName);
        if (!File.Exists(legacyPath))
        {
            if (Directory.Exists(legacyPath))
            {
                throw new InvalidDataException(
                    "The legacy vault path is occupied by a directory.");
            }

            return;
        }

        for (var generation = 1; generation <= RotatingBackupCount; generation++)
        {
            MigrateFileIfDestinationIsMissing(
                $"{legacyPath}.bak{generation}",
                $"{currentPath}.bak{generation}",
                fileSecurity);
        }

        MigrateFileIfDestinationIsMissing(legacyPath, currentPath, fileSecurity);
    }

    private static void MigrateFileIfDestinationIsMissing(
        string legacyPath,
        string currentPath,
        IPlatformFileSecurity fileSecurity)
    {
        if (!File.Exists(legacyPath) || File.Exists(currentPath)) return;
        if (Directory.Exists(currentPath))
        {
            throw new InvalidDataException(
                "A vault migration destination is occupied by a directory.");
        }

        fileSecurity.RestrictFileToCurrentUser(legacyPath);
        File.Move(legacyPath, currentPath, overwrite: false);
    }
}
