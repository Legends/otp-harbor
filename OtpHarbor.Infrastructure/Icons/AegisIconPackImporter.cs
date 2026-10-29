using System.Text.Json;
using FluentResults;
using TOTP.Core.Icons;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Icons;

public sealed class AegisIconPackImporter : IIconPackImporter
{
    private const string MetadataSuffix = "pack.json";

    public string Id => "aegis";

    public string DisplayName => "Aegis";

    public ValueTask<bool> CanImportAsync(
        IconPackSource source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var archive = IconImportArchive.Open(source);
            return ValueTask.FromResult(
                IconImportArchive.ContainsEntryBySuffix(archive, MetadataSuffix));
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
            var metadataEntry = IconImportArchive.FindUniqueEntryBySuffix(
                archive,
                MetadataSuffix,
                IconImportArchive.MaximumMetadataBytes)
                ?? throw new InvalidDataException("Aegis pack metadata was not found.");
            var prefix = IconImportArchive.PrefixBefore(metadataEntry, MetadataSuffix);
            await using var metadataStream = metadataEntry.Open();
            using var document = await JsonDocument.ParseAsync(
                metadataStream,
                new JsonDocumentOptions { MaxDepth = 16 },
                cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("uuid", out var uuidElement)
                || !Guid.TryParse(uuidElement.GetString(), out _)
                || !root.TryGetProperty("name", out var nameElement)
                || !IconImportArchive.IsSafeDisplayText(nameElement.GetString())
                || !root.TryGetProperty("version", out var versionElement)
                || !root.TryGetProperty("icons", out var iconsElement)
                || iconsElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Unexpected Aegis icon-pack metadata.");

            var packName = nameElement.GetString()!.Trim();
            var version = versionElement.ValueKind switch
            {
                JsonValueKind.Number => versionElement.GetRawText(),
                JsonValueKind.String when IconImportArchive.IsSafeDisplayText(versionElement.GetString(), 64) =>
                    versionElement.GetString()!,
                _ => throw new InvalidDataException("The Aegis icon-pack version is invalid.")
            };
            var icons = new List<ImportedIcon>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in iconsElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("filename", out var fileElement)
                    || !IconImportArchive.IsSafeDisplayText(fileElement.GetString(), 512)) continue;
                var relativeName = IconImportArchive.NormalizeEntryName(fileElement.GetString()!);
                IconImportArchive.EnsureSafeRelativePath(relativeName);
                if (!relativeName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    continue;
                var entry = IconImportArchive.FindEntryByPath(archive, prefix + relativeName);
                if (entry is null) continue;
                var fallbackName = Path.GetFileNameWithoutExtension(relativeName);
                var displayName = item.TryGetProperty("name", out var displayNameElement)
                    && IconImportArchive.IsSafeDisplayText(displayNameElement.GetString())
                        ? displayNameElement.GetString()!
                        : fallbackName;
                var id = IconImportArchive.ToCanonicalId(fallbackName);
                if (!IconImportArchive.IsCanonicalId(id)) continue;
                // Official Aegis packs may intentionally contain a primary and a
                // generic icon with the same filename stem. Keep the first
                // manifest entry (the provider-defined preference order) so the
                // issuer alias remains deterministic instead of ambiguous.
                if (!ids.Add(id)) continue;

                var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id, displayName };
                if (item.TryGetProperty("issuer", out var issuers)
                    && issuers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var issuer in issuers.EnumerateArray())
                    {
                        var value = issuer.ValueKind == JsonValueKind.String ? issuer.GetString() : null;
                        if (IconImportArchive.IsSafeDisplayText(value, 128)) aliases.Add(value!);
                    }
                }
                var svg = await IconImportArchive.ReadValidatedSvgWithMetadataAsync(
                    entry,
                    cancellationToken);
                icons.Add(new ImportedIcon(
                    id,
                    displayName,
                    svg.Data,
                    aliases.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .Take(IconImportArchive.MaximumAliasesPerIcon)
                        .ToArray(),
                    Id,
                    svg.BackgroundColor ?? "#334155"));
                if (icons.Count > IconImportArchive.MaximumIcons)
                    throw new InvalidDataException("The Aegis icon pack contains too many SVG icons.");
            }
            if (icons.Count == 0)
                throw new InvalidDataException("The Aegis icon pack contains no supported SVG icons.");

            return Result.Ok(new IconPackImportResult(
                Id,
                packName,
                version,
                BrandIconPackFormat.Aegis,
                icons,
                await IconImportArchive.ReadNoticesAsync(archive, prefix, cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
            or InvalidDataException
            or JsonException
            or System.Xml.XmlException
            or IconImportArchive.SvgValidationException)
        {
            return Result.Fail("The Aegis icon-pack archive is invalid or unsupported.");
        }
    }
}
