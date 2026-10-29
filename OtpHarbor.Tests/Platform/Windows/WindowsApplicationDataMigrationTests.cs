using TOTP.Platform.Windows;

namespace TOTP.Tests.Platform.Windows;

public sealed class WindowsApplicationDataMigrationTests
{
    [Fact]
    public void MigrateLegacyDirectory_WhenCurrentDirectoryIsMissing_MovesAllData()
    {
        using var temp = new TemporaryDirectory();
        var paths = CreatePaths(temp.Path);
        Directory.CreateDirectory(paths.LegacyApplicationDataDirectory);
        File.WriteAllText(
            Path.Combine(paths.LegacyApplicationDataDirectory, "master.totp"),
            "encrypted-test-data");

        WindowsApplicationDataMigration.MigrateLegacyDirectory(paths);

        Assert.False(Directory.Exists(paths.LegacyApplicationDataDirectory));
        Assert.Equal(
            "encrypted-test-data",
            File.ReadAllText(Path.Combine(paths.ApplicationDataDirectory, "master.totp")));
    }

    [Fact]
    public void MigrateLegacyDirectory_WhenCurrentDirectoryIsEmpty_ReplacesItWithLegacyData()
    {
        using var temp = new TemporaryDirectory();
        var paths = CreatePaths(temp.Path);
        Directory.CreateDirectory(paths.LegacyApplicationDataDirectory);
        Directory.CreateDirectory(paths.ApplicationDataDirectory);
        File.WriteAllText(
            Path.Combine(paths.LegacyApplicationDataDirectory, "authorization-envelope.bin"),
            "encrypted-test-data");

        WindowsApplicationDataMigration.MigrateLegacyDirectory(paths);

        Assert.False(Directory.Exists(paths.LegacyApplicationDataDirectory));
        Assert.True(File.Exists(Path.Combine(
            paths.ApplicationDataDirectory,
            "authorization-envelope.bin")));
    }

    [Fact]
    public void MigrateLegacyDirectory_WhenBothDirectoriesContainData_DoesNotMergeOrOverwrite()
    {
        using var temp = new TemporaryDirectory();
        var paths = CreatePaths(temp.Path);
        Directory.CreateDirectory(paths.LegacyApplicationDataDirectory);
        Directory.CreateDirectory(paths.ApplicationDataDirectory);
        File.WriteAllText(
            Path.Combine(paths.LegacyApplicationDataDirectory, "legacy.txt"),
            "legacy");
        File.WriteAllText(
            Path.Combine(paths.ApplicationDataDirectory, "current.txt"),
            "current");

        WindowsApplicationDataMigration.MigrateLegacyDirectory(paths);

        Assert.Equal(
            "legacy",
            File.ReadAllText(Path.Combine(paths.LegacyApplicationDataDirectory, "legacy.txt")));
        Assert.Equal(
            "current",
            File.ReadAllText(Path.Combine(paths.ApplicationDataDirectory, "current.txt")));
    }

    private static WindowsApplicationPaths CreatePaths(string roamingDirectory) =>
        new(
            executableDirectory: roamingDirectory,
            roamingApplicationDataDirectory: roamingDirectory,
            hasPackageIdentity: false,
            hasInstalledDistributionMarker: false);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"otp-harbor-path-migration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
