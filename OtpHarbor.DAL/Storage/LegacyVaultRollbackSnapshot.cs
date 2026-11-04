using System.Security.Cryptography;
using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;

namespace TOTP.DAL.Storage;

public static class LegacyVaultRollbackSnapshot
{
    public const string DirectoryName = "pre-upgrade-rollback";
    private const int RotatingBackupCount = 5;

    public static void EnsureForFormatMigration(
        string encryptedVaultPath,
        string authorizationEnvelopePath,
        IPlatformFileSecurity fileSecurity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedVaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationEnvelopePath);
        ArgumentNullException.ThrowIfNull(fileSecurity);

        var vaultPath = Path.GetFullPath(encryptedVaultPath);
        var envelopePath = Path.GetFullPath(authorizationEnvelopePath);
        if (!File.Exists(vaultPath) || !File.Exists(envelopePath))
            throw new FileNotFoundException("A legacy vault and its authorization envelope are required for migration.");

        var directory = Path.GetDirectoryName(vaultPath)
            ?? throw new DirectoryNotFoundException("The vault directory is unavailable.");
        var snapshot = Path.Combine(directory, DirectoryName);
        if (!Directory.Exists(snapshot))
            Ensure(vaultPath, envelopePath, fileSecurity);

        RejectReparsePoint(directory);
        RejectReparsePoint(snapshot);
        fileSecurity.RestrictDirectoryToCurrentUser(directory);
        fileSecurity.RestrictDirectoryToCurrentUser(snapshot);
        if (Directory.EnumerateDirectories(snapshot).Any())
            throw new InvalidDataException("The rollback snapshot contains an unexpected directory.");

