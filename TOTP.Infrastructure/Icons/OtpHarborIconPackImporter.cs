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
    private const int MaximumFormatVersion = 3;
    private const string RuntimeMediaType = "image/png";
    private const string V2RuntimeRenderProfile = "otp-harbor-foreground-v1";
    private const string V3RuntimeRenderProfile = "otp-harbor-themed-appearance-v1";
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
        var runtimeAssets = new Dictionary<string, RuntimeIconReference>(StringComparer.Ordinal);
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
            ImportedRuntimeAppearances? runtimeAppearances = null;
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
            else if (document.FormatVersion == 2)
            {
                var expectedRuntimePath = $"runtime/foreground-128/{brand.Id}.png";
                var runtime = brand.RuntimeIcon!;
                if (!string.Equals(runtime.Path, expectedRuntimePath, StringComparison.Ordinal)
                    || !expectedFiles.Add(expectedRuntimePath))
                    throw new InvalidDataException("The OTP Harbor icon pack contains an invalid runtime icon reference.");
                runtimeAssets.Add(expectedRuntimePath, runtime);
                runtimeIcon = new ImportedRuntimeIcon(
                    expectedRuntimePath,
                    runtime.MediaType!,
                    runtime.Width,
                    runtime.Height,
                    runtime.Sha256!,
                    runtime.RenderProfile!);
            }
            else
            {
                var appearances = brand.Appearances!;
                runtimeAppearances = new ImportedRuntimeAppearances(
                    ImportAppearance(
                        appearances.LightTheme!,
                        expectedFiles,
                        runtimeAssets),
                    ImportAppearance(
                        appearances.DarkTheme!,
                        expectedFiles,
                        runtimeAssets));
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
                document.FormatVersion == 3
                    ? CompatibleBackgroundColor(runtimeAppearances!.LightTheme)
                    : brand.BackgroundColor!,
                iconPath,
                selected,
                contributing,
                runtimeIcon,
                runtimeAppearances));
        }

        foreach (var asset in runtimeAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtimeEntry = FindCanonicalEntry(entriesByPath, asset.Key)
                ?? throw new InvalidDataException("The OTP Harbor icon pack is missing a referenced runtime icon.");
            var png = await IconImportArchive.ReadBoundedAsync(
                runtimeEntry,
                RuntimePngValidator.MaximumEncodedBytes,
                cancellationToken);
            RuntimePngValidator.ValidateEncoded(png, asset.Value.Sha256!);
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
            var containsAppearances = brand.ValueKind == JsonValueKind.Object
                && brand.TryGetProperty("appearances", out _);
            var containsBackgroundColor = brand.ValueKind == JsonValueKind.Object
                && brand.TryGetProperty("backgroundColor", out _);
            if (formatVersion == 1
                    && (!containsIcon || containsRuntimeIcon || containsAppearances || !containsBackgroundColor)
                || formatVersion == 2
                    && (containsIcon || !containsRuntimeIcon || containsAppearances || !containsBackgroundColor)
                || formatVersion == 3
                    && (containsIcon || containsRuntimeIcon || !containsAppearances || containsBackgroundColor))
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
            || brand.IssuerAliases is null
            || brand.IssuerAliases.Count == 0
            || brand.SelectedSource is null
            || brand.Sources is null
            || brand.Sources.Count is <= 0 or > MaximumSourcesPerBrand)
            throw new InvalidDataException("The OTP Harbor icon pack contains invalid brand metadata.");

        if (formatVersion is 1 or 2
            && (brand.BackgroundColor is null || !UppercaseColorRegex().IsMatch(brand.BackgroundColor))
            || formatVersion == 3 && brand.BackgroundColor is not null)
            throw new InvalidDataException("The OTP Harbor icon pack contains invalid brand color metadata.");

        if (formatVersion == 1
            && (brand.Icon is null || brand.RuntimeIcon is not null || brand.Appearances is not null))
            throw new InvalidDataException("A format-1 OTP Harbor icon pack cannot contain runtime icons.");
        if (formatVersion == 2
            && (brand.Icon is not null
                || brand.RuntimeIcon is null
                || brand.Appearances is not null
                || !string.Equals(brand.RuntimeIcon.MediaType, RuntimeMediaType, StringComparison.Ordinal)
                || brand.RuntimeIcon.Width != RuntimePngValidator.Width
                || brand.RuntimeIcon.Height != RuntimePngValidator.Height
                || brand.RuntimeIcon.Sha256 is null
                || !LowercaseSha256Regex().IsMatch(brand.RuntimeIcon.Sha256)
                || !string.Equals(
                    brand.RuntimeIcon.RenderProfile,
                    V2RuntimeRenderProfile,
                    StringComparison.Ordinal)))
            throw new InvalidDataException("The OTP Harbor icon pack contains invalid runtime icon metadata.");
        if (formatVersion == 3
            && (brand.Icon is not null || brand.RuntimeIcon is not null || brand.Appearances is null))
            throw new InvalidDataException("The OTP Harbor icon pack contains invalid themed appearance metadata.");

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

        if (formatVersion == 3)
        {
            ValidateAppearance(brand.Appearances!.LightTheme, sourceProviders, references);
            ValidateAppearance(brand.Appearances.DarkTheme, sourceProviders, references);
        }
    }

    private static void ValidateAppearance(
        AppearanceReference? appearance,
        IReadOnlySet<string> sourceProviders,
        IReadOnlySet<string> brandSources)
    {
        if (appearance?.Asset is null
            || appearance.Source is null
            || !ValidateV3Asset(appearance.Asset)
            || !TryParseAssetRole(appearance.AssetRole, out var role)
            || !TryParseBackgroundMode(appearance.Background?.Mode, out var backgroundMode)
            || !TryParseSelection(appearance.Selection, out _)
            || !IconImportArchive.IsSafeDisplayText(appearance.AppearanceSelectionReason, 4_096)
            || !sourceProviders.Contains(appearance.Source.Provider ?? string.Empty)
            || appearance.Source.Provider is null
            || !CanonicalIdRegex().IsMatch(appearance.Source.Provider)
            || !IconImportArchive.IsSafeDisplayText(appearance.Source.SourceId)
            || !IconImportArchive.IsSafeDisplayText(appearance.Source.AssetPath, 4_096)
            || !brandSources.Any(source => source.StartsWith(
                appearance.Source.Provider + "/",
                StringComparison.Ordinal))
            || role == BrandIconAssetRole.Foreground
                && (backgroundMode != BrandIconBackgroundMode.Solid
                    || appearance.Background!.Color is null
                    || !UppercaseColorRegex().IsMatch(appearance.Background.Color))
            || role == BrandIconAssetRole.CompleteIcon
                && (backgroundMode != BrandIconBackgroundMode.Embedded
                    || appearance.Background!.Color is not null))
            throw new InvalidDataException("The OTP Harbor icon pack contains an invalid themed appearance.");
    }

    private static bool ValidateV3Asset(RuntimeIconReference asset) =>
        string.Equals(asset.MediaType, RuntimeMediaType, StringComparison.Ordinal)
        && asset.Width == RuntimePngValidator.Width
        && asset.Height == RuntimePngValidator.Height
        && asset.Sha256 is not null
        && LowercaseSha256Regex().IsMatch(asset.Sha256)
        && string.Equals(asset.RenderProfile, V3RuntimeRenderProfile, StringComparison.Ordinal)
        && string.Equals(
            asset.Path,
            $"runtime/appearance-128/{asset.Sha256}.png",
            StringComparison.Ordinal);

    private static ImportedRuntimeAppearance ImportAppearance(
        AppearanceReference appearance,
        ISet<string> expectedFiles,
        IDictionary<string, RuntimeIconReference> runtimeAssets)
    {
        var asset = appearance.Asset!;
        var path = asset.Path!;
        expectedFiles.Add(path);
        if (runtimeAssets.TryGetValue(path, out var existing))
        {
            if (!string.Equals(existing.Sha256, asset.Sha256, StringComparison.Ordinal)
                || !string.Equals(existing.MediaType, asset.MediaType, StringComparison.Ordinal)
                || existing.Width != asset.Width
                || existing.Height != asset.Height
                || !string.Equals(existing.RenderProfile, asset.RenderProfile, StringComparison.Ordinal))
                throw new InvalidDataException("The OTP Harbor icon pack contains conflicting runtime asset metadata.");
        }
        else
        {
            runtimeAssets.Add(path, asset);
        }

        TryParseAssetRole(appearance.AssetRole, out var role);
        TryParseBackgroundMode(appearance.Background!.Mode, out var backgroundMode);
        TryParseSelection(appearance.Selection, out var selection);
        return new ImportedRuntimeAppearance(
            new ImportedRuntimeIcon(
                path,
                asset.MediaType!,
                asset.Width,
                asset.Height,
                asset.Sha256!,
                asset.RenderProfile!),
            role,
            backgroundMode,
            appearance.Background.Color,
            selection,
            new BrandIconAppearanceSource(
                appearance.Source!.Provider!,
                appearance.Source.SourceId!,
                appearance.Source.AssetPath!),
            appearance.AppearanceSelectionReason!);
    }

    private static string CompatibleBackgroundColor(ImportedRuntimeAppearance appearance) =>
        appearance.BackgroundMode == BrandIconBackgroundMode.Solid
        && appearance.BackgroundColor is { } color
            ? color
            : "#334155";

    private static bool TryParseAssetRole(string? value, out BrandIconAssetRole role)
    {
        role = value switch
        {
            "foreground" => BrandIconAssetRole.Foreground,
            "complete-icon" => BrandIconAssetRole.CompleteIcon,
            _ => default
        };
        return value is "foreground" or "complete-icon";
    }

    private static bool TryParseBackgroundMode(string? value, out BrandIconBackgroundMode mode)
    {
        mode = value switch
        {
            "solid" => BrandIconBackgroundMode.Solid,
            "embedded" => BrandIconBackgroundMode.Embedded,
            _ => default
        };
        return value is "solid" or "embedded";
    }

    private static bool TryParseSelection(string? value, out BrandIconAppearanceSelection selection)
    {
        selection = value switch
        {
            "native-theme-variant" => BrandIconAppearanceSelection.NativeThemeVariant,
            "brand-color-foreground" => BrandIconAppearanceSelection.BrandColorForeground,
            "complete-source-artwork" => BrandIconAppearanceSelection.CompleteSourceArtwork,
            "source-default" => BrandIconAppearanceSelection.SourceDefault,
            "reviewed-badge-composition" => BrandIconAppearanceSelection.ReviewedBadgeComposition,
            _ => default
        };
        return value is "native-theme-variant"
            or "brand-color-foreground"
            or "complete-source-artwork"
            or "source-default"
            or "reviewed-badge-composition";
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
        public AppearanceSet? Appearances { get; init; }
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

    private sealed class AppearanceSet
    {
        public AppearanceReference? LightTheme { get; init; }
        public AppearanceReference? DarkTheme { get; init; }
    }

    private sealed class AppearanceReference
    {
        public RuntimeIconReference? Asset { get; init; }
        public string? AssetRole { get; init; }
        public AppearanceBackground? Background { get; init; }
        public string? Selection { get; init; }
        public AppearanceSourceReference? Source { get; init; }
        public string? AppearanceSelectionReason { get; init; }
    }

    private sealed class AppearanceBackground
    {
        public string? Mode { get; init; }
        public string? Color { get; init; }
    }

    private sealed class AppearanceSourceReference
    {
        public string? Provider { get; init; }
        public string? SourceId { get; init; }
        public string? AssetPath { get; init; }
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
