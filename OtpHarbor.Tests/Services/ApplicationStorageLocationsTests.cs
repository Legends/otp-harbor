using Moq;
using TOTP.Core.Backup;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class ApplicationStorageLocationsTests
{
    [Fact]
    public void GetLocations_UsesPlatformAndServiceLocationsWithoutCreatingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"otp-paths-{Guid.NewGuid():N}");
        var paths = CreatePaths(root);
        var vault = Path.Combine(root, "custom", "master.vault");
        var provider = new ApplicationStorageLocations(paths.Object, vault, Mock.Of<IAutomaticBackupCoordinator>());

        var locations = provider.GetLocations().ToDictionary(location => location.Kind);

        Assert.Equal(vault, locations[ApplicationStorageLocationKind.Vault].Path);
        Assert.Equal(Path.GetDirectoryName(vault), locations[ApplicationStorageLocationKind.Vault].FolderPath);
        Assert.Equal(paths.Object.PreferencesFilePath, locations[ApplicationStorageLocationKind.Preferences].Path);
        Assert.Equal(paths.Object.AuthorizationEnvelopeFilePath, locations[ApplicationStorageLocationKind.UnlockMetadata].Path);
        Assert.Equal(paths.Object.CacheDirectory, locations[ApplicationStorageLocationKind.Cache].Path);
        Assert.Equal(Path.Combine(root, AppearanceSettingsService.FileName), locations[ApplicationStorageLocationKind.Appearance].Path);
        Assert.Equal(Path.Combine(root, SimpleIconsBrandIconPackService.StorageDirectoryName), locations[ApplicationStorageLocationKind.Icons].Path);
        Assert.Equal(Path.Combine(root, PortableUpdateService.DownloadDirectoryName), locations[ApplicationStorageLocationKind.UpdateDownloads].Path);
        Assert.All(locations.Values, location => Assert.True(Path.IsPathFullyQualified(location.Path)));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void GetLocations_TracksConfiguredBackupTargetsAndKeepsDocumentUrisIntact()
    {
        var root = Path.Combine(Path.GetTempPath(), "otp-paths");
        var backup = new Mock<IAutomaticBackupCoordinator>();
        AutomaticBackupDestination? destination = null;
        backup.SetupGet(value => value.Destination).Returns(() => destination);
        var provider = new ApplicationStorageLocations(CreatePaths(root).Object, Path.Combine(root, "master.vault"), backup.Object);
        var changed = 0;
        provider.Changed += (_, _) => changed++;
        Assert.DoesNotContain(provider.GetLocations(), location => location.Kind == ApplicationStorageLocationKind.AutomaticBackupDestination);

        destination = new AutomaticBackupDestination("filesystem", Path.Combine(root, "selected-backups"), "Selected backups");
        backup.Raise(value => value.StatusChanged += null, EventArgs.Empty);
        var folder = Assert.Single(provider.GetLocations(), location => location.Kind == ApplicationStorageLocationKind.AutomaticBackupDestination);
        Assert.Equal(destination.Location, folder.Path);
        Assert.Equal(destination.Location, folder.FolderPath);

        destination = new AutomaticBackupDestination("android-saf", "content://example.documents/tree/primary%3ABackups", "Backups");
        backup.Raise(value => value.StatusChanged += null, EventArgs.Empty);
        var document = Assert.Single(provider.GetLocations(), location => location.Kind == ApplicationStorageLocationKind.AutomaticBackupDestination);
        Assert.Equal(destination.Location, document.Path);
        Assert.Null(document.FolderPath);
        Assert.Equal(2, changed);

        destination = null;
        Assert.DoesNotContain(provider.GetLocations(), location => location.Kind == ApplicationStorageLocationKind.AutomaticBackupDestination);
    }

    [Fact]
    public void GetLocations_OmitsAnUnavailablePlatformCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "otp-paths");
        var paths = CreatePaths(root);
        paths.SetupGet(value => value.CacheDirectory).Returns((string?)null);
        var provider = new ApplicationStorageLocations(paths.Object, Path.Combine(root, "master.vault"), Mock.Of<IAutomaticBackupCoordinator>());
        Assert.DoesNotContain(provider.GetLocations(), location => location.Kind == ApplicationStorageLocationKind.Cache);
    }

    private static Mock<IPlatformApplicationPaths> CreatePaths(string root)
    {
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.ApplicationDataDirectory).Returns(root);
        paths.SetupGet(value => value.VaultFilePath).Returns(Path.Combine(root, "master.vault"));
        paths.SetupGet(value => value.AuthorizationEnvelopeFilePath).Returns(Path.Combine(root, "authorization-envelope.bin"));
        paths.SetupGet(value => value.PreferencesFilePath).Returns(Path.Combine(root, "config", "preferences.json"));
        paths.SetupGet(value => value.BackupDirectory).Returns(Path.Combine(root, "backups"));
        paths.SetupGet(value => value.CacheDirectory).Returns(Path.Combine(root, "cache"));
        paths.SetupGet(value => value.LogDirectory).Returns(Path.Combine(root, "logs"));
        paths.SetupGet(value => value.LogFilePath).Returns(Path.Combine(root, "logs", "app.log"));
        paths.SetupGet(value => value.ExecutableDirectory).Returns(Path.Combine(root, "program"));
        paths.SetupGet(value => value.ConfigurationFilePath).Returns(Path.Combine(root, "program", "appsettings.json"));
        paths.SetupGet(value => value.UpdateStateFilePath).Returns(Path.Combine(root, "autoupdate-state.json"));
        return paths;
    }
}
