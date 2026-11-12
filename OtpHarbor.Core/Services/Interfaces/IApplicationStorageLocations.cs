namespace TOTP.Core.Services.Interfaces;

public enum ApplicationStorageLocationKind
{
    DataDirectory,
    Vault,
    UnlockMetadata,
    Preferences,
    Appearance,
    BackupDirectory,
    AutomaticBackupConfiguration,
    AutomaticBackupDestination,
    Icons,
    Cache,
    LogDirectory,
    LogFile,
    Installation,
    Configuration,
    UpdateState,
    UpdateDownloads
}

public sealed record ApplicationStorageLocation(
    ApplicationStorageLocationKind Kind,
    string Path,
    string? FolderPath);

public interface IApplicationStorageLocations
{
    event EventHandler? Changed;
    IReadOnlyList<ApplicationStorageLocation> GetLocations();
}
