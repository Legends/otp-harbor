using FluentResults;
using TOTP.Core.Backup;

namespace TOTP.Infrastructure.Services;

public sealed class UnavailableAutomaticBackupDestinationAdapter : IAutomaticBackupDestinationAdapter
{
    public string Kind => "unavailable";

    public Task<Result<AutomaticBackupDestination?>> SelectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Fail<AutomaticBackupDestination?>());

    public Task<Result> WriteAsync(
        AutomaticBackupDestination destination,
        string fileName,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Fail());

    public Task<Result<IReadOnlyList<AutomaticBackupStoredFile>>> ListAsync(
        AutomaticBackupDestination destination,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Fail<IReadOnlyList<AutomaticBackupStoredFile>>());

    public Task<Result> DeleteAsync(
        AutomaticBackupDestination destination,
        AutomaticBackupStoredFile file,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Fail());

    private static Result Fail() => Result.Fail(new AutomaticBackupError(
        AutomaticBackupErrorCode.DestinationUnavailable,
        "Automatic backup destinations are unavailable on this platform."));

    private static Result<T> Fail<T>() => Result.Fail<T>(new AutomaticBackupError(
        AutomaticBackupErrorCode.DestinationUnavailable,
        "Automatic backup destinations are unavailable on this platform."));
}
