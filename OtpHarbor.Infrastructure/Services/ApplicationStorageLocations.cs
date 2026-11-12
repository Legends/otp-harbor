using TOTP.Core.Backup;
using TOTP.Core.Services.Interfaces;
using TOTP.DAL.Services;

namespace TOTP.Infrastructure.Services;

public sealed class ApplicationStorageLocations(
    IPlatformApplicationPaths paths,
    string vaultPath,
    IAutomaticBackupCoordinator automaticBackup) : IApplicationStorageLocations
{
    public event EventHandler? Changed
    {
        add => automaticBackup.StatusChanged += value;
        remove => automaticBackup.StatusChanged -= value;
    }

    public IReadOnlyList<ApplicationStorageLocation> GetLocations()
    {
        var locations = new List<ApplicationStorageLocation>
        {
            Folder(ApplicationStorageLocationKind.DataDirectory, paths.ApplicationDataDirectory),
            File(ApplicationStorageLocationKind.Vault, Path.GetFullPath(vaultPath)),
            File(ApplicationStorageLocationKind.UnlockMetadata, paths.AuthorizationEnvelopeFilePath),
            File(ApplicationStorageLocationKind.Preferences, paths.PreferencesFilePath),
            File(ApplicationStorageLocationKind.Appearance,
                Path.Combine(paths.ApplicationDataDirectory, AppearanceSettingsService.FileName)),
            Folder(ApplicationStorageLocationKind.BackupDirectory, paths.BackupDirectory),
            File(ApplicationStorageLocationKind.AutomaticBackupConfiguration,
                Path.Combine(paths.ApplicationDataDirectory, AutomaticBackupConfigurationStore.FileName)),
            Folder(ApplicationStorageLocationKind.Icons,
                Path.Combine(paths.ApplicationDataDirectory, SimpleIconsBrandIconPackService.StorageDirectoryName)),
            Folder(ApplicationStorageLocationKind.Cache, paths.CacheDirectory ?? string.Empty),
            Folder(ApplicationStorageLocationKind.LogDirectory, paths.LogDirectory),
            File(ApplicationStorageLocationKind.LogFile, paths.LogFilePath),
            Folder(ApplicationStorageLocationKind.Installation, paths.ExecutableDirectory),
            File(ApplicationStorageLocationKind.Configuration, paths.ConfigurationFilePath),
            File(ApplicationStorageLocationKind.UpdateState, paths.UpdateStateFilePath),
            Folder(ApplicationStorageLocationKind.UpdateDownloads,
                Path.Combine(paths.ApplicationDataDirectory, PortableUpdateService.DownloadDirectoryName))
        };
        if (automaticBackup.Destination is { } destination)
            locations.Insert(7, Folder(ApplicationStorageLocationKind.AutomaticBackupDestination, destination.Location));

        return locations.Where(location => !string.IsNullOrWhiteSpace(location.Path)).ToArray();
    }

    private static ApplicationStorageLocation Folder(ApplicationStorageLocationKind kind, string path) =>
        new(kind, path, Path.IsPathFullyQualified(path) ? path : null);

    private static ApplicationStorageLocation File(ApplicationStorageLocationKind kind, string path) =>
        new(kind, path, string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path));
}
