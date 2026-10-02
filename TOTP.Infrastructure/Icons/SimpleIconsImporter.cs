using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentResults;
using TOTP.Core.Icons;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Icons;

public sealed class SimpleIconsImporter : IIconPackImporter
{
    private const string MetadataSuffix = "data/simple-icons.json";

    public string Id => "simple-icons";

    public string DisplayName => "Simple Icons";

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
                ?? throw new InvalidDataException("Simple Icons metadata was not found.");
            var prefix = IconImportArchive.PrefixBefore(metadataEntry, MetadataSuffix);
            var version = await ReadPackageVersionAsync(archive, prefix, cancellationToken)
                ?? ReadVersionFromPrefix(prefix)
                ?? "unknown";

            await using var metadataStream = metadataEntry.Open();
            using var document = await JsonDocument.ParseAsync(
                metadataStream,
                new JsonDocumentOptions { MaxDepth = 16 },
                cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Unexpected Simple Icons metadata.");

            var icons = new List<ImportedIcon>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!item.TryGetProperty("title", out var titleElement)
                    || !item.TryGetProperty("hex", out var hexElement)) continue;
                var title = titleElement.GetString();
                var hex = hexElement.GetString();
                if (!IconImportArchive.IsSafeDisplayText(title)
                    || hex is null
                    || !IconImportArchive.IsHexColor(hex)) continue;
                var slug = item.TryGetProperty("slug", out var slugElement)
                    ? slugElement.GetString()
                    : TitleToSlug(title!);
                if (slug is null || !IconImportArchive.IsCanonicalId(slug) || !ids.Add(slug))
                    throw new InvalidDataException("Simple Icons contains an invalid or duplicate slug.");

                var iconEntry = archive.GetEntry($"{prefix}icons/{slug}.svg");
                if (iconEntry is null) continue;
                var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug, title! };
                if (item.TryGetProperty("aliases", out var aliasObject))
                {
                    AddAliases(aliasObject, "aka", aliases);
                    AddAliases(aliasObject, "old", aliases);
                }
                icons.Add(new ImportedIcon(
                    slug,
                    title!,
                    await IconImportArchive.ReadValidatedSvgAsync(iconEntry, cancellationToken),
                    aliases.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .Take(IconImportArchive.MaximumAliasesPerIcon)
                        .ToArray(),
                    Id,
                    $"#{hex.ToUpperInvariant()}"));
                if (icons.Count > IconImportArchive.MaximumIcons)
                    throw new InvalidDataException("The Simple Icons pack contains too many icons.");
            }
            if (icons.Count == 0)
                throw new InvalidDataException("The Simple Icons pack contains no usable SVG icons.");

            return Result.Ok(new IconPackImportResult(
                Id,
                DisplayName,
                version,
                BrandIconPackFormat.SimpleIcons,
                icons,
                await IconImportArchive.ReadNoticesAsync(archive, prefix, cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or System.Xml.XmlException)
        {
            return Result.Fail("The Simple Icons archive is invalid or unsupported.");
        }
    }

    private static async Task<string?> ReadPackageVersionAsync(
        ZipArchive archive,
        string prefix,
        CancellationToken cancellationToken)
    {
        var package = archive.GetEntry(prefix + "package.json");
        if (package is null || package.Length is <= 0 or > 256 * 1024) return null;
        await using var stream = package.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "simple-icons", StringComparison.Ordinal)
            && document.RootElement.TryGetProperty("version", out var version)
            && IconImportArchive.IsSafeDisplayText(version.GetString(), 64)
                ? version.GetString()
                : null;
    }

    private static string? ReadVersionFromPrefix(string prefix)
    {
        var folder = prefix.TrimEnd('/').Split('/').LastOrDefault();
        const string marker = "simple-icons-";
        return folder?.StartsWith(marker, StringComparison.OrdinalIgnoreCase) == true
            ? folder[marker.Length..]
            : null;
    }

    private static void AddAliases(JsonElement aliasObject, string propertyName, ISet<string> target)
    {
        if (!aliasObject.TryGetProperty(propertyName, out var aliases)
            || aliases.ValueKind != JsonValueKind.Array) return;
        foreach (var alias in aliases.EnumerateArray())
        {
            var value = alias.ValueKind == JsonValueKind.String
                ? alias.GetString()
                : alias.ValueKind == JsonValueKind.Object
                    && alias.TryGetProperty("title", out var title)
                        ? title.GetString()
                        : null;
            if (IconImportArchive.IsSafeDisplayText(value, 128)) target.Add(value!);
        }
    }

    // Mirrors the provider's documented title-to-slug substitutions.
    private static string TitleToSlug(string title)
    {
        var lowered = title.ToLowerInvariant();
        var replaced = new StringBuilder(lowered.Length);
        foreach (var character in lowered)
        {
            replaced.Append(character switch
            {
                '+' => "plus",
                '.' => "dot",
                '&' => "and",
                'đ' => "d",
                'ħ' => "h",
                'ı' => "i",
                'ĸ' => "k",
                'ŀ' => "l",
                'ł' => "l",
                'ß' => "ss",
                'ŧ' => "t",
                'ø' => "o",
                _ => character.ToString()
            });
        }
        return IconImportArchive.ToCanonicalId(replaced.ToString());
    }
}
