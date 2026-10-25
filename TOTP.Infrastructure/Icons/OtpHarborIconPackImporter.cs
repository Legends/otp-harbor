using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentResults;
using TOTP.Core.Icons;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Branding;

namespace TOTP.Infrastructure.Icons;

public sealed partial class OtpHarborIconPackImporter : IIconPackImporter
{
    private const int MinimumFormatVersion = 1;
    private const int MaximumFormatVersion = 2;
    private const string RuntimeMediaType = "image/png";
    private const string RuntimeRenderProfile = "otp-harbor-foreground-v1";
    private const int MaximumPackJsonBytes = 128 * 1024 * 1024;
    private const int MaximumSources = 16;
    private const int MaximumSourcesPerBrand = 32;
    private const int MaximumMetadataEntries = 64;
    private const int MaximumMetadataValueLength = 4_096;
    private const int MaximumLicenseBytes = 1_048_576;
    private const int MaximumLicenseBytesTotal = 8 * 1024 * 1024;
    private const string ManifestPath = "pack.json";
    private const string ExpectedPackId = "otp-harbor-icons";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public string Id => ExpectedPackId;

    public string DisplayName => "OTP Harbor Icons";

    public async ValueTask<bool> CanImportAsync(
        IconPackSource source,
        CancellationToken cancellationToken = default)
    {
        var hasOtpHarborExtension = string.Equals(
            Path.GetExtension(source.FileName),
            ".otphicons",
            StringComparison.OrdinalIgnoreCase);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var archive = IconImportArchive.Open(source);
            var manifest = FindRootManifest(CreateCanonicalFileIndex(archive));
            if (manifest is null) return hasOtpHarborExtension;
            var bytes = await IconImportArchive.ReadBoundedAsync(
                manifest,
                MaximumPackJsonBytes,
                cancellationToken);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("formatVersion", out _)
                && root.TryGetProperty("packId", out _)
                && root.TryGetProperty("issuerAliases", out _);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            return hasOtpHarborExtension;
        }
    }

    public async Task<Result<IconPackImportResult>> ImportAsync(
        IconPackSource source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var archive = IconImportArchive.Open(source);
            var entriesByPath = CreateCanonicalFileIndex(archive);
            var manifestEntry = FindRootManifest(entriesByPath)
                ?? throw new InvalidDataException("The OTP Harbor icon pack has no root pack.json.");
            var manifestBytes = await IconImportArchive.ReadBoundedAsync(
                manifestEntry,
                MaximumPackJsonBytes,
                cancellationToken);
            PackDocument document;
            try
            {
                document = JsonSerializer.Deserialize<PackDocument>(manifestBytes, JsonOptions)
                    ?? throw new InvalidDataException("The OTP Harbor icon pack manifest is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The OTP Harbor icon pack manifest is malformed.", ex);
            }
            ValidateVersionSpecificManifestShape(manifestBytes, document.FormatVersion);

            var imported = await ValidateAndReadAsync(
                entriesByPath,
                manifestEntry,
                document,
                cancellationToken);
            return Result.Ok(imported);
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
            return Result.Fail(new Error("The OTP Harbor icon pack is invalid or unsupported.")
                .CausedBy(ex));
        }
    }

    private static async Task<IconPackImportResult> ValidateAndReadAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entriesByPath,
        ZipArchiveEntry manifestEntry,
        PackDocument document,
        CancellationToken cancellationToken)
    {
        if (document.FormatVersion is < MinimumFormatVersion or > MaximumFormatVersion)
            throw new InvalidDataException("The OTP Harbor icon pack format version is unsupported.");
        if (!string.Equals(document.PackId, ExpectedPackId, StringComparison.Ordinal)
            || !IconImportArchive.IsSafeDisplayText(document.Name))
            throw new InvalidDataException("The OTP Harbor icon pack identity is invalid.");
        if (document.Sources is null || document.Sources.Count is <= 0 or > MaximumSources
            || document.Brands is null || document.Brands.Count == 0
            || document.IssuerAliases is null
            || document.IssuerAliases.Count == 0)
            throw new InvalidDataException("The OTP Harbor icon pack manifest exceeds supported limits.");

        var expectedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            IconImportArchive.NormalizeEntryName(manifestEntry.FullName)
        };
        var sourceProviders = new HashSet<string>(StringComparer.Ordinal);
        var sources = new List<IconPackSourceProvenance>(document.Sources.Count);
        var notices = new List<ImportedIconNotice>();
        var totalLicenseBytes = 0;
        foreach (var source in document.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePackSource(source, sourceProviders);
            var licensePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var licensePath in source.LicenseFiles!)
            {
                if (!licensePaths.Add(licensePath)
                    || !licensePath.StartsWith($"licenses/{source.Provider}/", StringComparison.Ordinal)
                    || !IsCanonicalArchivePath(licensePath))
                    throw new InvalidDataException("The OTP Harbor icon pack contains an invalid license reference.");
                var licenseEntry = FindCanonicalEntry(entriesByPath, licensePath)
                    ?? throw new InvalidDataException("The OTP Harbor icon pack is missing a referenced license.");
                var data = await IconImportArchive.ReadBoundedAsync(
                    licenseEntry,
                    MaximumLicenseBytes,
                    cancellationToken);
                totalLicenseBytes = checked(totalLicenseBytes + data.Length);
                if (totalLicenseBytes > MaximumLicenseBytesTotal)
                    throw new InvalidDataException("The OTP Harbor icon pack license data is too large.");
                if (!expectedFiles.Add(licensePath))
                    throw new InvalidDataException("The OTP Harbor icon pack contains a duplicate license reference.");
                notices.Add(new ImportedIconNotice(
                    Path.GetFileName(licensePath),
                    data,
                    licensePath));
            }
            sources.Add(new IconPackSourceProvenance(
                source.Provider!,
                source.InputFileName!,
                source.Sha256!,
                source.Version,
                source.Revision,
                source.SourceUrl,
                source.Metadata!,
                source.LicenseFiles));
        }

        var brandsById = new Dictionary<string, PackBrand>(StringComparer.Ordinal);
        var expectedAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var icons = new List<ImportedIcon>(document.Brands.Count);
        foreach (var brand in document.Brands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateBrandShape(brand, sourceProviders, document.FormatVersion);
            if (!brandsById.TryAdd(brand.Id!, brand))
                throw new InvalidDataException("The OTP Harbor icon pack contains duplicate brand IDs.");
            byte[]? svg = null;
            string? iconPath = null;
            ImportedRuntimeIcon? runtimeIcon = null;
            if (document.FormatVersion == 1)
            {
                iconPath = $"icons/{brand.Id}.svg";
                if (!string.Equals(brand.Icon, iconPath, StringComparison.Ordinal)
                    || !expectedFiles.Add(iconPath))
                    throw new InvalidDataException("The OTP Harbor icon pack contains an invalid icon reference.");
                var iconEntry = FindCanonicalEntry(entriesByPath, iconPath)
                    ?? throw new InvalidDataException("The OTP Harbor icon pack is missing a referenced icon.");
                svg = await IconImportArchive.ReadValidatedSvgAsync(iconEntry, cancellationToken);
            }
            else
            {
                var expectedRuntimePath = $"runtime/foreground-128/{brand.Id}.png";
                var runtime = brand.RuntimeIcon!;
                if (!string.Equals(runtime.Path, expectedRuntimePath, StringComparison.Ordinal)
                    || !expectedFiles.Add(expectedRuntimePath))
                    throw new InvalidDataException("The OTP Harbor icon pack contains an invalid runtime icon reference.");
                var runtimeEntry = FindCanonicalEntry(entriesByPath, expectedRuntimePath)
                    ?? throw new InvalidDataException("The OTP Harbor icon pack is missing a referenced runtime icon.");
                var png = await IconImportArchive.ReadBoundedAsync(
                    runtimeEntry,
                    RuntimePngValidator.MaximumEncodedBytes,
                    cancellationToken);
                RuntimePngValidator.ValidateEncoded(png, runtime.Sha256!);
                runtimeIcon = new ImportedRuntimeIcon(
                    expectedRuntimePath,
                    runtime.MediaType!,
                    runtime.Width,
                    runtime.Height,
                    runtime.Sha256!,
                    runtime.RenderProfile!);
            }

            foreach (var alias in brand.IssuerAliases!)
            {
                var key = OtpHarborIssuerNormalizer.Normalize(alias);
                if (key.Length == 0
                    || expectedAliases.TryGetValue(key, out var owner)
                    && !string.Equals(owner, brand.Id, StringComparison.Ordinal))
                    throw new InvalidDataException("The OTP Harbor icon pack contains an ambiguous issuer alias.");
                expectedAliases.TryAdd(key, brand.Id!);
            }

            var selected = ToSourceReference(brand.SelectedSource!);
            var contributing = brand.Sources!
                .Select(ToSourceReference)
                .ToArray();
            icons.Add(new ImportedIcon(
                brand.Id!,
                brand.DisplayName!,
                svg,
                brand.IssuerAliases,
                ExpectedPackId,
                brand.BackgroundColor!,
                iconPath,
                selected,
                contributing,
                runtimeIcon));
        }

        var aliasKeys = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new List<IconPackIssuerAlias>(document.IssuerAliases.Count);
        foreach (var alias in document.IssuerAliases)
        {
            if (alias.Key is null
                || alias.Key.Length is 0 or > 512
                || !string.Equals(
                    OtpHarborIssuerNormalizer.Normalize(alias.Key),
                    alias.Key,
                    StringComparison.Ordinal)
                || !aliasKeys.Add(alias.Key)
                || alias.BrandId is null
                || !brandsById.ContainsKey(alias.BrandId)
                || !expectedAliases.TryGetValue(alias.Key, out var expectedBrandId)
                || !string.Equals(expectedBrandId, alias.BrandId, StringComparison.Ordinal))
                throw new InvalidDataException("The OTP Harbor icon pack contains an invalid issuer index.");
            aliases.Add(new IconPackIssuerAlias(alias.Key, alias.BrandId));
        }
        if (aliasKeys.Count != expectedAliases.Count)
            throw new InvalidDataException("The OTP Harbor icon pack issuer index is incomplete.");

        foreach (var path in entriesByPath.Keys)
        {
            if (!expectedFiles.Contains(path))
                throw new InvalidDataException("The OTP Harbor icon pack contains an unreferenced file.");
        }

        return new IconPackImportResult(
            ExpectedPackId,
            document.Name!,
            $"format-{document.FormatVersion}",
            BrandIconPackFormat.OtpHarbor,
            icons,
            notices,
            new IconPackMetadata(
                document.FormatVersion,
                document.PackId!,
                document.Name!,
                sources,
                aliases));
    }

    private static void ValidateVersionSpecificManifestShape(
        ReadOnlyMemory<byte> manifestBytes,
        int formatVersion)
    {
        if (formatVersion is < MinimumFormatVersion or > MaximumFormatVersion) return;
        using var json = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        if (!json.RootElement.TryGetProperty("brands", out var brands)
            || brands.ValueKind != JsonValueKind.Array)
            return;
        foreach (var brand in brands.EnumerateArray())
        {
            var containsRuntimeIcon = brand.ValueKind == JsonValueKind.Object
                && brand.TryGetProperty("runtimeIcon", out _);
            var containsIcon = brand.ValueKind == JsonValueKind.Object
                && brand.TryGetProperty("icon", out _);
            if (formatVersion == 1 && (!containsIcon || containsRuntimeIcon)
                || formatVersion == 2 && (containsIcon || !containsRuntimeIcon))
                throw new InvalidDataException("The OTP Harbor icon pack mixes incompatible format members.");
        }
    }

    private static void ValidatePackSource(PackSource source, ISet<string> providers)
    {
        if (source.Provider is null
            || !CanonicalIdRegex().IsMatch(source.Provider)
            || !providers.Add(source.Provider)
            || !IconImportArchive.IsSafeDisplayText(source.InputFileName)
            || Path.GetFileName(source.InputFileName) != source.InputFileName
            || source.Sha256 is null
            || !LowercaseSha256Regex().IsMatch(source.Sha256)
            || source.Version is not null && !IconImportArchive.IsSafeDisplayText(source.Version)
            || source.Revision is not null && !IconImportArchive.IsSafeDisplayText(source.Revision)
            || source.SourceUrl is not null && (!Uri.TryCreate(source.SourceUrl, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || !IsSafeMetadata(source.Metadata)
            || source.LicenseFiles is null
            || source.LicenseFiles.Count > IconImportArchive.MaximumNotices)
            throw new InvalidDataException("The OTP Harbor icon pack contains invalid source provenance.");
    }

    private static void ValidateBrandShape(
        PackBrand brand,
        IReadOnlySet<string> sourceProviders,
        int formatVersion)
    {
        if (brand.Id is null
            || !CanonicalIdRegex().IsMatch(brand.Id)
            || !IconImportArchive.IsSafeDisplayText(brand.DisplayName)
            || brand.BackgroundColor is null
            || !UppercaseColorRegex().IsMatch(brand.BackgroundColor)
            || brand.IssuerAliases is null
            || brand.IssuerAliases.Count == 0
            || brand.SelectedSource is null
            || brand.Sources is null
            || brand.Sources.Count is <= 0 or > MaximumSourcesPerBrand)
            throw new InvalidDataException("The OTP Harbor icon pack contains invalid brand metadata.");

        if (formatVersion == 1 && (brand.Icon is null || brand.RuntimeIcon is not null))
            throw new InvalidDataException("A format-1 OTP Harbor icon pack cannot contain runtime icons.");
        if (formatVersion == 2
            && (brand.Icon is not null
                || brand.RuntimeIcon is null
                || !string.Equals(brand.RuntimeIcon.MediaType, RuntimeMediaType, StringComparison.Ordinal)
                || brand.RuntimeIcon.Width != RuntimePngValidator.Width
                || brand.RuntimeIcon.Height != RuntimePngValidator.Height
                || brand.RuntimeIcon.Sha256 is null
                || !LowercaseSha256Regex().IsMatch(brand.RuntimeIcon.Sha256)
                || !string.Equals(
                    brand.RuntimeIcon.RenderProfile,
                    RuntimeRenderProfile,
                    StringComparison.Ordinal)))
            throw new InvalidDataException("The OTP Harbor icon pack contains invalid runtime icon metadata.");

        var originalAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in brand.IssuerAliases)
            if (!IconImportArchive.IsSafeDisplayText(alias)
                || !originalAliases.Add(alias))
                throw new InvalidDataException("The OTP Harbor icon pack contains an invalid original issuer alias.");

        ValidateSourceReference(brand.SelectedSource, sourceProviders);
        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in brand.Sources)
        {
            ValidateSourceReference(source, sourceProviders);
            if (!references.Add(source.Provider + "/" + source.SourceId))
                throw new InvalidDataException("The OTP Harbor icon pack contains duplicate brand provenance.");
        }
        if (!references.Contains(brand.SelectedSource.Provider + "/" + brand.SelectedSource.SourceId))
            throw new InvalidDataException("The OTP Harbor icon pack selected source is not a contributor.");
    }

    private static void ValidateSourceReference(
        SourceReference source,
        IReadOnlySet<string> sourceProviders)
    {
        if (source.Provider is null
            || !sourceProviders.Contains(source.Provider)
            || !CanonicalIdRegex().IsMatch(source.Provider)
            || !IconImportArchive.IsSafeDisplayText(source.SourceId)
            || !IsSafeMetadata(source.Metadata))
            throw new InvalidDataException("The OTP Harbor icon pack contains invalid brand provenance.");
    }

    private static bool IsSafeMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata is null || metadata.Count > MaximumMetadataEntries) return false;
        foreach (var pair in metadata)
            if (!IconImportArchive.IsSafeDisplayText(pair.Key, 128)
                || pair.Value is not null
                && (pair.Value.Length > MaximumMetadataValueLength
                    || pair.Value.Any(char.IsControl)))
                return false;
        return true;
    }

    private static IconPackSourceReference ToSourceReference(SourceReference source) =>
        new(source.Provider!, source.SourceId!, source.Metadata!);

    private static IReadOnlyDictionary<string, ZipArchiveEntry> CreateCanonicalFileIndex(
        ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries.Where(static entry => entry.Name.Length > 0))
        {
            var path = IconImportArchive.NormalizeEntryName(entry.FullName);
            if (!entries.TryAdd(path, entry))
                throw new InvalidDataException("The OTP Harbor icon pack contains duplicate files.");
        }
        return entries;
    }

    private static ZipArchiveEntry? FindRootManifest(
        IReadOnlyDictionary<string, ZipArchiveEntry> entriesByPath)
    {
        return entriesByPath.TryGetValue(ManifestPath, out var manifest)
               && manifest.Length is > 0 and <= MaximumPackJsonBytes
            ? manifest
            : null;
    }

    private static ZipArchiveEntry? FindCanonicalEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entriesByPath,
        string path)
    {
        return entriesByPath.TryGetValue(path, out var entry) ? entry : null;
    }

    private static bool IsCanonicalArchivePath(string path)
    {
        if (!string.Equals(IconImportArchive.NormalizeEntryName(path), path, StringComparison.Ordinal))
            return false;
        try
        {
            IconImportArchive.EnsureSafeRelativePath(path);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalIdRegex();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex LowercaseSha256Regex();

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex UppercaseColorRegex();

    private sealed class PackDocument
    {
        public int FormatVersion { get; init; }
        public string? PackId { get; init; }
        public string? Name { get; init; }
        public List<PackSource>? Sources { get; init; }
        public List<PackBrand>? Brands { get; init; }
        public List<IssuerAliasEntry>? IssuerAliases { get; init; }
    }

    private sealed class PackSource
    {
        public string? Provider { get; init; }
        public string? InputFileName { get; init; }
        public string? Sha256 { get; init; }
        public string? Version { get; init; }
        public string? Revision { get; init; }
        public string? SourceUrl { get; init; }
        public Dictionary<string, string?>? Metadata { get; init; }
        public List<string>? LicenseFiles { get; init; }
    }

    private sealed class PackBrand
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public string? BackgroundColor { get; init; }
        public string? Icon { get; init; }
        public List<string>? IssuerAliases { get; init; }
        public SourceReference? SelectedSource { get; init; }
        public List<SourceReference>? Sources { get; init; }
        public RuntimeIconReference? RuntimeIcon { get; init; }
    }

    private sealed class RuntimeIconReference
    {
        public string? Path { get; init; }
        public string? MediaType { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public string? Sha256 { get; init; }
        public string? RenderProfile { get; init; }
    }

    private sealed class SourceReference
    {
        public string? Provider { get; init; }
        public string? SourceId { get; init; }
        public Dictionary<string, string?>? Metadata { get; init; }
    }

    private sealed class IssuerAliasEntry
    {
        public string? Key { get; init; }
        public string? BrandId { get; init; }
    }
}
