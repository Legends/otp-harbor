using Microsoft.Extensions.Configuration;
using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;
using TOTP.DAL.Storage;

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

        var configuredPath = configuration[StringsConstants.TokensStorageFilePathConfigKey];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var customPath = Path.GetFullPath(configuredPath);
            if (File.Exists(customPath) && HasLegacyHeader(customPath))
                LegacyVaultRollbackSnapshot.Ensure(
                    customPath, applicationPaths.AuthorizationEnvelopeFilePath, fileSecurity);
            return;
        }

        var currentPath = Path.GetFullPath(applicationPaths.VaultFilePath);
        if (File.Exists(currentPath))
        {
            if (HasLegacyHeader(currentPath))
                LegacyVaultRollbackSnapshot.Ensure(
                    currentPath, applicationPaths.AuthorizationEnvelopeFilePath, fileSecurity);
            return;
        }
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

        LegacyVaultRollbackSnapshot.Ensure(
            legacyPath, applicationPaths.AuthorizationEnvelopeFilePath, fileSecurity, currentPath);
        for (var generation = 1; generation <= RotatingBackupCount; generation++)
        {
            MigrateFileIfDestinationIsMissing(
                $"{legacyPath}.bak{generation}",
                $"{currentPath}.bak{generation}",
                fileSecurity);
        }

        MigrateFileIfDestinationIsMissing(legacyPath, currentPath, fileSecurity);
    }

    private static bool HasLegacyHeader(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The vault path is a reparse point.");

        Span<byte> header = stackalloc byte[4];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.Read(header) == header.Length && header.SequenceEqual("TVLT"u8);
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
