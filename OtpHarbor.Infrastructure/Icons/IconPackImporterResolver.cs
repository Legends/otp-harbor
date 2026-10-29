using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Icons;

namespace TOTP.Infrastructure.Icons;

public sealed class IconPackImporterResolver(
    IEnumerable<IIconPackImporter> importers,
    ILogger<IconPackImporterResolver> logger) : IIconPackImporterResolver
{
    private readonly IReadOnlyList<IIconPackImporter> _importers =
        importers?.ToArray() ?? throw new ArgumentNullException(nameof(importers));
    private readonly ILogger<IconPackImporterResolver> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Result<IconPackImportResult>> ImportAsync(
        IconPackSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var importer in _importers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reset(source.Stream);
            if (!await importer.CanImportAsync(source, cancellationToken)) continue;
            Reset(source.Stream);
            var result = await importer.ImportAsync(source, cancellationToken);
            if (result.IsFailed)
                _logger.LogWarning("The {IconPackProvider} icon-pack importer rejected the selected archive.", importer.Id);
            return result;
        }
        return Result.Fail("No supported icon-pack format was detected.");
    }

    private static void Reset(Stream stream)
    {
        if (!stream.CanSeek)
            throw new InvalidDataException("Icon pack streams must be seekable during format detection.");
        stream.Position = 0;
    }
}
