using FluentResults;

namespace TOTP.Core.Icons;

/// <summary>
/// Parses one documented icon-pack format into OTP Harbor's platform-neutral model.
/// Implementations must not depend on UI or platform storage APIs.
/// </summary>
public interface IIconPackImporter
{
    string Id { get; }

    string DisplayName { get; }

    ValueTask<bool> CanImportAsync(
        IconPackSource source,
        CancellationToken cancellationToken = default);

    Task<Result<IconPackImportResult>> ImportAsync(
        IconPackSource source,
        CancellationToken cancellationToken = default);
}
