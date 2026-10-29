using FluentResults;
using TOTP.Core.Models;

namespace TOTP.Core.Backup;

public interface IAutomaticBackupConfigurationStore
{
    Task<Result<AutomaticBackupConfigurationV1?>> LoadAsync(CancellationToken cancellationToken = default);
    Task<Result> SaveAsync(AutomaticBackupConfigurationV1 configuration, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(CancellationToken cancellationToken = default);
}

public interface IAutomaticBackupDestinationAdapter
{
    string Kind { get; }

    Task<Result<AutomaticBackupDestination?>> SelectAsync(CancellationToken cancellationToken = default);

    Task<Result> WriteAsync(
        AutomaticBackupDestination destination,
        string fileName,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AutomaticBackupStoredFile>>> ListAsync(
        AutomaticBackupDestination destination,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        AutomaticBackupDestination destination,
        AutomaticBackupStoredFile file,
        CancellationToken cancellationToken = default);
}

public interface IAutomaticBackupSnapshotService
{
    Task<Result<AutomaticBackupSnapshot>> CreateAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<Account>>> ReadAccountsAsync(
        Stream source,
        string masterPassword,
        CancellationToken cancellationToken = default);
}

public interface IAutomaticBackupCoordinator : IDisposable
{
    event EventHandler? StatusChanged;

    AutomaticBackupStatus Status { get; }

    Task<Result> InitializeAsync(CancellationToken cancellationToken = default);
    Task<Result<AutomaticBackupRunResult>> ConfigureAsync(CancellationToken cancellationToken = default);
    Task<Result> DisableAsync(CancellationToken cancellationToken = default);
    Task<Result<AutomaticBackupRunResult>> BackupNowAsync(CancellationToken cancellationToken = default);
    Task<Result<AutomaticBackupRunResult>> EnsureCurrentCheckpointAsync(CancellationToken cancellationToken = default);
    void RequestBackup();
}

public interface IVaultSnapshotReader
{
    Result<IReadOnlyList<Account>> ReadAccounts(
        ReadOnlySpan<byte> encryptedVault,
        ReadOnlySpan<byte> vaultKey);
}