        var allowedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            StringsConstants.LegacyTokensStorageFileName,
            StringsConstants.AuthorizationEnvelopeFileName
        };
        for (var generation = 1; generation <= RotatingBackupCount; generation++)
            allowedNames.Add($"{StringsConstants.LegacyTokensStorageFileName}.bak{generation}");

        foreach (var path in Directory.EnumerateFiles(snapshot))
        {
            if (!allowedNames.Contains(Path.GetFileName(path)))
                throw new InvalidDataException("The rollback snapshot contains an unexpected file.");
            RejectReparsePoint(path);
            fileSecurity.RestrictFileToCurrentUser(path);
        }

        foreach (var (source, name) in new[]
                 {
                     (vaultPath, StringsConstants.LegacyTokensStorageFileName),
                     (envelopePath, StringsConstants.AuthorizationEnvelopeFileName)
                 })
        {
            RejectReparsePoint(source);
            fileSecurity.RestrictFileToCurrentUser(source);
            VerifyIdentical(source, Path.Combine(snapshot, name));
        }
    }

    public static void Ensure(
        string encryptedVaultPath,
        string authorizationEnvelopePath,
        IPlatformFileSecurity fileSecurity,
        string? migratedVaultPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedVaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationEnvelopePath);
        ArgumentNullException.ThrowIfNull(fileSecurity);

        var vaultPath = Path.GetFullPath(encryptedVaultPath);
        if (!File.Exists(vaultPath))
        {
            if (Directory.Exists(vaultPath))
                throw new InvalidDataException("The legacy vault path is a directory.");
            return;
        }

        var directory = Path.GetDirectoryName(vaultPath)
            ?? throw new DirectoryNotFoundException("The vault directory is unavailable.");
        RejectReparsePoint(directory);
        fileSecurity.RestrictDirectoryToCurrentUser(directory);

        var files = new List<(string Source, string Name)>
        {
            (vaultPath, StringsConstants.LegacyTokensStorageFileName)
        };
        for (var generation = 1; generation <= RotatingBackupCount; generation++)
        {
            var backup = $"{vaultPath}.bak{generation}";
            if (File.Exists(backup))
                files.Add((backup, $"{StringsConstants.LegacyTokensStorageFileName}.bak{generation}"));
            else if (Directory.Exists(backup))
                throw new InvalidDataException("A legacy vault backup path is a directory.");
        }

        var envelope = Path.GetFullPath(authorizationEnvelopePath);
        if (File.Exists(envelope))
            files.Add((envelope, StringsConstants.AuthorizationEnvelopeFileName));
        else if (Directory.Exists(envelope))
            throw new InvalidDataException("The authorization envelope path is a directory.");

        foreach (var (source, _) in files)
        {
            RejectReparsePoint(source);
            fileSecurity.RestrictFileToCurrentUser(source);
        }

        var snapshot = Path.Combine(directory, DirectoryName);
        if (Directory.Exists(snapshot))
        {
            VerifySnapshot(snapshot, files, fileSecurity, migratedVaultPath);
            return;
        }
        if (File.Exists(snapshot))
            throw new InvalidDataException("The rollback snapshot path is occupied by a file.");

        var staging = Path.Combine(directory, $"{DirectoryName}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(staging);
        try
        {
            fileSecurity.RestrictDirectoryToCurrentUser(staging);
            foreach (var (source, name) in files)
            {
                var destination = Path.Combine(staging, name);
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                           FileShare.None, 81920, FileOptions.WriteThrough))
                {
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                fileSecurity.RestrictFileToCurrentUser(destination);
                VerifyIdentical(source, destination);
            }

            Directory.Move(staging, snapshot);
            VerifySnapshot(snapshot, files, fileSecurity, migratedVaultPath);
        }
        catch (Exception original)
        {
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(
                        "The rollback snapshot failed and its staging directory could not be removed.",
                        original, cleanup);
                }
            }
            throw;
        }
    }

    private static void VerifySnapshot(
        string snapshot,
        IReadOnlyList<(string Source, string Name)> files,
        IPlatformFileSecurity fileSecurity,
        string? migratedVaultPath)
    {
        RejectReparsePoint(snapshot);
        fileSecurity.RestrictDirectoryToCurrentUser(snapshot);
        if (Directory.EnumerateDirectories(snapshot).Any())
            throw new InvalidDataException("The rollback snapshot contains an unexpected directory.");

        var sources = files.ToDictionary(file => file.Name, file => file.Source,
            StringComparer.Ordinal);
        var actualNames = Directory.EnumerateFiles(snapshot)
            .Select(path => Path.GetFileName(path)!)
            .ToHashSet(StringComparer.Ordinal);
        if (migratedVaultPath is not null)
        {
            for (var generation = 1; generation <= RotatingBackupCount; generation++)
            {
                var name = $"{StringsConstants.LegacyTokensStorageFileName}.bak{generation}";
                if (!actualNames.Contains(name) || sources.ContainsKey(name)) continue;
                var migrated = $"{Path.GetFullPath(migratedVaultPath)}.bak{generation}";
                if (File.Exists(migrated)) sources.Add(name, migrated);
            }
        }
        if (!sources.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(actualNames))
            throw new InvalidDataException("The rollback snapshot file set differs from the legacy vault.");

        foreach (var (name, source) in sources)
        {
            var copy = Path.Combine(snapshot, name);
            RejectReparsePoint(copy);
            fileSecurity.RestrictFileToCurrentUser(copy);
            VerifyIdentical(source, copy);
        }
    }

    private static void VerifyIdentical(string source, string copy)
    {
        byte[]? sourceHash = null;
        byte[]? copyHash = null;
        try
        {
            using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                sourceHash = SHA256.HashData(stream);
            using (var stream = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read))
                copyHash = SHA256.HashData(stream);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, copyHash))
                throw new InvalidDataException("The rollback snapshot does not match the legacy vault.");
        }
        finally
        {
            if (sourceHash is not null) CryptographicOperations.ZeroMemory(sourceHash);
            if (copyHash is not null) CryptographicOperations.ZeroMemory(copyHash);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A sensitive migration path is a reparse point.");
    }
}
