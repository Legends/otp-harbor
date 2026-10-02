using FluentResults;

namespace TOTP.Core.Icons;

public interface IIconPackImporterResolver
{
    Task<Result<IconPackImportResult>> ImportAsync(
        IconPackSource source,
        CancellationToken cancellationToken = default);
}
