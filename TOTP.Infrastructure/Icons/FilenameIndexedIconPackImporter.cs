using FluentResults;
using TOTP.Core.Icons;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Icons;

public sealed class FilenameIndexedIconPackImporter : IIconPackImporter
{
    public string Id => "filename-indexed";

    public string DisplayName => "Filename-indexed SVG pack";

    public ValueTask<bool> CanImportAsync(
        IconPackSource source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var archive = IconImportArchive.Open(source);
            return ValueTask.FromResult(archive.Entries.Any(entry =>
                entry.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ValueTask.FromResult(false);
        }
    }

    public async Task<Result<IconPackImportResult>> ImportAsync(
        IconPackSource source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var archive = IconImportArchive.Open(source);
            var icons = new List<ImportedIcon>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Name.Length == 0
                    || !entry.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                var sourceName = Path.GetFileNameWithoutExtension(entry.Name);
                var id = IconImportArchive.ToCanonicalId(sourceName);
                if (!string.Equals(id, sourceName, StringComparison.OrdinalIgnoreCase)
                    || !IconImportArchive.IsCanonicalId(id)
                    || !ids.Add(id))
                    throw new InvalidDataException("A filename-indexed SVG has an invalid or duplicate id.");
                icons.Add(new ImportedIcon(
                    id,
                    sourceName,
                    await IconImportArchive.ReadValidatedSvgAsync(entry, cancellationToken),
                    [id, sourceName],
                    Id));
                if (icons.Count > IconImportArchive.MaximumIcons)
                    throw new InvalidDataException("The filename-indexed pack contains too many icons.");
            }
            if (icons.Count == 0)
                throw new InvalidDataException("The archive contains no filename-indexed SVG files.");

            return Result.Ok(new IconPackImportResult(
                Id,
                DisplayName,
                "filename-indexed",
                BrandIconPackFormat.FilenameIndexed,
                icons,
                await IconImportArchive.ReadNoticesAsync(archive, string.Empty, cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException)
        {
            return Result.Fail("The filename-indexed SVG archive is invalid or unsupported.");
        }
    }
}
