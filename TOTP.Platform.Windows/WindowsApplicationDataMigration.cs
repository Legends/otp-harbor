namespace TOTP.Platform.Windows;

public static class WindowsApplicationDataMigration
{
    public static void MigrateLegacyDirectory(WindowsApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var legacyDirectory = Path.GetFullPath(paths.LegacyApplicationDataDirectory);
        var currentDirectory = Path.GetFullPath(paths.ApplicationDataDirectory);
        var legacyParent = Directory.GetParent(legacyDirectory)?.FullName;
        var currentParent = Directory.GetParent(currentDirectory)?.FullName;

        if (legacyParent is null
            || currentParent is null
            || !string.Equals(legacyParent, currentParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The legacy and current application-data directories must share the same parent.");
        }

        if (!Directory.Exists(legacyDirectory)) return;
        EnsureRegularDirectory(legacyDirectory, "legacy");

        if (Directory.Exists(currentDirectory))
        {
            EnsureRegularDirectory(currentDirectory, "current");
            if (Directory.EnumerateFileSystemEntries(currentDirectory).Any())
                return;

            Directory.Delete(currentDirectory, recursive: false);
        }

        Directory.Move(legacyDirectory, currentDirectory);
    }

    private static void EnsureRegularDirectory(string path, string description)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"The {description} application-data directory cannot be a reparse point.");
        }
    }
}
